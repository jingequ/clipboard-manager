import { useState, useEffect, useRef } from "react";
import { invoke } from "@tauri-apps/api/core";
import { listen } from "@tauri-apps/api/event";
import { getCurrentWindow } from "@tauri-apps/api/window";
import { 
  Search, 
  Settings, 
  Trash2, 
  FileText, 
  Image as ImageIcon, 
  File as FileIcon, 
  Folder as FolderIcon, 
  Clock, 
  Check, 
  X, 
  AlertCircle, 
  CornerDownLeft, 
  Terminal, 
  ExternalLink,
  Clipboard,
  ToggleLeft,
  ToggleRight,
  Sparkles,
  Info
} from "lucide-react";

// For loading local file assets in Tauri v2
import { convertFileSrc } from "@tauri-apps/api/core";

const appWindow = getCurrentWindow();

function App() {
  const [items, setItems] = useState([]);
  const [totalCount, setTotalCount] = useState(0);
  const [searchQuery, setSearchQuery] = useState("");
  const [selectedIndex, setSelectedIndex] = useState(0);
  const [statusMessage, setStatusMessage] = useState("Loading...");
  
  // Settings state
  const [showSettings, setShowSettings] = useState(false);
  const [settings, setSettings] = useState({
    LaunchAtStartup: true,
    CaptureImages: true,
    CaptureFiles: true,
    RetentionDays: 30,
    MaxItems: 500,
    HotkeyGesture: "Alt+C",
  });
  const [settingsSavedMsg, setSettingsSavedMsg] = useState("");

  // Rich Text Preview tab
  const [richTextTab, setRichTextTab] = useState("preview"); // "preview" | "source"

  const searchInputRef = useRef(null);
  const listContainerRef = useRef(null);

  // Command state
  const [clearCommand, setClearCommand] = useState(null);

  // Parse custom clear command
  const parseClearCommand = (query) => {
    if (!query) return null;
    const trimmed = query.trim();
    if (trimmed.toLowerCase() === "clear") {
      return { mode: "all", desc: "删除全部记录", count: 0, duration: 0 };
    }
    if (trimmed.toLowerCase().startsWith("clear ")) {
      const arg = trimmed.slice(6).trim();
      
      // Check for numeric count
      const count = parseInt(arg, 10);
      if (!isNaN(count) && String(count) === arg) {
        if (count > 0) {
          return { mode: "count", desc: `删除前 ${count} 条记录 (最新)`, count, duration: 0 };
        } else if (count < 0) {
          return { mode: "count", desc: `删除后 ${-count} 条记录 (最旧)`, count, duration: 0 };
        } else {
          return { mode: "count", desc: "不删除记录 (n=0)", count: 0, duration: 0 };
        }
      }

      // Check for time duration: e.g. 1d, 12h, 30m
      if (arg.toLowerCase().endsWith("d")) {
        const days = parseInt(arg.slice(0, -1), 10);
        if (!isNaN(days) && days > 0) {
          return { mode: "recent", desc: `删除近 ${days} 天的记录`, count: 0, duration: days * 1440 };
        }
      }
      if (arg.toLowerCase().endsWith("h")) {
        const hours = parseInt(arg.slice(0, -1), 10);
        if (!isNaN(hours) && hours > 0) {
          return { mode: "recent", desc: `删除近 ${hours} 小时的记录`, count: 0, duration: hours * 60 };
        }
      }
      if (arg.toLowerCase().endsWith("m")) {
        const minutes = parseInt(arg.slice(0, -1), 10);
        if (!isNaN(minutes) && minutes > 0) {
          return { mode: "recent", desc: `删除近 ${minutes} 分钟的记录`, count: 0, duration: minutes };
        }
      }

      return { mode: "invalid", desc: "无效清除命令。用法: clear 5, clear -5, clear 1d, clear 30m", count: 0, duration: 0 };
    }
    return null;
  };

  // Fetch settings from Rust backend
  const fetchSettings = async () => {
    try {
      const res = await invoke("get_settings");
      setSettings(res);
    } catch (err) {
      console.error("Failed to load settings:", err);
    }
  };

  // Fetch history from database
  const refreshHistory = async (queryStr = searchQuery) => {
    try {
      const cmd = parseClearCommand(queryStr);
      if (cmd) {
        setClearCommand(cmd);
        setItems([]);
        setSelectedIndex(0);
        setStatusMessage(cmd.desc);
        return;
      }
      setClearCommand(null);

      // Search limit matches standard Raycast/Alfred preview listing limit
      const results = await invoke("search_history", { query: queryStr, limit: 100 });
      setItems(results);
      
      const total = await invoke("get_total_count_cmd", { query: queryStr });
      setTotalCount(total);
      
      if (results.length > 0) {
        // Adjust selected index if it is out of range
        setSelectedIndex((prev) => (prev >= results.length ? results.length - 1 : prev));
        setStatusMessage(total === 0 ? "No clipboard items yet" : `${total} items`);
      } else {
        setSelectedIndex(0);
        setStatusMessage("No clipboard items yet");
      }
    } catch (err) {
      console.error("Failed to refresh history:", err);
      setStatusMessage("Failed to fetch history");
    }
  };

  // Handle saving settings
  const handleSaveSettings = async (newSettings) => {
    try {
      await invoke("save_settings", { settings: newSettings });
      setSettings(newSettings);
      setSettingsSavedMsg("设置保存成功！");
      setTimeout(() => setSettingsSavedMsg(""), 3000);
      refreshHistory();
    } catch (err) {
      setSettingsSavedMsg(`保存失败: ${err}`);
    }
  };

  // Execute copy paste
  const handleReplay = async (item) => {
    if (!item) return;
    try {
      await invoke("replay_and_paste", { id: item.id });
    } catch (err) {
      console.error("Replay failed:", err);
    }
  };

  // Delete individual item
  const handleDeleteItem = async (e, item) => {
    e.stopPropagation();
    try {
      await invoke("delete_history_item", { id: item.id });
      await refreshHistory();
    } catch (err) {
      console.error("Delete failed:", err);
    }
  };

  // Execute parsed clear command
  const handleExecuteClear = async () => {
    if (!clearCommand || clearCommand.mode === "invalid") return;
    try {
      if (clearCommand.mode === "all") {
        await invoke("clear_history");
      } else {
        await invoke("execute_clear_command_cmd", {
          mode: clearCommand.mode,
          count: clearCommand.count,
          durationMinutes: clearCommand.duration
        });
      }
      setSearchQuery("");
      setClearCommand(null);
      await refreshHistory("");
    } catch (err) {
      console.error("Clear command execution failed:", err);
    }
  };

  // Setup event listeners
  useEffect(() => {
    fetchSettings();
    refreshHistory("");

    // Listen to backend events
    const setupListeners = async () => {
      const unlistenUpdated = await listen("clipboard-updated", () => {
        refreshHistory();
      });
      const unlistenSettings = await listen("open-settings", () => {
        setShowSettings(true);
      });
      const unlistenShown = await listen("window-shown", () => {
        if (searchInputRef.current) {
          searchInputRef.current.focus();
          searchInputRef.current.select();
        }
        refreshHistory();
      });

      return () => {
        unlistenUpdated();
        unlistenSettings();
        unlistenShown();
      };
    };

    const cleanupPromise = setupListeners();

    // Keydown listener for global window actions
    const handleGlobalKeyDown = (e) => {
      if (e.key === "Escape") {
        if (showSettings) {
          setShowSettings(false);
        } else if (searchQuery) {
          setSearchQuery("");
          refreshHistory("");
        } else {
          appWindow.hide();
        }
      }
    };
    window.addEventListener("keydown", handleGlobalKeyDown);

    return () => {
      window.removeEventListener("keydown", handleGlobalKeyDown);
      cleanupPromise.then(cleanup => cleanup && cleanup());
    };
  }, [showSettings, searchQuery]);

  // Handle search text changes
  const handleSearchChange = (e) => {
    const val = e.target.value;
    setSearchQuery(val);
    refreshHistory(val);
  };

  // Navigate items using keyboard
  const handleKeyDown = (e) => {
    if (clearCommand) {
      if (e.key === "Enter") {
        e.preventDefault();
        handleExecuteClear();
      }
      return;
    }

    if (items.length === 0) return;

    if (e.key === "ArrowDown") {
      e.preventDefault();
      setSelectedIndex((prev) => {
        const next = prev + 1 >= items.length ? 0 : prev + 1;
        // Scroll into view
        const itemEl = document.getElementById(`item-${next}`);
        if (itemEl) itemEl.scrollIntoView({ block: "nearest" });
        return next;
      });
    } else if (e.key === "ArrowUp") {
      e.preventDefault();
      setSelectedIndex((prev) => {
        const next = prev - 1 < 0 ? items.length - 1 : prev - 1;
        // Scroll into view
        const itemEl = document.getElementById(`item-${next}`);
        if (itemEl) itemEl.scrollIntoView({ block: "nearest" });
        return next;
      });
    } else if (e.key === "Enter") {
      e.preventDefault();
      handleReplay(items[selectedIndex]);
    } else if (e.key === "Delete" || (e.key === "d" && e.ctrlKey)) {
      e.preventDefault();
      handleDeleteItem(e, items[selectedIndex]);
    }
  };

  // Format relative timestamps
  const formatTime = (isoString) => {
    try {
      const date = new Date(isoString);
      const now = new Date();
      const diffMs = now - date;
      const diffMins = Math.floor(diffMs / 60000);
      const diffHours = Math.floor(diffMins / 60);

      if (diffMins < 1) return "Just now";
      if (diffMins < 60) return `${diffMins}m ago`;
      if (diffHours < 24) return `${diffHours}h ago`;
      
      return date.toLocaleDateString(undefined, {
        month: "short",
        day: "numeric",
        hour: "2-digit",
        minute: "2-digit"
      });
    } catch {
      return "";
    }
  };

  const selectedItem = items[selectedIndex];

  // Helper to render file icon based on ext
  const getFileIcon = (isDir, ext) => {
    if (isDir) return <FolderIcon className="w-4 h-4 text-amber-500" />;
    const textExts = ["txt", "md", "json", "xml", "csv", "ini", "log"];
    if (textExts.includes(ext.toLowerCase())) return <FileText className="w-4 h-4 text-blue-500" />;
    const imgExts = ["png", "jpg", "jpeg", "gif", "bmp", "ico", "svg"];
    if (imgExts.includes(ext.toLowerCase())) return <ImageIcon className="w-4 h-4 text-green-500" />;
    return <FileIcon className="w-4 h-4 text-zinc-500" />;
  };

  return (
    <div className="w-full h-full p-4 box-border relative select-none">
      {/* Outer shadow wrapper */}
      <div className="w-full h-full bg-white/95 border border-zinc-200 shadow-2xl rounded-2xl flex flex-col overflow-hidden relative">
        
        {/* Search Header */}
        <div className="h-16 border-b border-zinc-200/80 flex items-center px-4 gap-3 bg-zinc-50/50">
          <Search className="w-5 h-5 text-zinc-400" />
          <input
            ref={searchInputRef}
            type="text"
            className="flex-1 bg-transparent border-0 outline-none text-zinc-900 placeholder-zinc-400 text-lg"
            placeholder="Search clipboard history... (e.g. type 'clear 5' to clean)"
            value={searchQuery}
            onChange={handleSearchChange}
            onKeyDown={handleKeyDown}
            autoFocus
          />
          {searchQuery && (
            <button 
              onClick={() => { setSearchQuery(""); refreshHistory(""); }}
              className="p-1 hover:bg-zinc-200 rounded text-zinc-400 hover:text-zinc-600 transition"
            >
              <X className="w-4 h-4" />
            </button>
          )}
          <button 
            onClick={() => setShowSettings(true)}
            className="p-2 hover:bg-zinc-200 rounded-lg text-zinc-500 hover:text-zinc-700 transition"
            title="Settings"
          >
            <Settings className="w-5 h-5" />
          </button>
        </div>

        {/* Command Executing overlay when clear command parsed */}
        {clearCommand ? (
          <div className="flex-1 flex flex-col items-center justify-center bg-zinc-50/30 p-8">
            <div className="max-w-md w-full bg-white border border-zinc-200/80 rounded-2xl shadow-lg p-6 flex flex-col items-center text-center">
              <div className={`p-4 rounded-full ${clearCommand.mode === "invalid" ? "bg-red-50 text-red-600" : "bg-indigo-50 text-indigo-600"} mb-4`}>
                {clearCommand.mode === "invalid" ? (
                  <AlertCircle className="w-8 h-8" />
                ) : (
                  <Terminal className="w-8 h-8" />
                )}
              </div>
              <h3 className="text-lg font-bold text-zinc-950 mb-1">执行清理命令</h3>
              <p className="text-zinc-500 text-sm mb-4">
                键入了快捷清理命令。请确认您的操作。
              </p>
              
              <div className={`w-full py-3 px-4 rounded-xl border text-sm font-semibold mb-6 flex items-center justify-center gap-2 ${
                clearCommand.mode === "invalid" 
                  ? "bg-red-50/50 border-red-200 text-red-700" 
                  : "bg-indigo-50/50 border-indigo-200 text-indigo-700"
              }`}>
                {clearCommand.desc}
              </div>

              {clearCommand.mode !== "invalid" ? (
                <div className="flex w-full gap-3">
                  <button 
                    onClick={() => { setSearchQuery(""); refreshHistory(""); }}
                    className="flex-1 py-2 px-4 rounded-xl border border-zinc-200 hover:bg-zinc-50 text-zinc-700 font-medium transition text-sm"
                  >
                    取消 (ESC)
                  </button>
                  <button 
                    onClick={handleExecuteClear}
                    className="flex-1 py-2 px-4 rounded-xl bg-indigo-600 hover:bg-indigo-700 text-white font-medium transition text-sm flex items-center justify-center gap-1"
                  >
                    确认执行 <CornerDownLeft className="w-3.5 h-3.5" />
                  </button>
                </div>
              ) : (
                <button 
                  onClick={() => { setSearchQuery(""); refreshHistory(""); }}
                  className="w-full py-2 px-4 rounded-xl bg-zinc-100 hover:bg-zinc-200 text-zinc-700 font-medium transition text-sm"
                >
                  清空输入框
                </button>
              )}
            </div>
          </div>
        ) : (
          /* Main Workspace Panels */
          <div className="flex-1 flex overflow-hidden">
            
            {/* Left History List (width: 380px) */}
            <div className="w-[380px] border-r border-zinc-200/80 bg-zinc-50/40 flex flex-col">
              <div className="flex-1 overflow-y-auto p-2 space-y-1">
                {items.length === 0 ? (
                  <div className="h-full flex flex-col items-center justify-center text-center p-6 text-zinc-400 gap-2">
                    <Clipboard className="w-10 h-10 text-zinc-300 stroke-[1.5]" />
                    <p className="text-sm font-medium">No items found</p>
                    <p className="text-xs text-zinc-400">Copy text, HTML, images or files to get started.</p>
                  </div>
                ) : (
                  items.map((item, idx) => {
                    const isSelected = idx === selectedIndex;
                    return (
                      <div
                        id={`item-${idx}`}
                        key={item.id}
                        onClick={() => setSelectedIndex(idx)}
                        onDoubleClick={() => handleReplay(item)}
                        className={`group flex items-start gap-3 p-3 rounded-xl cursor-pointer transition-all border ${
                          isSelected 
                            ? "bg-indigo-600/[0.08] border-indigo-600/20 text-indigo-900" 
                            : "bg-transparent border-transparent hover:bg-zinc-200/50"
                        }`}
                      >
                        {/* Type Icon */}
                        <div className={`p-2 rounded-lg mt-0.5 ${
                          isSelected ? "bg-indigo-600/10 text-indigo-600" : "bg-zinc-200/60 text-zinc-600"
                        }`}>
                          {item.type === 0 && <span className="font-bold text-xs select-none block w-4 h-4 text-center leading-4">T</span>}
                          {item.type === 3 && <span className="font-bold text-xs select-none block w-4 h-4 text-center leading-4">RT</span>}
                          {item.type === 1 && <ImageIcon className="w-4 h-4" />}
                          {item.type === 2 && <FileIcon className="w-4 h-4" />}
                        </div>

                        {/* Summary Details */}
                        <div className="flex-1 min-w-0">
                          <p className={`text-sm font-medium truncate leading-tight ${
                            isSelected ? "text-indigo-950 font-semibold" : "text-zinc-950"
                          }`}>
                            {item.summary || "No Summary"}
                          </p>
                          <div className="flex items-center gap-2 mt-1">
                            <span className="text-[11px] text-zinc-400 font-medium">
                              {formatTime(item.createdAt)}
                            </span>
                            {item.displayMetadata && (
                              <>
                                <span className="text-[10px] text-zinc-300">•</span>
                                <span className="text-[11px] text-zinc-400 truncate max-w-[120px]">
                                  {item.displayMetadata}
                                </span>
                              </>
                            )}
                          </div>
                        </div>

                        {/* Action buttons */}
                        <button
                          onClick={(e) => handleDeleteItem(e, item)}
                          className="opacity-0 group-hover:opacity-100 p-1.5 hover:bg-zinc-200 rounded-lg text-zinc-400 hover:text-red-600 transition self-center"
                          title="Delete (Delete)"
                        >
                          <Trash2 className="w-4 h-4" />
                        </button>
                      </div>
                    );
                  })
                )}
              </div>
            </div>

            {/* Right Preview Panel */}
            <div className="flex-1 bg-white flex flex-col overflow-hidden">
              {selectedItem ? (
                <div className="flex-1 flex flex-col overflow-hidden">
                  
                  {/* Preview Header / Metadata */}
                  <div className="px-6 py-4 border-b border-zinc-200/60 flex items-center justify-between bg-zinc-50/10">
                    <div>
                      <h4 className="text-sm font-bold text-zinc-950 flex items-center gap-1.5">
                        {selectedItem.type === 0 && <span>Text Document</span>}
                        {selectedItem.type === 3 && <span>Rich Text Document</span>}
                        {selectedItem.type === 1 && <span>Captured Image</span>}
                        {selectedItem.type === 2 && <span>Files List</span>}
                      </h4>
                      <p className="text-xs text-zinc-400 mt-0.5">
                        Created {new Date(selectedItem.createdAt).toLocaleString()}
                      </p>
                    </div>

                    <button
                      onClick={() => handleReplay(selectedItem)}
                      className="py-1.5 px-3 bg-indigo-600 hover:bg-indigo-700 text-white rounded-lg text-xs font-semibold flex items-center gap-1 transition shadow-sm"
                    >
                      Paste Content <CornerDownLeft className="w-3 h-3" />
                    </button>
                  </div>

                  {/* Preview Body */}
                  <div className="flex-1 overflow-y-auto p-6">
                    
                    {/* Text Preview */}
                    {selectedItem.type === 0 && (
                      <div className="w-full h-full flex flex-col">
                        <pre className="flex-1 bg-zinc-50 p-4 border border-zinc-200/80 rounded-xl font-mono text-xs text-zinc-800 whitespace-pre-wrap overflow-y-auto select-text selection:bg-indigo-100">
                          {selectedItem.textContent}
                        </pre>
                      </div>
                    )}

                    {/* Rich Text Preview */}
                    {selectedItem.type === 3 && (
                      <div className="w-full h-full flex flex-col gap-3">
                        <div className="flex border-b border-zinc-200">
                          <button
                            onClick={() => setRichTextTab("preview")}
                            className={`py-2 px-4 border-b-2 font-medium text-xs transition ${
                              richTextTab === "preview" 
                                ? "border-indigo-600 text-indigo-600" 
                                : "border-transparent text-zinc-500 hover:text-zinc-700"
                            }`}
                          >
                            Rendered Preview
                          </button>
                          <button
                            onClick={() => setRichTextTab("source")}
                            className={`py-2 px-4 border-b-2 font-medium text-xs transition ${
                              richTextTab === "source" 
                                ? "border-indigo-600 text-indigo-600" 
                                : "border-transparent text-zinc-500 hover:text-zinc-700"
                            }`}
                          >
                            Source HTML
                          </button>
                        </div>
                        <div className="flex-1 border border-zinc-200 rounded-xl overflow-hidden bg-white min-h-[300px]">
                          {richTextTab === "preview" ? (
                            selectedItem.htmlContent ? (
                              <iframe
                                srcDoc={`<!DOCTYPE html><html><head><style>body { font-family: system-ui, -apple-system, sans-serif; margin: 16px; color: #18181b; background-color: #ffffff; line-height: 1.5; font-size: 14px; }</style></head><body>${selectedItem.htmlContent}</body></html>`}
                                className="w-full h-full border-none bg-white"
                                sandbox="allow-same-origin"
                              />
                            ) : (
                              <pre className="p-4 font-mono text-xs whitespace-pre-wrap bg-zinc-50 h-full text-zinc-600">
                                {selectedItem.textContent}
                              </pre>
                            )
                          ) : (
                            <pre className="p-4 font-mono text-xs text-zinc-800 whitespace-pre-wrap overflow-auto h-full bg-zinc-50 select-text">
                              {selectedItem.htmlContent || "No HTML source content available."}
                            </pre>
                          )}
                        </div>
                      </div>
                    )}

                    {/* Image Preview */}
                    {selectedItem.type === 1 && (
                      <div className="w-full h-full flex flex-col items-center justify-center bg-zinc-50/50 border border-zinc-200/80 rounded-xl p-4 min-h-[300px]">
                        {selectedItem.imagePath ? (
                          <div className="flex flex-col items-center gap-4 max-w-full">
                            <img
                              src={convertFileSrc(selectedItem.imagePath)}
                              alt="Captured clipboard data"
                              className="max-h-[360px] max-w-full object-contain rounded-lg shadow-md border border-zinc-200 bg-white"
                              draggable="false"
                            />
                            <div className="text-center">
                              <p className="text-sm font-semibold text-zinc-900">
                                Dimensions: {selectedItem.displayMetadata || "Unknown"}
                              </p>
                              <p className="text-xs text-zinc-400 mt-1 truncate max-w-md select-text font-mono">
                                Path: {selectedItem.imagePath}
                              </p>
                            </div>
                          </div>
                        ) : (
                          <div className="text-center text-zinc-400">
                            <ImageIcon className="w-12 h-12 mx-auto stroke-[1.5] text-zinc-300 mb-2" />
                            <p className="text-sm">Image cache file missing</p>
                          </div>
                        )}
                      </div>
                    )}

                    {/* File List Preview */}
                    {selectedItem.type === 2 && (
                      <div className="space-y-4">
                        <div className="flex items-center justify-between py-2 border-b border-zinc-100">
                          <span className="text-sm font-bold text-zinc-900">
                            {selectedItem.files?.length || 0} items in file clipboard
                          </span>
                        </div>
                        <div className="border border-zinc-200/80 rounded-xl overflow-hidden divide-y divide-zinc-100 bg-white shadow-sm max-h-[380px] overflow-y-auto">
                          {selectedItem.files?.map((file, fIdx) => (
                            <div key={fIdx} className="p-3 flex items-center gap-3 hover:bg-zinc-50/60 select-text">
                              <div className="p-2 bg-zinc-100 rounded-lg text-zinc-600">
                                {getFileIcon(file.isDirectory, file.extension)}
                              </div>
                              <div className="flex-1 min-w-0">
                                <p className="text-sm font-semibold text-zinc-900 truncate">
                                  {file.name}
                                </p>
                                <p className="text-xs text-zinc-400 truncate font-mono">
                                  {file.path}
                                </p>
                              </div>
                            </div>
                          ))}
                        </div>
                      </div>
                    )}

                  </div>
                </div>
              ) : (
                <div className="flex-1 flex flex-col items-center justify-center text-zinc-400 p-8 text-center gap-2">
                  <Sparkles className="w-12 h-12 text-zinc-200 stroke-[1.2]" />
                  <h3 className="text-sm font-bold text-zinc-800">Preview Selected Item</h3>
                  <p className="text-xs max-w-[280px]">
                    Select any clipboard entry from the list to view its formatted content details.
                  </p>
                </div>
              )}
            </div>

          </div>
        )}

        {/* Footer Bar */}
        <div className="h-10 border-t border-zinc-200 bg-zinc-50/80 flex items-center justify-between px-4 text-xs text-zinc-500">
          <div className="flex items-center gap-2 font-medium">
            <Info className="w-3.5 h-3.5 text-zinc-400" />
            <span>{statusMessage}</span>
          </div>

          <div className="flex items-center gap-4 text-[11px] font-medium text-zinc-400">
            <div className="flex items-center gap-1 bg-zinc-200/60 px-1.5 py-0.5 rounded text-[10px] text-zinc-600 font-mono">
              ESC
            </div>
            <span>Hide</span>
            
            <div className="flex items-center gap-1 bg-zinc-200/60 px-1.5 py-0.5 rounded text-[10px] text-zinc-600 font-mono font-bold">
              ↑↓
            </div>
            <span>Navigate</span>

            <div className="flex items-center gap-1 bg-zinc-200/60 px-1.5 py-0.5 rounded text-[10px] text-zinc-600 font-mono">
              Delete
            </div>
            <span>Delete</span>

            <div className="flex items-center gap-1 bg-zinc-200/60 px-1.5 py-0.5 rounded text-[10px] text-zinc-600 font-mono">
              Enter
            </div>
            <span>Paste</span>
          </div>
        </div>

        {/* Settings Modal (Overlay) */}
        {showSettings && (
          <div className="absolute inset-0 bg-zinc-950/20 backdrop-blur-sm z-50 flex items-center justify-center p-8 transition-all duration-200">
            <div className="bg-white border border-zinc-200 rounded-2xl shadow-2xl max-w-lg w-full overflow-hidden flex flex-col max-h-full animate-in fade-in zoom-in-95 duration-150">
              
              {/* Modal Header */}
              <div className="px-6 py-4 border-b border-zinc-200/80 flex items-center justify-between bg-zinc-50/50">
                <div className="flex items-center gap-2 font-bold text-zinc-950">
                  <Settings className="w-5 h-5 text-indigo-600" />
                  <span>Clipboard Manager Settings</span>
                </div>
                <button 
                  onClick={() => setShowSettings(false)}
                  className="p-1 hover:bg-zinc-200 rounded text-zinc-400 hover:text-zinc-600 transition"
                >
                  <X className="w-4 h-4" />
                </button>
              </div>

              {/* Modal Content */}
              <div className="flex-1 overflow-y-auto p-6 space-y-6">
                
                {/* Options List */}
                <div className="space-y-4">
                  <h4 className="text-xs font-bold text-zinc-400 uppercase tracking-wider">General Options</h4>
                  
                  {/* Launch At Startup */}
                  <div className="flex items-center justify-between p-3 bg-zinc-50 border border-zinc-200/50 rounded-xl">
                    <div>
                      <p className="text-sm font-semibold text-zinc-900">开机自启动</p>
                      <p className="text-xs text-zinc-500">在 Windows 登录时自动启动剪贴板管理器</p>
                    </div>
                    <button 
                      onClick={() => handleSaveSettings({ ...settings, LaunchAtStartup: !settings.LaunchAtStartup })}
                      className="text-indigo-600 hover:text-indigo-700 transition"
                    >
                      {settings.LaunchAtStartup ? (
                        <ToggleRight className="w-12 h-8" />
                      ) : (
                        <ToggleLeft className="w-12 h-8 text-zinc-300" />
                      )}
                    </button>
                  </div>

                  {/* Capture Images */}
                  <div className="flex items-center justify-between p-3 bg-zinc-50 border border-zinc-200/50 rounded-xl">
                    <div>
                      <p className="text-sm font-semibold text-zinc-900">收集剪贴板图片</p>
                      <p className="text-xs text-zinc-500">允许监视并自动收集剪切的图像文件</p>
                    </div>
                    <button 
                      onClick={() => handleSaveSettings({ ...settings, CaptureImages: !settings.CaptureImages })}
                      className="text-indigo-600 hover:text-indigo-700 transition"
                    >
                      {settings.CaptureImages ? (
                        <ToggleRight className="w-12 h-8" />
                      ) : (
                        <ToggleLeft className="w-12 h-8 text-zinc-300" />
                      )}
                    </button>
                  </div>

                  {/* Capture Files */}
                  <div className="flex items-center justify-between p-3 bg-zinc-50 border border-zinc-200/50 rounded-xl">
                    <div>
                      <p className="text-sm font-semibold text-zinc-900">收集剪贴板文件</p>
                      <p className="text-xs text-zinc-500">跟踪并存储剪贴的文件列表记录</p>
                    </div>
                    <button 
                      onClick={() => handleSaveSettings({ ...settings, CaptureFiles: !settings.CaptureFiles })}
                      className="text-indigo-600 hover:text-indigo-700 transition"
                    >
                      {settings.CaptureFiles ? (
                        <ToggleRight className="w-12 h-8" />
                      ) : (
                        <ToggleLeft className="w-12 h-8 text-zinc-300" />
                      )}
                    </button>
                  </div>
                </div>

                {/* Numeric values */}
                <div className="space-y-4">
                  <h4 className="text-xs font-bold text-zinc-400 uppercase tracking-wider">Limits & Cleanups</h4>

                  <div className="grid grid-cols-2 gap-4">
                    {/* Retention Days */}
                    <div className="flex flex-col gap-1.5">
                      <label className="text-xs font-semibold text-zinc-700">保留天数 (0=永久)</label>
                      <input 
                        type="number"
                        min="0"
                        className="bg-zinc-50 border border-zinc-200 rounded-xl py-2 px-3 text-sm text-zinc-900 outline-none focus:border-indigo-600 transition"
                        value={settings.RetentionDays}
                        onChange={(e) => setSettings({ ...settings, RetentionDays: parseInt(e.target.value, 10) || 0 })}
                        onBlur={() => handleSaveSettings(settings)}
                      />
                    </div>

                    {/* Max Items */}
                    <div className="flex flex-col gap-1.5">
                      <label className="text-xs font-semibold text-zinc-700">最大保留记录条数</label>
                      <input 
                        type="number"
                        min="1"
                        className="bg-zinc-50 border border-zinc-200 rounded-xl py-2 px-3 text-sm text-zinc-900 outline-none focus:border-indigo-600 transition"
                        value={settings.MaxItems}
                        onChange={(e) => setSettings({ ...settings, MaxItems: parseInt(e.target.value, 10) || 1 })}
                        onBlur={() => handleSaveSettings(settings)}
                      />
                    </div>
                  </div>

                  {/* Hotkey configuration */}
                  <div className="flex flex-col gap-1.5">
                    <label className="text-xs font-semibold text-zinc-700">全局热键 (按回车保存)</label>
                    <input 
                      type="text"
                      className="bg-zinc-50 border border-zinc-200 rounded-xl py-2 px-3 text-sm text-zinc-900 font-mono outline-none focus:border-indigo-600 transition"
                      placeholder="e.g. Alt+C, Ctrl+Shift+H"
                      value={settings.HotkeyGesture}
                      onChange={(e) => setSettings({ ...settings, HotkeyGesture: e.target.value })}
                      onKeyDown={(e) => {
                        if (e.key === "Enter") {
                          e.preventDefault();
                          handleSaveSettings(settings);
                        }
                      }}
                      onBlur={() => handleSaveSettings(settings)}
                    />
                    <p className="text-[10px] text-zinc-400">
                      支持修饰键 Alt, Ctrl, Shift, Win 和 A-Z 键。组合如 Alt+C。
                    </p>
                  </div>
                </div>

              </div>

              {/* Modal Footer */}
              <div className="px-6 py-4 border-t border-zinc-200 bg-zinc-50/50 flex items-center justify-between">
                <span className="text-xs text-emerald-600 font-semibold">{settingsSavedMsg}</span>
                <button
                  onClick={() => setShowSettings(false)}
                  className="py-1.5 px-4 bg-indigo-600 hover:bg-indigo-700 text-white rounded-lg text-xs font-bold transition shadow-sm"
                >
                  完成设置
                </button>
              </div>

            </div>
          </div>
        )}

      </div>
    </div>
  );
}

export default App;
