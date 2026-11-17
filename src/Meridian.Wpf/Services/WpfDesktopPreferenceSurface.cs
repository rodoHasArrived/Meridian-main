using System;
using System.Collections.Generic;
using Meridian.Ui.Services.Contracts;
using Meridian.Ui.Services.Services;

namespace Meridian.Wpf.Services;

/// <summary>
/// The desktop shell's theme, density and keyboard shortcut overrides as
/// <see cref="DesktopOperatorPreferenceSync"/> reads and writes them. Theme and shortcut changes touch
/// UI state, so values applied from the host are marshalled onto the UI thread; the sync may call in
/// from a background continuation. Host shortcut overrides are also written to the local desktop
/// preferences, which stay the cache used when the host cannot be reached.
/// </summary>
public sealed class WpfDesktopPreferenceSurface : IDesktopPreferenceSurface, IDisposable
{
    private readonly ThemeService _themeService;
    private readonly SettingsConfigurationService _settings;
    private readonly KeyboardShortcutService _shortcuts;
    private ShellDensityMode _lastDensity;
    private IReadOnlyDictionary<string, DesktopShortcutOverride> _lastShortcutOverrides;

    public WpfDesktopPreferenceSurface(
        ThemeService themeService,
        SettingsConfigurationService settings,
        KeyboardShortcutService shortcuts)
    {
        _themeService = themeService ?? throw new ArgumentNullException(nameof(themeService));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _shortcuts = shortcuts ?? throw new ArgumentNullException(nameof(shortcuts));
        _lastDensity = _settings.GetShellDensityMode();
        _lastShortcutOverrides = _settings.GetShortcutOverrides();
        _themeService.ThemeChanged += OnThemeChanged;
        _settings.DesktopShellPreferencesChanged += OnDesktopShellPreferencesChanged;
    }

    public AppTheme Theme => _themeService.CurrentTheme;

    public ShellDensityMode Density => _settings.GetShellDensityMode();

    public event EventHandler? PreferenceChanged;

    public IReadOnlyDictionary<string, DesktopShortcutOverride> ShortcutOverrides => _settings.GetShortcutOverrides();

    public event EventHandler? ShortcutOverridesChanged;

    public void ApplyTheme(AppTheme theme) => OnUiThread(() => _themeService.SetTheme(theme));

    public void ApplyDensity(ShellDensityMode density) => _settings.SetShellDensityMode(density);

    // The shortcut service skips entries it cannot apply (an unknown action, a colliding chord), so
    // the local store keeps what actually applied rather than the host set as sent.
    public void ApplyShortcutOverrides(IReadOnlyDictionary<string, DesktopShortcutOverride> overrides)
    {
        ArgumentNullException.ThrowIfNull(overrides);
        OnUiThread(() =>
        {
            _shortcuts.ApplyOverrides(overrides);
            _settings.SetShortcutOverrides(_shortcuts.GetOverrides());
        });
    }

    private void OnThemeChanged(object? sender, ThemeChangedEventArgs e) => PreferenceChanged?.Invoke(this, EventArgs.Empty);

    // Density and shortcut overrides share the desktop shell preference store; each change is
    // reported on its own event so the sync saves only the value that changed.
    private void OnDesktopShellPreferencesChanged(object? sender, DesktopShellPreferences preferences)
    {
        if (!ReferenceEquals(preferences.ShortcutOverrides, _lastShortcutOverrides))
        {
            _lastShortcutOverrides = preferences.ShortcutOverrides;
            ShortcutOverridesChanged?.Invoke(this, EventArgs.Empty);
        }

        if (preferences.ShellDensityMode == _lastDensity)
        {
            return;
        }

        _lastDensity = preferences.ShellDensityMode;
        PreferenceChanged?.Invoke(this, EventArgs.Empty);
    }

    private static void OnUiThread(Action action)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            action();
            return;
        }

        dispatcher.Invoke(action);
    }

    public void Dispose()
    {
        _themeService.ThemeChanged -= OnThemeChanged;
        _settings.DesktopShellPreferencesChanged -= OnDesktopShellPreferencesChanged;
    }
}
