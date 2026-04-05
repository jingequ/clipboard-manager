using System.IO;
using System.Text.Json;
using ClipboardManager.Application.Interfaces;
using ClipboardManager.Domain.Entities;
using ClipboardManager.Domain.Enums;
using Microsoft.Data.Sqlite;

namespace ClipboardManager.Infrastructure.Storage;

public sealed class SqliteClipboardHistoryService : IClipboardHistoryService
{
    private readonly string _connectionString;

    public SqliteClipboardHistoryService(string databasePath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
        _connectionString = $"Data Source={databasePath}";
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText =
            """
            CREATE TABLE IF NOT EXISTS clipboard_items (
                id TEXT PRIMARY KEY,
                type INTEGER NOT NULL,
                summary TEXT NOT NULL,
                search_text TEXT NOT NULL,
                text_content TEXT NULL,
                html_content TEXT NULL,
                rtf_content TEXT NULL,
                image_path TEXT NULL,
                file_list_json TEXT NULL,
                created_at TEXT NOT NULL,
                expires_at TEXT NULL,
                content_hash TEXT NULL,
                display_metadata TEXT NULL
            );
            CREATE INDEX IF NOT EXISTS idx_clipboard_items_created_at ON clipboard_items(created_at DESC);
            CREATE INDEX IF NOT EXISTS idx_clipboard_items_expires_at ON clipboard_items(expires_at);
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
        await EnsureColumnExistsAsync(connection, "rtf_content", "TEXT", cancellationToken);
    }

    public async Task<IReadOnlyList<ClipboardItem>> SearchAsync(string query, int limit = 100, CancellationToken cancellationToken = default)
    {
        var items = new List<ClipboardItem>();
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT id, type, summary, search_text, text_content, html_content, rtf_content, image_path, file_list_json, created_at, expires_at, content_hash, display_metadata
            FROM clipboard_items
            WHERE (@query = '' OR summary LIKE @pattern OR search_text LIKE @pattern)
            ORDER BY datetime(created_at) DESC
            LIMIT @limit;
            """;
        command.Parameters.AddWithValue("@query", query ?? string.Empty);
        command.Parameters.AddWithValue("@pattern", $"%{query}%");
        command.Parameters.AddWithValue("@limit", limit);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(new ClipboardItem
            {
                Id = Guid.Parse(reader.GetString(0)),
                Type = (ClipboardItemType)reader.GetInt32(1),
                Summary = reader.GetString(2),
                SearchText = reader.GetString(3),
                TextContent = reader.IsDBNull(4) ? null : reader.GetString(4),
                HtmlContent = reader.IsDBNull(5) ? null : reader.GetString(5),
                RtfContent = reader.IsDBNull(6) ? null : reader.GetString(6),
                ImagePath = reader.IsDBNull(7) ? null : reader.GetString(7),
                Files = reader.IsDBNull(8) ? null : JsonSerializer.Deserialize<List<FileClipboardEntry>>(reader.GetString(8)),
                CreatedAt = DateTimeOffset.Parse(reader.GetString(9)),
                ExpiresAt = reader.IsDBNull(10) ? null : DateTimeOffset.Parse(reader.GetString(10)),
                ContentHash = reader.IsDBNull(11) ? null : reader.GetString(11),
                DisplayMetadata = reader.IsDBNull(12) ? null : reader.GetString(12)
            });
        }

