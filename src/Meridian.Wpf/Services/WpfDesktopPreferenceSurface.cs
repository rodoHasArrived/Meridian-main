using System;
using Meridian.Ui.Services.Contracts;
using Meridian.Ui.Services.Services;

namespace Meridian.Wpf.Services;

/// <summary>
/// The desktop shell's theme and density as <see cref="DesktopOperatorPreferenceSync"/> reads and
/// writes them. Theme changes swap application resources, so values applied from the host are
/// marshalled onto the UI thread; the sync may call in from a background continuation.
/// </summary>
public sealed class WpfDesktopPreferenceSurface : IDesktopPreferenceSurface, IDisposable
{
    private readonly ThemeService _themeService;
    private readonly SettingsConfigurationService _settings;
    private ShellDensityMode _lastDensity;

    public WpfDesktopPreferenceSurface(ThemeService themeService, SettingsConfigurationService settings)
    {
        _themeService = themeService ?? throw new ArgumentNullException(nameof(themeService));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _lastDensity = _settings.GetShellDensityMode();
        _themeService.ThemeChanged += OnThemeChanged;
        _settings.DesktopShellPreferencesChanged += OnDesktopShellPreferencesChanged;
    }

    public AppTheme Theme => _themeService.CurrentTheme;

    public ShellDensityMode Density => _settings.GetShellDensityMode();

    public event EventHandler? PreferenceChanged;

    public void ApplyTheme(AppTheme theme) => OnUiThread(() => _themeService.SetTheme(theme));

    public void ApplyDensity(ShellDensityMode density) => _settings.SetShellDensityMode(density);

    private void OnThemeChanged(object? sender, ThemeChangedEventArgs e) => PreferenceChanged?.Invoke(this, EventArgs.Empty);

    // Shortcut overrides share the desktop shell preference store; only a density change is a
    // presentation preference the host sync owns.
    private void OnDesktopShellPreferencesChanged(object? sender, DesktopShellPreferences preferences)
    {
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
