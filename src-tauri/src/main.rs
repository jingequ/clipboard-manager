#![cfg_attr(not(debug_assertions), windows_subsystem = "windows")]

mod database;
mod clipboard_service;

use std::path::Path;
use std::sync::Mutex;
use tauri::{AppHandle, Manager, WebviewWindow, Emitter};
use tauri::menu::{Menu, MenuItem};
use tauri::tray::{TrayIconBuilder, TrayIconEvent};
use serde::{Deserialize, Serialize};
use lazy_static::lazy_static;
use windows::Win32::Foundation::HWND;
use windows::Win32::UI::Input::KeyboardAndMouse::{RegisterHotKey, UnregisterHotKey, MOD_ALT, MOD_CONTROL, MOD_SHIFT, MOD_NOREPEAT};
use windows::Win32::UI::WindowsAndMessaging::{GetMessageW, MSG, WM_HOTKEY};
use clipboard_master::{Master, ClipboardHandler, CallbackResult};

#[derive(Debug, Serialize, Deserialize, Clone)]
pub struct AppSettings {
    #[serde(rename = "LaunchAtStartup")]
    pub launch_at_startup: bool,
    #[serde(rename = "CaptureImages")]
    pub capture_images: bool,
    #[serde(rename = "CaptureFiles")]
    pub capture_files: bool,
    #[serde(rename = "RetentionDays")]
    pub retention_days: i32,
    #[serde(rename = "MaxItems")]
    pub max_items: i32,
    #[serde(rename = "HotkeyGesture")]
    pub hotkey_gesture: String,
}

lazy_static! {
    static ref DB_PATH: Mutex<String> = Mutex::new(String::new());
    static ref IMAGE_DIR: Mutex<String> = Mutex::new(String::new());
    static ref SETTINGS_PATH: Mutex<String> = Mutex::new(String::new());
    static ref CURRENT_SETTINGS: Mutex<Option<AppSettings>> = Mutex::new(None);
    static ref LAST_FOREGROUND_WINDOW: Mutex<Option<isize>> = Mutex::new(None);
    static ref HOTKEY_THREAD_SENDER: Mutex<Option<std::sync::mpsc::Sender<String>>> = Mutex::new(None);
    static ref MONITOR_PAUSED: Mutex<bool> = Mutex::new(false);
}

fn load_settings_or_default(path: &str) -> AppSettings {
    if let Ok(content) = std::fs::read_to_string(path) {
        if let Ok(settings) = serde_json::from_str::<AppSettings>(&content) {
            return settings;
        }
    }
    AppSettings {
        launch_at_startup: true,
        capture_images: true,
        capture_files: true,
        retention_days: 30,
        max_items: 500,
        hotkey_gesture: "Alt+C".to_string(),
    }
}

fn save_settings_to_file(path: &str, settings: &AppSettings) {
    if let Ok(json) = serde_json::to_string_pretty(settings) {
        let _ = std::fs::write(path, json);
    }
}

fn set_autostart(enabled: bool) -> Result<(), std::io::Error> {
    use winreg::enums::{HKEY_CURRENT_USER, KEY_SET_VALUE};
    use winreg::RegKey;
    let hkcu = RegKey::predef(HKEY_CURRENT_USER);
    let run_key = hkcu.open_subkey_with_flags("Software\\Microsoft\\Windows\\CurrentVersion\\Run", KEY_SET_VALUE)?;
    if enabled {
        let exe_path = std::env::current_exe()?;
        let path_str = format!("\"{}\"", exe_path.to_string_lossy());
        run_key.set_value("ClipboardManager", &path_str)?;
    } else {
        let _ = run_key.delete_value("ClipboardManager");
    }
    Ok(())
}

fn compute_sha256(input: &str) -> String {
    use sha2::{Sha256, Digest};
    let mut hasher = Sha256::new();
    hasher.update(input.as_bytes());
    let result = hasher.finalize();
    format!("{:X}", result)
}

struct ClipboardChangeHandler {
    db_path: String,
    image_dir: String,
    app_handle: AppHandle,
}

