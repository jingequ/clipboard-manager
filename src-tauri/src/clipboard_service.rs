use std::ptr;
use std::slice;
use std::path::Path;
use windows::Win32::System::DataExchange::{
    OpenClipboard, CloseClipboard, IsClipboardFormatAvailable, GetClipboardData, SetClipboardData, EmptyClipboard, RegisterClipboardFormatW
};
use windows::Win32::System::Memory::{GlobalLock, GlobalUnlock, GlobalAlloc, GHND};
use windows::Win32::Foundation::{HANDLE, HWND, HGLOBAL};
use windows::Win32::UI::Shell::{DragQueryFileW, HDROP};
use windows::Win32::UI::Input::KeyboardAndMouse::{SendInput, INPUT, INPUT_KEYBOARD, KEYEVENTF_KEYUP, VK_CONTROL, VK_V};
use windows::Win32::UI::WindowsAndMessaging::SetForegroundWindow;

#[repr(C, packed)]
struct BITMAPINFOHEADER {
    biSize: u32,
    biWidth: i32,
    biHeight: i32,
    biPlanes: u16,
    biBitCount: u16,
    biCompression: u32,
    biSizeImage: u32,
    biXPelsPerMeter: i32,
    biYPelsPerMeter: i32,
    biClrUsed: u32,
    biClrImportant: u32,
}

#[repr(C)]
struct DROPFILES {
    pFiles: u32,
    pt_x: i32,
    pt_y: i32,
    fNC: windows::core::BOOL,
    fWide: windows::core::BOOL,
}

pub fn get_clipboard_text() -> Option<String> {
    unsafe {
        if !OpenClipboard(None).is_ok() {
            return None;
        }
        let mut text = None;
        if IsClipboardFormatAvailable(13).is_ok() { // CF_UNICODETEXT
            if let Ok(handle) = GetClipboardData(13) {
                let h_global = HGLOBAL(handle.0);
                let ptr = GlobalLock(h_global);
                if !ptr.is_null() {
                    let wide_slice = slice_from_ptr_until_null(ptr as *const u16);
                    text = Some(String::from_utf16_lossy(wide_slice));
                    let _ = GlobalUnlock(h_global);
                }
            }
        }
        let _ = CloseClipboard();
        text
    }
}

pub fn get_clipboard_custom_format(format_name: &str) -> Option<String> {
    unsafe {
        let format_utf16: Vec<u16> = format_name.encode_utf16().chain(std::iter::once(0)).collect();
        let format_id = RegisterClipboardFormatW(windows::core::PCWSTR(format_utf16.as_ptr()));
        if format_id == 0 {
            return None;
        }

        if !OpenClipboard(None).is_ok() {
            return None;
        }
        let mut content = None;
        if IsClipboardFormatAvailable(format_id).is_ok() {
            if let Ok(handle) = GetClipboardData(format_id) {
                let h_global = HGLOBAL(handle.0);
                let ptr = GlobalLock(h_global);
                if !ptr.is_null() {
                    let data_size = windows::Win32::System::Memory::GlobalSize(h_global);
                    let byte_slice = slice::from_raw_parts(ptr as *const u8, data_size);
                    let len = byte_slice.iter().position(|&x| x == 0).unwrap_or(data_size);
                    content = Some(String::from_utf8_lossy(&byte_slice[..len]).into_owned());
                    let _ = GlobalUnlock(h_global);
                }
            }
        }
        let _ = CloseClipboard();
        content
    }
}

pub fn get_clipboard_files() -> Option<Vec<String>> {
    unsafe {
        if !OpenClipboard(None).is_ok() {
            return None;
        }
        let mut files = None;
        if IsClipboardFormatAvailable(15).is_ok() { // CF_HDROP
            if let Ok(handle) = GetClipboardData(15) {
                let hdrop = HDROP(handle.0);
                let count = DragQueryFileW(hdrop, 0xFFFFFFFF, None);
                let mut paths = Vec::new();
                for i in 0..count {
                    let len = DragQueryFileW(hdrop, i, None);
                    let mut buffer = vec![0u16; len as usize + 1];
                    DragQueryFileW(hdrop, i, Some(&mut buffer));
                    let path = String::from_utf16_lossy(&buffer[..len as usize]);
                    paths.push(path);
                }
                files = Some(paths);
            }
        }
        let _ = CloseClipboard();
        files
    }
}

