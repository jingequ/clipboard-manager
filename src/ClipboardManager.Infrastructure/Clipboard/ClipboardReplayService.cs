using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Media.Imaging;
using ClipboardManager.Application.Interfaces;
using ClipboardManager.Domain.Entities;
using ClipboardManager.Domain.Enums;
using WindowsClipboard = System.Windows.Clipboard;

namespace ClipboardManager.Infrastructure.Clipboard;

public sealed class ClipboardReplayService : IClipboardReplayService
{
    private readonly ILogger _logger;

    public ClipboardReplayService(ILogger logger)
    {
        _logger = logger;
    }
    public async Task ReplayAsync(ClipboardItem item, CancellationToken cancellationToken = default)
    {
        _logger.Info($"ReplayAsync started for type {item.Type}");
        switch (item.Type)
        {
            case ClipboardItemType.Text when !string.IsNullOrWhiteSpace(item.TextContent):
                _logger.Info("Calling SetTextDataObjectAsync");
                await SetTextDataObjectAsync(item.TextContent, cancellationToken);
                break;
            case ClipboardItemType.RichText:
                await ReplayRichTextAsync(item, cancellationToken);
                break;
            case ClipboardItemType.Image when !string.IsNullOrWhiteSpace(item.ImagePath) && File.Exists(item.ImagePath):
                await RetryClipboardAccessAsync(() =>
                {
                    WindowsClipboard.SetImage(LoadBitmap(item.ImagePath));
                }, cancellationToken);
                break;
            case ClipboardItemType.FileList when item.Files is { Count: > 0 }:
                await RetryClipboardAccessAsync(() =>
                {
                    var collection = new StringCollection();
                    foreach (var file in item.Files)
                    {
                        collection.Add(file.Path);
                    }

                    WindowsClipboard.SetFileDropList(collection);
                }, cancellationToken);
                break;
        }
    }

    private async Task ReplayRichTextAsync(ClipboardItem item, CancellationToken cancellationToken)
    {
        try
        {
            _logger.Info("Replaying rich text data object");
            await RetryClipboardAccessAsync(() =>
            {
                var dataObject = new System.Windows.DataObject();
                if (!string.IsNullOrWhiteSpace(item.TextContent))
                {
                    dataObject.SetText(item.TextContent, System.Windows.TextDataFormat.UnicodeText);
                    dataObject.SetText(item.TextContent, System.Windows.TextDataFormat.Text);
                }

                if (!string.IsNullOrWhiteSpace(item.HtmlContent))
                {
                    dataObject.SetData(System.Windows.DataFormats.Html, item.HtmlContent);
                }

                if (!string.IsNullOrWhiteSpace(item.RtfContent))
                {
                    dataObject.SetData(System.Windows.DataFormats.Rtf, item.RtfContent);
                }

                WindowsClipboard.SetDataObject(dataObject, false);
            }, cancellationToken, maxAttempts: 20);
        }
        catch (Exception ex) when (IsClipboardAccessException(ex) && !string.IsNullOrWhiteSpace(item.TextContent))
        {
            await SetTextDataObjectAsync(item.TextContent, cancellationToken);
        }
    }

    private async Task SetTextDataObjectAsync(string text, CancellationToken cancellationToken)
    {
        _logger.Info("Replaying text using reliable SetUnicodeTextAsync");
        await SetUnicodeTextAsync(text, cancellationToken);
    }

    private async Task RetryClipboardAccessAsync(Action action, CancellationToken cancellationToken, int maxAttempts = 40)
    {
        _logger.Info($"RetryClipboardAccessAsync started. Max attempts: {maxAttempts}");
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                action();
                return;
            }
            catch (COMException ex) when (attempt < maxAttempts)
            {
                _logger.Info($"COMException on attempt {attempt}: {ex.Message}");
                await Task.Delay(GetRetryDelay(attempt), cancellationToken);
            }
            catch (ExternalException ex) when (attempt < maxAttempts)
            {
                _logger.Info($"ExternalException on attempt {attempt}: {ex.Message}");
                await Task.Delay(GetRetryDelay(attempt), cancellationToken);
            }
        }

        _logger.Info("RetryClipboardAccessAsync executing action on final attempt.");
        action();
    }

    private async Task SetUnicodeTextAsync(string text, CancellationToken cancellationToken)
    {
        const int maxAttempts = 100;
        _logger.Info("SetUnicodeTextAsync started. Falling back to Win32 clipboard API.");

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (TrySetUnicodeText(text))
            {
                _logger.Info($"SetUnicodeTextAsync succeeded on attempt {attempt}.");
                return;
            }

            _logger.Info($"SetUnicodeTextAsync failed on attempt {attempt}. Delaying.");
            await Task.Delay(GetRetryDelay(attempt), cancellationToken);
        }

        _logger.Error("SetUnicodeTextAsync failed after multiple retries.");
        throw new ExternalException("OpenClipboard failed after multiple retries.");
    }

    private static bool TrySetUnicodeText(string text)
    {
        const uint cfUnicodeText = 13;
        const uint gmemMoveable = 0x0002;
        IntPtr globalHandle = IntPtr.Zero;

        if (!OpenClipboard(IntPtr.Zero))
        {
            return false;
        }

        try
        {
            if (!EmptyClipboard())
            {
                return false;
            }

            var bytes = Encoding.Unicode.GetBytes(text + '\0');
            globalHandle = GlobalAlloc(gmemMoveable, (UIntPtr)bytes.Length);
            if (globalHandle == IntPtr.Zero)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            var target = GlobalLock(globalHandle);
            if (target == IntPtr.Zero)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            try
            {
                Marshal.Copy(bytes, 0, target, bytes.Length);
            }
            finally
            {
                GlobalUnlock(globalHandle);
            }

            if (SetClipboardData(cfUnicodeText, globalHandle) == IntPtr.Zero)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            globalHandle = IntPtr.Zero;
            return true;
        }
        finally
        {
            if (globalHandle != IntPtr.Zero)
            {
                GlobalFree(globalHandle);
            }

            CloseClipboard();
        }
    }

    private static bool IsClipboardAccessException(Exception ex)
    {
        return ex is COMException or ExternalException;
    }

    private static int GetRetryDelay(int attempt)
    {
        return Math.Min(500, 50 + attempt * 25);
    }

    private static BitmapImage LoadBitmap(string imagePath)
    {
        using var stream = new FileStream(imagePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var memory = new MemoryStream();
        stream.CopyTo(memory);
        memory.Position = 0;

        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.StreamSource = memory;
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool OpenClipboard(IntPtr hWndNewOwner);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool EmptyClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetClipboardData(uint uFormat, IntPtr hMem);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool CloseClipboard();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalAlloc(uint uFlags, UIntPtr dwBytes);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalLock(IntPtr hMem);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalUnlock(IntPtr hMem);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalFree(IntPtr hMem);
}

