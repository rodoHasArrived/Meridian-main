namespace Meridian.Ui.Services.Services;

/// <summary>
/// Shared shell density modes for the desktop workstation chrome.
/// </summary>
public enum ShellDensityMode : byte
{
    Standard = 0,
    Compact = 1
}

/// <summary>
/// Persisted desktop shell preferences consumed by the WPF workstation shell.
/// </summary>
public sealed record DesktopShellPreferences(
    ShellDensityMode ShellDensityMode)
{
    public static DesktopShellPreferences Default { get; } = new(ShellDensityMode.Standard);
}