        return items;
    }

    public async Task AddOrUpdateAsync(ClipboardItem item, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        var existing = await FindExistingByHashAsync(connection, item, cancellationToken);
        var itemToPersist = existing is null
            ? item
            : new ClipboardItem
            {
                Id = existing.Id,
                Type = item.Type,
                Summary = item.Summary,
                SearchText = item.SearchText,
                TextContent = item.TextContent,
                HtmlContent = item.HtmlContent,
                RtfContent = item.RtfContent,
                ImagePath = ResolveImagePath(item, existing),
                Files = item.Files,
                CreatedAt = DateTimeOffset.Now,
                ExpiresAt = item.ExpiresAt,
                ContentHash = item.ContentHash,
                DisplayMetadata = item.DisplayMetadata
            };

        var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT OR REPLACE INTO clipboard_items (
                id, type, summary, search_text, text_content, html_content, rtf_content, image_path, file_list_json, created_at, expires_at, content_hash, display_metadata
            )
            VALUES (
                @id, @type, @summary, @searchText, @textContent, @htmlContent, @rtfContent, @imagePath, @fileListJson, @createdAt, @expiresAt, @contentHash, @displayMetadata
            );
            """;
        command.Parameters.AddWithValue("@id", itemToPersist.Id.ToString());
        command.Parameters.AddWithValue("@type", (int)itemToPersist.Type);
        command.Parameters.AddWithValue("@summary", itemToPersist.Summary);
        command.Parameters.AddWithValue("@searchText", itemToPersist.SearchText);
        command.Parameters.AddWithValue("@textContent", (object?)itemToPersist.TextContent ?? DBNull.Value);
        command.Parameters.AddWithValue("@htmlContent", (object?)itemToPersist.HtmlContent ?? DBNull.Value);
        command.Parameters.AddWithValue("@rtfContent", (object?)itemToPersist.RtfContent ?? DBNull.Value);
        command.Parameters.AddWithValue("@imagePath", (object?)itemToPersist.ImagePath ?? DBNull.Value);
        command.Parameters.AddWithValue("@fileListJson", itemToPersist.Files is null ? DBNull.Value : JsonSerializer.Serialize(itemToPersist.Files));
        command.Parameters.AddWithValue("@createdAt", itemToPersist.CreatedAt.ToString("O"));
        command.Parameters.AddWithValue("@expiresAt", itemToPersist.ExpiresAt?.ToString("O") ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@contentHash", (object?)itemToPersist.ContentHash ?? DBNull.Value);
        command.Parameters.AddWithValue("@displayMetadata", (object?)itemToPersist.DisplayMetadata ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        var imagePaths = await QueryImagePathsAsync(connection, "SELECT image_path FROM clipboard_items WHERE id = @id", ("@id", id.ToString()), cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM clipboard_items WHERE id = @id";
        command.Parameters.AddWithValue("@id", id.ToString());
        await command.ExecuteNonQueryAsync(cancellationToken);
        DeleteFiles(imagePaths);
    }

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        var imagePaths = await QueryImagePathsAsync(connection, "SELECT image_path FROM clipboard_items WHERE image_path IS NOT NULL", null, cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM clipboard_items";
        await command.ExecuteNonQueryAsync(cancellationToken);
        DeleteFiles(imagePaths);
    }

    public async Task<int> DeleteRecentAsync(DateTimeOffset since, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        var imagePaths = await QueryImagePathsAsync(
            connection,
            "SELECT image_path FROM clipboard_items WHERE image_path IS NOT NULL AND datetime(created_at) >= datetime(@since)",
            ("@since", since.ToString("O")),
            cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM clipboard_items WHERE datetime(created_at) >= datetime(@since)";
        command.Parameters.AddWithValue("@since", since.ToString("O"));
        var deleted = await command.ExecuteNonQueryAsync(cancellationToken);
        DeleteFiles(imagePaths);
        return deleted;
    }

    public async Task TouchAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = "UPDATE clipboard_items SET created_at = @createdAt WHERE id = @id";
        command.Parameters.AddWithValue("@createdAt", DateTimeOffset.Now.ToString("O"));
        command.Parameters.AddWithValue("@id", id.ToString());
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<int> PruneExpiredAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        var imagePaths = await QueryImagePathsAsync(
            connection,
            "SELECT image_path FROM clipboard_items WHERE image_path IS NOT NULL AND expires_at IS NOT NULL AND datetime(expires_at) <= datetime('now')",
            null,
            cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM clipboard_items WHERE expires_at IS NOT NULL AND datetime(expires_at) <= datetime('now')";
        var deleted = await command.ExecuteNonQueryAsync(cancellationToken);
        DeleteFiles(imagePaths);
        return deleted;
    }

    public async Task<int> EnforceMaxItemsAsync(int maxItems, CancellationToken cancellationToken = default)
    {
        if (maxItems <= 0)
        {
            return 0;
        }

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        var imagePaths = await QueryImagePathsAsync(
            connection,
            """
            SELECT image_path
            FROM clipboard_items
            WHERE id IN (
                SELECT id
                FROM clipboard_items
                ORDER BY datetime(created_at) DESC
                LIMIT -1 OFFSET @maxItems
            );
            """,
            ("@maxItems", maxItems),
            cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText =
            """
            DELETE FROM clipboard_items
            WHERE id IN (
                SELECT id
                FROM clipboard_items
                ORDER BY datetime(created_at) DESC
                LIMIT -1 OFFSET @maxItems
            );
            """;
        command.Parameters.AddWithValue("@maxItems", maxItems);
        var deleted = await command.ExecuteNonQueryAsync(cancellationToken);
        DeleteFiles(imagePaths);
        return deleted;
    }

    public async Task<IReadOnlyList<string>> GetReferencedImagePathsAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        return await QueryImagePathsAsync(connection, "SELECT image_path FROM clipboard_items WHERE image_path IS NOT NULL", null, cancellationToken);
    }

    private static string? ResolveImagePath(ClipboardItem incoming, ClipboardItem existing)
    {
        if (incoming.Type != ClipboardItemType.Image)
        {
            return incoming.ImagePath;
        }

        if (!string.IsNullOrWhiteSpace(existing.ImagePath) && File.Exists(existing.ImagePath))
        {
            if (!string.Equals(existing.ImagePath, incoming.ImagePath, StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(incoming.ImagePath) &&
                File.Exists(incoming.ImagePath))
            {
                File.Delete(incoming.ImagePath);
            }

            return existing.ImagePath;
        }

        return incoming.ImagePath;
    }

    private static void DeleteFiles(IEnumerable<string> imagePaths)
    {
        foreach (var imagePath in imagePaths.Where(path => !string.IsNullOrWhiteSpace(path)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                if (File.Exists(imagePath))
                {
                    File.Delete(imagePath);
                }
            }
            catch (IOException)
            {
                // Best-effort cleanup: if a preview is still holding the file,
                // keep the database operation successful and clean it up later.
            }
            catch (UnauthorizedAccessException)
            {
                // Keep history operations resilient even when a cache file cannot be removed now.
            }
        }
    }

    private static async Task<ClipboardItem?> FindExistingByHashAsync(SqliteConnection connection, ClipboardItem item, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(item.ContentHash))
        {
            return null;
        }

        var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT id, type, summary, search_text, text_content, html_content, image_path, file_list_json, created_at, expires_at, content_hash, display_metadata
            , rtf_content
            FROM clipboard_items
            WHERE type = @type AND content_hash = @contentHash
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("@type", (int)item.Type);
        command.Parameters.AddWithValue("@contentHash", item.ContentHash);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new ClipboardItem
        {
            Id = Guid.Parse(reader.GetString(0)),
            Type = (ClipboardItemType)reader.GetInt32(1),
            Summary = reader.GetString(2),
            SearchText = reader.GetString(3),
            TextContent = reader.IsDBNull(4) ? null : reader.GetString(4),
            HtmlContent = reader.IsDBNull(5) ? null : reader.GetString(5),
            ImagePath = reader.IsDBNull(6) ? null : reader.GetString(6),
            Files = reader.IsDBNull(7) ? null : JsonSerializer.Deserialize<List<FileClipboardEntry>>(reader.GetString(7)),
            CreatedAt = DateTimeOffset.Parse(reader.GetString(8)),
            ExpiresAt = reader.IsDBNull(9) ? null : DateTimeOffset.Parse(reader.GetString(9)),
            ContentHash = reader.IsDBNull(10) ? null : reader.GetString(10),
            DisplayMetadata = reader.IsDBNull(11) ? null : reader.GetString(11),
            RtfContent = reader.IsDBNull(12) ? null : reader.GetString(12)
        };
    }

    private static async Task EnsureColumnExistsAsync(SqliteConnection connection, string columnName, string columnDefinition, CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.CommandText = "PRAGMA table_info(clipboard_items)";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            if (string.Equals(reader.GetString(1), columnName, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
        }

        await reader.DisposeAsync();
        var alter = connection.CreateCommand();
        alter.CommandText = $"ALTER TABLE clipboard_items ADD COLUMN {columnName} {columnDefinition}";
        await alter.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<IReadOnlyList<string>> QueryImagePathsAsync(
        SqliteConnection connection,
        string sql,
        (string Name, object Value)? parameter,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        if (parameter is { } value)
        {
            command.Parameters.AddWithValue(value.Name, value.Value);
        }

        var imagePaths = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            if (!reader.IsDBNull(0))
            {
                imagePaths.Add(reader.GetString(0));
            }
        }

        return imagePaths;
    }
}