pub fn get_clipboard_image(output_path: &str) -> Option<(i32, i32)> {
    unsafe {
        if !OpenClipboard(None).is_ok() {
            return None;
        }
        let mut dimensions = None;
        if IsClipboardFormatAvailable(8).is_ok() { // CF_DIB
            if let Ok(handle) = GetClipboardData(8) {
                let h_global = HGLOBAL(handle.0);
                let ptr = GlobalLock(h_global);
                if !ptr.is_null() {
                    let header = &*(ptr as *const BITMAPINFOHEADER);
                    let width = header.biWidth;
                    let height = header.biHeight.abs();
                    let bit_count = header.biBitCount;
                    let header_size = header.biSize as usize;
                    
                    let colors_used = if header.biClrUsed > 0 {
                        header.biClrUsed as usize
                    } else if bit_count <= 8 {
                        1 << bit_count
                    } else {
                        0
                    };
                    let color_table_size = colors_used * 4;
                    
                    let pixel_offset = header_size + color_table_size;
                    let total_size = windows::Win32::System::Memory::GlobalSize(h_global);
                    if pixel_offset < total_size {
                        let pixels_ptr = (ptr as *const u8).add(pixel_offset);
                        let pixels_size = total_size - pixel_offset;
                        let raw_pixels = slice::from_raw_parts(pixels_ptr, pixels_size);
                        
                        if bit_count == 32 {
                            let mut rgba_pixels = vec![0u8; (width * height * 4) as usize];
                            let is_bottom_up = header.biHeight > 0;
                            for y in 0..height {
                                let src_y = if is_bottom_up { height - 1 - y } else { y };
                                for x in 0..width {
                                    let src_idx = ((src_y * width + x) * 4) as usize;
                                    let dest_idx = ((y * width + x) * 4) as usize;
                                    if src_idx + 3 < raw_pixels.len() {
                                        rgba_pixels[dest_idx] = raw_pixels[src_idx + 2];
                                        rgba_pixels[dest_idx + 1] = raw_pixels[src_idx + 1];
                                        rgba_pixels[dest_idx + 2] = raw_pixels[src_idx];
                                        rgba_pixels[dest_idx + 3] = raw_pixels[src_idx + 3];
                                    }
                                }
                            }
                            if image::save_buffer(
                                Path::new(output_path),
                                &rgba_pixels,
                                width as u32,
                                height as u32,
                                image::ExtendedColorType::Rgba8,
                            ).is_ok() {
                                dimensions = Some((width, height));
                            }
                        } else if bit_count == 24 {
                            let row_stride = ((width * 3 + 3) / 4) * 4;
                            let mut rgb_pixels = vec![0u8; (width * height * 3) as usize];
                            let is_bottom_up = header.biHeight > 0;
                            for y in 0..height {
                                let src_y = if is_bottom_up { height - 1 - y } else { y };
                                for x in 0..width {
                                    let src_idx = (src_y * row_stride + x * 3) as usize;
                                    let dest_idx = ((y * width + x) * 3) as usize;
                                    if src_idx + 2 < raw_pixels.len() {
                                        rgb_pixels[dest_idx] = raw_pixels[src_idx + 2];
                                        rgb_pixels[dest_idx + 1] = raw_pixels[src_idx + 1];
                                        rgb_pixels[dest_idx + 2] = raw_pixels[src_idx];
                                    }
                                }
                            }
                            if image::save_buffer(
                                Path::new(output_path),
                                &rgb_pixels,
                                width as u32,
                                height as u32,
                                image::ExtendedColorType::Rgb8,
                            ).is_ok() {
                                dimensions = Some((width, height));
                            }
                        }
                    }
                    let _ = GlobalUnlock(h_global);
                }
            }
        }
        let _ = CloseClipboard();
        dimensions
    }
}

