using ClipboardManager.Domain.Entities;

namespace ClipboardManager.Application.Interfaces;

public interface IClipboardHistoryService
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ClipboardItem>> SearchAsync(string query, int limit = 100, CancellationToken cancellationToken = default);
    Task<int> GetTotalCountAsync(string query, CancellationToken cancellationToken = default);
    Task AddOrUpdateAsync(ClipboardItem item, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
    Task ClearAsync(CancellationToken cancellationToken = default);
    Task<int> DeleteRecentAsync(DateTimeOffset since, CancellationToken cancellationToken = default);
    Task<int> DeleteLatestAsync(int count, CancellationToken cancellationToken = default);
    Task TouchAsync(Guid id, CancellationToken cancellationToken = default);
    Task<int> PruneExpiredAsync(CancellationToken cancellationToken = default);
    Task<int> EnforceMaxItemsAsync(int maxItems, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> GetReferencedImagePathsAsync(CancellationToken cancellationToken = default);
}
