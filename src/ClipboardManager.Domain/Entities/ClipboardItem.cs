using ClipboardManager.Domain.Enums;

namespace ClipboardManager.Domain.Entities;

public sealed class ClipboardItem
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public ClipboardItemType Type { get; init; }
    public string Summary { get; set; } = string.Empty;
    public string SearchText { get; set; } = string.Empty;
    public string? TextContent { get; set; }
    public string? HtmlContent { get; set; }

    public string? RtfContent { get; set; }
    public string? ImagePath { get; set; }
    public IReadOnlyList<FileClipboardEntry>? Files { get; set; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.Now;
    public DateTimeOffset? ExpiresAt { get; set; }
    public string? ContentHash { get; set; }
    public string? DisplayMetadata { get; set; }
}
