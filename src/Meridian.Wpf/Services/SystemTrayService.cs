using System;
using System.Drawing;
using System.Windows;
using System.Windows.Forms;
using Meridian.Ui.Services.Contracts;

namespace Meridian.Wpf.Services;

/// <summary>
/// Interface for system tray integration with balloon notifications and status icons.
/// Provides minimize-to-tray, connection status indication, and notification display.
/// </summary>
public interface ISystemTrayService : IDisposable
{
    /// <summary>
    /// Initializes the system tray with the given main window.
    /// Sets up minimize-to-tray behavior, context menu, and icon management.
    /// </summary>
    void Initialize(Window mainWindow);

    /// <summary>
    /// Shows a balloon notification in the system tray.
    /// </summary>
    /// <param name="title">Notification title.</param>
    /// <param name="message">Notification message.</param>
    /// <param name="icon">Icon to display (Info, Warning, Error, or None).</param>
    /// <param name="durationMs">Duration to display balloon in milliseconds (default 3000).</param>
    void ShowBalloonTip(string title, string message, ToolTipIcon icon = ToolTipIcon.Info, int durationMs = 3000);

    /// <summary>
    /// Updates the system tray icon and tooltip based on connection health status.
    /// Green = Connected, Amber = Reconnecting, Red = Disconnected, Gray = Unknown.
    /// </summary>
    void UpdateHealthStatus(ConnectionStatus status);
}

/// <summary>
/// WPF system tray integration service.
/// Manages NotifyIcon, balloon notifications, and connection status visualization.
/// Implements minimize-to-tray and quick-access context menu.
/// </summary>
public sealed class SystemTrayService : ISystemTrayService
{
    private NotifyIcon? _notifyIcon;
    private Window? _mainWindow;
    private bool _isDisposed;

    private static readonly Icon GreenIcon = CreateStatusIcon(Color.Green);
    private static readonly Icon AmberIcon = CreateStatusIcon(Color.Orange);
    private static readonly Icon RedIcon = CreateStatusIcon(Color.Red);
    private static readonly Icon GrayIcon = CreateStatusIcon(Color.Gray);

    /// <summary>
    /// Creates a 16x16 icon filled with a solid color and labeled with 'M'.
    /// </summary>
    private static Icon CreateStatusIcon(Color color)
    {
        var bmp = new Bitmap(16, 16);
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(color);
            g.DrawString("M", new Font("Arial", 8, FontStyle.Bold), Brushes.White, 1f, 2f);
        }

        var handle = bmp.GetHicon();
        var icon = Icon.FromHandle(handle);
        return icon;
    }

    /// <summary>
    /// Initializes the system tray icon and event handlers.
    /// </summary>
    public void Initialize(Window mainWindow)
    {
        if (_notifyIcon != null)
            return;

        _mainWindow = mainWindow;

        _notifyIcon = new NotifyIcon
        {
            Text = "Meridian",
            Icon = GrayIcon,
            Visible = true
        };

        // Build context menu
        var contextMenu = new ContextMenuStrip();
        contextMenu.Items.Add("Open Meridian", null, (s, e) => RestoreWindow());
        contextMenu.Items.Add(new ToolStripSeparator());
        contextMenu.Items.Add("Exit", null, (s, e) => Application.Current.Shutdown());

        _notifyIcon.ContextMenuStrip = contextMenu;
        _notifyIcon.DoubleClick += (s, e) => RestoreWindow();

        // Wire main window state changes
        _mainWindow.StateChanged += MainWindow_StateChanged;
        _mainWindow.Closing += MainWindow_Closing;
    }

    /// <summary>
    /// Shows a balloon notification in the system tray.
    /// </summary>
    public void ShowBalloonTip(string title, string message, ToolTipIcon icon = ToolTipIcon.Info, int durationMs = 3000)
    {
        if (_notifyIcon == null)
            return;

        _notifyIcon.ShowBalloonTip(durationMs, title, message, icon);
    }

    /// <summary>
    /// Updates the tray icon and tooltip based on connection status.
    /// </summary>
    public void UpdateHealthStatus(ConnectionStatus status)
    {
        if (_notifyIcon == null)
            return;

        // Dispatch to UI thread to avoid threading issues with GDI
        Application.Current.Dispatcher.InvokeAsync(() =>
        {
            if (_notifyIcon == null)
                return;

            var (icon, statusText) = status switch
            {
                ConnectionStatus.Connected => (GreenIcon, "Connected"),
                ConnectionStatus.Reconnecting => (AmberIcon, "Reconnecting"),
                ConnectionStatus.Disconnected => (RedIcon, "Disconnected"),
                _ => (GrayIcon, "Unknown")
            };

            _notifyIcon.Icon = icon;
            _notifyIcon.Text = $"Meridian - {statusText}";
        });
    }

    /// <summary>
    /// Properly disposes of NotifyIcon and GDI resources.
    /// </summary>
    public void Dispose()
    {
        if (_isDisposed)
            return;

        _isDisposed = true;

        if (_mainWindow != null)
        {
            _mainWindow.StateChanged -= MainWindow_StateChanged;
            _mainWindow.Closing -= MainWindow_Closing;
        }

        if (_notifyIcon != null)
        {
            _notifyIcon.Visible = false;
            _notifyIcon.ContextMenuStrip?.Dispose();
            _notifyIcon.Dispose();
        }

        GreenIcon.Dispose();
        AmberIcon.Dispose();
        RedIcon.Dispose();
        GrayIcon.Dispose();
    }

    private void MainWindow_StateChanged(object? sender, EventArgs e)
    {
        if (_mainWindow == null)
            return;

        // Hide from taskbar and show tray icon when minimized
        if (_mainWindow.WindowState == WindowState.Minimized)
        {
            _mainWindow.Hide();
            _mainWindow.ShowInTaskbar = false;
        }
    }

    private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        Dispose();
    }

    private void RestoreWindow()
    {
        if (_mainWindow == null)
            return;

        _mainWindow.Show();
        _mainWindow.ShowInTaskbar = true;
        _mainWindow.WindowState = WindowState.Normal;
        _mainWindow.Activate();
    }
}