impl ClipboardHandler for ClipboardChangeHandler {
    fn on_clipboard_change(&mut self) -> CallbackResult {
        if *MONITOR_PAUSED.lock().unwrap() {
            return CallbackResult::Next;
        }
        let db_path = &self.db_path;
        let image_dir = &self.image_dir;
        
        if let Some(_item) = capture_current_clipboard(db_path, image_dir) {
            // Enforce limits
            if let Some(settings) = CURRENT_SETTINGS.lock().unwrap().as_ref() {
                let expired_images = database::get_expired_image_paths(db_path).unwrap_or_default();
                let _ = database::prune_expired(db_path);
                
                let pruned_images = database::get_pruned_image_paths_for_max(db_path, settings.max_items).unwrap_or_default();
                let _ = database::enforce_max_items(db_path, settings.max_items);
                
                // Delete image cache files
                for img in expired_images.iter().chain(pruned_images.iter()) {
                    let _ = std::fs::remove_file(img);
                }
                
                clean_orphaned_image_cache(db_path, image_dir);
            }
            
            let _ = self.app_handle.emit("clipboard-updated", ());
        }
        CallbackResult::Next
    }

    fn on_clipboard_error(&mut self, _error: std::io::Error) -> CallbackResult {
        CallbackResult::Next
    }
}

fn clean_orphaned_image_cache(db_path: &str, image_dir: &str) {
    if let Ok(referenced) = database::get_referenced_image_paths(db_path) {
        let ref_set: std::collections::HashSet<String> = referenced.into_iter().collect();
        if let Ok(entries) = std::fs::read_dir(image_dir) {
            for entry in entries.flatten() {
                if let Ok(file_type) = entry.file_type() {
                    if file_type.is_file() {
                        let path = entry.path().to_string_lossy().into_owned();
                        if !ref_set.contains(&path) {
                            let _ = std::fs::remove_file(path);
                        }
                    }
                }
            }
        }
    }
}

// Global hotkey message loop
fn start_hotkey_manager(app_handle: AppHandle, initial_gesture: String) -> std::sync::mpsc::Sender<String> {
    let (tx, rx) = std::sync::mpsc::channel::<String>();
    
    std::thread::spawn(move || {
        let mut registered_id = 1i32;
        let mut current_mods = 0u32;
        let mut current_key = 0u32;
        
        // Register initial hotkey
        if let Some((mods, key)) = parse_hotkey_string(&initial_gesture) {
            unsafe {
                if let Err(e) = RegisterHotKey(None, registered_id, windows::Win32::UI::Input::KeyboardAndMouse::HOT_KEY_MODIFIERS(mods), key) {
                    eprintln!("Failed to register initial hotkey '{}': {:?}", initial_gesture, e);
                } else {
                    println!("Successfully registered initial hotkey '{}'", initial_gesture);
                    current_mods = mods;
                    current_key = key;
                }
            }
        } else {
            eprintln!("Invalid initial hotkey gesture '{}'", initial_gesture);
        }

        // Spawn thread to listen for updates
        let thread_id = unsafe { windows::Win32::System::Threading::GetCurrentThreadId() };
        std::thread::spawn(move || {
            while let Ok(gesture) = rx.recv() {
                unsafe {
                    let boxed = Box::new(gesture);
                    let lparam = Box::into_raw(boxed) as isize;
                    // Retrying PostThreadMessageW a few times if it fails
                    let mut success = false;
                    for _ in 0..10 {
                        if windows::Win32::UI::WindowsAndMessaging::PostThreadMessageW(
                            thread_id,
                            0x0400 + 1,
                            windows::Win32::Foundation::WPARAM(0),
                            windows::Win32::Foundation::LPARAM(lparam)
                        ).is_ok() {
                            success = true;
                            break;
                        }
                        std::thread::sleep(std::time::Duration::from_millis(50));
                    }
                    if !success {
                        eprintln!("Failed to post hotkey update message to message loop thread.");
                        // Reclaim memory to avoid leak
                        let _ = Box::from_raw(lparam as *mut String);
                    }
                }
            }
        });

        // Run message loop
        unsafe {
            // Force creation of message queue for this thread
            let mut msg = MSG::default();
            let _ = windows::Win32::UI::WindowsAndMessaging::PeekMessageW(&mut msg, None, 0, 0, windows::Win32::UI::WindowsAndMessaging::PM_NOREMOVE);
            
            while GetMessageW(&mut msg, None, 0, 0).as_bool() {
                if msg.message == WM_HOTKEY && msg.wParam.0 as i32 == registered_id {
                    println!("Hotkey triggered!");
                    if let Some(window) = app_handle.get_webview_window("main") {
                        show_window(&window);
                    }
                } else if msg.message == 0x0400 + 1 {
                    let raw_ptr = msg.lParam.0 as *mut String;
                    if !raw_ptr.is_null() {
                        let gesture = Box::from_raw(raw_ptr);
                        println!("Updating hotkey to: {}", gesture);
                        if let Some((mods, key)) = parse_hotkey_string(&gesture) {
                            let _ = UnregisterHotKey(None, registered_id);
                            registered_id += 1;
                            if RegisterHotKey(None, registered_id, windows::Win32::UI::Input::KeyboardAndMouse::HOT_KEY_MODIFIERS(mods), key).is_ok() {
                                println!("Successfully updated hotkey to '{}'", gesture);
                                current_mods = mods;
                                current_key = key;
                            } else {
                                eprintln!("Failed to update hotkey to '{}', reverting to previous", gesture);
                                let _ = RegisterHotKey(None, registered_id, windows::Win32::UI::Input::KeyboardAndMouse::HOT_KEY_MODIFIERS(current_mods), current_key);
                            }
                        }
                    }
                }
            }
            let _ = UnregisterHotKey(None, registered_id);
        }
    });

    tx
}

