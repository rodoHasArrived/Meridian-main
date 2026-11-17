using FluentAssertions;
using Meridian.Contracts.Workstation;
using Meridian.Ui.Services;
using Meridian.Ui.Services.Contracts;
using Meridian.Ui.Services.Services;

namespace Meridian.Ui.Tests.Services;

public sealed class DesktopOperatorPreferenceSyncTests
{
    private sealed class FakeApi : IOperatorPreferencesApi
    {
        public OperatorPreferencesDto? Stored { get; set; }
        public bool Unreachable { get; set; }
        public bool FailSaves { get; set; }
        public List<OperatorPreferenceValuesDto> Saves { get; } = [];
        public int Reads { get; private set; }

        public Task<OperatorPreferencesDto?> GetAsync(CancellationToken ct = default)
        {
            Reads++;
            return Task.FromResult(Unreachable ? null : Stored ?? new OperatorPreferencesDto(new OperatorPreferenceValuesDto(), null));
        }

        public Task<OperatorPreferencesDto?> SaveAsync(OperatorPreferenceValuesDto preferences, CancellationToken ct = default)
        {
            if (FailSaves)
            {
                return Task.FromResult<OperatorPreferencesDto?>(null);
            }

            Saves.Add(preferences);
            Stored = new OperatorPreferencesDto(preferences, DateTimeOffset.UtcNow);
            return Task.FromResult<OperatorPreferencesDto?>(Stored);
        }
    }

    private sealed class FakeSurface : IDesktopPreferenceSurface
    {
        public AppTheme Theme { get; private set; } = AppTheme.Light;
        public ShellDensityMode Density { get; private set; } = ShellDensityMode.Standard;
        public int Applied { get; private set; }

        public event EventHandler? PreferenceChanged;

        // Applying from the host goes through the same setters an operator uses, so a surface that
        // raised PreferenceChanged here would loop back into a save; the sync must ignore that.
        public void ApplyTheme(AppTheme theme)
        {
            Theme = theme;
            Applied++;
            PreferenceChanged?.Invoke(this, EventArgs.Empty);
        }

        public void ApplyDensity(ShellDensityMode density)
        {
            Density = density;
            Applied++;
            PreferenceChanged?.Invoke(this, EventArgs.Empty);
        }

        public void OperatorChangesTheme(AppTheme theme)
        {
            Theme = theme;
            PreferenceChanged?.Invoke(this, EventArgs.Empty);
        }

        public void OperatorChangesDensity(ShellDensityMode density)
        {
            Density = density;
            PreferenceChanged?.Invoke(this, EventArgs.Empty);
        }

        public IReadOnlyDictionary<string, DesktopShortcutOverride> ShortcutOverrides { get; private set; } =
            new Dictionary<string, DesktopShortcutOverride>();

        public int ShortcutsApplied { get; private set; }

        public event EventHandler? ShortcutOverridesChanged;

        // Like the WPF surface, applying host overrides writes the local store, which reports a change.
        public void ApplyShortcutOverrides(IReadOnlyDictionary<string, DesktopShortcutOverride> overrides)
        {
            ShortcutOverrides = overrides;
            ShortcutsApplied++;
            ShortcutOverridesChanged?.Invoke(this, EventArgs.Empty);
        }

        public void OperatorChangesShortcuts(IReadOnlyDictionary<string, DesktopShortcutOverride> overrides)
        {
            ShortcutOverrides = overrides;
            ShortcutOverridesChanged?.Invoke(this, EventArgs.Empty);
        }

        public void HasLocalShortcuts(IReadOnlyDictionary<string, DesktopShortcutOverride> overrides) => ShortcutOverrides = overrides;

        public NotificationSettings Notifications { get; private set; } = new() { SoundType = "Subtle" };

        public int NotificationsApplied { get; private set; }

        public event EventHandler? NotificationsChanged;

        public void ApplyNotifications(NotificationSettings settings)
        {
            Notifications = settings;
            NotificationsApplied++;
            NotificationsChanged?.Invoke(this, EventArgs.Empty);
        }

