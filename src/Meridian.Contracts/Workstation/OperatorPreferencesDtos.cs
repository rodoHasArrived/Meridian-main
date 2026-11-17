namespace Meridian.Contracts.Workstation;

/// <summary>
/// The shared value sets for <see cref="OperatorPreferenceValuesDto"/>, used by the host store and by
/// both operator clients so the browser and WPF read one vocabulary. Clients map these to their own
/// presentation types (the WPF shell has fewer density levels than the browser).
/// </summary>
public static class OperatorPreferenceVocabulary
{
    public const string AppearanceSystem = "system";
    public const string AppearanceLight = "light";
    public const string AppearanceDark = "dark";

    public const string DensityCompact = "compact";
    public const string DensityDefault = "default";
    public const string DensitySpacious = "spacious";

    /// <summary>Every appearance the host accepts.</summary>
    public static IReadOnlyList<string> Appearances { get; } = [AppearanceSystem, AppearanceLight, AppearanceDark];

    /// <summary>Every density the host accepts, from tightest to loosest.</summary>
    public static IReadOnlyList<string> Densities { get; } = [DensityCompact, DensityDefault, DensitySpacious];
}

/// <summary>
/// The signed-in operator's presentation preferences, kept on the host so they follow the operator
/// to another browser or machine. Every value is optional: null means "no preference stored", and
/// the client keeps its own default. Values are display-only and never change stored or ledger data.
/// </summary>
/// <param name="Appearance">Colour scheme: <c>system</c>, <c>light</c> or <c>dark</c>.</param>
/// <param name="Density">Workstation density: <c>compact</c>, <c>default</c> or <c>spacious</c>.</param>
/// <param name="DisplayTimeZone">IANA zone timestamps are shown in, for example <c>Europe/London</c>.</param>
/// <param name="PageDescriptionsHidden">True when page and section descriptions sit behind an information icon.</param>
/// <param name="KeyBindings">Browser workstation global shortcut overrides, keyed by binding id (for example
/// <c>command-palette</c>), valued with the combo (for example <c>Mod+Shift+P</c>).</param>
/// <param name="DesktopKeyBindings">WPF desktop shortcut overrides, keyed by desktop action id (for example
/// <c>NavigateDashboard</c>). Owned by the desktop: a save that leaves this null keeps the stored set, so a
/// browser save never clears it; an empty set means every desktop shortcut is on its default.</param>
public sealed record OperatorPreferenceValuesDto(
    string? Appearance = null,
    string? Density = null,
    string? DisplayTimeZone = null,
    bool? PageDescriptionsHidden = null,
    IReadOnlyDictionary<string, string>? KeyBindings = null,
    IReadOnlyDictionary<string, OperatorDesktopShortcutDto>? DesktopKeyBindings = null);

/// <summary>
/// One WPF desktop shortcut's difference from its default. Each value is optional: null keeps the
/// default. <paramref name="Key"/> and <paramref name="Modifiers"/> use WPF enum names (for example
/// <c>D</c> and <c>Control, Shift</c>) so this contract stays free of WPF types.
/// </summary>
public sealed record OperatorDesktopShortcutDto(
    string? Key = null,
    string? Modifiers = null,
    bool? IsEnabled = null);

/// <summary>The signed-in operator's stored presentation preferences and when they last changed.</summary>
public sealed record OperatorPreferencesDto(
    OperatorPreferenceValuesDto Preferences,
    DateTimeOffset? UpdatedAt);

/// <summary>
/// Replaces the signed-in operator's presentation preferences as a whole. A null or empty set clears them.
/// </summary>
public sealed record OperatorPreferencesSaveRequest(OperatorPreferenceValuesDto? Preferences);
