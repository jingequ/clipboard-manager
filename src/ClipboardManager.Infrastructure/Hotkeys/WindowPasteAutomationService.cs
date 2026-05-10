using System.Runtime.InteropServices;
using System.Windows.Forms;
using ClipboardManager.Application.Interfaces;

namespace ClipboardManager.Infrastructure.Hotkeys;

public sealed class WindowPasteAutomationService : IPasteAutomationService
{
    private readonly ILogger _logger;

    public WindowPasteAutomationService(ILogger logger)
    {
        _logger = logger;
    }
    public async Task PasteToWindowAsync(IntPtr windowHandle, CancellationToken cancellationToken = default)
    {
        _logger.Info($"PasteToWindowAsync started for handle {windowHandle}");
        if (windowHandle == IntPtr.Zero)
        {
            _logger.Info("Handle is zero, aborting paste.");
            return;
        }

        if (IsIconic(windowHandle))
        {
            ShowWindow(windowHandle, 9);
        }

        _logger.Info("Forcing foreground window.");
        ForceForegroundWindow(windowHandle);
        _logger.Info("Waiting 220ms before pasting.");
        await Task.Delay(220, cancellationToken);
        _logger.Info("Sending Ctrl+V via SendInput/SendKeys.");
        SendCtrlV();
        _logger.Info("SendCtrlV finished.");
    }

    private static void SendCtrlV()
    {
        const uint keyEventKeyUp = 0x0002;
        const ushort vkControl = 0x11;
        const ushort vkV = 0x56;

        var inputs = new System.Collections.Generic.List<INPUT>();

        // Only release modifiers if they are currently held down
        if ((GetAsyncKeyState(0xA0) & 0x8000) != 0) inputs.Add(CreateKeyboardInput(0xA0, keyEventKeyUp)); // LShift
        if ((GetAsyncKeyState(0xA1) & 0x8000) != 0) inputs.Add(CreateKeyboardInput(0xA1, keyEventKeyUp)); // RShift
        if ((GetAsyncKeyState(0xA4) & 0x8000) != 0) inputs.Add(CreateKeyboardInput(0xA4, keyEventKeyUp)); // LMenu
        if ((GetAsyncKeyState(0xA5) & 0x8000) != 0) inputs.Add(CreateKeyboardInput(0xA5, keyEventKeyUp)); // RMenu
        if ((GetAsyncKeyState(0x5B) & 0x8000) != 0) inputs.Add(CreateKeyboardInput(0x5B, keyEventKeyUp)); // LWin
        if ((GetAsyncKeyState(0x5C) & 0x8000) != 0) inputs.Add(CreateKeyboardInput(0x5C, keyEventKeyUp)); // RWin

        inputs.Add(CreateKeyboardInput(vkControl, 0));
        inputs.Add(CreateKeyboardInput(vkV, 0));
        var result = SendInput((uint)inputs.Count, inputs.ToArray(), Marshal.SizeOf<INPUT>());
        if (result == 0)
        {
            SendKeys.SendWait("^v");
        }
    }

    private static void ForceForegroundWindow(IntPtr hWnd)
    {
        uint targetThread = GetWindowThreadProcessId(hWnd, out _);
        uint currentThread = GetCurrentThreadId();
        if (targetThread != currentThread)
        {
            AttachThreadInput(currentThread, targetThread, true);
            SetForegroundWindow(hWnd);
            AttachThreadInput(currentThread, targetThread, false);
        }
        else
        {
            SetForegroundWindow(hWnd);
        }
    }

    private static INPUT CreateKeyboardInput(ushort virtualKey, uint flags)
    {
        return new INPUT
        {
            Type = 1,
            Data = new INPUTUNION
            {
                Keyboard = new KEYBDINPUT
                {
                    VirtualKey = virtualKey,
                    ScanCode = 0,
                    Flags = flags,
                    Time = 0,
                    ExtraInfo = UIntPtr.Zero
                }
            }
        };
    }

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint Type;
        public INPUTUNION Data;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct INPUTUNION
    {
        [FieldOffset(0)]
        public KEYBDINPUT Keyboard;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort VirtualKey;
        public ushort ScanCode;
        public uint Flags;
        public uint Time;
        public UIntPtr ExtraInfo;
    }
}