        public void OperatorChangesNotifications(Action<NotificationSettings> change)
        {
            var next = Notifications.Copy();
            change(next);
            Notifications = next;
            NotificationsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private static readonly IReadOnlyDictionary<string, DesktopShortcutOverride> NoShortcuts =
        new Dictionary<string, DesktopShortcutOverride>();

    private static IReadOnlyDictionary<string, DesktopShortcutOverride> HelpDisabledAndDashboardOnCtrlShiftH() =>
        new Dictionary<string, DesktopShortcutOverride>
        {
            ["Help"] = new(IsEnabled: false),
            ["NavigateDashboard"] = new("H", "Control, Shift")
        };

    private static IReadOnlyDictionary<string, OperatorDesktopShortcutDto> HostShortcuts(
        IReadOnlyDictionary<string, DesktopShortcutOverride> overrides)
        => DesktopOperatorPreferenceMapping.ToHostShortcuts(overrides);

    private static OperatorPreferencesDto Host(OperatorPreferenceValuesDto values) => new(values, DateTimeOffset.Parse("2026-11-17T09:00:00Z"));

    private static async Task WaitForAsync(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 100 && !condition(); attempt++)
        {
            await Task.Delay(10);
        }
    }

    [Fact]
    public async Task StartAsync_AppliesTheOperatorsBrowserChoicesWithoutSavingBack()
    {
        var api = new FakeApi { Stored = Host(new OperatorPreferenceValuesDto(Appearance: "dark", Density: "compact", DisplayTimeZone: "UTC")) };
        var surface = new FakeSurface();
        using var sync = new DesktopOperatorPreferenceSync(api, surface, _ => { });

        await sync.StartAsync();

        surface.Theme.Should().Be(AppTheme.Dark);
        surface.Density.Should().Be(ShellDensityMode.Compact);
        sync.IsSyncing.Should().BeTrue();
        sync.DisplayTimeZone.Should().Be(TimeZoneInfo.Utc);
        await Task.Delay(50);
        api.Saves.Should().BeEmpty("applying host values is not an operator change");
    }

    [Fact]
    public async Task DisplayTimeZone_IsAppliedAtStartAndRefreshedWhenTheDesktopSaves()
    {
        var applied = new List<TimeZoneInfo?>();
        var api = new FakeApi { Stored = Host(new OperatorPreferenceValuesDto(Appearance: "light", DisplayTimeZone: "Europe/London")) };
        var surface = new FakeSurface();
        using var sync = new DesktopOperatorPreferenceSync(api, surface, applied.Add);

        await sync.StartAsync();
        applied.Should().ContainSingle().Which!.Id.Should().Be("Europe/London");

        // The operator picks Tokyo in the browser, then changes the theme on the desktop.
        api.Stored = Host(new OperatorPreferenceValuesDto(Appearance: "light", DisplayTimeZone: "Asia/Tokyo"));
        surface.OperatorChangesTheme(AppTheme.Dark);
        await WaitForAsync(() => applied.Count == 2);

        applied[^1]!.Id.Should().Be("Asia/Tokyo");
        sync.DisplayTimeZone!.Id.Should().Be("Asia/Tokyo");
    }

    [Fact]
    public async Task DisplayTimeZone_FallsBackToUtcForAZoneThisMachineCannotResolve()
    {
        var applied = new List<TimeZoneInfo?>();
        var api = new FakeApi { Stored = Host(new OperatorPreferenceValuesDto(DisplayTimeZone: "Mars/Olympus_Mons")) };
        using var sync = new DesktopOperatorPreferenceSync(api, new FakeSurface(), applied.Add);

        await sync.StartAsync();

        applied.Should().ContainSingle().Which.Should().BeNull("null restores UTC");
    }

    [Fact]
    public async Task StartAsync_AppliesTheHostsDesktopShortcutsWithoutSavingBack()
    {
        var api = new FakeApi
        {
            Stored = Host(new OperatorPreferenceValuesDto(DesktopKeyBindings: HostShortcuts(HelpDisabledAndDashboardOnCtrlShiftH())))
        };
        var surface = new FakeSurface();
        surface.HasLocalShortcuts(new Dictionary<string, DesktopShortcutOverride> { ["Refresh"] = new(IsEnabled: false) });
        using var sync = new DesktopOperatorPreferenceSync(api, surface, _ => { });

        await sync.StartAsync();

        surface.ShortcutOverrides.Should().BeEquivalentTo(HelpDisabledAndDashboardOnCtrlShiftH(), "the host set replaces this desktop's");
        await Task.Delay(50);
        api.Saves.Should().BeEmpty("applying host values is not an operator change");
    }

