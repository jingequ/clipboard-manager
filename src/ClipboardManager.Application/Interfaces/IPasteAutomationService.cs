namespace ClipboardManager.Application.Interfaces;

public interface IPasteAutomationService
{
    Task PasteToWindowAsync(IntPtr windowHandle, CancellationToken cancellationToken = default);
}