fn parse_hotkey_string(gesture: &str) -> Option<(u32, u32)> {
    let parts: Vec<&str> = gesture.split('+').map(|s| s.trim()).collect();
    if parts.is_empty() {
        return None;
    }
    
    let mut modifiers = MOD_NOREPEAT.0;
    let mut key = 0u32;
    
    for part in parts {
        match part.to_lowercase().as_str() {
            "ctrl" | "control" => modifiers |= MOD_CONTROL.0,
            "alt" => modifiers |= MOD_ALT.0,
            "shift" => modifiers |= MOD_SHIFT.0,
            "win" | "windows" => modifiers |= windows::Win32::UI::Input::KeyboardAndMouse::MOD_WIN.0,
            k => {
                if k.len() == 1 {
                    key = k.chars().next().unwrap().to_ascii_uppercase() as u32;
                } else if k.starts_with('f') && k.len() > 1 {
                    if let Ok(f_num) = k[1..].parse::<u32>() {
                        if (1..=12).contains(&f_num) {
                            key = 0x6F + f_num; // VK_F1 is 0x70
                        }
                    }
                } else {
                    match k {
                        "space" => key = 0x20, // VK_SPACE
                        "enter" => key = 0x0D, // VK_RETURN
                        "tab" => key = 0x09,   // VK_TAB
                        _ => {}
                    }
                }
            }
        }
    }
    
    if key != 0 {
        Some((modifiers, key))
    } else {
        None
    }
}

// Commands exported to Frontend
#[tauri::command]
fn get_settings() -> AppSettings {
    CURRENT_SETTINGS.lock().unwrap().clone().unwrap()
}

#[tauri::command]
fn save_settings(settings: AppSettings, _app_handle: AppHandle) -> Result<(), String> {
    let path = SETTINGS_PATH.lock().unwrap().clone();
    save_settings_to_file(&path, &settings);
    *CURRENT_SETTINGS.lock().unwrap() = Some(settings.clone());
    let _ = set_autostart(settings.launch_at_startup);
    
    // Update global hotkey
    if let Some(sender) = HOTKEY_THREAD_SENDER.lock().unwrap().as_ref() {
        let _ = sender.send(settings.hotkey_gesture);
    }
    Ok(())
}