    [Fact]
    public async Task StartAsync_AppliesAnEmptyHostSetAsEveryDefault()
    {
        var api = new FakeApi { Stored = Host(new OperatorPreferenceValuesDto(DesktopKeyBindings: HostShortcuts(NoShortcuts))) };
        var surface = new FakeSurface();
        surface.HasLocalShortcuts(HelpDisabledAndDashboardOnCtrlShiftH());
        using var sync = new DesktopOperatorPreferenceSync(api, surface, _ => { });

        await sync.StartAsync();

        surface.ShortcutOverrides.Should().BeEmpty("another desktop reset every shortcut");
        api.Saves.Should().BeEmpty();
    }

    [Fact]
    public async Task StartAsync_SeedsTheHostWithThisDesktopsShortcutsWhenItHasNone()
    {
        var api = new FakeApi { Stored = Host(new OperatorPreferenceValuesDto(Appearance: "dark", KeyBindings: new Dictionary<string, string> { ["command-palette"] = "Mod+P" })) };
        var surface = new FakeSurface();
        surface.HasLocalShortcuts(HelpDisabledAndDashboardOnCtrlShiftH());
        using var sync = new DesktopOperatorPreferenceSync(api, surface, _ => { });

        await sync.StartAsync();

        surface.ShortcutsApplied.Should().Be(0);
        var saved = api.Saves.Should().ContainSingle().Subject;
        saved.DesktopKeyBindings.Should().BeEquivalentTo(HostShortcuts(HelpDisabledAndDashboardOnCtrlShiftH()));
        saved.Appearance.Should().Be("dark");
        saved.KeyBindings.Should().ContainKey("command-palette", "browser shortcut overrides are kept");
    }

    [Fact]
    public async Task StartAsync_SavesNothingWhenNeitherSideHasDesktopShortcuts()
    {
        var api = new FakeApi { Stored = Host(new OperatorPreferenceValuesDto(Appearance: "dark")) };
        using var sync = new DesktopOperatorPreferenceSync(api, new FakeSurface(), _ => { });

        await sync.StartAsync();
        await Task.Delay(50);

        api.Saves.Should().BeEmpty();
    }

    [Fact]
    public async Task ShortcutChange_SavesOnlyDesktopShortcutsAndKeepsEverythingElse()
    {
        var api = new FakeApi
        {
            Stored = Host(new OperatorPreferenceValuesDto(
                Appearance: "dark",
                Density: "spacious",
                DisplayTimeZone: "Europe/London",
                PageDescriptionsHidden: true,
                KeyBindings: new Dictionary<string, string> { ["command-palette"] = "Mod+P" }))
        };
        var surface = new FakeSurface();
        using var sync = new DesktopOperatorPreferenceSync(api, surface, _ => { });
        await sync.StartAsync();
        var before = api.Stored!.Preferences;

        surface.OperatorChangesShortcuts(HelpDisabledAndDashboardOnCtrlShiftH());
        await WaitForAsync(() => api.Saves.Count == 1);

        var saved = api.Saves.Should().ContainSingle().Subject;
        saved.DesktopKeyBindings.Should().BeEquivalentTo(HostShortcuts(HelpDisabledAndDashboardOnCtrlShiftH()));
        saved.Should().BeEquivalentTo(
            before with { DesktopKeyBindings = saved.DesktopKeyBindings },
            "only the desktop shortcuts change");
        saved.Density.Should().Be("spacious");
        saved.KeyBindings.Should().ContainKey("command-palette");
    }

    [Fact]
    public async Task ShortcutResetToDefaults_SavesAnEmptySetSoOtherDesktopsResetToo()
    {
        var api = new FakeApi { Stored = Host(new OperatorPreferenceValuesDto(DesktopKeyBindings: HostShortcuts(HelpDisabledAndDashboardOnCtrlShiftH()))) };
        var surface = new FakeSurface();
        using var sync = new DesktopOperatorPreferenceSync(api, surface, _ => { });
        await sync.StartAsync();

        surface.OperatorChangesShortcuts(NoShortcuts);
        await WaitForAsync(() => api.Saves.Count == 1);

        api.Saves.Should().ContainSingle().Which.DesktopKeyBindings.Should().NotBeNull().And.BeEmpty();
    }

