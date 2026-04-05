namespace ClipboardManager.Application.Interfaces;

public interface IHotkeyService
{
    event EventHandler? HotkeyPressed;
    bool Register(IntPtr handle, string gesture);
    void Unregister(IntPtr handle);
}