#[tauri::command]
fn search_history(query: String, limit: i32) -> Result<Vec<database::ClipboardItemSummary>, String> {
    let db = DB_PATH.lock().unwrap().clone();
    database::search_summaries(&db, &query, limit).map_err(|e| e.to_string())
}

#[tauri::command]
fn get_item_details(id: String) -> Result<Option<database::ClipboardItem>, String> {
    let db = DB_PATH.lock().unwrap().clone();
    database::get_item_preview(&db, &id).map_err(|e| e.to_string())
}

#[tauri::command]
fn get_total_count_cmd(query: String) -> Result<i32, String> {
    let db = DB_PATH.lock().unwrap().clone();
    database::get_total_count(&db, &query).map_err(|e| e.to_string())
}

#[tauri::command]
fn delete_history_item(id: String) -> Result<(), String> {
    let db = DB_PATH.lock().unwrap().clone();
    // Delete image if exists
    if let Ok(Some(img_path)) = database::get_item_image_path(&db, &id) {
        let _ = std::fs::remove_file(img_path);
    }
    database::delete_item(&db, &id).map_err(|e| e.to_string())
}

#[tauri::command]
fn clear_history() -> Result<(), String> {
    let db = DB_PATH.lock().unwrap().clone();
    let image_dir = IMAGE_DIR.lock().unwrap().clone();
    
    // Delete all images
    let _ = database::clear_all(&db);
    clean_orphaned_image_cache(&db, &image_dir);
    Ok(())
}

#[tauri::command]
fn execute_clear_command_cmd(mode: String, count: i32, duration_minutes: i32) -> Result<(), String> {
    let db = DB_PATH.lock().unwrap().clone();
    let image_dir = IMAGE_DIR.lock().unwrap().clone();
    
    match mode.as_str() {
        "all" => {
            let _ = database::clear_all(&db);
        }
        "count" => {
            // Delete image files first
            if let Ok(img_paths) = database::get_image_paths_for_latest(&db, count) {
                for img in img_paths {
                    let _ = std::fs::remove_file(img);
                }
            }
            let _ = database::delete_latest(&db, count);
        }
        "recent" => {
            let minutes = if duration_minutes > 0 { duration_minutes as i64 } else { 0 };
            let since = (chrono::Local::now() - chrono::Duration::minutes(minutes)).to_rfc3339();
            
            // Delete images
            let _ = database::delete_recent(&db, &since);
        }
        _ => {}
    }
    
    clean_orphaned_image_cache(&db, &image_dir);
    Ok(())
}

#[tauri::command]
fn replay_and_paste(id: String, app_handle: AppHandle) -> Result<(), String> {
    // Pause monitor so we don't capture the replay copy event!
    *MONITOR_PAUSED.lock().unwrap() = true;
    
    let db = DB_PATH.lock().unwrap().clone();
    let matched_item = database::get_item_by_id(&db, &id).map_err(|e| e.to_string())?;
    if let Some(item) = matched_item {
        let success = match item.item_type {
            database::ClipboardItemType::Text => {
                let txt = item.text_content.unwrap_or_default();
                clipboard_service::set_clipboard_data_object(Some(&txt), None, None)
            }
            database::ClipboardItemType::RichText => {
                clipboard_service::set_clipboard_data_object(
                    item.text_content.as_deref(),
                    item.html_content.as_deref(),
                    item.rtf_content.as_deref(),
                )
            }
            database::ClipboardItemType::FileList => {
                if let Some(files) = item.files {
                    let paths: Vec<String> = files.into_iter().map(|f| f.path).collect();
                    clipboard_service::set_clipboard_files(&paths)
                } else {
                    false
                }
            }
            database::ClipboardItemType::Image => {
                if let Some(path) = item.image_path {
                    clipboard_service::set_clipboard_image(&path)
                } else {
                    false
                }
            }
        };
        
        if success {
            // Move item to top by touching it
            let now = chrono::Local::now().to_rfc3339();
            let _ = database::touch_item(&db, &id, &now);
            
            // Replay succeeded, hide main window
            if let Some(window) = app_handle.get_webview_window("main") {
                let _ = window.hide();
            }
            
            // Paste to target window
            let target_hwnd = *LAST_FOREGROUND_WINDOW.lock().unwrap();
            if let Some(hwnd) = target_hwnd {
                std::thread::spawn(move || {
                    clipboard_service::paste_to_window(hwnd);
                    
                    // Resume monitor after paste finishes (sleep 500ms like C# did)
                    std::thread::sleep(std::time::Duration::from_millis(500));
                    *MONITOR_PAUSED.lock().unwrap() = false;
                });
            } else {
                *MONITOR_PAUSED.lock().unwrap() = false;
            }
        } else {
            *MONITOR_PAUSED.lock().unwrap() = false;
        }
    } else {
        *MONITOR_PAUSED.lock().unwrap() = false;
    }
    
    Ok(())
}