    [Fact]
    public async Task ThemeChange_DoesNotTouchTheHostsDesktopShortcuts()
    {
        var hostShortcuts = HostShortcuts(HelpDisabledAndDashboardOnCtrlShiftH());
        var api = new FakeApi { Stored = Host(new OperatorPreferenceValuesDto(Appearance: "light", DesktopKeyBindings: hostShortcuts)) };
        var surface = new FakeSurface();
        using var sync = new DesktopOperatorPreferenceSync(api, surface, _ => { });
        await sync.StartAsync();

        surface.OperatorChangesTheme(AppTheme.Dark);
        await WaitForAsync(() => api.Saves.Count == 1);

        var saved = api.Saves.Should().ContainSingle().Subject;
        saved.Appearance.Should().Be("dark");
        saved.DesktopKeyBindings.Should().BeEquivalentTo(hostShortcuts);
    }

    [Fact]
    public void ShortcutMapping_RoundTripsAndComparesByValue()
    {
        var host = DesktopOperatorPreferenceMapping.ToHostShortcuts(HelpDisabledAndDashboardOnCtrlShiftH());

        host.Keys.Should().Equal("Help", "NavigateDashboard");
        DesktopOperatorPreferenceMapping.ToDesktopShortcuts(host).Should().BeEquivalentTo(HelpDisabledAndDashboardOnCtrlShiftH());
        DesktopOperatorPreferenceMapping.SameShortcuts(host, HostShortcuts(HelpDisabledAndDashboardOnCtrlShiftH())).Should().BeTrue();
        DesktopOperatorPreferenceMapping.SameShortcuts(host, HostShortcuts(NoShortcuts)).Should().BeFalse();
        DesktopOperatorPreferenceMapping.SameShortcuts(null, HostShortcuts(NoShortcuts)).Should().BeFalse("not stored differs from every default");
        DesktopOperatorPreferenceMapping.SameShortcuts(null, null).Should().BeTrue();
    }

    [Fact]
    public async Task StartAsync_AppliesTheHostsNotificationChoicesWithoutSavingBack()
    {
        var api = new FakeApi
        {
            Stored = Host(new OperatorPreferenceValuesDto(Notifications: new OperatorNotificationPreferencesDto(
                BackfillComplete: false,
                QuietHoursEnabled: true,
                QuietHoursStart: "23:15",
                QuietHoursEnd: "bad")))
        };
        var surface = new FakeSurface();
        using var sync = new DesktopOperatorPreferenceSync(api, surface, _ => { });

        await sync.StartAsync();

        surface.NotificationsApplied.Should().Be(1);
        surface.Notifications.NotifyBackfillComplete.Should().BeFalse();
        surface.Notifications.NotifyErrors.Should().BeTrue("an unset type keeps its default");
        surface.Notifications.QuietHoursEnabled.Should().BeTrue();
        surface.Notifications.QuietHoursStart.Should().Be(new TimeSpan(23, 15, 0));
        surface.Notifications.QuietHoursEnd.Should().Be(QuietHours.DefaultEnd, "an unreadable time keeps its default");
        surface.Notifications.SoundType.Should().Be("Subtle", "the sound choice is desktop-only");
        await Task.Delay(50);
        api.Saves.Should().BeEmpty();
    }

    [Fact]
    public async Task StartAsync_LeavesNotificationsAloneWhenTheHostHasNone()
    {
        var api = new FakeApi { Stored = Host(new OperatorPreferenceValuesDto(Appearance: "dark")) };
        var surface = new FakeSurface();
        using var sync = new DesktopOperatorPreferenceSync(api, surface, _ => { });

        await sync.StartAsync();

        surface.NotificationsApplied.Should().Be(0);
    }

