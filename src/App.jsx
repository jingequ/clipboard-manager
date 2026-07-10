import { useState, useEffect, useRef, useCallback, useMemo } from "react";
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

// Clean raw Windows CF_HTML headers and extract actual HTML content/fragment
const cleanHtmlContent = (htmlStr) => {
  if (!htmlStr) return "";
  
  // 1. Try to extract content between <!--StartFragment--> and <!--EndFragment-->
  const fragmentMatch = htmlStr.match(/<!--\s*StartFragment\s*-->([\s\S]*?)<!--\s*EndFragment\s*-->/i);
  if (fragmentMatch) {
    return fragmentMatch[1].trim();
  }
  
  // 2. If no fragment comments, but it starts with Version: header, strip the header
  if (/^Version:\d+\.\d+/i.test(htmlStr)) {
    // Find the first occurrence of '<' which starts the HTML
    const htmlStart = htmlStr.indexOf("<");
    if (htmlStart !== -1) {
      return htmlStr.substring(htmlStart).trim();
    }
    
    // Fallback: strip lines that look like headers
    const lines = htmlStr.split(/\r?\n/);
    const cleanLines = [];
    let inHeader = true;
    for (const line of lines) {
      if (inHeader) {
        if (/^(Version|StartHTML|EndHTML|StartFragment|EndFragment|SourceURL):/i.test(line)) {
          continue;
        }
        inHeader = false;
      }
      cleanLines.push(line);
    }
    return cleanLines.join("\n").trim();
  }
  
  return htmlStr.trim();
};

const truncateText = (text) => {
  if (!text) return "";
  if (text.length <= 2000) return text;
  return text.slice(0, 2000) + `\n\n... [Content truncated, total length: ${text.length} characters] ...`;
};

// Helper to render file icon based on ext
const getFileIcon = (isDir, ext) => {
  if (isDir) return <FolderIcon className="w-4 h-4 text-amber-500" />;
  const textExts = ["txt", "md", "json", "xml", "csv", "ini", "log"];
  if (textExts.includes(ext.toLowerCase())) return <FileText className="w-4 h-4 text-blue-500" />;
  const imgExts = ["png", "jpg", "jpeg", "gif", "bmp", "ico", "svg"];
  if (imgExts.includes(ext.toLowerCase())) return <ImageIcon className="w-4 h-4 text-green-500" />;
  return <FileIcon className="w-4 h-4 text-zinc-500" />;
};

