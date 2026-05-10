using System.IO;
using System.Windows;
using System.Windows.Interop;
using ClipboardManager.App.ViewModels;
using ClipboardManager.Application.Interfaces;
using ClipboardManager.Infrastructure.Clipboard;
using ClipboardManager.Infrastructure.Hotkeys;
using ClipboardManager.Infrastructure.Logging;
using ClipboardManager.Infrastructure.Startup;
using ClipboardManager.Infrastructure.Storage;
using ClipboardManager.Infrastructure.Tray;

namespace ClipboardManager.App;

public partial class App : System.Windows.Application
{
    private readonly string _appDataDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ClipboardManager");
    private Mutex? _singleInstanceMutex;
    private ITrayService? _trayService;
    private IClipboardMonitorService? _monitorService;
    private IHotkeyService? _hotkeyService;
    private IPasteAutomationService? _pasteAutomationService;
    private WindowInteropHelper? _mainWindowInterop;
    private IntPtr _lastForegroundWindow;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstanceMutex = new Mutex(true, "ClipboardManager.SingleInstance", out var isPrimaryInstance);
        if (!isPrimaryInstance)
        {
            System.Windows.MessageBox.Show("Clipboard Manager is already running.", "Clipboard Manager", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        Directory.CreateDirectory(_appDataDirectory);
        var logger = new FileLogger(Path.Combine(_appDataDirectory, "logs"));
        var settingsService = new JsonSettingsService(Path.Combine(_appDataDirectory, "settings.json"));
        var historyService = new SqliteClipboardHistoryService(Path.Combine(_appDataDirectory, "clipboard.db"));
        await historyService.InitializeAsync();

        var settings = await settingsService.LoadAsync();
        settings.MaxItems = Math.Max(1, settings.MaxItems);
        settings.RetentionDays = Math.Max(0, settings.RetentionDays);
        var snapshotFactory = new ClipboardSnapshotFactory(Path.Combine(_appDataDirectory, "images"));
        var startupService = new RegistryStartupService(Environment.ProcessPath ?? "ClipboardManager.exe");
        _monitorService = new ClipboardMonitorService(snapshotFactory, () => settings.CaptureImages, () => settings.CaptureFiles, () => settings.RetentionDays);
        _hotkeyService = new GlobalHotkeyService();
        _pasteAutomationService = new WindowPasteAutomationService(logger);
        _trayService = new TrayService();
        var replayService = new ClipboardReplayService(logger);

        var mainViewModel = new MainWindowViewModel(historyService, replayService, logger);
        var settingsViewModel = new SettingsViewModel(settings, settingsService, startupService);
        var mainWindow = new MainWindow
        {
            DataContext = mainViewModel
        };
        MainWindow = mainWindow;
        _mainWindowInterop = new WindowInteropHelper(mainWindow);
        mainViewModel.FocusSearchRequested = mainWindow.FocusSearchBox;
        mainWindow.ConfirmSelectionAsync = async () =>
        {
            try
            {
                logger.Info("Confirming clipboard selection. Pausing monitor.");
                _monitorService?.Pause();
                try
                {
                    logger.Info("Calling mainViewModel.ReplaySelectedAsync()");
                    await mainViewModel.ReplaySelectedAsync();
                    logger.Info("ReplaySelectedAsync() finished. Hiding mainWindow.");
                    mainWindow.Hide();
                    logger.Info("Calling _pasteAutomationService.PasteToWindowAsync()");
                    await _pasteAutomationService.PasteToWindowAsync(_lastForegroundWindow);
                    logger.Info("PasteToWindowAsync() finished.");
                }
                finally
                {
                    logger.Info("Scheduling monitor resume in 500ms.");
                    _ = Task.Run(async () =>
                    {
                        await Task.Delay(500);
                        logger.Info("Resuming monitor.");
                        _monitorService?.Resume();
                    });
                }
            }
            catch (Exception ex)
            {
                logger.Error("Failed to confirm and paste clipboard selection.", ex);
                System.Windows.MessageBox.Show(
                    $"Failed to paste the selected clipboard item.{Environment.NewLine}{ex.Message}",
                    "Clipboard Manager",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        };

        _trayService.Initialize(
            () => Dispatcher.Invoke(() => ShowMainWindow(mainWindow)),
            () => Dispatcher.Invoke(() => new SettingsWindow { DataContext = settingsViewModel, Owner = mainWindow }.ShowDialog()),
            () => _ = Dispatcher.InvokeAsync(async () =>
            {
                try
                {
                    await mainViewModel.ClearAsync();
                    await PurgeImageCacheAsync(Path.Combine(_appDataDirectory, "images"));
                }
                catch (Exception ex)
                {
                    logger.Error("Failed to clear history from tray.", ex);
                    System.Windows.MessageBox.Show(
                        $"Failed to clear history.{Environment.NewLine}{ex.Message}",
                        "Clipboard Manager",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
            }),
            Shutdown);

        _monitorService.ClipboardItemCaptured += async (_, item) =>
        {
            await historyService.AddOrUpdateAsync(item);
            await historyService.PruneExpiredAsync();
            await historyService.EnforceMaxItemsAsync(settings.MaxItems);
            await CleanOrphanedImageCacheAsync(historyService, Path.Combine(_appDataDirectory, "images"));
            await mainViewModel.RefreshAsync();
        };
        _monitorService.Start();

        settingsViewModel.SettingsSaved += async (_, _) =>
        {
            await ApplyRuntimeSettingsAsync(historyService, mainViewModel, settings);
        };
        settingsViewModel.ClearHistoryRequested += async (_, _) =>
        {
            await mainViewModel.ClearAsync();
            await PurgeImageCacheAsync(Path.Combine(_appDataDirectory, "images"));
            settingsViewModel.MarkHistoryCleared();
        };

        mainWindow.SourceInitialized += (_, _) =>
        {
            if (!_hotkeyService.Register(_mainWindowInterop.Handle, settings.HotkeyGesture))
            {
                System.Windows.MessageBox.Show(
                    $"Unable to register hotkey '{settings.HotkeyGesture}'. Try another shortcut.",
                    "Hotkey Conflict",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        };

        _hotkeyService.HotkeyPressed += async (_, _) =>
        {
            await Dispatcher.InvokeAsync(() => ShowMainWindow(mainWindow));
        };

        await ApplyRuntimeSettingsAsync(historyService, mainViewModel, settings);
        await mainViewModel.RefreshAsync();
        mainWindow.Show();
        mainWindow.Hide();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _monitorService?.Stop();
        if (MainWindow is not null && _hotkeyService is not null)
        {
            _hotkeyService.Unregister(_mainWindowInterop?.Handle ?? new WindowInteropHelper(MainWindow).Handle);
        }

        _trayService?.Dispose();
        _singleInstanceMutex?.ReleaseMutex();
        _singleInstanceMutex?.Dispose();
        base.OnExit(e);
    }

    private static void ShowMainWindow(MainWindow window)
    {
        ((App)Current).CaptureForegroundWindow(window);
        window.Show();
        window.Activate();
        window.Topmost = true;
        window.Topmost = false;
        window.FocusSearchBox();
    }

    private void CaptureForegroundWindow(Window window)
    {
        var foregroundWindow = GetForegroundWindow();
        var currentHandle = _mainWindowInterop?.Handle ?? new WindowInteropHelper(window).EnsureHandle();
        if (foregroundWindow != IntPtr.Zero && foregroundWindow != currentHandle)
        {
            _lastForegroundWindow = foregroundWindow;
        }
    }

    private async Task ApplyRuntimeSettingsAsync(
        IClipboardHistoryService historyService,
        MainWindowViewModel mainViewModel,
        Domain.Entities.AppSettings settings)
    {
        await historyService.PruneExpiredAsync();
        await historyService.EnforceMaxItemsAsync(settings.MaxItems);
        await CleanOrphanedImageCacheAsync(historyService, Path.Combine(_appDataDirectory, "images"));

        if (_hotkeyService is not null && _mainWindowInterop is not null && _mainWindowInterop.Handle != IntPtr.Zero)
        {
            _hotkeyService.Unregister(_mainWindowInterop.Handle);
            if (!_hotkeyService.Register(_mainWindowInterop.Handle, settings.HotkeyGesture))
            {
                System.Windows.MessageBox.Show(
                    $"Unable to register hotkey '{settings.HotkeyGesture}'. The previous hotkey has been cleared.",
                    "Hotkey Conflict",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        await mainViewModel.RefreshAsync();
    }

    private static async Task CleanOrphanedImageCacheAsync(IClipboardHistoryService historyService, string imageDirectory)
    {
        if (!Directory.Exists(imageDirectory))
        {
            return;
        }

        var referencedFiles = new HashSet<string>(
            await historyService.GetReferencedImagePathsAsync(),
            StringComparer.OrdinalIgnoreCase);

        foreach (var file in Directory.GetFiles(imageDirectory))
        {
            if (!referencedFiles.Contains(file) || new FileInfo(file).Length == 0)
            {
                TryDeleteFile(file);
            }
        }
    }

    private static async Task PurgeImageCacheAsync(string imageDirectory)
    {
        if (!Directory.Exists(imageDirectory))
        {
            return;
        }

        for (var attempt = 0; attempt < 5; attempt++)
        {
            var remainingFiles = Directory.GetFiles(imageDirectory);
            if (remainingFiles.Length == 0)
            {
                return;
            }

            foreach (var file in remainingFiles)
            {
                TryDeleteFile(file);
            }

            if (Directory.GetFiles(imageDirectory).Length == 0)
            {
                return;
            }

            if (attempt == 1)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
            }

            await Task.Delay(150);
        }
    }

    private static void TryDeleteFile(string file)
    {
        try
        {
            if (File.Exists(file))
            {
                File.Delete(file);
            }
        }
        catch (IOException)
        {
            // Best-effort cleanup: preview images may still be materialized by the UI for a short time.
        }
        catch (UnauthorizedAccessException)
        {
            // Keep clear-history resilient even if a cache file cannot be removed immediately.
        }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();
}