pub fn set_clipboard_data_object(text: Option<&str>, html: Option<&str>, rtf: Option<&str>) -> bool {
    unsafe {
        if !OpenClipboard(None).is_ok() {
            return false;
        }
        let _ = EmptyClipboard();
        
        if let Some(t) = text {
            let wide: Vec<u16> = t.encode_utf16().chain(std::iter::once(0)).collect();
            let size = wide.len() * 2;
            if let Ok(h_mem) = GlobalAlloc(GHND, size) {
                let ptr = GlobalLock(h_mem);
                if !ptr.is_null() {
                    ptr::copy_nonoverlapping(wide.as_ptr(), ptr as *mut u16, wide.len());
                    let _ = GlobalUnlock(h_mem);
                    let _ = SetClipboardData(13, Some(HANDLE(h_mem.0)));
                }
            }
        }

        if let Some(h) = html {
            let format_utf16: Vec<u16> = "HTML Format".encode_utf16().chain(std::iter::once(0)).collect();
            let format_id = RegisterClipboardFormatW(windows::core::PCWSTR(format_utf16.as_ptr()));
            if format_id != 0 {
                let bytes = h.as_bytes();
                let size = bytes.len() + 1;
                if let Ok(h_mem) = GlobalAlloc(GHND, size) {
                    let ptr = GlobalLock(h_mem);
                    if !ptr.is_null() {
                        ptr::copy_nonoverlapping(bytes.as_ptr(), ptr as *mut u8, bytes.len());
                        *(ptr.add(bytes.len()) as *mut u8) = 0;
                        let _ = GlobalUnlock(h_mem);
                        let _ = SetClipboardData(format_id, Some(HANDLE(h_mem.0)));
                    }
                }
            }
        }

        if let Some(r) = rtf {
            let format_utf16: Vec<u16> = "Rich Text Format".encode_utf16().chain(std::iter::once(0)).collect();
            let format_id = RegisterClipboardFormatW(windows::core::PCWSTR(format_utf16.as_ptr()));
            if format_id != 0 {
                let bytes = r.as_bytes();
                let size = bytes.len() + 1;
                if let Ok(h_mem) = GlobalAlloc(GHND, size) {
                    let ptr = GlobalLock(h_mem);
                    if !ptr.is_null() {
                        ptr::copy_nonoverlapping(bytes.as_ptr(), ptr as *mut u8, bytes.len());
                        *(ptr.add(bytes.len()) as *mut u8) = 0;
                        let _ = GlobalUnlock(h_mem);
                        let _ = SetClipboardData(format_id, Some(HANDLE(h_mem.0)));
                    }
                }
            }
        }

        let _ = CloseClipboard();
        true
    }
}

pub fn set_clipboard_files(files: &[String]) -> bool {
    unsafe {
        if !OpenClipboard(None).is_ok() {
            return false;
        }
        let _ = EmptyClipboard();
        
        let mut file_data = Vec::new();
        for file in files {
            let wide: Vec<u16> = file.encode_utf16().chain(std::iter::once(0)).collect();
            file_data.extend(wide);
        }
        file_data.push(0);
        
        let header_size = std::mem::size_of::<DROPFILES>();
        let total_size = header_size + file_data.len() * 2;
        
        if let Ok(h_mem) = GlobalAlloc(GHND, total_size) {
            let ptr = GlobalLock(h_mem);
            if !ptr.is_null() {
                let dropfiles = ptr as *mut DROPFILES;
                (*dropfiles).pFiles = header_size as u32;
                (*dropfiles).fWide = windows::core::BOOL(1);
                
                let dest_ptr = (ptr as *mut u8).add(header_size) as *mut u16;
                ptr::copy_nonoverlapping(file_data.as_ptr(), dest_ptr, file_data.len());
                
                let _ = GlobalUnlock(h_mem);
                let _ = SetClipboardData(15, Some(HANDLE(h_mem.0)));
            }
        }
        let _ = CloseClipboard();
        true
    }
}

