using System;
using System.Collections.Generic;

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
/// A persisted operator change to one default desktop keyboard shortcut, keyed by action id.
/// Keys and modifiers are stored as their WPF enum names (for example <c>F10</c> and
/// <c>Control, Shift</c>) so this contract stays free of WPF types; a null member means
/// "keep the default" for that part of the binding.
/// </summary>
public sealed record DesktopShortcutOverride(
    string? Key = null,
    string? Modifiers = null,
    bool? IsEnabled = null);

/// <summary>
/// Persisted desktop shell preferences consumed by the WPF workstation shell.
/// </summary>
public sealed record DesktopShellPreferences(
    ShellDensityMode ShellDensityMode)
{
    private static readonly IReadOnlyDictionary<string, DesktopShortcutOverride> NoShortcutOverrides =
        new Dictionary<string, DesktopShortcutOverride>(StringComparer.Ordinal);

    public static DesktopShellPreferences Default { get; } = new(ShellDensityMode.Standard);

    /// <summary>
    /// The operator's shortcut overrides; empty when every shortcut uses its default binding.
    /// </summary>
    public IReadOnlyDictionary<string, DesktopShortcutOverride> ShortcutOverrides { get; init; } =
        NoShortcutOverrides;
}