fn show_window(window: &WebviewWindow) {
    println!("show_window called for window label: {}", window.label());
    unsafe {
        let fg_hwnd = windows::Win32::UI::WindowsAndMessaging::GetForegroundWindow();
        let current_hwnd = window.hwnd().unwrap_or(HWND(std::ptr::null_mut()));
        if fg_hwnd != current_hwnd {
            *LAST_FOREGROUND_WINDOW.lock().unwrap() = Some(fg_hwnd.0 as isize);
        }
    }
    if let Err(e) = window.unminimize() {
        eprintln!("Failed to unminimize window: {:?}", e);
    }
    if let Err(e) = window.show() {
        eprintln!("Failed to show window: {:?}", e);
    }
    if let Err(e) = window.center() {
        eprintln!("Failed to center window: {:?}", e);
    }
    if let Err(e) = window.set_focus() {
        eprintln!("Failed to focus window: {:?}", e);
    }
    
    if let Ok(pos) = window.outer_position() {
        println!("Window position: {:?}", pos);
    }
    if let Ok(size) = window.outer_size() {
        println!("Window size: {:?}", size);
    }
    if let Ok(visible) = window.is_visible() {
        println!("Window visibility state: {:?}", visible);
    }
    
    let _ = window.emit("window-shown", ());
}

#[tauri::command]
fn log_from_js(msg: String) {
    println!("[JS LOG] {}", msg);
}

#[tauri::command]
fn hide_window(app_handle: AppHandle) {
    if let Some(window) = app_handle.get_webview_window("main") {
        let _ = window.hide();
    }
}

