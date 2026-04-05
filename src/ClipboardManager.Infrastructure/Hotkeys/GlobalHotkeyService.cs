using System.Runtime.InteropServices;
using System.Windows.Interop;
using ClipboardManager.Application.Interfaces;

namespace ClipboardManager.Infrastructure.Hotkeys;

public sealed class GlobalHotkeyService : IHotkeyService
{
    private const int HotkeyId = 4096;
    private HwndSource? _source;

    public event EventHandler? HotkeyPressed;

    public bool Register(IntPtr handle, string gesture)
    {
        var (modifiers, key) = ParseGesture(gesture);
        if (key == 0 || modifiers == 0)
        {
            return false;
        }

        _source = HwndSource.FromHwnd(handle);
        _source?.AddHook(WndProc);
        return RegisterHotKey(handle, HotkeyId, modifiers, key);
    }

    public void Unregister(IntPtr handle)
    {
        UnregisterHotKey(handle, HotkeyId);
        _source?.RemoveHook(WndProc);
        _source = null;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WM_HOTKEY = 0x0312;
        if (msg == WM_HOTKEY && wParam.ToInt32() == HotkeyId)
        {
            handled = true;
            HotkeyPressed?.Invoke(this, EventArgs.Empty);
        }

        return IntPtr.Zero;
    }

    private static (uint Modifiers, uint Key) ParseGesture(string gesture)
    {
        uint modifiers = 0;
        uint key = 0;

        foreach (var token in gesture.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            switch (token.ToUpperInvariant())
            {
                case "CTRL":
                    modifiers |= 0x0002;
                    break;
                case "SHIFT":
                    modifiers |= 0x0004;
                    break;
                case "ALT":
                    modifiers |= 0x0001;
                    break;
                case "WIN":
                    modifiers |= 0x0008;
                    break;
                default:
                    if (token.Length == 1)
                    {
                        key = char.ToUpperInvariant(token[0]);
                    }
                    else if (token.StartsWith("F", StringComparison.OrdinalIgnoreCase) &&
                             int.TryParse(token[1..], out var functionKeyNumber) &&
                             functionKeyNumber is >= 1 and <= 24)
                    {
                        key = (uint)(0x70 + functionKeyNumber - 1);
                    }
                    break;
            }
        }

        return (modifiers, key);
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
