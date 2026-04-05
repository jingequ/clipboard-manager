using ClipboardManager.Domain.Entities;

namespace ClipboardManager.Application.Interfaces;

public interface IClipboardReplayService
{
    Task ReplayAsync(ClipboardItem item, CancellationToken cancellationToken = default);
}
