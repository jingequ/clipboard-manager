namespace ClipboardManager.Domain.Entities;

public sealed class FileClipboardEntry
{
    public required string Path { get; init; }

    public required string Name { get; init; }

    public bool IsDirectory { get; init; }

    public string? Extension { get; init; }
}