function App() {
  console.log("[JS LOG] App component function execution started");
  const [items, setItems] = useState([]);
  const [totalCount, setTotalCount] = useState(0);
  const [searchQuery, setSearchQuery] = useState("");
  const searchQueryRef = useRef("");

  useEffect(() => {
    console.log("[JS LOG] App component mounted");
    searchQueryRef.current = searchQuery;
  }, [searchQuery]);
  const [selectedIndex, setSelectedIndex] = useState(0);
  const [selectedItemDetails, setSelectedItemDetails] = useState(null);
  const [detailsLoadingId, setDetailsLoadingId] = useState(null);
  const [statusMessage, setStatusMessage] = useState("Loading...");
  const [showHtmlSource, setShowHtmlSource] = useState(false);
  
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



  const searchInputRef = useRef(null);
  const searchTimeoutRef = useRef(null);
  const listContainerRef = useRef(null);
  const detailsCacheRef = useRef({});

  // Command state
  const [clearCommand, setClearCommand] = useState(null);

  // Parse custom clear command
  const parseClearCommand = (query) => {
    if (!query) return null;
    const trimmed = query.trim().toLowerCase();
    
    // Check if it starts with "clear" or "clean"
    const isClear = trimmed.startsWith("clear");
    const isClean = trimmed.startsWith("clean");
    if (!isClear && !isClean) return null;
    
    // Determine the keyword length (both "clear" and "clean" are 5 chars)
    const keywordLen = 5;
    
    // Get the remainder of the string
    const remainder = trimmed.slice(keywordLen).trim();
    
    // If no remainder, it means the command is exactly "clear" or "clean"
    if (remainder === "") {
      return { mode: "all", desc: "删除全部记录", count: 0, duration: 0 };
    }
    
    // Parse numeric count
    const count = parseInt(remainder, 10);
    if (!isNaN(count) && String(count) === remainder) {
      if (count > 0) {
        return { mode: "count", desc: `删除前 ${count} 条记录 (最新)`, count, duration: 0 };
      } else if (count < 0) {
        return { mode: "count", desc: `删除后 ${-count} 条记录 (最旧)`, count, duration: 0 };
      } else {
        return { mode: "count", desc: "不删除记录 (n=0)", count: 0, duration: 0 };
      }
    }

    // Parse time duration: e.g. 1d, 12h, 30m
    if (remainder.endsWith("d")) {
      const days = parseInt(remainder.slice(0, -1).trim(), 10);
      if (!isNaN(days) && days > 0) {
        return { mode: "recent", desc: `删除近 ${days} 天的记录`, count: 0, duration: days * 1440 };
      }
    }
    if (remainder.endsWith("h")) {
      const hours = parseInt(remainder.slice(0, -1).trim(), 10);
      if (!isNaN(hours) && hours > 0) {
        return { mode: "recent", desc: `删除近 ${hours} 小时的记录`, count: 0, duration: hours * 60 };
      }
    }
    if (remainder.endsWith("m")) {
      const minutes = parseInt(remainder.slice(0, -1).trim(), 10);
      if (!isNaN(minutes) && minutes > 0) {
        return { mode: "recent", desc: `删除近 ${minutes} 分钟的记录`, count: 0, duration: minutes };
      }
    }

    return { mode: "invalid", desc: "无效清除命令。用法: clear 5, clear -5, clear 1d, clear 30m", count: 0, duration: 0 };
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
  const refreshHistory = async (queryStr = searchQueryRef.current, preserveIndex = false) => {
    try {
      // Run both SQLite search and count queries in parallel
      const [results, total] = await Promise.all([
        invoke("search_history", { query: queryStr, limit: 999999 }),
        invoke("get_total_count_cmd", { query: queryStr })
      ]);

      setItems(results);
      setTotalCount(total);
      
      if (preserveIndex) {
        setSelectedIndex((prev) => {
          if (results.length === 0) return 0;
          return prev >= results.length ? results.length - 1 : prev;
        });
      } else {
        setSelectedIndex(0);
      }
      setStatusMessage(total === 0 ? "No clipboard items yet" : `${total} items`);
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
  const handleReplay = useCallback(async (item) => {
    if (!item) return;
    try {
      await invoke("replay_and_paste", { id: item.id });
    } catch (err) {
      console.error("Replay failed:", err);
    }
  }, []);

  // Delete individual item
  const handleDeleteItem = async (e, item) => {
    if (e && e.stopPropagation) {
      e.stopPropagation();
    }
    if (!item) return;
    try {
      if (detailsCacheRef.current[item.id]) {
        delete detailsCacheRef.current[item.id];
      }
      await invoke("delete_history_item", { id: item.id });
      await refreshHistory(searchQueryRef.current, true);
    } catch (err) {
      console.error("Delete failed:", err);
    }
  };

  // Execute parsed clear command directly (no confirmation)
  const handleExecuteClear = async (cmd) => {
    if (!cmd || cmd.mode === "invalid") return;
    try {
      detailsCacheRef.current = {};
      if (cmd.mode === "all") {
        await invoke("clear_history");
      } else {
        await invoke("execute_clear_command_cmd", {
          mode: cmd.mode,
          count: cmd.count,
          durationMinutes: cmd.duration
        });
      }
      if (searchInputRef.current) {
        searchInputRef.current.value = "";
      }
      setSearchQuery("");
      setClearCommand(null);
      await refreshHistory("");
    } catch (err) {
      console.error("Clear command execution failed:", err);
    }
  };

  // Tauri event listeners - run once
  useEffect(() => {
    fetchSettings();
    refreshHistory("");

    let unlistenUpdated;
    let unlistenSettings;
    let unlistenShown;

    const setupListeners = async () => {
      unlistenUpdated = await listen("clipboard-updated", () => {
        refreshHistory();
      });
      unlistenSettings = await listen("open-settings", () => {
        setShowSettings(true);
      });
      unlistenShown = await listen("window-shown", () => {
        if (searchInputRef.current) {
          searchInputRef.current.focus();
          searchInputRef.current.select();
        }
        refreshHistory();
      });
    };

    setupListeners();

    return () => {
      if (unlistenUpdated) unlistenUpdated();
      if (unlistenSettings) unlistenSettings();
      if (unlistenShown) unlistenShown();
    };
  }, []);

  // Global keydown listener
  useEffect(() => {
    const handleGlobalKeyDown = (e) => {
      if (e.key === "Escape" || e.key === "Esc") {
        console.log("[JS LOG] ESC pressed in handleGlobalKeyDown. showSettings:", showSettings, "clearCommand:", !!clearCommand);
        if (showSettings) {
          setShowSettings(false);
        } else if (clearCommand) {
          setClearCommand(null);
          if (searchInputRef.current) {
            searchInputRef.current.value = "";
          }
          setSearchQuery("");
          refreshHistory("");
        } else {
          if (searchInputRef.current) {
            searchInputRef.current.value = "";
          }
          setSearchQuery("");
          refreshHistory("");
          console.log("[JS LOG] ESC pressed: calling hide_window command");
          invoke("hide_window").catch((err) => console.error("[JS LOG] Failed to hide window:", err));
        }
        return;
      }

      // Ignore other keys if settings modal is open
      if (showSettings) {
        return;
      }

      if (clearCommand) {
        if (e.key === "Enter") {
          e.preventDefault();
          handleExecuteClear(clearCommand);
        }
        return;
      }

      // Execute clear command directly on Enter (no confirmation)
      if (e.key === "Enter") {
        const inputVal = searchInputRef.current?.value || "";
        const cmd = parseClearCommand(inputVal);
        if (cmd) {
          e.preventDefault();
          if (cmd.mode !== "invalid") {
            handleExecuteClear(cmd);
          }
          return;
        }
      }

      if (items.length === 0) return;

      if (e.key === "ArrowDown") {
        e.preventDefault();
        setSelectedIndex((prev) => {
          const next = prev + 1 >= items.length ? 0 : prev + 1;
          const itemEl = document.getElementById(`item-${next}`);
          if (itemEl) itemEl.scrollIntoView({ block: "nearest" });
          return next;
        });
      } else if (e.key === "ArrowUp") {
        e.preventDefault();
        setSelectedIndex((prev) => {
          const next = prev - 1 < 0 ? items.length - 1 : prev - 1;
          const itemEl = document.getElementById(`item-${next}`);
          if (itemEl) itemEl.scrollIntoView({ block: "nearest" });
          return next;
        });
      } else if (e.key === "Enter") {
        e.preventDefault();
        handleReplay(items[selectedIndex]);
      } else if (e.key === "Delete" || e.key === "Del" || (e.key === "d" && e.ctrlKey)) {
        e.preventDefault();
        handleDeleteItem(e, items[selectedIndex]);
      }
    };
    window.addEventListener("keydown", handleGlobalKeyDown);
    return () => {
      window.removeEventListener("keydown", handleGlobalKeyDown);
    };
  }, [showSettings, clearCommand, items, selectedIndex, handleReplay]);

  // Handle search text changes
  const handleSearchChange = (e) => {
    const val = e.target.value;
    
    // Clear previous timeout to debounce the query
    if (searchTimeoutRef.current) {
      clearTimeout(searchTimeoutRef.current);
    }
    
    // Set 120ms debounce
    searchTimeoutRef.current = setTimeout(() => {
      setSearchQuery(val);
      refreshHistory(val);
    }, 120);
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

  // Fetch details of selected item asynchronously (debounced)
  useEffect(() => {
    setShowHtmlSource(false);
    const selectedItem = items[selectedIndex];
    if (!selectedItem) {
      setSelectedItemDetails(null);
      setDetailsLoadingId(null);
      return;
    }

    const itemId = selectedItem.id;
    // Check cache first — show cached content immediately, no loading state
    if (detailsCacheRef.current[itemId]) {
      setSelectedItemDetails(detailsCacheRef.current[itemId]);
      setDetailsLoadingId(null);
      return;
    }

    // Clear previous details immediately to avoid showing stale content
    // but do NOT set loading yet (wait for debounce)
    let isCurrent = true;
    
    // Short debounce before starting the fetch
    const timer = setTimeout(() => {
      if (!isCurrent) return;
      
      // Only show loading for THIS specific item
      setDetailsLoadingId(itemId);
      invoke("get_item_details", { id: itemId })
        .then((details) => {
          if (isCurrent) {
            if (details) {
              detailsCacheRef.current[itemId] = details;
            }
            setSelectedItemDetails(details);
            setDetailsLoadingId(null);
          }
        })
        .catch((err) => {
          console.error("Failed to fetch item details:", err);
          if (isCurrent) {
            setSelectedItemDetails(null);
            setDetailsLoadingId(null);
          }
        });
    }, 80); // 80ms debounce — fast enough to feel instant for cached items

    return () => {
      isCurrent = false;
      clearTimeout(timer);
      // When cursor moves away, clear loading state for old item
      setDetailsLoadingId((prev) => prev === itemId ? null : prev);
    };
  }, [selectedIndex, items]);

  const previewPanel = useMemo(() => {
    if (detailsLoadingId && detailsLoadingId === selectedItem?.id && !selectedItemDetails) {
      return (
        <div className="flex-1 flex flex-col items-center justify-center text-zinc-400 p-8 text-center gap-2">
          <Clock className="w-12 h-12 text-zinc-200 animate-spin" />
          <h3 className="text-sm font-bold text-zinc-800">Loading details...</h3>
        </div>
      );
    }
    
    if (selectedItemDetails && selectedItemDetails.id === selectedItem?.id) {
      return (
        <div className="flex-1 flex flex-col min-h-0 p-6">
          {/* Text Preview */}
          {selectedItemDetails.type === 1 && (
            <div className="w-full h-full flex flex-col min-h-0">
              <pre className="flex-1 bg-zinc-50/50 p-4 rounded-xl font-mono text-xs text-zinc-800 whitespace-pre-wrap overflow-y-auto select-text selection:bg-indigo-100 border border-zinc-200/60">
                {truncateText(selectedItemDetails.textContent)}
              </pre>
            </div>
          )}

          {/* Rich Text Preview */}
          {selectedItemDetails.type === 4 && (
            <div className="w-full h-full flex flex-col min-h-0 bg-white">
              {selectedItemDetails.htmlContent ? (
                <div className="flex flex-col h-full min-h-0">
                  {/* Toolbar for switching between Preview & Source */}
                  <div className="flex items-center justify-between border-b border-zinc-100 pb-3 mb-4 select-none">
                    <div className="flex items-center gap-1.5 text-xs font-semibold text-zinc-500">
                      <Sparkles className="w-3.5 h-3.5 text-indigo-500" />
                      <span>Rich Text (富文本)</span>
                    </div>
                    <div className="flex bg-zinc-100 p-0.5 rounded-lg border border-zinc-200/50">
                      <button
                        onClick={() => setShowHtmlSource(false)}
                        className={`px-3 py-1 text-xs font-medium rounded-md transition-all ${
                          !showHtmlSource
                            ? "bg-white text-zinc-950 shadow-sm"
                            : "text-zinc-600 hover:text-zinc-950"
                        }`}
                      >
                        预览 (Preview)
                      </button>
                      <button
                        onClick={() => setShowHtmlSource(true)}
                        className={`px-3 py-1 text-xs font-medium rounded-md transition-all ${
                          showHtmlSource
                            ? "bg-white text-zinc-950 shadow-sm"
                            : "text-zinc-600 hover:text-zinc-950"
                        }`}
                      >
                        源码 (Source)
                      </button>
                    </div>
                  </div>

                  {/* Content area */}
                  <div className="flex-1 min-h-0 bg-white">
                    {showHtmlSource ? (
                      <pre className="w-full h-full bg-zinc-50 p-4 rounded-xl font-mono text-xs text-zinc-800 whitespace-pre-wrap overflow-y-auto select-text selection:bg-indigo-100 border border-zinc-200/60">
                        {selectedItemDetails.htmlContent}
                      </pre>
                    ) : (
                      <iframe
                        srcDoc={`<!DOCTYPE html><html><head><style>body { font-family: system-ui, -apple-system, sans-serif; margin: 16px; color: #18181b; background-color: #ffffff; line-height: 1.5; font-size: 14px; }</style></head><body>${cleanHtmlContent(selectedItemDetails.htmlContent)}</body></html>`}
                        className="w-full h-full border border-zinc-200/60 rounded-xl bg-white"
                        sandbox="allow-same-origin"
                      />
                    )}
                  </div>
                </div>
              ) : (
                <div className="w-full h-full flex flex-col min-h-0">
                  <pre className="flex-1 p-4 font-mono text-xs whitespace-pre-wrap bg-zinc-50 text-zinc-600 rounded-xl overflow-y-auto">
                    {truncateText(selectedItemDetails.textContent)}
                  </pre>
                </div>
              )}
            </div>
          )}

          {/* Image Preview */}
          {selectedItemDetails.type === 2 && (
            <div className="w-full h-full flex flex-col items-center justify-center p-4 min-h-[300px]">
              {selectedItemDetails.imagePath ? (
                <div className="flex flex-col items-center gap-4 max-w-full">
                  <img
                    src={convertFileSrc(selectedItemDetails.imagePath)}
                    alt="Captured clipboard data"
                    className="max-h-[360px] max-w-full object-contain rounded-lg shadow-md border border-zinc-200 bg-white"
                    draggable="false"
                  />
                  <div className="text-center">
                    <p className="text-sm font-semibold text-zinc-950">
                      Dimensions: {selectedItemDetails.displayMetadata || "Unknown"}
                    </p>
                    <p className="text-xs text-zinc-400 mt-1 truncate max-w-md select-text font-mono">
                      Path: {selectedItemDetails.imagePath}
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
          {selectedItemDetails.type === 3 && (
            <div className="w-full h-full flex flex-col min-h-0">
              <div className="flex-1 overflow-hidden divide-y divide-zinc-100 bg-white overflow-y-auto border border-zinc-200/60 rounded-xl">
                {selectedItemDetails.files?.map((file, fIdx) => (
                  <div key={fIdx} className="p-3 flex items-center gap-3 hover:bg-zinc-50/60 select-text">
                    <div className="p-2 bg-zinc-100 rounded-lg text-zinc-600">
                      {getFileIcon(file.isDirectory, file.extension)}
                    </div>
                    <div className="flex-1 min-w-0">
                      <p className="text-sm font-semibold text-zinc-900 truncate">
                        {file.name}
                      </p>
                      <p className="text-xs text-zinc-400 break-all font-mono">
                        {file.path}
                      </p>
                    </div>
                  </div>
                ))}
              </div>
            </div>
          )}
        </div>
      );
    }
    
    return (
      <div className="flex-1 flex flex-col items-center justify-center text-zinc-400 p-8 text-center gap-2">
        <Sparkles className="w-12 h-12 text-zinc-200 stroke-[1.2]" />
        <h3 className="text-sm font-bold text-zinc-800">Preview Selected Item</h3>
        <p className="text-xs max-w-[280px]">
          Select any clipboard entry from the list to view its formatted content details.
        </p>
      </div>
    );
  }, [selectedItemDetails, detailsLoadingId, selectedItem?.id, showHtmlSource]);



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
            defaultValue={searchQuery}
            onChange={handleSearchChange}
            autoFocus
          />
          {searchQuery && (
            <button 
              onClick={() => {
                if (searchTimeoutRef.current) {
                  clearTimeout(searchTimeoutRef.current);
                }
                if (searchInputRef.current) {
                  searchInputRef.current.value = "";
                }
                setSearchQuery("");
                refreshHistory("");
              }}
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
                        onMouseEnter={() => setSelectedIndex(idx)}
                        onClick={() => handleReplay(item)}
                        className={`group flex items-start gap-2.5 py-1.5 px-2 rounded-lg cursor-pointer transition-all border ${
                          isSelected 
                            ? "bg-indigo-600/[0.08] border-indigo-600/20 text-indigo-900" 
                            : "bg-transparent border-transparent hover:bg-zinc-200/50"
                        }`}
                      >
                        {/* Type Icon */}
                        <div className={`w-7 h-7 flex-shrink-0 flex items-center justify-center rounded-lg mt-0.5 ${
                          isSelected ? "bg-indigo-600/10 text-indigo-600" : "bg-zinc-200/60 text-zinc-600"
                        }`}>
                          {item.type === 1 && <span className="font-bold text-[10px] select-none text-center">T</span>}
                          {item.type === 4 && <span className="font-bold text-[10px] select-none text-center">RT</span>}
                          {item.type === 2 && <ImageIcon className="w-3.5 h-3.5" />}
                          {item.type === 3 && <FileIcon className="w-3.5 h-3.5" />}
                        </div>

                        {/* Summary Details */}
                        <div className="flex-1 min-w-0">
                          <p className={`text-sm font-medium truncate leading-tight ${
                            isSelected ? "text-indigo-950 font-semibold" : "text-zinc-950"
                          }`}>
                            {item.summary || "No Summary"}
                          </p>
                          {item.displayMetadata && (
                            <span className="text-[11px] text-zinc-400 truncate max-w-[150px] block mt-0.5">
                              {item.displayMetadata}
                            </span>
                          )}
                        </div>

                        {/* Right side info & actions */}
                        <div className="flex items-center gap-1.5 flex-shrink-0 self-center">
                          <span className="text-[11px] text-zinc-400 font-medium">
                            {formatTime(item.createdAt)}
                          </span>
                          <button
                            onClick={(e) => handleDeleteItem(e, item)}
                            className="opacity-0 group-hover:opacity-100 p-1 hover:bg-zinc-200 rounded text-zinc-400 hover:text-red-600 transition"
                            title="Delete (Delete)"
                          >
                            <Trash2 className="w-3.5 h-3.5" />
                          </button>
                        </div>
                      </div>
                    );
                  })
                )}
              </div>
            </div>

            {/* Right Preview Panel */}
            <div className="flex-1 bg-white flex flex-col overflow-hidden">
              {previewPanel}
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
          <div className="absolute inset-0 bg-black/50 backdrop-blur-md z-50 flex items-center justify-center p-8">
            <div className="bg-white border border-zinc-200 rounded-2xl shadow-2xl max-w-lg w-full flex flex-col max-h-[80%] overflow-hidden">
              
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
