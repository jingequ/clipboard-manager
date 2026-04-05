using System.IO;
using System.Windows.Forms;
using ClipboardManager.Application.Interfaces;

namespace ClipboardManager.Infrastructure.Tray;

public sealed class TrayService : ITrayService
{
    private NotifyIcon? _notifyIcon;

    public void Initialize(Action showMainWindow, Action openSettings, Action clearHistory, Action exitApplication)
    {
        _notifyIcon = new NotifyIcon
        {
            Visible = true,
            Text = "Clipboard Manager",
            Icon = LoadTrayIcon(),
            ContextMenuStrip = new ContextMenuStrip()
        };

        _notifyIcon.ContextMenuStrip.Items.Add("Open", null, (_, _) => showMainWindow());
        _notifyIcon.ContextMenuStrip.Items.Add("Settings", null, (_, _) => openSettings());
        _notifyIcon.ContextMenuStrip.Items.Add("Clear History", null, (_, _) => clearHistory());
        _notifyIcon.ContextMenuStrip.Items.Add(new ToolStripSeparator());
        _notifyIcon.ContextMenuStrip.Items.Add("Exit", null, (_, _) => exitApplication());
        _notifyIcon.DoubleClick += (_, _) => showMainWindow();
    }

    private static System.Drawing.Icon LoadTrayIcon()
    {
        var processPath = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(processPath) && File.Exists(processPath))
        {
            return System.Drawing.Icon.ExtractAssociatedIcon(processPath) ?? System.Drawing.SystemIcons.Application;
        }

        return System.Drawing.SystemIcons.Application;
    }

    public void Dispose()
    {
        if (_notifyIcon is null)
        {
            return;
        }

        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _notifyIcon = null;
    }
}
