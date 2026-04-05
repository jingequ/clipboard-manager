using System.Collections.ObjectModel;
using System.Globalization;
using ClipboardManager.Application.Interfaces;
using ClipboardManager.Domain.Entities;
using ClipboardManager.Shared;

namespace ClipboardManager.App.ViewModels;

public sealed class MainWindowViewModel : ViewModelBase
{
    private readonly IClipboardHistoryService _historyService;
    private readonly IClipboardReplayService _replayService;
    private readonly ILogger _logger;
    private string _searchQuery = string.Empty;
    private ClipboardItemViewModel? _selectedItem;
    private string _statusMessage = "Ready";
    private string _commandPreview = string.Empty;
    private ClearCommandInfo? _clearCommandInfo;

    public MainWindowViewModel(
        IClipboardHistoryService historyService,
        IClipboardReplayService replayService,
        ILogger logger)
    {
        _historyService = historyService;
        _replayService = replayService;
        _logger = logger;
        DeleteSelectedCommand = new RelayCommand(async () => await DeleteSelectedAsync(), () => SelectedItem is not null);
        ClearAllCommand = new RelayCommand(async () => await ClearAsync());
    }

    public ObservableCollection<ClipboardItemViewModel> Items { get; } = [];
    public RelayCommand DeleteSelectedCommand { get; }
    public RelayCommand ClearAllCommand { get; }
    public Action? FocusSearchRequested { get; set; }
    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public string CommandPreview
    {
        get => _commandPreview;
        private set => SetProperty(ref _commandPreview, value);
    }

    public string ClearCommandUsage => "clear 删除全部，clear 5 删除5分钟内记录，clear 1d 删除1天内记录";

    public bool HasCommandPreview => !string.IsNullOrWhiteSpace(CommandPreview);

    public bool IsClearCommandActive => _clearCommandInfo is not null;

    public string SearchQuery
    {
        get => _searchQuery;
        set
        {
            if (SetProperty(ref _searchQuery, value))
            {
                UpdateCommandState();
                _ = RefreshAsync();
            }
        }
    }

    public ClipboardItemViewModel? SelectedItem
    {
        get => _selectedItem;
        set
        {
            if (SetProperty(ref _selectedItem, value))
            {
                DeleteSelectedCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public async Task RefreshAsync()
    {
        try
        {
            if (IsClearCommandActive)
            {
                Items.Clear();
                SelectedItem = null;
                return;
            }

            var results = await _historyService.SearchAsync(SearchQuery);
            Items.Clear();
            foreach (var item in results.Select(result => new ClipboardItemViewModel(result)))
            {
                Items.Add(item);
            }

            SelectedItem = Items.FirstOrDefault();
            StatusMessage = Items.Count == 0 ? "No clipboard items yet" : $"{Items.Count} items";
        }
        catch (Exception ex)
        {
            _logger.Error("Failed to refresh clipboard history.", ex);
            StatusMessage = "Failed to refresh history";
        }
    }

    public async Task ReplaySelectedAsync()
    {
        if (SelectedItem is null)
        {
            return;
        }

        var replayedItem = SelectedItem;
        await _replayService.ReplayAsync(SelectedItem.Model);
        await _historyService.TouchAsync(replayedItem.Model.Id);
        await RefreshAsync();
        SelectedItem = Items.FirstOrDefault(item => item.Model.Id == replayedItem.Model.Id) ?? Items.FirstOrDefault();
    }

    public async Task DeleteSelectedAsync()
    {
        if (SelectedItem is null)
        {
            return;
        }

        await _historyService.DeleteAsync(SelectedItem.Model.Id);
        await RefreshAsync();
        if (!IsClearCommandActive)
        {
            CommandPreview = string.Empty;
        }
    }

    public async Task ClearAsync()
    {
        SelectedItem = null;
        Items.Clear();
        StatusMessage = "No clipboard items yet";
        await _historyService.ClearAsync();
        await RefreshAsync();
        if (!IsClearCommandActive)
        {
            CommandPreview = string.Empty;
        }
    }

    public async Task<bool> ExecuteClearCommandAsync()
    {
        if (_clearCommandInfo is null || _clearCommandInfo.Mode == ClearCommandMode.Invalid)
        {
            return false;
        }

        var deletedCount = _clearCommandInfo.Mode switch
        {
            ClearCommandMode.All => await ExecuteDeleteAllAsync(),
            ClearCommandMode.Recent => await _historyService.DeleteRecentAsync(DateTimeOffset.Now - _clearCommandInfo.Duration),
            _ => 0
        };

        await RefreshAsync();
        return true;
    }

    private async Task<int> ExecuteDeleteAllAsync()
    {
        var count = Items.Count;
        await _historyService.ClearAsync();
        return count;
    }

    private void UpdateCommandState()
    {
        _clearCommandInfo = TryParseClearCommand(SearchQuery);
        OnPropertyChanged(nameof(IsClearCommandActive));

        if (_clearCommandInfo is null)
        {
            CommandPreview = string.Empty;
            OnPropertyChanged(nameof(HasCommandPreview));
            return;
        }

        CommandPreview = _clearCommandInfo.Description;
        OnPropertyChanged(nameof(HasCommandPreview));
    }

    private static ClearCommandInfo? TryParseClearCommand(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return null;
        }

        var trimmed = input.Trim();
        if (string.Equals(trimmed, "clear", StringComparison.OrdinalIgnoreCase))
        {
            return new ClearCommandInfo(ClearCommandMode.All, TimeSpan.Zero, "删除全部");
        }

        if (!trimmed.StartsWith("clear ", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var arg = trimmed["clear ".Length..].Trim();
        if (int.TryParse(arg, NumberStyles.Integer, CultureInfo.InvariantCulture, out var minutes) && minutes > 0)
        {
            return new ClearCommandInfo(ClearCommandMode.Recent, TimeSpan.FromMinutes(minutes), $"删除{minutes}分钟内记录");
        }

        if (arg.EndsWith("d", StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(arg[..^1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var days) &&
            days > 0)
        {
            return new ClearCommandInfo(ClearCommandMode.Recent, TimeSpan.FromDays(days), $"删除{days}天内记录");
        }

        return new ClearCommandInfo(ClearCommandMode.Invalid, TimeSpan.Zero, "无效命令，示例：clear 5 或 clear 1d");
    }

    protected override void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
    {
        base.OnPropertyChanged(propertyName);
        if (propertyName == nameof(StatusMessage) && !IsClearCommandActive)
        {
            CommandPreview = string.Empty;
        }
        if (propertyName == nameof(CommandPreview))
        {
            OnPropertyChanged(nameof(HasCommandPreview));
        }
    }

    private sealed record ClearCommandInfo(ClearCommandMode Mode, TimeSpan Duration, string Description);

    private enum ClearCommandMode
    {
        Invalid,
        All,
        Recent
    }
}
