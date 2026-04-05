using ClipboardManager.Domain.Entities;

namespace ClipboardManager.Application.Interfaces;

public interface ISettingsService
{
    Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default);
}
