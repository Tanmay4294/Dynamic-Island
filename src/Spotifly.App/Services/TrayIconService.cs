using System.Drawing;
using System.Windows.Forms;

namespace Spotifly.App.Services;

/// <summary>
/// Provides a lightweight, reliable system tray icon with an explicit Exit command.
/// Ensures the background application can be shut down cleanly even when no windows are open.
/// </summary>
public sealed class TrayIconService : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private readonly Action _onExitRequested;
    private readonly Action? _onOpenRequested;
    private bool _disposed;

    public TrayIconService(Action onExitRequested, Action? onOpenRequested = null)
    {
        _onExitRequested = onExitRequested;
        _onOpenRequested = onOpenRequested;

        var contextMenu = new ContextMenuStrip();
        
        var openItem = new ToolStripMenuItem("Spotifly (Island)");
        openItem.Font = new System.Drawing.Font(openItem.Font, System.Drawing.FontStyle.Bold);
        openItem.Click += (s, e) => _onOpenRequested?.Invoke();
        contextMenu.Items.Add(openItem);

        contextMenu.Items.Add(new ToolStripSeparator());

        var exitItem = new ToolStripMenuItem("Exit Spotifly");
        exitItem.Click += (s, e) => _onExitRequested();
        contextMenu.Items.Add(exitItem);

        _notifyIcon = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "Spotifly - Windows Dynamic Island",
            ContextMenuStrip = contextMenu,
            Visible = true
        };

        _notifyIcon.DoubleClick += (s, e) => _onOpenRequested?.Invoke();
    }

    public void ShowNotification(string title, string message, ToolTipIcon icon = ToolTipIcon.Info)
    {
        if (!_disposed)
        {
            _notifyIcon.ShowBalloonTip(3000, title, message, icon);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _notifyIcon.Visible = false;
        _notifyIcon.ContextMenuStrip?.Dispose();
        _notifyIcon.Dispose();
    }
}
