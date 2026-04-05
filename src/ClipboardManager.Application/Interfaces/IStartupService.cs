namespace ClipboardManager.Application.Interfaces;

public interface IStartupService
{
    bool IsEnabled();
    void SetEnabled(bool enabled);
}
