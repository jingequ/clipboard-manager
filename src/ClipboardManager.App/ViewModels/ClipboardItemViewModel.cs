using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ClipboardManager.Domain.Entities;
using ClipboardManager.Domain.Enums;

namespace ClipboardManager.App.ViewModels;

public sealed class ClipboardItemViewModel
{
    public ClipboardItemViewModel(ClipboardItem item)
    {
        Model = item;
        var fileCount = item.Files?.Count ?? 0;
        var imageSize = !string.IsNullOrWhiteSpace(item.ImagePath) && File.Exists(item.ImagePath)
            ? new FileInfo(item.ImagePath).Length
            : 0;

        Title = (item.Summary ?? string.Empty).ReplaceLineEndings(" ");
        Subtitle = item.Type switch
        {
            ClipboardItemType.Text => $"{item.TextContent?.Length ?? 0} characters",
            ClipboardItemType.RichText => "Rich Text Format",
            ClipboardItemType.Image => imageSize > 0 ? $"{item.DisplayMetadata} • {FormatSize(imageSize)}" : item.DisplayMetadata ?? "Image",
            ClipboardItemType.FileList => fileCount > 1 ? $"{fileCount} items" : item.DisplayMetadata ?? "File",
            _ => string.Empty
        };
        TypeGlyph = item.Type switch
        {
            ClipboardItemType.Text => "\uE7C3", // Text
            ClipboardItemType.RichText => "\uE179", // RichText
            ClipboardItemType.Image => "\uEB9F", // Image
            ClipboardItemType.FileList => item.Files?.FirstOrDefault()?.IsDirectory == true ? "\uE8B7" : "\uE723",
            _ => "•"
        };
        TimeLabel = item.CreatedAt.ToLocalTime().ToString("HH:mm");
        PreviewText = item.Type switch
        {
            ClipboardItemType.Text => item.TextContent ?? string.Empty,
            ClipboardItemType.RichText => item.TextContent ?? string.Empty,
            ClipboardItemType.FileList => string.Join(Environment.NewLine, item.Files?.Select((file, index) => $"{index + 1}. {file.Path}") ?? []),
            _ => item.DisplayMetadata ?? item.Summary ?? string.Empty
        };
        PreviewDetails = item.Type switch
        {
            ClipboardItemType.Text => $"{(item.TextContent?.Length ?? 0)} characters",
            ClipboardItemType.RichText => "Formatted Rich Text Content",
            ClipboardItemType.Image => imageSize > 0 ? $"{item.DisplayMetadata} • {FormatSize(imageSize)}" : item.DisplayMetadata ?? "Image",
            ClipboardItemType.FileList => fileCount == 0
                ? "No files"
                : fileCount == 1
                    ? item.Files![0].Path
                    : $"{fileCount} files and folders",
            _ => string.Empty
        };
        Footer = item.CreatedAt.ToLocalTime().ToString("MMM dd, HH:mm");
    }

    public ClipboardItem Model { get; }
    public string Title { get; }
    public string Subtitle { get; }
    public string TypeGlyph { get; }
    public string TimeLabel { get; }
    public string PreviewText { get; }
    public string PreviewDetails { get; }
    public string Footer { get; }
    
    private BitmapSource? _previewImage;
    private bool _previewImageLoaded;
    public BitmapSource? PreviewImage
    {
        get
        {
            if (!_previewImageLoaded)
            {
                _previewImageLoaded = true;
                _previewImage = TryCreatePreviewImage();
            }
            return _previewImage;
        }
    }
    public Visibility IsTextPreviewVisible => Model.Type is ClipboardItemType.Text or ClipboardItemType.RichText ? Visibility.Visible : Visibility.Collapsed;
    public Visibility IsImagePreviewVisible => Model.Type == ClipboardItemType.Image ? Visibility.Visible : Visibility.Collapsed;
    public Visibility IsFilePreviewVisible => Model.Type == ClipboardItemType.FileList ? Visibility.Visible : Visibility.Collapsed;

    private static string FormatSize(long sizeInBytes)
    {
        string[] units = ["B", "KB", "MB", "GB"];
        double size = sizeInBytes;
        var unitIndex = 0;

        while (size >= 1024 && unitIndex < units.Length - 1)
        {
            size /= 1024;
            unitIndex++;
        }

        return $"{size:0.#} {units[unitIndex]}";
    }

    private static BitmapSource? CreateFilePreviewImage(FileClipboardEntry? entry)
    {
        if (entry is null)
        {
            return null;
        }

        if (!entry.IsDirectory &&
            IsImageFile(entry.Path) &&
            File.Exists(entry.Path))
        {
            return LoadBitmapWithoutLock(entry.Path);
        }

        var fileInfo = new SHFILEINFO();
        const uint flags = 0x100 | 0x1;
        SHGetFileInfo(
            entry.Path,
            entry.IsDirectory ? 0x10u : 0u,
            ref fileInfo,
            (uint)Marshal.SizeOf<SHFILEINFO>(),
            flags);

        if (fileInfo.hIcon == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            var bitmapSource = Imaging.CreateBitmapSourceFromHIcon(
                fileInfo.hIcon,
                Int32Rect.Empty,
                BitmapSizeOptions.FromWidthAndHeight(96, 96));
            bitmapSource.Freeze();
            return bitmapSource;
        }
        finally
        {
            DestroyIcon(fileInfo.hIcon);
        }
    }

    private static bool IsImageFile(string path)
    {
        var extension = Path.GetExtension(path);
        return extension is ".png" or ".jpg" or ".jpeg" or ".bmp" or ".gif" or ".webp" or ".ico";
    }

    private BitmapSource? TryCreatePreviewImage()
    {
        if (Model.Type == ClipboardItemType.Image && !string.IsNullOrWhiteSpace(Model.ImagePath) && File.Exists(Model.ImagePath))
        {
            return LoadBitmapWithoutLock(Model.ImagePath);
        }

        if (Model.Type == ClipboardItemType.FileList)
        {
            return CreateFilePreviewImage(Model.Files?.FirstOrDefault());
        }

        return null;
    }

    private static BitmapSource? LoadBitmapWithoutLock(string path)
    {
        const int maxAttempts = 8;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                var memory = new MemoryStream();
                stream.CopyTo(memory);
                memory.Position = 0;

                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.CreateOptions = BitmapCreateOptions.PreservePixelFormat;
                bitmap.StreamSource = memory;
                bitmap.EndInit();
                bitmap.Freeze();

                return bitmap;
            }
            catch (IOException) when (attempt < maxAttempts)
            {
                Thread.Sleep(25 * attempt);
            }
            catch (NotSupportedException) when (attempt < maxAttempts)
            {
                Thread.Sleep(25 * attempt);
            }
        }

        using var finalStream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var finalMemory = new MemoryStream();
        finalStream.CopyTo(finalMemory);
        finalMemory.Position = 0;

        var finalBitmap = new BitmapImage();
        finalBitmap.BeginInit();
        finalBitmap.CacheOption = BitmapCacheOption.OnLoad;
        finalBitmap.CreateOptions = BitmapCreateOptions.PreservePixelFormat;
        finalBitmap.StreamSource = finalMemory;
        finalBitmap.EndInit();
        finalBitmap.Freeze();

        return finalBitmap;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr SHGetFileInfo(
        string pszPath,
        uint dwFileAttributes,
        ref SHFILEINFO psfi,
        uint cbFileInfo,
        uint uFlags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string szTypeName;
    }
}