fn main() {
    tauri::Builder::default()
        .setup(|app| {
            println!("Starting setup hook...");
            let app_data = Path::new(&std::env::var("LOCALAPPDATA").unwrap()).join("ClipboardManager");
            std::fs::create_dir_all(&app_data).unwrap();

            let db_path = app_data.join("clipboard.db").to_string_lossy().into_owned();
            let image_dir = app_data.join("images").to_string_lossy().into_owned();
            let settings_path = app_data.join("settings.json").to_string_lossy().into_owned();
            println!("Data paths: DB='{}', Settings='{}'", db_path, settings_path);

            std::fs::create_dir_all(&image_dir).unwrap();

            // Initialize DB
            println!("Initializing database...");
            database::initialize(&db_path).unwrap();
            println!("Database initialized.");

            // Load settings
            let settings = load_settings_or_default(&settings_path);
            let _ = set_autostart(settings.launch_at_startup);

            *DB_PATH.lock().unwrap() = db_path.clone();
            *IMAGE_DIR.lock().unwrap() = image_dir.clone();
            *SETTINGS_PATH.lock().unwrap() = settings_path.clone();
            *CURRENT_SETTINGS.lock().unwrap() = Some(settings.clone());

            // Build Tray Menu
            println!("Building tray menu...");
            let open_item = MenuItem::with_id(app, "open", "Open", true, None::<&str>)?;
            let settings_item = MenuItem::with_id(app, "settings", "Settings", true, None::<&str>)?;
            let clear_item = MenuItem::with_id(app, "clear", "Clear History", true, None::<&str>)?;
            let exit_item = MenuItem::with_id(app, "exit", "Exit", true, None::<&str>)?;
            let tray_menu = Menu::with_items(app, &[&open_item, &settings_item, &clear_item, &tauri::menu::PredefinedMenuItem::separator(app)?, &exit_item])?;

            println!("Building tray icon...");
            let tray = TrayIconBuilder::new()
                .icon(app.default_window_icon().unwrap().clone())
                .menu(&tray_menu)
                .on_tray_icon_event(|tray, event| {
                    if let TrayIconEvent::Click { button: tauri::tray::MouseButton::Left, .. } = event {
                        if let Some(window) = tray.app_handle().get_webview_window("main") {
                            println!("Tray left-clicked, showing window");
                            show_window(&window);
                        }
                    }
                })
                .build(app)?;
            app.manage(tray);
            println!("Tray icon registered and state managed.");

            // Start Hotkey message loop
            println!("Starting hotkey manager...");
            let hotkey_tx = start_hotkey_manager(app.handle().clone(), settings.hotkey_gesture.clone());
            *HOTKEY_THREAD_SENDER.lock().unwrap() = Some(hotkey_tx);

            // Listen to tray menu click events
            app.on_menu_event(move |app, event| {
                println!("Tray menu event triggered: {:?}", event.id);
                match event.id.as_ref() {
                    "open" => {
                        if let Some(window) = app.get_webview_window("main") {
                            show_window(&window);
                        }
                    }
                    "settings" => {
                        let _ = app.emit("open-settings", ());
                        if let Some(window) = app.get_webview_window("main") {
                            show_window(&window);
                        }
                    }
                    "clear" => {
                        let db = DB_PATH.lock().unwrap().clone();
                        let image_dir = IMAGE_DIR.lock().unwrap().clone();
                        let _ = database::clear_all(&db);
                        clean_orphaned_image_cache(&db, &image_dir);
                        let _ = app.emit("clipboard-updated", ());
                    }
                    "exit" => {
                        app.exit(0);
                    }
                    _ => {}
                }
            });

            // Start Clipboard monitor thread
            println!("Starting clipboard monitor thread...");
            let db_path_clone = db_path.clone();
            let image_dir_clone = image_dir.clone();
            let app_handle_clone = app.handle().clone();
            std::thread::spawn(move || {
                let handler = ClipboardChangeHandler {
                    db_path: db_path_clone,
                    image_dir: image_dir_clone,
                    app_handle: app_handle_clone,
                };
                if let Ok(mut master) = Master::new(handler) {
                    println!("Clipboard master running.");
                    let _ = master.run();
                } else {
                    eprintln!("Failed to initialize clipboard master.");
                }
            });

            // Setup main window lose focus event
            if let Some(window) = app.get_webview_window("main") {
                println!("Main window found.");
                let window_clone = window.clone();
                window.on_window_event(move |event| {
                    println!("[Rust] Window event: {:?}", event);
                    if let tauri::WindowEvent::Focused(false) = event {
                        println!("[Rust] Window lost focus, hiding main window!");
                        let _ = window_clone.hide();
                    }
                });
            } else {
                eprintln!("Main window NOT found in setup!");
            }

            println!("Setup hook finished successfully.");
            Ok(())
        })
        .invoke_handler(tauri::generate_handler![
            get_settings,
            save_settings,
            search_history,
            get_item_details,
            get_total_count_cmd,
            delete_history_item,
            clear_history,
            execute_clear_command_cmd,
            replay_and_paste,
            log_from_js,
            hide_window
        ])
        .run(tauri::generate_context!())
        .expect("error while running tauri application");
}

