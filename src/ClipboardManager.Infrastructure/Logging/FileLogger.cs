using System.IO;
using ClipboardManager.Application.Interfaces;

namespace ClipboardManager.Infrastructure.Logging;

public sealed class FileLogger : ILogger
{
    private readonly string _logPath;
    private readonly object _gate = new();

    public FileLogger(string baseDirectory)
    {
        Directory.CreateDirectory(baseDirectory);
        _logPath = Path.Combine(baseDirectory, "app.log");
    }

    public void Info(string message) => Write("INFO", message, null);

    public void Error(string message, Exception? exception = null) => Write("ERROR", message, exception);

    private void Write(string level, string message, Exception? exception)
    {
        var line = $"{DateTimeOffset.Now:O} [{level}] {message}{Environment.NewLine}{exception}{Environment.NewLine}";
        lock (_gate)
        {
            File.AppendAllText(_logPath, line);
        }
    }
}

