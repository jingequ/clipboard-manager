namespace ClipboardManager.Application.Interfaces;

public interface ILogger
{
    void Info(string message);
    void Error(string message, Exception? exception = null);
}