fn capture_current_clipboard(db_path: &str, image_dir: &str) -> Option<database::ClipboardItem> {
    let settings = CURRENT_SETTINGS.lock().unwrap().clone()?;
    
    if settings.capture_files {
        println!("[Capture] Checking for files on clipboard...");
        // Retry a few times since the clipboard might still be locked by the source app
        let mut files_result = None;
        for attempt in 0..3 {
            files_result = clipboard_service::get_clipboard_files();
            if files_result.is_some() {
                break;
            }
            if attempt < 2 {
                println!("[Capture] get_clipboard_files returned None (attempt {}), retrying...", attempt + 1);
                std::thread::sleep(std::time::Duration::from_millis(50));
            }
        }
        
        if let Some(files) = files_result {
            println!("[Capture] Found {} file(s) on clipboard", files.len());
            if !files.is_empty() {
                let mut entries = Vec::new();
                for f in &files {
                    let path_obj = Path::new(f);
                    let name = path_obj.file_name()
                        .map(|n| n.to_string_lossy().into_owned())
                        .unwrap_or_else(|| f.clone());
                    let is_dir = path_obj.is_dir();
                    let ext = path_obj.extension()
                        .map(|e| e.to_string_lossy().into_owned())
                        .unwrap_or_default();
                    entries.push(database::FileClipboardEntry {
                        path: f.clone(),
                        name,
                        is_directory: is_dir,
                        extension: ext,
                    });
                }
                
                let summary = if entries.len() == 1 {
                    entries[0].name.clone()
                } else {
                    format!("{} and {} more", entries[0].name, entries.len() - 1)
                };
                
                let search_text = entries.iter()
                    .map(|x| format!("{} {}", x.name, x.path))
                    .collect::<Vec<String>>()
                    .join(" ");
                
                let paths_str = files.join("|");
                let hash = compute_sha256(&paths_str);
                
                if let Ok(Some(existing)) = database::find_existing_by_hash(db_path, &hash) {
                    println!("[Capture] File(s) already exist in DB, touching item");
                    let now = chrono::Local::now().to_rfc3339();
                    let _ = database::touch_item(db_path, &existing.id, &now);
                    return None;
                }
                
                let expires_at = if settings.retention_days > 0 {
                    Some((chrono::Local::now() + chrono::Duration::days(settings.retention_days as i64)).to_rfc3339())
                } else {
                    None
                };
                
                let display_metadata = if entries.len() == 1 {
                    entries[0].path.clone()
                } else {
                    format!("{} items", entries.len())
                };
                
                let item = database::ClipboardItem {
                    id: uuid::Uuid::new_v4().to_string(),
                    item_type: database::ClipboardItemType::FileList,
                    summary,
                    search_text,
                    text_content: None,
                    html_content: None,
                    rtf_content: None,
                    image_path: None,
                    files: Some(entries),
                    created_at: chrono::Local::now().to_rfc3339(),
                    expires_at,
                    content_hash: Some(hash),
                    display_metadata: Some(display_metadata),
                };
                
                println!("[Capture] Saving file clipboard item: {}", item.summary);
                let _ = database::add_or_update_item(db_path, &item);
                return Some(item);
            }
        }
    }
    
    if settings.capture_images {
        let temp_id = uuid::Uuid::new_v4().to_string();
        let temp_path = Path::new(image_dir).join(format!("{}.png", temp_id));
        let temp_path_str = temp_path.to_string_lossy().into_owned();
        
        if let Some((w, h)) = clipboard_service::get_clipboard_image(&temp_path_str) {
            let file_len = std::fs::metadata(&temp_path).map(|m| m.len()).unwrap_or(0);
            let hash = compute_sha256(&format!("{}x{}:{}", w, h, file_len));
            
            if let Ok(Some(existing)) = database::find_existing_by_hash(db_path, &hash) {
                let _ = std::fs::remove_file(temp_path);
                let now = chrono::Local::now().to_rfc3339();
                let _ = database::touch_item(db_path, &existing.id, &now);
                return None;
            }
            
            let expires_at = if settings.retention_days > 0 {
                Some((chrono::Local::now() + chrono::Duration::days(settings.retention_days as i64)).to_rfc3339())
            } else {
                None
            };
            
            let item = database::ClipboardItem {
                id: temp_id,
                item_type: database::ClipboardItemType::Image,
                summary: format!("Image: {}x{}", w, h),
                search_text: format!("image {} {}", w, h),
                text_content: None,
                html_content: None,
                rtf_content: None,
                image_path: Some(temp_path_str),
                files: None,
                created_at: chrono::Local::now().to_rfc3339(),
                expires_at,
                content_hash: Some(hash),
                display_metadata: Some(format!("{}x{}", w, h)),
            };
            
            let _ = database::add_or_update_item(db_path, &item);
            return Some(item);
        }
    }
    
    let html = clipboard_service::get_clipboard_custom_format("HTML Format");
    let rtf = clipboard_service::get_clipboard_custom_format("Rich Text Format");
    let text = clipboard_service::get_clipboard_text();
    
    if html.is_some() || rtf.is_some() {
        let text_val = text.clone().unwrap_or_default();
        let html_val = html.clone().unwrap_or_default();
        let rtf_val = rtf.clone().unwrap_or_default();
        
        if !text_val.trim().is_empty() || !html_val.trim().is_empty() || !rtf_val.trim().is_empty() {
            let hash = compute_sha256(&format!("{}|{}|{}", text_val, html_val, rtf_val));
            
            if let Ok(Some(existing)) = database::find_existing_by_hash(db_path, &hash) {
                let now = chrono::Local::now().to_rfc3339();
                let _ = database::touch_item(db_path, &existing.id, &now);
                return None;
            }
            
            let summary = if !text_val.trim().is_empty() {
                if text_val.chars().count() > 120 {
                    format!("{}...", text_val.chars().take(117).collect::<String>())
                } else {
                    text_val.clone()
                }
            } else {
                "Rich text content".to_string()
            };
            
            let expires_at = if settings.retention_days > 0 {
                Some((chrono::Local::now() + chrono::Duration::days(settings.retention_days as i64)).to_rfc3339())
            } else {
                None
            };
            
            let item = database::ClipboardItem {
                id: uuid::Uuid::new_v4().to_string(),
                item_type: database::ClipboardItemType::RichText,
                summary,
                search_text: text_val.clone(),
                text_content: Some(text_val.clone()),
                html_content: html,
                rtf_content: rtf,
                image_path: None,
                files: None,
                created_at: chrono::Local::now().to_rfc3339(),
                expires_at,
                content_hash: Some(hash),
                display_metadata: Some(format!("{} chars", text_val.chars().count())),
            };
            
            let _ = database::add_or_update_item(db_path, &item);
            return Some(item);
        }
     }
     
     if let Some(text_val) = text {
        if !text_val.trim().is_empty() {
            let hash = compute_sha256(&text_val);
            
            if let Ok(Some(existing)) = database::find_existing_by_hash(db_path, &hash) {
                let now = chrono::Local::now().to_rfc3339();
                let _ = database::touch_item(db_path, &existing.id, &now);
                return None;
            }
            
            let summary = if text_val.chars().count() > 120 {
                format!("{}...", text_val.chars().take(117).collect::<String>())
            } else {
                text_val.clone()
            };
            
            let expires_at = if settings.retention_days > 0 {
                Some((chrono::Local::now() + chrono::Duration::days(settings.retention_days as i64)).to_rfc3339())
            } else {
                None
            };
            
            let item = database::ClipboardItem {
                id: uuid::Uuid::new_v4().to_string(),
                item_type: database::ClipboardItemType::Text,
                summary,
                search_text: text_val.clone(),
                text_content: Some(text_val.clone()),
                html_content: None,
                rtf_content: None,
                image_path: None,
                files: None,
                created_at: chrono::Local::now().to_rfc3339(),
                expires_at,
                content_hash: Some(hash),
                display_metadata: Some(format!("{} chars", text_val.chars().count())),
            };
            
            let _ = database::add_or_update_item(db_path, &item);
            return Some(item);
        }
    }
    
    None
}
