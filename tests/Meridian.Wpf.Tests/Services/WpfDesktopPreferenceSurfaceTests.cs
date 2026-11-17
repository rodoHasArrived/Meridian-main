using System.Collections.Generic;
using Meridian.Ui.Services.Contracts;
using Meridian.Ui.Services.Services;
using Meridian.Wpf.Services;

namespace Meridian.Wpf.Tests.Services;

/// <summary>
/// The WPF adapter the host preference sync reads and writes. Shortcut overrides share the desktop
/// shell preference store, so only a density or theme change may count as a presentation change.
/// </summary>
public sealed class WpfDesktopPreferenceSurfaceTests
{
    private static string NewPreferencesPath() => Path.Combine(
        Path.GetTempPath(), "meridian-wpf-surface-tests", $"{Guid.NewGuid():N}.desktop-shell-preferences.json");

    [Fact]
    public void DensityChange_RaisesPreferenceChangedButShortcutChangeDoesNot()
    {
        var path = NewPreferencesPath();
        SettingsConfigurationService.SetDesktopPreferencesFilePathOverrideForTests(path);
        var settings = SettingsConfigurationService.Instance;
        using var surface = new WpfDesktopPreferenceSurface(ThemeService.Instance, settings);
        var raised = 0;
        surface.PreferenceChanged += (_, _) => raised++;

        try
        {
            settings.SetShortcutOverrides(new Dictionary<string, DesktopShortcutOverride>
            {
                ["Help"] = new(IsEnabled: false)
            });
            raised.Should().Be(0);

            surface.ApplyDensity(ShellDensityMode.Compact);
            raised.Should().Be(1);
            surface.Density.Should().Be(ShellDensityMode.Compact);

            settings.SetShortcutOverrides(new Dictionary<string, DesktopShortcutOverride>());
            raised.Should().Be(1);
        }
        finally
        {
            SettingsConfigurationService.SetDesktopPreferencesFilePathOverrideForTests(null);
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public void ThemeChange_RaisesPreferenceChanged()
    {
        var path = NewPreferencesPath();
        SettingsConfigurationService.SetDesktopPreferencesFilePathOverrideForTests(path);
        var theme = ThemeService.Instance;
        var original = theme.CurrentTheme;
        using var surface = new WpfDesktopPreferenceSurface(theme, SettingsConfigurationService.Instance);
        var raised = 0;
        surface.PreferenceChanged += (_, _) => raised++;

        try
        {
            var next = original == AppTheme.Dark ? AppTheme.Light : AppTheme.Dark;
            surface.ApplyTheme(next);

            raised.Should().Be(1);
            surface.Theme.Should().Be(next);
        }
        finally
        {
            theme.SetTheme(original);
            SettingsConfigurationService.SetDesktopPreferencesFilePathOverrideForTests(null);
        }
    }
}
