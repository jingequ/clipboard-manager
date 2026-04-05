using System.Windows;
using ClipboardManager.Application.Interfaces;
using ClipboardManager.Shared;

namespace ClipboardManager.App.ViewModels;

public sealed class SettingsViewModel : ViewModelBase
{
    private readonly Domain.Entities.AppSettings _settings;
    private readonly ISettingsService _settingsService;
    private readonly IStartupService _startupService;
    private bool _launchAtStartup;
    private bool _captureImages;
    private bool _captureFiles;
    private int _retentionDays;
    private int _maxItems;
    private string _hotkeyGesture;
    private string _validationMessage = string.Empty;
    private string _clearHistoryMessage = string.Empty;

    public SettingsViewModel(
        Domain.Entities.AppSettings settings,
        ISettingsService settingsService,
        IStartupService startupService)
    {
        _settings = settings;
        _settingsService = settingsService;
        _startupService = startupService;

        _launchAtStartup = startupService.IsEnabled() || settings.LaunchAtStartup;
        _captureImages = settings.CaptureImages;
        _captureFiles = settings.CaptureFiles;
        _retentionDays = settings.RetentionDays;
        _maxItems = settings.MaxItems;
        _hotkeyGesture = settings.HotkeyGesture;

        SaveCommand = new RelayCommand(async () => await SaveAsync());
        CancelCommand = new RelayCommand(CloseWindow);
        ClearHistoryCommand = new RelayCommand(RequestClearHistory);
    }

    public RelayCommand SaveCommand { get; }
    public RelayCommand CancelCommand { get; }
    public RelayCommand ClearHistoryCommand { get; }
    public event EventHandler? SettingsSaved;
    public event EventHandler? ClearHistoryRequested;
    public string ValidationMessage
    {
        get => _validationMessage;
        private set => SetProperty(ref _validationMessage, value);
    }

    public string ClearHistoryMessage
    {
        get => _clearHistoryMessage;
        private set => SetProperty(ref _clearHistoryMessage, value);
    }

    public bool LaunchAtStartup
    {
        get => _launchAtStartup;
        set => SetProperty(ref _launchAtStartup, value);
    }

    public bool CaptureImages
    {
        get => _captureImages;
        set => SetProperty(ref _captureImages, value);
    }

    public bool CaptureFiles
    {
        get => _captureFiles;
        set => SetProperty(ref _captureFiles, value);
    }

    public int RetentionDays
    {
        get => _retentionDays;
        set => SetProperty(ref _retentionDays, value);
    }

    public int MaxItems
    {
        get => _maxItems;
        set => SetProperty(ref _maxItems, value);
    }

    public string HotkeyGesture
    {
        get => _hotkeyGesture;
        set => SetProperty(ref _hotkeyGesture, value);
    }

    private async Task SaveAsync()
    {
        if (string.IsNullOrWhiteSpace(HotkeyGesture))
        {
            ValidationMessage = "Hotkey cannot be empty.";
            return;
        }

        if (RetentionDays < 0)
        {
            ValidationMessage = "Retention days cannot be negative.";
            return;
        }

        if (MaxItems < 1)
        {
            ValidationMessage = "Max history items must be at least 1.";
            return;
        }

        ValidationMessage = string.Empty;
        _settings.LaunchAtStartup = LaunchAtStartup;
        _settings.CaptureImages = CaptureImages;
        _settings.CaptureFiles = CaptureFiles;
        _settings.RetentionDays = Math.Max(0, RetentionDays);
        _settings.MaxItems = Math.Max(1, MaxItems);
        _settings.HotkeyGesture = string.IsNullOrWhiteSpace(HotkeyGesture) ? "Alt+C" : HotkeyGesture.Trim();

        _startupService.SetEnabled(LaunchAtStartup);
        await _settingsService.SaveAsync(_settings);
        SettingsSaved?.Invoke(this, EventArgs.Empty);
        CloseWindow();
    }

    public void MarkHistoryCleared()
    {
        ClearHistoryMessage = "History cleared.";
    }

    private void RequestClearHistory()
    {
        ClearHistoryRequested?.Invoke(this, EventArgs.Empty);
    }

    private static void CloseWindow()
    {
        System.Windows.Application.Current.Windows.OfType<Window>().FirstOrDefault(window => window.IsActive)?.Close();
    }
}

