using System.IO;
using System.Text.Json;
using ClipboardManager.Application.Interfaces;
using ClipboardManager.Domain.Entities;

namespace ClipboardManager.Infrastructure.Storage;

public sealed class JsonSettingsService : ISettingsService
{
    private readonly string _settingsPath;

    public JsonSettingsService(string settingsPath)
    {
        _settingsPath = settingsPath;
    }

    public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_settingsPath))
        {
            return new AppSettings();
        }

        await using var stream = File.OpenRead(_settingsPath);
        var settings = await JsonSerializer.DeserializeAsync<AppSettings>(stream, cancellationToken: cancellationToken);
        return settings ?? new AppSettings();
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
        await using var stream = File.Create(_settingsPath);
        await JsonSerializer.SerializeAsync(stream, settings, cancellationToken: cancellationToken, options: new JsonSerializerOptions
        {
            WriteIndented = true
        });
    }
}

