using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Meridian.Wpf.Services;

/// <summary>
/// Delivers Windows balloon-tip notifications via <see cref="NotifyIcon"/>.
///
/// The project targets <c>net9.0-windows</c> (no explicit Windows version suffix), so
/// the WinRT <c>Windows.UI.Notifications</c> API is unavailable without adding
/// <c>Microsoft.Windows.SDK.Contracts</c> or bumping the TFM to
/// <c>net9.0-windows10.0.17763.0</c>.  This service provides a functionally
/// equivalent experience through the system-tray balloon-tip channel.
///
/// <b>Limitation:</b> Balloon tips do NOT persist in the Windows Action Center;
/// they appear for ≈5 s and are then silently dismissed.  For true Action Center
/// persistence, change <c>TargetFramework</c> to <c>net9.0-windows10.0.17763.0</c>
/// and use <c>Windows.UI.Notifications.ToastNotificationManager</c>.
/// </summary>
public sealed class ToastNotificationService : IDisposable
{
    /// <summary>
    /// AppUserModelID shared with the Windows shell (JumpList, taskbar pinning).
    /// </summary>
    internal const string AppId = "Meridian.TradingPlatform";

    [DllImport("shell32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern void SetCurrentProcessExplicitAppUserModelID(
        [MarshalAs(UnmanagedType.LPWStr)] string appId);

    private static readonly Lazy<ToastNotificationService> _instance =
        new(() => new ToastNotificationService(), isThreadSafe: true);

    private readonly NotifyIcon _notifyIcon;
    private volatile string _pendingNavigationTag = string.Empty;

    /// <summary>Gets the singleton instance.</summary>
    public static ToastNotificationService Instance => _instance.Value;

    private ToastNotificationService()
    {
        _notifyIcon = new NotifyIcon
        {
            Text = "Meridian",
            Icon = System.Drawing.SystemIcons.Application,
            Visible = true
        };

        _notifyIcon.BalloonTipClicked += OnBalloonTipClicked;
    }

    /// <summary>
    /// Registers the AppUserModelID with Windows so that toast activations,
    /// JumpList entries, and taskbar pins all share the same identity.
    /// Must be called before any window is shown.
    /// </summary>
    public static void SetAppUserModelId() =>
        SetCurrentProcessExplicitAppUserModelID(AppId);

    /// <summary>
    /// Shows a "Backfill Complete" balloon notification.
    /// </summary>
    /// <param name="symbolCount">Number of symbols processed.</param>
    /// <param name="barsWritten">Total bars written to storage.</param>
    /// <param name="duration">Elapsed wall-clock time for the operation.</param>
    public void ShowBackfillComplete(int symbolCount, long barsWritten, TimeSpan duration)
    {
        _pendingNavigationTag = "Backfill";
        var durationText = duration > TimeSpan.Zero ? $" · {FormatDuration(duration)}" : string.Empty;
        ShowBalloon(
            "Backfill Complete",
            $"{symbolCount} symbol{(symbolCount != 1 ? "s" : string.Empty)} · {barsWritten:N0} bars{durationText}",
            ToolTipIcon.Info);
    }

    /// <summary>
    /// Shows a "Provider Disconnected" balloon notification.
    /// </summary>
    /// <param name="providerName">Display name of the disconnected provider.</param>
    public void ShowProviderDisconnected(string providerName)
    {
        _pendingNavigationTag = "ProviderHealth";
        ShowBalloon(
            $"Provider Disconnected: {providerName}",
            $"Connection to {providerName} lost. Data collection paused.",
            ToolTipIcon.Warning);
    }

    /// <summary>
    /// Shows a "Data Quality Alert" balloon notification when a quality score
    /// drops below the warning threshold.
    /// </summary>
    /// <param name="symbol">Affected symbol, or "Overall" for a global score drop.</param>
    /// <param name="score">Quality score that triggered the alert (0–100).</param>
    public void ShowDataQualityAlert(string symbol, double score)
    {
        _pendingNavigationTag = "DataQuality";
        ShowBalloon(
            "Data Quality Alert",
            $"{symbol}: quality score dropped to {score:F1}%",
            ToolTipIcon.Warning);
    }

    // ── Internal helpers ────────────────────────────────────────────────────

    private void ShowBalloon(string title, string body, ToolTipIcon icon)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher != null && !dispatcher.CheckAccess())
        {
            dispatcher.BeginInvoke(() => ShowBalloon(title, body, icon));
            return;
        }

        _notifyIcon.ShowBalloonTip(
            timeout: 5000,
            tipTitle: title,
            tipText: body,
            tipIcon: icon);
    }

    private void OnBalloonTipClicked(object? sender, EventArgs e)
    {
        var tag = _pendingNavigationTag;
        _pendingNavigationTag = string.Empty;
        if (string.IsNullOrEmpty(tag))
            return;

        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        dispatcher?.BeginInvoke(() =>
        {
            var mainWindow = System.Windows.Application.Current?.MainWindow;
            if (mainWindow is not null)
            {
                if (mainWindow.WindowState == System.Windows.WindowState.Minimized)
                    mainWindow.WindowState = System.Windows.WindowState.Normal;
                mainWindow.Activate();
            }

            NavigationService.Instance.NavigateTo(tag);
        });
    }

    private static string FormatDuration(TimeSpan ts) =>
        ts.TotalHours >= 1
            ? $"{(int)ts.TotalHours}h {ts.Minutes:D2}m"
            : ts.TotalMinutes >= 1
                ? $"{(int)ts.TotalMinutes}m {ts.Seconds:D2}s"
                : $"{ts.Seconds}s";

    /// <inheritdoc />
    public void Dispose()
    {
        _notifyIcon.BalloonTipClicked -= OnBalloonTipClicked;
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
    }
}
