using System.Runtime.InteropServices;
using System.Windows.Forms;
using ClipboardManager.Application.Interfaces;

namespace ClipboardManager.Infrastructure.Hotkeys;

public sealed class WindowPasteAutomationService : IPasteAutomationService
{
    public async Task PasteToWindowAsync(IntPtr windowHandle, CancellationToken cancellationToken = default)
    {
        if (windowHandle == IntPtr.Zero)
        {
            return;
        }

        if (IsIconic(windowHandle))
        {
            ShowWindow(windowHandle, 9);
        }

        SetForegroundWindow(windowHandle);
        await Task.Delay(80, cancellationToken);
        SendKeys.SendWait("^v");
    }

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hWnd);
}
