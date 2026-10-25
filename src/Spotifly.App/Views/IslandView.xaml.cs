using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;
using Spotifly.App.Models;
using Spotifly.App.Services;

namespace Spotifly.App.Views;

/// <summary>
/// Borderless, transparent, topmost window hosting the semicircular dynamic island.
/// Handles resolution changes, DPI adjustments, non-activating window styles,
/// and transparent hit-test passthrough.
/// </summary>
public partial class IslandView : Window
{
    private readonly IOverlayPositionService _positionService;
    private readonly ILoggerService? _logger;
    private bool _isDisposed;

    // Win32 constants for click-through and non-activating window management
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_NOACTIVATE = 0x08000000;

    [DllImport("user32.dll", EntryPoint = "GetWindowLong")]
    private static extern IntPtr GetWindowLongPtr32(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")]
    private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLong")]
    private static extern int SetWindowLong32(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")]
    private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    private static IntPtr GetWindowLong(IntPtr hWnd, int nIndex) =>
        IntPtr.Size == 8 ? GetWindowLongPtr64(hWnd, nIndex) : GetWindowLongPtr32(hWnd, nIndex);

    private static IntPtr SetWindowLong(IntPtr hWnd, int nIndex, IntPtr dwNewLong) =>
        IntPtr.Size == 8 ? SetWindowLongPtr64(hWnd, nIndex, dwNewLong) : new IntPtr(SetWindowLong32(hWnd, nIndex, dwNewLong.ToInt32()));

    public IslandView(IOverlayPositionService positionService, ILoggerService? logger = null)
    {
        _positionService = positionService;
        _logger = logger;

        InitializeComponent();

        ApplyDimensions();

        // Listen for system display/resolution changes
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd != IntPtr.Zero)
        {
            // Set extended style WS_EX_TOOLWINDOW and WS_EX_NOACTIVATE to prevent stealing focus
            var exStyle = GetWindowLong(hwnd, GWL_EXSTYLE).ToInt64();
            exStyle |= WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
            SetWindowLong(hwnd, GWL_EXSTYLE, new IntPtr(exStyle));

            // Hook HwndSource for WM_NCHITTEST transparent passthrough
            var source = HwndSource.FromHwnd(hwnd);
            source?.AddHook(WndProc);
        }

        Reposition();
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WM_NCHITTEST = 0x0084;
        const int HTTRANSPARENT = -1;
        const int HTCLIENT = 1;

        if (msg == WM_NCHITTEST)
        {
            // Convert screen coordinates to window DIP coordinates
            int x = (short)(lParam.ToInt64() & 0xFFFF);
            int y = (short)((lParam.ToInt64() >> 16) & 0xFFFF);
            var screenPoint = new System.Windows.Point(x, y);

            try
            {
                var pointInWindow = PointFromScreen(screenPoint);

                // Hit test our visual tree to see if the click is over the visible island
                var hitResult = VisualTreeHelper.HitTest(this, pointInWindow);
                if (hitResult == null || hitResult.VisualHit == RootLayout)
                {
                    // Transparent area outside the semicircle: pass clicks straight through to desktop/windows behind
                    handled = true;
                    return new IntPtr(HTTRANSPARENT);
                }

                // Hit is on the semicircle island itself: handle normally
                handled = true;
                return new IntPtr(HTCLIENT);
            }
            catch
            {
                // Fallback: don't intercept if coordinates cannot be transformed
            }
        }

        return IntPtr.Zero;
    }

    public void ApplyDimensions()
    {
        var d = _positionService.Dimensions;
        Width = d.WindowWidth;
        Height = d.WindowHeight;

        IslandContainer.Width = d.CompactWidth;
        IslandContainer.Height = d.CompactHeight;
        IslandContainer.CornerRadius = new CornerRadius(0, 0, d.CornerRadius, d.CornerRadius);
    }

    public void Reposition()
    {
        try
        {
            var workArea = SystemParameters.WorkArea;
            var (left, top) = _positionService.CalculateWindowPosition(
                Width,
                Height,
                workArea.Width,
                workArea.Top,
                workArea.Left);

            Left = left;
            Top = top;

            _logger?.LogDebug($"Repositioned IslandView to Left={Left}, Top={Top} on screen width={workArea.Width}.");
        }
        catch (Exception ex)
        {
            _logger?.LogError("Failed to reposition IslandView.", ex);
        }
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
    {
        _logger?.LogInformation("Display settings or resolution changed event received.");
        Dispatcher.InvokeAsync(Reposition);
    }

    public void ToggleVisibility()
    {
        if (Visibility == Visibility.Visible)
        {
            Hide();
            _logger?.LogInformation("IslandView hidden via toggle.");
        }
        else
        {
            Reposition();
            Show();
            _logger?.LogInformation("IslandView made visible via toggle.");
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        if (!_isDisposed)
        {
            _isDisposed = true;
            SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        }

        base.OnClosed(e);
    }
}
