namespace ClipboardManager.Application.Interfaces;

public interface ITrayService : IDisposable
{
    void Initialize(Action showMainWindow, Action openSettings, Action clearHistory, Action exitApplication);
}
