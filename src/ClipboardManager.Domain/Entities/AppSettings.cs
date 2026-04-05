namespace ClipboardManager.Domain.Entities;

public sealed class AppSettings
{
    public bool LaunchAtStartup { get; set; }
    public bool CaptureImages { get; set; } = true;
    public bool CaptureFiles { get; set; } = true;
    public int RetentionDays { get; set; } = 30;
    public int MaxItems { get; set; } = 500;
    public string HotkeyGesture { get; set; } = "Alt+C";
}