pub fn set_clipboard_image(image_path: &str) -> bool {
    unsafe {
        let img = match image::open(Path::new(image_path)) {
            Ok(i) => i.to_rgba8(),
            Err(_) => return false,
        };
        let width = img.width() as i32;
        let height = img.height() as i32;
        let raw_pixels = img.into_raw();
        
        let header_size = std::mem::size_of::<BITMAPINFOHEADER>();
        let row_stride = ((width * 4 + 3) / 4) * 4;
        let pixel_size = (row_stride * height) as usize;
        let total_size = header_size + pixel_size;
        
        if let Ok(h_mem) = GlobalAlloc(GHND, total_size) {
            let ptr = GlobalLock(h_mem);
            if !ptr.is_null() {
                let header = ptr as *mut BITMAPINFOHEADER;
                (*header).biSize = header_size as u32;
                (*header).biWidth = width;
                (*header).biHeight = height;
                (*header).biPlanes = 1;
                (*header).biBitCount = 32;
                (*header).biCompression = 0;
                (*header).biSizeImage = pixel_size as u32;
                (*header).biXPelsPerMeter = 0;
                (*header).biYPelsPerMeter = 0;
                (*header).biClrUsed = 0;
                (*header).biClrImportant = 0;
                
                let dest_pixels = (ptr as *mut u8).add(header_size);
                for y in 0..height {
                    let src_y = height - 1 - y;
                    for x in 0..width {
                        let src_idx = ((src_y * width + x) * 4) as usize;
                        let dest_idx = ((y * width + x) * 4) as usize;
                        if src_idx + 3 < raw_pixels.len() {
                            let r = raw_pixels[src_idx];
                            let g = raw_pixels[src_idx + 1];
                            let b = raw_pixels[src_idx + 2];
                            let a = raw_pixels[src_idx + 3];
                            *dest_pixels.add(dest_idx) = b;
                            *dest_pixels.add(dest_idx + 1) = g;
                            *dest_pixels.add(dest_idx + 2) = r;
                            *dest_pixels.add(dest_idx + 3) = a;
                        }
                    }
                }
                
                let _ = GlobalUnlock(h_mem);
                if !OpenClipboard(None).is_ok() {
                    return false;
                }
                let _ = EmptyClipboard();
                let success = SetClipboardData(8, Some(HANDLE(h_mem.0))).is_ok();
                let _ = CloseClipboard();
                return success;
            }
        }
        false
    }
}

pub fn paste_to_window(window_handle: isize) {
    unsafe {
        let hwnd = HWND(window_handle as *mut std::ffi::c_void);
        if hwnd.0 != std::ptr::null_mut() {
            let _ = SetForegroundWindow(hwnd);
        }
        std::thread::sleep(std::time::Duration::from_millis(220));
        
        let mut inputs = [INPUT::default(); 4];
        
        inputs[0].r#type = INPUT_KEYBOARD;
        inputs[0].Anonymous.ki.wVk = VK_CONTROL;
        
        inputs[1].r#type = INPUT_KEYBOARD;
        inputs[1].Anonymous.ki.wVk = VK_V;
        
        inputs[2].r#type = INPUT_KEYBOARD;
        inputs[2].Anonymous.ki.wVk = VK_V;
        inputs[2].Anonymous.ki.dwFlags = KEYEVENTF_KEYUP;
        
        inputs[3].r#type = INPUT_KEYBOARD;
        inputs[3].Anonymous.ki.wVk = VK_CONTROL;
        inputs[3].Anonymous.ki.dwFlags = KEYEVENTF_KEYUP;
        
        SendInput(&inputs, std::mem::size_of::<INPUT>() as i32);
    }
}

unsafe fn slice_from_ptr_until_null(ptr: *const u16) -> &'static [u16] {
    let mut len = 0;
    while *ptr.add(len) != 0 {
        len += 1;
    }
    slice::from_raw_parts(ptr, len)
}
