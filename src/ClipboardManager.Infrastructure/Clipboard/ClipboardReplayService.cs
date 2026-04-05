using System.Collections.Specialized;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media.Imaging;
using ClipboardManager.Application.Interfaces;
using ClipboardManager.Domain.Entities;
using ClipboardManager.Domain.Enums;
using WindowsClipboard = System.Windows.Clipboard;

namespace ClipboardManager.Infrastructure.Clipboard;

public sealed class ClipboardReplayService : IClipboardReplayService
{
    public async Task ReplayAsync(ClipboardItem item, CancellationToken cancellationToken = default)
    {
        await RetryClipboardAccessAsync(() =>
        {
            switch (item.Type)
            {
                case ClipboardItemType.Text when !string.IsNullOrWhiteSpace(item.TextContent):
                    WindowsClipboard.SetText(item.TextContent);
                    break;
                case ClipboardItemType.RichText:
                    var dataObject = new System.Windows.DataObject();
                    if (!string.IsNullOrWhiteSpace(item.TextContent))
                    {
                        dataObject.SetText(item.TextContent);
                    }

                    if (!string.IsNullOrWhiteSpace(item.HtmlContent))
                    {
                        dataObject.SetData(System.Windows.DataFormats.Html, item.HtmlContent);
                    }

                    if (!string.IsNullOrWhiteSpace(item.RtfContent))
                    {
                        dataObject.SetData(System.Windows.DataFormats.Rtf, item.RtfContent);
                    }

                    WindowsClipboard.SetDataObject(dataObject, true);
                    break;
                case ClipboardItemType.Image when !string.IsNullOrWhiteSpace(item.ImagePath) && File.Exists(item.ImagePath):
                    WindowsClipboard.SetImage(new BitmapImage(new Uri(item.ImagePath)));
                    break;
                case ClipboardItemType.FileList when item.Files is { Count: > 0 }:
                    var collection = new StringCollection();
                    foreach (var file in item.Files)
                    {
                        collection.Add(file.Path);
                    }

                    WindowsClipboard.SetFileDropList(collection);
                    break;
            }
        }, cancellationToken);
    }

    private static async Task RetryClipboardAccessAsync(Action action, CancellationToken cancellationToken)
    {
        const int maxAttempts = 10;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                action();
                return;
            }
            catch (COMException) when (attempt < maxAttempts)
            {
                await Task.Delay(40 * attempt, cancellationToken);
            }
            catch (ExternalException) when (attempt < maxAttempts)
            {
                await Task.Delay(40 * attempt, cancellationToken);
            }
        }

        action();
    }
}

