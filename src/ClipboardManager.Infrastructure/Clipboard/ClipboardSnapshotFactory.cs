using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Media.Imaging;
using ClipboardManager.Domain.Entities;
using ClipboardManager.Domain.Enums;
using WindowsClipboard = System.Windows.Clipboard;

namespace ClipboardManager.Infrastructure.Clipboard;

public sealed class ClipboardSnapshotFactory
{
    private readonly string _imageCacheDirectory;

    public ClipboardSnapshotFactory(string imageCacheDirectory)
    {
        _imageCacheDirectory = imageCacheDirectory;
        Directory.CreateDirectory(_imageCacheDirectory);
    }

    public ClipboardItem? CreateFromCurrentClipboard(bool captureImages, bool captureFiles, int retentionDays)
    {
        return RetryClipboardAccess(() => CreateFromClipboardCore(captureImages, captureFiles, retentionDays));
    }

    private ClipboardItem? CreateFromClipboardCore(bool captureImages, bool captureFiles, int retentionDays)
    {
        DateTimeOffset? expiresAt = retentionDays > 0 ? DateTimeOffset.Now.AddDays(retentionDays) : null;

        if (captureFiles && WindowsClipboard.ContainsFileDropList())
        {
            var paths = WindowsClipboard.GetFileDropList().Cast<string>().ToList();
            if (paths.Count == 0)
            {
                return null;
            }

            var entries = paths.Select(path => new FileClipboardEntry
            {
                Path = path,
                Name = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar)) is { Length: > 0 } name ? name : path,
                IsDirectory = Directory.Exists(path),
                Extension = Path.GetExtension(path)
            }).ToList();

            var title = entries.Count == 1 ? entries[0].Name : $"{entries[0].Name} and {entries.Count - 1} more";
            return new ClipboardItem
            {
                Type = ClipboardItemType.FileList,
                Summary = title,
                SearchText = string.Join(' ', entries.Select(x => $"{x.Name} {x.Path}")),
                Files = entries,
                ExpiresAt = expiresAt,
                ContentHash = ComputeHash(string.Join('|', paths)),
                DisplayMetadata = entries.Count == 1 ? entries[0].Path : $"{entries.Count} items"
            };
        }

        if (captureImages && WindowsClipboard.ContainsImage())
        {
            var image = WindowsClipboard.GetImage();
            if (image is null)
            {
                return null;
            }

            var imagePath = Path.Combine(_imageCacheDirectory, $"{Guid.NewGuid():N}.png");
            using (var stream = File.Create(imagePath))
            {
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(image));
                encoder.Save(stream);
            }

            return new ClipboardItem
            {
                Type = ClipboardItemType.Image,
                Summary = $"Image: {image.PixelWidth}x{image.PixelHeight}",
                SearchText = $"image {image.PixelWidth} {image.PixelHeight}",
                ImagePath = imagePath,
                ExpiresAt = expiresAt,
                ContentHash = ComputeHash($"{image.PixelWidth}x{image.PixelHeight}:{new FileInfo(imagePath).Length}"),
                DisplayMetadata = $"{image.PixelWidth}x{image.PixelHeight}"
            };
        }

        if (WindowsClipboard.ContainsData(System.Windows.DataFormats.Html) || WindowsClipboard.ContainsData(System.Windows.DataFormats.Rtf))
        {
            var text = TryGetClipboardText();
            var html = TryGetClipboardString(System.Windows.DataFormats.Html);
            var rtf = TryGetClipboardString(System.Windows.DataFormats.Rtf);

            if (!string.IsNullOrWhiteSpace(text) || !string.IsNullOrWhiteSpace(html) || !string.IsNullOrWhiteSpace(rtf))
            {
                var summary = !string.IsNullOrWhiteSpace(text)
                    ? (text.Length > 120 ? $"{text[..117]}..." : text)
                    : "Rich text content";

                return new ClipboardItem
                {
                    Type = ClipboardItemType.RichText,
                    Summary = summary,
                    SearchText = text,
                    TextContent = text,
                    HtmlContent = html,
                    RtfContent = rtf,
                    ExpiresAt = expiresAt,
                    ContentHash = ComputeHash($"{text}|{html}|{rtf}"),
                    DisplayMetadata = "Rich text"
                };
            }
        }

        if (WindowsClipboard.ContainsText())
        {
            var text = TryGetClipboardText();
            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            return new ClipboardItem
            {
                Type = ClipboardItemType.Text,
                Summary = text.Length > 120 ? $"{text[..117]}..." : text,
                SearchText = text,
                TextContent = text,
                ExpiresAt = expiresAt,
                ContentHash = ComputeHash(text)
            };
        }

        return null;
    }

    private static ClipboardItem? RetryClipboardAccess(Func<ClipboardItem?> action)
    {
        const int maxAttempts = 10;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                return action();
            }
            catch (COMException) when (attempt < maxAttempts)
            {
                Thread.Sleep(40 * attempt);
            }
            catch (ExternalException) when (attempt < maxAttempts)
            {
                Thread.Sleep(40 * attempt);
            }
        }

        return action();
    }

    private static string ComputeHash(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes);
    }

    private static string TryGetClipboardText()
    {
        try
        {
            return WindowsClipboard.ContainsText() ? WindowsClipboard.GetText() : string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string? TryGetClipboardString(string format)
    {
        try
        {
            if (!WindowsClipboard.ContainsData(format))
            {
                return null;
            }

            var data = WindowsClipboard.GetData(format);
            return data switch
            {
                string text => text,
                MemoryStream stream => ReadStream(stream),
                _ => data?.ToString()
            };
        }
        catch
        {
            return null;
        }
    }

    private static string ReadStream(MemoryStream stream)
    {
        stream.Position = 0;
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        return reader.ReadToEnd();
    }
}

