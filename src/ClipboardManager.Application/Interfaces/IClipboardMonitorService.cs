using ClipboardManager.Domain.Entities;

namespace ClipboardManager.Application.Interfaces;

public interface IClipboardMonitorService
{
    event EventHandler<ClipboardItem>? ClipboardItemCaptured;
    void Start();
    void Stop();
    void Pause();
    void Resume();
}
