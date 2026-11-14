namespace Meridian.Ui.Services.Services;

/// <summary>
/// Platform-agnostic color palette defining all ARGB color values used across desktop applications.
/// Both platform BrushRegistry classes reference this shared source of truth for color definitions
/// and state-to-color mapping logic.
/// <para>
/// These are the runtime twins of <c>src/Meridian.Wpf/Styles/ThemeTokens.xaml</c>. Charting,
/// QuantScript and the ScottPlot surfaces render from here rather than from the XAML resources,
/// so a value that moves there must move here in the same change or the two identities split —
/// which is exactly what happened when the desktop lane was restyled and this file was not.
/// The static palette follows the institutional light card/data surfaces in Design System 4.3.0.
/// ThemeServiceBase changes window theme state, not these immutable values; plot backgrounds and
/// their ink therefore remain paired in either window mode. Every core value names its XAML twin.
/// </para>
/// </summary>
public static class ColorPalette
{
    /// <summary>
    /// Represents an ARGB color value.
    /// </summary>
    public readonly record struct ArgbColor(byte A, byte R, byte G, byte B);


    /// <summary>Success/Active state — ConsoleAccentGreen #2E7655.</summary>
    public static readonly ArgbColor Success = new(255, 46, 118, 85);

    /// <summary>Warning state — ConsoleAccentOrange #986717.</summary>
    public static readonly ArgbColor Warning = new(255, 152, 103, 23);

    /// <summary>Error/Danger state — ConsoleAccentRed #B54B42.</summary>
    public static readonly ArgbColor Error = new(255, 181, 75, 66);

    /// <summary>Informational interaction — ConsoleAccentBlue #2D6FB3.</summary>
    public static readonly ArgbColor Info = new(255, 45, 111, 179);

    /// <summary>Inactive/Disabled state — ConsoleTextDisabled #A4ABB2.</summary>
    public static readonly ArgbColor Inactive = new(255, 164, 171, 178);

    /// <summary>Critical severity retains the error role.</summary>
    public static readonly ArgbColor Critical = Error;

    /// <summary>Warning events retain the warning role.</summary>
    public static readonly ArgbColor WarningEvent = Warning;

    /// <summary>Actual chart trace, graphite rather than brand signal — ChartPrimaryLineColor #29323A.</summary>
    public static readonly ArgbColor ChartPrimary = new(255, 41, 50, 58);

    /// <summary>Secondary comparison trace — ChartSecondaryLineColor #8C918F.</summary>
    public static readonly ArgbColor ChartSecondary = new(255, 140, 145, 143);

    /// <summary>Warning chart trace — ChartWarningLineColor #986717.</summary>
    public static readonly ArgbColor ChartTertiary = Warning;

    /// <summary>Chart positive/up color — ChartEquityColor #2E7655.</summary>
    public static readonly ArgbColor ChartPositive = Success;

    /// <summary>Chart negative/down color — ChartDrawdownColor #B54B42.</summary>
    public static readonly ArgbColor ChartNegative = Error;

    /// <summary>Chart card background — ChartSurfaceColor #F8F7F3.</summary>
    public static readonly ArgbColor ChartBackground = new(255, 248, 247, 243);

    /// <summary>Chart data well — ChartPlotAreaColor #FCFBF8.</summary>
    public static readonly ArgbColor ChartDataBackground = new(255, 252, 251, 248);

    /// <summary>Chart grid rule — ChartGridLineColor #E2DED5.</summary>
    public static readonly ArgbColor ChartGrid = new(255, 226, 222, 213);

    /// <summary>Chart axis and tick-label ink — ChartAxisLabelColor #606870.</summary>
    public static readonly ArgbColor ChartAxis = new(255, 96, 104, 112);

    /// <summary>Chart border rule — ChartBorderColor #D2CDC3.</summary>
    public static readonly ArgbColor ChartBorder = new(255, 210, 205, 195);

    /// <summary>Data quality excellent retains the success role.</summary>
    public static readonly ArgbColor QualityExcellent = Success;

    /// <summary>Data quality fair retains the warning role.</summary>
    public static readonly ArgbColor QualityFair = Warning;

    /// <summary>Interaction accent, independent of the terracotta brand signal.</summary>
    public static readonly ArgbColor Accent = Info;

    /// <summary>Inset data background.</summary>
    public static readonly ArgbColor SubtleBackground = ChartDataBackground;

    /// <summary>Card background.</summary>
    public static readonly ArgbColor CardBackground = ChartBackground;

    /// <summary>Muted text ink.</summary>
    public static readonly ArgbColor MutedText = ChartAxis;

    /// <summary>Inverse chrome text — TopBarText #F1EFEA; never for paper cards or plot wells.</summary>
    public static readonly ArgbColor LightText = new(255, 241, 239, 234);

    /// <summary>20% success wash — ConsoleAccentGreenAlpha20.</summary>
    public static readonly ArgbColor SuccessBackground = new(51, Success.R, Success.G, Success.B);

    /// <summary>20% warning wash — ConsoleAccentOrangeAlpha20.</summary>
    public static readonly ArgbColor WarningBackground = new(51, Warning.R, Warning.G, Warning.B);

    /// <summary>20% error wash — ConsoleAccentRedAlpha20.</summary>
    public static readonly ArgbColor ErrorBackground = new(51, Error.R, Error.G, Error.B);

    /// <summary>20% information wash — ConsoleAccentBlueAlpha20.</summary>
    public static readonly ArgbColor InfoBackground = new(51, Info.R, Info.G, Info.B);

    /// <summary>Gets the color for a notification type.</summary>
    public static ArgbColor GetNotificationColor(NotificationType type) => type switch
    {
        NotificationType.Success => Success,
        NotificationType.Warning => Warning,
        NotificationType.Error => Error,
        _ => Info
    };

    /// <summary>Gets the color for an integrity severity level.</summary>
    public static ArgbColor GetSeverityColor(IntegritySeverity severity) => severity switch
    {
        IntegritySeverity.Critical => Critical,
        IntegritySeverity.Warning => WarningEvent,
        _ => Info
    };

    /// <summary>Gets the color for a progress percentage (0-100).</summary>
    public static ArgbColor GetProgressColor(double percentage) => percentage switch
    {
        >= 90 => Success,
        >= 70 => Info,
        >= 50 => Warning,
        _ => Error
    };

    /// <summary>Gets the color for a latency value in milliseconds.</summary>
    public static ArgbColor GetLatencyColor(int latencyMs) => latencyMs switch
    {
        < 20 => Success,
        < 50 => Warning,
        _ => Error
    };

    /// <summary>Gets the color for stream status based on active state and collector state.</summary>
    public static ArgbColor GetStreamStatusColor(bool isStreamActive, bool isCollectorRunning, bool isCollectorPaused)
    {
        if (isStreamActive && isCollectorRunning && !isCollectorPaused)
            return Success;
        if (isStreamActive && isCollectorPaused)
            return Warning;
        return Inactive;
    }

}
