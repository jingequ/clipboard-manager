using ClipboardManager.Application.Interfaces;
using Microsoft.Win32;

namespace ClipboardManager.Infrastructure.Startup;

public sealed class RegistryStartupService : IStartupService
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "ClipboardManager";
    private readonly string _executablePath;

    public RegistryStartupService(string executablePath)
    {
        _executablePath = executablePath;
    }

    public bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, false);
        return string.Equals(key?.GetValue(ValueName)?.ToString(), $"\"{_executablePath}\"", StringComparison.OrdinalIgnoreCase);
    }

    public void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, true) ?? Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled)
        {
            key.SetValue(ValueName, $"\"{_executablePath}\"");
        }
        else
        {
            key.DeleteValue(ValueName, false);
        }
    }
}
