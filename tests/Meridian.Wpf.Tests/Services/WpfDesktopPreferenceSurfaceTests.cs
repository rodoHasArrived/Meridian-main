using System.Collections.Generic;
using Meridian.Ui.Services.Contracts;
using Meridian.Ui.Services.Services;
using Meridian.Wpf.Services;

namespace Meridian.Wpf.Tests.Services;

/// <summary>
/// The WPF adapter the host preference sync reads and writes. Density and shortcut overrides share the
/// desktop shell preference store, so each change must be reported on its own event.
/// </summary>
public sealed class WpfDesktopPreferenceSurfaceTests
{
    private sealed class IsolatedNotifications : NotificationServiceBase;

    private static string NewPreferencesPath() => Path.Combine(
        Path.GetTempPath(), "meridian-wpf-surface-tests", $"{Guid.NewGuid():N}.desktop-shell-preferences.json");

    [Fact]
    public void DensityAndShortcutChanges_RaiseTheirOwnEvents()
    {
        var path = NewPreferencesPath();
        SettingsConfigurationService.SetDesktopPreferencesFilePathOverrideForTests(path);
        var settings = SettingsConfigurationService.Instance;
        using var surface = new WpfDesktopPreferenceSurface(ThemeService.Instance, settings, KeyboardShortcutService.CreateIsolatedForTests(), new IsolatedNotifications());
        var raised = 0;
        var shortcutChanges = 0;
        surface.PreferenceChanged += (_, _) => raised++;
        surface.ShortcutOverridesChanged += (_, _) => shortcutChanges++;

        try
        {
            settings.SetShortcutOverrides(new Dictionary<string, DesktopShortcutOverride>
            {
                ["Help"] = new(IsEnabled: false)
            });
            raised.Should().Be(0);
            shortcutChanges.Should().Be(1);

            surface.ApplyDensity(ShellDensityMode.Compact);
            raised.Should().Be(1);
            shortcutChanges.Should().Be(1);
            surface.Density.Should().Be(ShellDensityMode.Compact);

            settings.SetShortcutOverrides(new Dictionary<string, DesktopShortcutOverride>());
            raised.Should().Be(1);
            shortcutChanges.Should().Be(2);
            surface.ShortcutOverrides.Should().BeEmpty();
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
        using var surface = new WpfDesktopPreferenceSurface(theme, SettingsConfigurationService.Instance, KeyboardShortcutService.CreateIsolatedForTests(), new IsolatedNotifications());
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

    [Fact]
    public void ApplyShortcutOverrides_RebindsAndStoresOnlyWhatApplied()
    {
        var path = NewPreferencesPath();
        SettingsConfigurationService.SetDesktopPreferencesFilePathOverrideForTests(path);
        var settings = SettingsConfigurationService.Instance;
        var shortcuts = KeyboardShortcutService.CreateIsolatedForTests();
        using var surface = new WpfDesktopPreferenceSurface(ThemeService.Instance, settings, shortcuts, new IsolatedNotifications());

        try
        {
            surface.ApplyShortcutOverrides(new Dictionary<string, DesktopShortcutOverride>
            {
                ["NavigateDashboard"] = new("H", "Control, Shift"),
                ["Help"] = new(IsEnabled: false),
                ["NoSuchAction"] = new("Q", "Control")
            });

            var dashboard = shortcuts.GetShortcut("NavigateDashboard")!;
            dashboard.Key.Should().Be(System.Windows.Input.Key.H);
            dashboard.Modifiers.Should().Be(System.Windows.Input.ModifierKeys.Control | System.Windows.Input.ModifierKeys.Shift);
            shortcuts.GetShortcut("Help")!.IsEnabled.Should().BeFalse();
            surface.ShortcutOverrides.Keys.Should().BeEquivalentTo(["NavigateDashboard", "Help"], "an action this desktop lacks is not kept");

            surface.ApplyShortcutOverrides(new Dictionary<string, DesktopShortcutOverride>());
            shortcuts.GetOverrides().Should().BeEmpty();
            surface.ShortcutOverrides.Should().BeEmpty();
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
    public void ApplyNotifications_ReplacesTheLiveSettingsAndReportsTheChange()
    {
        var path = NewPreferencesPath();
        SettingsConfigurationService.SetDesktopPreferencesFilePathOverrideForTests(path);
        var notifications = new IsolatedNotifications();
        using var surface = new WpfDesktopPreferenceSurface(
            ThemeService.Instance, SettingsConfigurationService.Instance, KeyboardShortcutService.CreateIsolatedForTests(), notifications);
        var changes = 0;
        var presentationChanges = 0;
        surface.NotificationsChanged += (_, _) => changes++;
        surface.PreferenceChanged += (_, _) => presentationChanges++;

        try
        {
            surface.ApplyNotifications(new NotificationSettings { NotifyBackfillComplete = false, QuietHoursEnabled = true });

            notifications.GetSettings().NotifyBackfillComplete.Should().BeFalse();
            surface.Notifications.QuietHoursEnabled.Should().BeTrue();
            changes.Should().Be(1);
            presentationChanges.Should().Be(0, "a notification change is not a theme or density change");
        }
        finally
        {
            SettingsConfigurationService.SetDesktopPreferencesFilePathOverrideForTests(null);
        }
    }
}
