using System.Drawing;
using System.Windows.Forms;

namespace Spotifly.App.Services;

/// <summary>
/// Provides a lightweight system tray icon with an explicit Exit command and developer testing toggles.
/// </summary>
public sealed class TrayIconService : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private readonly Action _onExitRequested;
    private readonly Action? _onToggleIslandRequested;
    private bool _disposed;

    public TrayIconService(Action onExitRequested, Action? onToggleIslandRequested = null)
    {
        _onExitRequested = onExitRequested;
        _onToggleIslandRequested = onToggleIslandRequested;

        var contextMenu = new ContextMenuStrip();
        
        var openItem = new ToolStripMenuItem("Spotifly (Toggle Island)");
        openItem.Font = new System.Drawing.Font(openItem.Font, System.Drawing.FontStyle.Bold);
        openItem.Click += (s, e) => _onToggleIslandRequested?.Invoke();
        contextMenu.Items.Add(openItem);

        var devToggle = new ToolStripMenuItem("Developer: Toggle Island Visibility");
        devToggle.Click += (s, e) => _onToggleIslandRequested?.Invoke();
        contextMenu.Items.Add(devToggle);

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

        _notifyIcon.DoubleClick += (s, e) => _onToggleIslandRequested?.Invoke();
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