    [Fact]
    public async Task NotificationChange_SavesOnlyTheNotificationSetAndKeepsEverythingElse()
    {
        var api = new FakeApi
        {
            Stored = Host(new OperatorPreferenceValuesDto(
                Appearance: "dark",
                DisplayTimeZone: "Europe/London",
                DesktopKeyBindings: HostShortcuts(HelpDisabledAndDashboardOnCtrlShiftH())))
        };
        var surface = new FakeSurface();
        using var sync = new DesktopOperatorPreferenceSync(api, surface, _ => { });
        await sync.StartAsync();
        var before = api.Stored!.Preferences;

        surface.OperatorChangesNotifications(settings =>
        {
            settings.NotifyDataGaps = false;
            settings.QuietHoursEnabled = true;
        });
        await WaitForAsync(() => api.Saves.Count == 1);

        var saved = api.Saves.Should().ContainSingle().Subject;
        saved.Notifications.Should().Be(new OperatorNotificationPreferencesDto(
            Enabled: true,
            ConnectionStatus: true,
            Errors: true,
            BackfillComplete: true,
            DataGaps: false,
            StorageWarnings: true,
            QuietHoursEnabled: true,
            QuietHoursStart: "22:00",
            QuietHoursEnd: "07:00"));
        saved.Should().BeEquivalentTo(before with { Notifications = saved.Notifications }, "only the notification set changes");
    }

    [Fact]
    public void NotificationMapping_RoundTrips()
    {
        var settings = new NotificationSettings
        {
            Enabled = false,
            NotifyConnectionStatus = false,
            NotifyStorageWarnings = false,
            QuietHoursEnabled = true,
            QuietHoursStart = new TimeSpan(21, 45, 0),
            QuietHoursEnd = new TimeSpan(6, 30, 0)
        };

        var host = DesktopOperatorPreferenceMapping.ToHostNotifications(settings);
        host.QuietHoursStart.Should().Be("21:45");
        DesktopOperatorPreferenceMapping.ToNotificationSettings(host, new NotificationSettings())
            .Should().BeEquivalentTo(settings);
    }

    [Fact]
    public async Task StartAsync_DoesNotUploadTheDesktopsDefaultThemeWhenTheHostHoldsNothing()
    {
        var api = new FakeApi();
        var surface = new FakeSurface();
        using var sync = new DesktopOperatorPreferenceSync(api, surface, _ => { });

        await sync.StartAsync();
        await Task.Delay(50);

        api.Saves.Should().BeEmpty();
        surface.Applied.Should().Be(0);
        sync.DisplayTimeZone.Should().BeNull();
    }

    [Fact]
    public async Task OperatorChange_SavesOnlyAppearanceAndDensityAndKeepsBrowserValues()
    {
        var bindings = new Dictionary<string, string> { ["command-palette"] = "Mod+Shift+P" };
        var api = new FakeApi
        {
            Stored = Host(new OperatorPreferenceValuesDto(
                Appearance: "light", Density: "default", DisplayTimeZone: "Europe/London",
                PageDescriptionsHidden: true, KeyBindings: bindings))
        };
        var surface = new FakeSurface();
        using var sync = new DesktopOperatorPreferenceSync(api, surface, _ => { });
        await sync.StartAsync();

        surface.OperatorChangesTheme(AppTheme.Dark);
        await WaitForAsync(() => api.Saves.Count == 1);

        api.Saves.Should().ContainSingle().Which.Should().BeEquivalentTo(new OperatorPreferenceValuesDto(
            Appearance: "dark", Density: "default", DisplayTimeZone: "Europe/London",
            PageDescriptionsHidden: true, KeyBindings: bindings));
    }

    [Fact]
    public async Task OperatorChange_ReReadsTheHostSoAConcurrentBrowserChangeIsKept()
    {
        var api = new FakeApi { Stored = Host(new OperatorPreferenceValuesDto(Appearance: "light")) };
        var surface = new FakeSurface();
        using var sync = new DesktopOperatorPreferenceSync(api, surface, _ => { });
        await sync.StartAsync();

        // The operator picks a zone in the browser while the desktop is open.
        api.Stored = Host(new OperatorPreferenceValuesDto(Appearance: "light", DisplayTimeZone: "Asia/Tokyo"));
        surface.OperatorChangesDensity(ShellDensityMode.Compact);
        await WaitForAsync(() => api.Saves.Count == 1);

        api.Saves.Single().DisplayTimeZone.Should().Be("Asia/Tokyo");
        api.Saves.Single().Density.Should().Be("compact");
    }

    [Fact]
    public async Task OperatorChoosesStandard_DoesNotNarrowABrowserSpaciousDensity()
    {
        var api = new FakeApi { Stored = Host(new OperatorPreferenceValuesDto(Appearance: "light", Density: "spacious")) };
        var surface = new FakeSurface();
        using var sync = new DesktopOperatorPreferenceSync(api, surface, _ => { });
        await sync.StartAsync();
        surface.Density.Should().Be(ShellDensityMode.Standard);

        surface.OperatorChangesTheme(AppTheme.Dark);
        await WaitForAsync(() => api.Saves.Count == 1);

        api.Saves.Single().Density.Should().Be("spacious");
        api.Saves.Single().Appearance.Should().Be("dark");
    }

