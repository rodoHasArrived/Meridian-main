using System.Windows.Media;
using Meridian.Ui.Services;
using Palette = Meridian.Ui.Services.Services.ColorPalette;
using ColorPalette = Meridian.Ui.Services.Services.ColorPalette;
using IntegritySeverity = Meridian.Ui.Services.IntegritySeverity;

namespace Meridian.Wpf.Services;

/// <summary>
/// Centralized registry for cached SolidColorBrush instances.
/// Delegates color definitions to the shared <see cref="ColorPalette"/> in Ui.Services.
/// </summary>
public static class BrushRegistry
{
    private static SolidColorBrush FromArgb(ColorPalette.ArgbColor c) => new(Color.FromArgb(c.A, c.R, c.G, c.B));

    #region Status Brushes

    public static readonly SolidColorBrush Success = FromArgb(Palette.Success);
    public static readonly SolidColorBrush Warning = FromArgb(Palette.Warning);
    public static readonly SolidColorBrush Error = FromArgb(Palette.Error);
    public static readonly SolidColorBrush Info = FromArgb(Palette.Info);
    public static readonly SolidColorBrush Inactive = FromArgb(Palette.Inactive);
    public static readonly SolidColorBrush Critical = FromArgb(Palette.Critical);
    public static readonly SolidColorBrush WarningEvent = FromArgb(Palette.WarningEvent);

    #endregion

    #region Chart/Visualization Brushes

    public static readonly SolidColorBrush ChartPrimary = FromArgb(Palette.ChartPrimary);
    public static readonly SolidColorBrush ChartSecondary = FromArgb(Palette.ChartSecondary);
    public static readonly SolidColorBrush ChartTertiary = FromArgb(Palette.ChartTertiary);
    public static readonly SolidColorBrush ChartPositive = FromArgb(Palette.ChartPositive);
    public static readonly SolidColorBrush ChartNegative = FromArgb(Palette.ChartNegative);

    #endregion

    #region Provider Status Brushes

    public static readonly SolidColorBrush ProviderConnected = Success;
    public static readonly SolidColorBrush ProviderConnecting = Info;
    public static readonly SolidColorBrush ProviderDisconnected = Inactive;
    public static readonly SolidColorBrush ProviderError = Error;

    #endregion

    #region Data Quality Brushes

    public static readonly SolidColorBrush QualityExcellent = FromArgb(Palette.QualityExcellent);
    public static readonly SolidColorBrush QualityGood = Success;
    public static readonly SolidColorBrush QualityFair = FromArgb(Palette.QualityFair);
    public static readonly SolidColorBrush QualityPoor = Warning;
    public static readonly SolidColorBrush QualityCritical = Error;

    #endregion

    #region UI Element Brushes

    public static readonly SolidColorBrush Accent = FromArgb(Palette.Accent);
    public static readonly SolidColorBrush SubtleBackground = FromArgb(Palette.SubtleBackground);
    public static readonly SolidColorBrush CardBackground = FromArgb(Palette.CardBackground);
    public static readonly SolidColorBrush MutedText = FromArgb(Palette.MutedText);
    public static readonly SolidColorBrush LightText = FromArgb(Palette.LightText);

    #endregion

    #region Semi-Transparent Status Backgrounds

    public static readonly SolidColorBrush SuccessBackground = FromArgb(Palette.SuccessBackground);
    public static readonly SolidColorBrush WarningBackground = FromArgb(Palette.WarningBackground);
    public static readonly SolidColorBrush ErrorBackground = FromArgb(Palette.ErrorBackground);
    public static readonly SolidColorBrush InfoBackground = FromArgb(Palette.InfoBackground);

    #endregion

    #region State-Based Brush Lookups

    public static SolidColorBrush GetNotificationBrush(NotificationType type) => FromArgb(Palette.GetNotificationColor((Meridian.Ui.Services.NotificationType)(int)type));
    public static SolidColorBrush GetSeverityBrush(IntegritySeverity severity) => FromArgb(Palette.GetSeverityColor(severity));

    public static Color GetSeverityColor(IntegritySeverity severity)
    {
        var c = Palette.GetSeverityColor(severity);
        return Color.FromArgb(c.A, c.R, c.G, c.B);
    }

    public static SolidColorBrush GetConnectionStateBrush(ConnectionState state) => state switch
    {
        ConnectionState.Connected => Success,
        ConnectionState.Connecting or ConnectionState.Reconnecting => Info,
        ConnectionState.Disconnected => Inactive,
        ConnectionState.Error => Error,
        _ => Inactive
    };

    public static SolidColorBrush GetProgressBrush(double percentage) => FromArgb(Palette.GetProgressColor(percentage));
    public static SolidColorBrush GetLatencyBrush(int latencyMs) => FromArgb(Palette.GetLatencyColor(latencyMs));

    public static SolidColorBrush GetStreamStatusBrush(bool isStreamActive, bool isCollectorRunning, bool isCollectorPaused) =>
        FromArgb(Palette.GetStreamStatusColor(isStreamActive, isCollectorRunning, isCollectorPaused));

    #endregion
}
