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


    /// <summary>Success/Active state (green).</summary>
    public static readonly ArgbColor Success = new(255, 72, 187, 120);

    /// <summary>Warning state (orange).</summary>
    public static readonly ArgbColor Warning = new(255, 237, 137, 54);

    /// <summary>Error/Danger state (red).</summary>
    public static readonly ArgbColor Error = new(255, 245, 101, 101);

    /// <summary>Informational state (blue).</summary>
    public static readonly ArgbColor Info = new(255, 88, 166, 255);

    /// <summary>Inactive/Disabled state (gray).</summary>
    public static readonly ArgbColor Inactive = new(255, 160, 174, 192);

    /// <summary>Critical severity (bright red).</summary>
    public static readonly ArgbColor Critical = new(255, 248, 81, 73);

    /// <summary>Warning events (amber/gold).</summary>
    public static readonly ArgbColor WarningEvent = new(255, 210, 153, 34);



    /// <summary>Primary chart line color (blue).</summary>
    public static readonly ArgbColor ChartPrimary = new(255, 66, 153, 225);

    /// <summary>Secondary chart line color (purple).</summary>
    public static readonly ArgbColor ChartSecondary = new(255, 159, 122, 234);

    /// <summary>Tertiary chart line color (teal).</summary>
    public static readonly ArgbColor ChartTertiary = new(255, 56, 178, 172);

    /// <summary>Chart positive/up color (green).</summary>
    public static readonly ArgbColor ChartPositive = new(255, 72, 187, 120);

    /// <summary>Chart negative/down color (red).</summary>
    public static readonly ArgbColor ChartNegative = new(255, 245, 101, 101);



    /// <summary>Data quality excellent (bright green).</summary>
    public static readonly ArgbColor QualityExcellent = new(255, 56, 161, 105);

    /// <summary>Data quality fair (yellow).</summary>
    public static readonly ArgbColor QualityFair = new(255, 236, 201, 75);



    /// <summary>Accent color for interactive elements.</summary>
    public static readonly ArgbColor Accent = new(255, 99, 102, 241);

    /// <summary>Subtle background color.</summary>
    public static readonly ArgbColor SubtleBackground = new(255, 45, 55, 72);

    /// <summary>Card background color.</summary>
    public static readonly ArgbColor CardBackground = new(255, 26, 32, 44);

    /// <summary>Muted text color.</summary>
    public static readonly ArgbColor MutedText = new(255, 160, 174, 192);

    /// <summary>Light text on dark background.</summary>
    public static readonly ArgbColor LightText = new(255, 226, 232, 240);



    /// <summary>Semi-transparent success background.</summary>
    public static readonly ArgbColor SuccessBackground = new(40, 72, 187, 120);

    /// <summary>Semi-transparent warning background.</summary>
    public static readonly ArgbColor WarningBackground = new(40, 237, 137, 54);

    /// <summary>Semi-transparent error background.</summary>
    public static readonly ArgbColor ErrorBackground = new(40, 245, 101, 101);

    /// <summary>Semi-transparent info background.</summary>
    public static readonly ArgbColor InfoBackground = new(40, 88, 166, 255);



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
