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
/// Every value below names the XAML key it mirrors.
/// </para>
/// </summary>
public static class ColorPalette
{
    /// <summary>
    /// Represents an ARGB color value.
    /// </summary>
    public readonly record struct ArgbColor(byte A, byte R, byte G, byte B);


    /// <summary>Success/Active state (muted forest) — ConsoleAccentGreen #3A7A56.</summary>
    public static readonly ArgbColor Success = new(255, 58, 122, 86);

    /// <summary>Warning state (amber ochre) — ConsoleAccentOrange #8A5C12.</summary>
    public static readonly ArgbColor Warning = new(255, 138, 92, 18);

    /// <summary>Error/Danger state (brick) — ConsoleAccentRed #A8443C.</summary>
    public static readonly ArgbColor Error = new(255, 168, 68, 60);

    /// <summary>Informational state (copper, the single accent) — ConsoleAccentBlue #A85436.</summary>
    public static readonly ArgbColor Info = new(255, 168, 84, 54);

    /// <summary>Inactive/Disabled state (flat warm gray) — ConsoleTextDisabled #94999F.</summary>
    public static readonly ArgbColor Inactive = new(255, 148, 153, 159);

    /// <summary>Critical severity (coral).</summary>
    public static readonly ArgbColor Critical = Error;

    /// <summary>Warning events (amber).</summary>
    public static readonly ArgbColor WarningEvent = Warning;



    /// <summary>Primary chart line color (copper) — ChartPrimaryLineColor #A85436.</summary>
    public static readonly ArgbColor ChartPrimary = new(255, 168, 84, 54);

    /// <summary>Secondary chart line color (warm gray) — ChartSecondaryLineColor #7E7A72.</summary>
    public static readonly ArgbColor ChartSecondary = new(255, 126, 122, 114);

    /// <summary>Tertiary chart line color (amber ochre) — ChartWarningLineColor #8A5C12.</summary>
    public static readonly ArgbColor ChartTertiary = new(255, 138, 92, 18);

    /// <summary>Chart positive/up color (muted forest) — ChartEquityColor #3A7A56.</summary>
    public static readonly ArgbColor ChartPositive = Success;

    /// <summary>Chart negative/down color (brick) — ChartDrawdownColor #A8443C.</summary>
    public static readonly ArgbColor ChartNegative = Error;

    /// <summary>Chart card background (Architect White) — ChartSurfaceColor #FBFAF8.</summary>
    public static readonly ArgbColor ChartBackground = new(255, 251, 250, 248);

    /// <summary>Chart plot-area background — ChartPlotAreaColor #F9F7F3.</summary>
    public static readonly ArgbColor ChartDataBackground = new(255, 249, 247, 243);

    /// <summary>Chart grid line color (hairline rule) — ChartGridLineColor #E4E3DE.</summary>
    public static readonly ArgbColor ChartGrid = new(255, 228, 227, 222);

    /// <summary>Chart axis and tick-label color (Slate) — ChartAxisLabelColor #5E666F.</summary>
    public static readonly ArgbColor ChartAxis = new(255, 94, 102, 111);

    /// <summary>Chart border color — ChartBorderColor #D3D0C9.</summary>
    public static readonly ArgbColor ChartBorder = new(255, 211, 208, 201);



    /// <summary>Data quality excellent (muted forest).</summary>
    public static readonly ArgbColor QualityExcellent = Success;

    /// <summary>Data quality fair (amber ochre).</summary>
    public static readonly ArgbColor QualityFair = Warning;



    /// <summary>Accent color for interactive elements.</summary>
    public static readonly ArgbColor Accent = Info;

    /// <summary>Subtle background color.</summary>
    public static readonly ArgbColor SubtleBackground = ChartDataBackground;

    /// <summary>Card background color.</summary>
    public static readonly ArgbColor CardBackground = ChartBackground;

    /// <summary>Muted text color.</summary>
    public static readonly ArgbColor MutedText = ChartAxis;

    /// <summary>Light text on the near-black chrome bars — TopBarText #F4F2ED.
    /// Only for ink over <c>TopBarBackground</c>/<c>StatusBarBackground</c>; the workstation
    /// itself is a light canvas, so this is unreadable on a card or a plot area.</summary>
    public static readonly ArgbColor LightText = new(255, 244, 242, 237);



    /// <summary>Semi-transparent success background — mirrors ConsoleAccentGreenAlpha.</summary>
    public static readonly ArgbColor SuccessBackground = new(40, 58, 122, 86);

    /// <summary>Semi-transparent warning background — mirrors ConsoleAccentOrangeAlpha.</summary>
    public static readonly ArgbColor WarningBackground = new(40, 138, 92, 18);

    /// <summary>Semi-transparent error background — mirrors ConsoleAccentRedAlpha.</summary>
    public static readonly ArgbColor ErrorBackground = new(40, 168, 68, 60);

    /// <summary>Semi-transparent info background — mirrors ConsoleAccentBlueAlpha.</summary>
    public static readonly ArgbColor InfoBackground = new(40, 168, 84, 54);



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
