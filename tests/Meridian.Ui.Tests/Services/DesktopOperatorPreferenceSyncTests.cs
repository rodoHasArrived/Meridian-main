using FluentAssertions;
using Meridian.Contracts.Workstation;
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
    }

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