    [Fact]
    public async Task UnreachableHost_StaysLocalForTheRun()
    {
        var api = new FakeApi { Unreachable = true };
        var surface = new FakeSurface();
        using var sync = new DesktopOperatorPreferenceSync(api, surface, _ => { });

        await sync.StartAsync();
        surface.OperatorChangesTheme(AppTheme.Dark);
        await Task.Delay(50);

        sync.IsSyncing.Should().BeFalse();
        api.Saves.Should().BeEmpty();
        surface.Theme.Should().Be(AppTheme.Dark);
    }

    [Fact]
    public async Task FailedSave_KeepsTheLocalChangeAndTheNextChangeRetries()
    {
        var api = new FakeApi { Stored = Host(new OperatorPreferenceValuesDto(Appearance: "light")), FailSaves = true };
        var surface = new FakeSurface();
        using var sync = new DesktopOperatorPreferenceSync(api, surface, _ => { });
        await sync.StartAsync();

        await sync.SaveLocalChangeAsync();
        surface.OperatorChangesTheme(AppTheme.Dark);
        await Task.Delay(50);
        api.Saves.Should().BeEmpty();
        surface.Theme.Should().Be(AppTheme.Dark);

        api.FailSaves = false;
        surface.OperatorChangesDensity(ShellDensityMode.Compact);
        await WaitForAsync(() => api.Saves.Count == 1);

        api.Saves.Single().Should().Match<OperatorPreferenceValuesDto>(saved => saved.Appearance == "dark" && saved.Density == "compact");
    }

    [Fact]
    public async Task Dispose_StopsSavingOperatorChanges()
    {
        var api = new FakeApi { Stored = Host(new OperatorPreferenceValuesDto(Appearance: "light")) };
        var surface = new FakeSurface();
        var sync = new DesktopOperatorPreferenceSync(api, surface, _ => { });
        await sync.StartAsync();

        sync.Dispose();
        surface.OperatorChangesTheme(AppTheme.Dark);
        await Task.Delay(50);

        api.Saves.Should().BeEmpty();
    }

    [Theory]
    [InlineData("light", AppTheme.Light)]
    [InlineData("dark", AppTheme.Dark)]
    [InlineData("system", AppTheme.System)]
    [InlineData("neon", null)]
    [InlineData(null, null)]
    public void ToAppTheme_MapsTheSharedVocabulary(string? appearance, AppTheme? expected)
    {
        DesktopOperatorPreferenceMapping.ToAppTheme(appearance).Should().Be(expected);
    }

    [Theory]
    [InlineData("compact", ShellDensityMode.Compact)]
    [InlineData("default", ShellDensityMode.Standard)]
    [InlineData("spacious", ShellDensityMode.Standard)]
    [InlineData("cosy", null)]
    public void ToShellDensity_FoldsTheBrowsersThreeDensitiesIntoTwo(string? density, ShellDensityMode? expected)
    {
        DesktopOperatorPreferenceMapping.ToShellDensity(density).Should().Be(expected);
    }

    [Fact]
    public void Mapping_RoundTripsEveryVocabularyValue()
    {
        foreach (var appearance in OperatorPreferenceVocabulary.Appearances)
        {
            DesktopOperatorPreferenceMapping.ToAppearance(DesktopOperatorPreferenceMapping.ToAppTheme(appearance)!.Value)
                .Should().Be(appearance);
        }

        foreach (var density in OperatorPreferenceVocabulary.Densities)
        {
            var shell = DesktopOperatorPreferenceMapping.ToShellDensity(density)!.Value;
            DesktopOperatorPreferenceMapping.ToHostDensity(shell, density).Should().Be(density);
        }
    }

    [Theory]
    [InlineData("UTC", true)]
    [InlineData("Europe/London", true)]
    [InlineData("Mars/Olympus_Mons", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void TryResolveTimeZone_UsesThisMachinesZoneData(string? zone, bool expected)
    {
        DesktopOperatorPreferenceMapping.TryResolveTimeZone(zone, out _).Should().Be(expected);
    }
}
