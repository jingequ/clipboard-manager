using System.Runtime.InteropServices;
using System.Windows.Interop;
using ClipboardManager.Application.Interfaces;
using ClipboardManager.Domain.Entities;

namespace ClipboardManager.Infrastructure.Clipboard;

public sealed class ClipboardMonitorService : IClipboardMonitorService, IDisposable
{
    private readonly ClipboardSnapshotFactory _snapshotFactory;
    private readonly Func<bool> _captureImages;
    private readonly Func<bool> _captureFiles;
    private readonly Func<int> _retentionDays;
    private HwndSource? _source;
    private bool _isPaused;

    public ClipboardMonitorService(
        ClipboardSnapshotFactory snapshotFactory,
        Func<bool> captureImages,
        Func<bool> captureFiles,
        Func<int> retentionDays)
    {
        _snapshotFactory = snapshotFactory;
        _captureImages = captureImages;
        _captureFiles = captureFiles;
        _retentionDays = retentionDays;
    }

    public event EventHandler<ClipboardItem>? ClipboardItemCaptured;

    public void Start()
    {
        if (_source is not null)
        {
            return;
        }

        var parameters = new HwndSourceParameters("ClipboardMonitorWindow")
        {
            Width = 0,
            Height = 0,
            WindowStyle = 0
        };
        _source = new HwndSource(parameters);
        _source.AddHook(WndProc);
        AddClipboardFormatListener(_source.Handle);
    }

    public void Stop()
    {
        if (_source is null)
        {
            return;
        }

        RemoveClipboardFormatListener(_source.Handle);
        _source.RemoveHook(WndProc);
        _source.Dispose();
        _source = null;
    }

    public void Dispose() => Stop();

    public void Pause() => _isPaused = true;

    public void Resume() => _isPaused = false;

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WM_CLIPBOARDUPDATE = 0x031D;
        if (msg == WM_CLIPBOARDUPDATE)
        {
            if (_isPaused)
            {
                return IntPtr.Zero;
            }

            var item = _snapshotFactory.CreateFromCurrentClipboard(_captureImages(), _captureFiles(), _retentionDays());
            if (item is not null)
            {
                ClipboardItemCaptured?.Invoke(this, item);
            }
        }

        return IntPtr.Zero;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool AddClipboardFormatListener(IntPtr hwnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RemoveClipboardFormatListener(IntPtr hwnd);
}
