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
    ShellDensityMode ShellDensityMode,
    string SelectedLayoutPresetId,
    string StartupWorkspace,
    bool IsSummaryPanelVisible,
    bool IsInspectorPanelVisible,
    bool IsActivityPanelVisible,
    string GridDensity,
    string DefaultFilterSet)
{
    public const string DefaultLayoutPresetId = "portfolio-manager";
    public const string DefaultStartupWorkspace = "Portfolio";
    public const string DefaultGridDensity = "Comfortable";
    public const string DefaultFilterSet = "Open work";

    public static DesktopShellPreferences Default { get; } = new(
        ShellDensityMode.Standard,
        DefaultLayoutPresetId,
        DefaultStartupWorkspace,
        IsSummaryPanelVisible: true,
        IsInspectorPanelVisible: true,
        IsActivityPanelVisible: true,
        DefaultGridDensity,
        DefaultFilterSet);
}

/// <summary>
/// Built-in role layout preset surfaced in the WPF Settings workspace.
/// </summary>
public sealed record DesktopLayoutPreset(
    string Id,
    string Name,
    string Description,
    string StartupWorkspace,
    ShellDensityMode ShellDensityMode,
    bool IsSummaryPanelVisible,
    bool IsInspectorPanelVisible,
    bool IsActivityPanelVisible,
    string GridDensity,
    string DefaultFilterSet);
