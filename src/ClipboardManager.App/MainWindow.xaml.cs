using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ClipboardManager.App.ViewModels;
using ClipboardManager.Domain.Enums;

namespace ClipboardManager.App;

public partial class MainWindow : Window
{
    public Func<Task>? ConfirmSelectionAsync { get; set; }

    public MainWindow()
    {
        InitializeComponent();
        Loaded += async (_, _) =>
        {
            SearchTextBox.Focus();
            await UpdatePreviewAsync();
        };
        Deactivated += (_, _) =>
        {
            if (IsVisible)
            {
                Hide();
            }
        };
    }

    protected override async void OnPreviewKeyDown(System.Windows.Input.KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);

        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        if (e.Key == Key.Escape)
        {
            Hide();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Enter)
        {
            if (viewModel.IsClearCommandActive)
            {
                await viewModel.ExecuteClearCommandAsync();
            }
            else if (ConfirmSelectionAsync is not null)
            {
                await ConfirmSelectionAsync();
            }
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Up)
        {
            MoveSelection(-1);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Down)
        {
            MoveSelection(1);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Delete)
        {
            await viewModel.DeleteSelectedAsync();
            e.Handled = true;
        }
    }

    public void FocusSearchBox()
    {
        SearchTextBox.Focus();
        SearchTextBox.SelectAll();
    }

    private async void HistoryListBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded)
        {
            return;
        }

        await UpdatePreviewAsync();
    }

    private async void HistoryListBox_OnMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ConfirmSelectionAsync is null)
        {
            return;
        }

        await ConfirmSelectionAsync();
    }

    private void HistoryListBoxItem_OnMouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (sender is ListBoxItem item)
        {
            item.IsSelected = true;
        }
    }

    private async void HistoryListBoxItem_OnPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (ConfirmSelectionAsync is null)
        {
            return;
        }

        if (sender is ListBoxItem item)
        {
            item.IsSelected = true;
            await ConfirmSelectionAsync();
            e.Handled = true;
        }
    }

    private void MoveSelection(int offset)
    {
        if (HistoryListBox.Items.Count == 0)
        {
            return;
        }

        var currentIndex = HistoryListBox.SelectedIndex;
        if (currentIndex < 0)
        {
            currentIndex = 0;
        }

        var nextIndex = Math.Clamp(currentIndex + offset, 0, HistoryListBox.Items.Count - 1);
        HistoryListBox.SelectedIndex = nextIndex;
        HistoryListBox.ScrollIntoView(HistoryListBox.SelectedItem);
        _ = UpdatePreviewAsync();
    }

    private CancellationTokenSource? _previewCts;

    private async Task UpdatePreviewAsync()
    {
        _previewCts?.Cancel();
        _previewCts = new CancellationTokenSource();
        var token = _previewCts.Token;

        EmptyPreviewPanel.Visibility = Visibility.Collapsed;
        TextPreviewPanel.Visibility = Visibility.Collapsed;
        ImagePreviewBox.Visibility = Visibility.Collapsed;
        FilePreviewPanel.Visibility = Visibility.Collapsed;
        ImagePreviewControl.Source = null;
        FilePreviewIcon.Source = null;
        TextPreviewContent.Text = string.Empty;
        FilePreviewText.Text = string.Empty;
        PreviewFooterText.Text = string.Empty;
        EmptyPreviewText.Text = "Loading preview...";

        if (DataContext is not MainWindowViewModel viewModel || viewModel.SelectedItem is not { } selectedItem)
        {
            EmptyPreviewText.Text = "Select an item to preview";
            EmptyPreviewPanel.Visibility = Visibility.Visible;
            return;
        }

        PreviewFooterText.Text = selectedItem.Footer;

        try
        {
            switch (selectedItem.Model.Type)
            {
                case ClipboardItemType.Text:
                case ClipboardItemType.RichText:
                    TextPreviewContent.Text = selectedItem.PreviewText;
                    TextPreviewPanel.Visibility = Visibility.Visible;
                    break;
                case ClipboardItemType.Image:
                    var previewImage = await Task.Run(() => LoadPreviewImage(selectedItem, out _), token);
                    if (token.IsCancellationRequested) return;

                    if (previewImage is not null)
                    {
                        ImagePreviewControl.Source = previewImage;
                        ImagePreviewBox.Visibility = Visibility.Visible;
                    }
                    else
                    {
                        EmptyPreviewText.Text = "Image preview not available";
                        EmptyPreviewPanel.Visibility = Visibility.Visible;
                    }
                    break;
                case ClipboardItemType.FileList:
                    var fileIcon = await Task.Run(() => selectedItem.GetPreviewImage(), token);
                    if (token.IsCancellationRequested) return;

                    FilePreviewIcon.Source = fileIcon;
                    FilePreviewText.Text = selectedItem.PreviewText;
                    FilePreviewPanel.Visibility = Visibility.Visible;
                    break;
                default:
                    EmptyPreviewText.Text = "Preview not available";
                    EmptyPreviewPanel.Visibility = Visibility.Visible;
                    break;
            }
        }
        catch (Exception)
        {
            if (!token.IsCancellationRequested)
            {
                EmptyPreviewText.Text = "Error loading preview";
                EmptyPreviewPanel.Visibility = Visibility.Visible;
            }
        }
    }

    private static BitmapSource? LoadPreviewImage(ClipboardItemViewModel selectedItem, out string debugMessage)
    {
        if (string.IsNullOrWhiteSpace(selectedItem.Model.ImagePath))
        {
            debugMessage = "Image path is empty";
            return null;
        }

        if (!File.Exists(selectedItem.Model.ImagePath))
        {
            debugMessage = $"Image file not found: {selectedItem.Model.ImagePath}";
            return null;
        }

        try
        {
            using var stream = new FileStream(selectedItem.Model.ImagePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var decoder = BitmapDecoder.Create(
                stream,
                BitmapCreateOptions.PreservePixelFormat,
                BitmapCacheOption.OnLoad);
            var frame = decoder.Frames[0];
            var prepared = PreparePreviewBitmap(frame);
            debugMessage = $"Image loaded: {prepared.PixelWidth}x{prepared.PixelHeight}";
            return prepared;
        }
        catch (Exception ex)
        {
            debugMessage = $"Image load failed: {ex.Message}";
            return null;
        }
    }

    private static BitmapSource PreparePreviewBitmap(BitmapSource source)
    {
        var width = Math.Max(1, source.PixelWidth);
        var height = Math.Max(1, source.PixelHeight);
        var converted = new FormatConvertedBitmap();
        converted.BeginInit();
        converted.Source = source;
        converted.DestinationFormat = PixelFormats.Bgra32;
        converted.EndInit();
        converted.Freeze();

        var stride = width * 4;
        var pixels = new byte[stride * height];
        converted.CopyPixels(pixels, stride, 0);

        var hasVisibleAlpha = false;
        for (var index = 3; index < pixels.Length; index += 4)
        {
            if (pixels[index] > 0)
            {
                hasVisibleAlpha = true;
                break;
            }
        }

        for (var index = 0; index < pixels.Length; index += 4)
        {
            if (!hasVisibleAlpha)
            {
                pixels[index + 3] = 255;
            }
        }

        var prepared = BitmapSource.Create(
            width,
            height,
            source.DpiX > 0 ? source.DpiX : 96,
            source.DpiY > 0 ? source.DpiY : 96,
            PixelFormats.Bgra32,
            null,
            pixels,
            stride);
        prepared.Freeze();
        return prepared;
    }
}

