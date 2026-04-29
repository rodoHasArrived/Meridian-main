namespace Meridian.Ui.Services.Services;

/// <summary>
/// Platform-agnostic color palette defining all ARGB color values used across desktop applications.
/// Both platform BrushRegistry classes reference this shared source of truth for color definitions
/// and state-to-color mapping logic.
/// </summary>
public static class ColorPalette
{
    /// <summary>
    /// Represents an ARGB color value.
    /// </summary>
    public readonly record struct ArgbColor(byte A, byte R, byte G, byte B);


    /// <summary>Success/Active state (mint).</summary>
    public static readonly ArgbColor Success = new(255, 38, 191, 134);

    /// <summary>Warning state (amber).</summary>
    public static readonly ArgbColor Warning = new(255, 214, 158, 56);

    /// <summary>Error/Danger state (coral).</summary>
    public static readonly ArgbColor Error = new(255, 222, 88, 120);

    /// <summary>Informational state (signal cyan).</summary>
    public static readonly ArgbColor Info = new(255, 42, 178, 212);

    /// <summary>Inactive/Disabled state (muted blue-gray).</summary>
    public static readonly ArgbColor Inactive = new(255, 141, 160, 179);

    /// <summary>Critical severity (coral).</summary>
    public static readonly ArgbColor Critical = Error;

    /// <summary>Warning events (amber).</summary>
    public static readonly ArgbColor WarningEvent = Warning;



    /// <summary>Primary chart line color (signal cyan).</summary>
    public static readonly ArgbColor ChartPrimary = new(255, 42, 178, 212);

    /// <summary>Secondary chart line color (paper blue).</summary>
    public static readonly ArgbColor ChartSecondary = new(255, 96, 165, 250);

    /// <summary>Tertiary chart line color (amber).</summary>
    public static readonly ArgbColor ChartTertiary = new(255, 214, 158, 56);

    /// <summary>Chart positive/up color (mint).</summary>
    public static readonly ArgbColor ChartPositive = Success;

    /// <summary>Chart negative/down color (coral).</summary>
    public static readonly ArgbColor ChartNegative = Error;

    /// <summary>Chart card background.</summary>
    public static readonly ArgbColor ChartBackground = new(255, 11, 21, 32);

    /// <summary>Chart plot-area background.</summary>
    public static readonly ArgbColor ChartDataBackground = new(255, 8, 16, 26);

    /// <summary>Chart grid line color.</summary>
    public static readonly ArgbColor ChartGrid = new(255, 26, 45, 68);

    /// <summary>Chart axis and tick-label color.</summary>
    public static readonly ArgbColor ChartAxis = new(255, 168, 181, 196);

    /// <summary>Chart border color.</summary>
    public static readonly ArgbColor ChartBorder = new(255, 42, 69, 102);



    /// <summary>Data quality excellent (mint).</summary>
    public static readonly ArgbColor QualityExcellent = Success;

    /// <summary>Data quality fair (amber).</summary>
    public static readonly ArgbColor QualityFair = Warning;



    /// <summary>Accent color for interactive elements.</summary>
    public static readonly ArgbColor Accent = Info;

    /// <summary>Subtle background color.</summary>
    public static readonly ArgbColor SubtleBackground = ChartDataBackground;

    /// <summary>Card background color.</summary>
    public static readonly ArgbColor CardBackground = ChartBackground;

    /// <summary>Muted text color.</summary>
    public static readonly ArgbColor MutedText = ChartAxis;

    /// <summary>Light text on dark background.</summary>
    public static readonly ArgbColor LightText = new(255, 232, 241, 249);



    /// <summary>Semi-transparent success background.</summary>
    public static readonly ArgbColor SuccessBackground = new(40, 38, 191, 134);

    /// <summary>Semi-transparent warning background.</summary>
    public static readonly ArgbColor WarningBackground = new(40, 214, 158, 56);

    /// <summary>Semi-transparent error background.</summary>
    public static readonly ArgbColor ErrorBackground = new(40, 222, 88, 120);

    /// <summary>Semi-transparent info background.</summary>
    public static readonly ArgbColor InfoBackground = new(40, 42, 178, 212);



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
