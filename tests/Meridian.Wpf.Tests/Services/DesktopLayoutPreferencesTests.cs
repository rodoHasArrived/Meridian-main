using System.Text.Json;
using Meridian.Ui.Services.Services;

namespace Meridian.Wpf.Tests.Services;

public sealed class DesktopLayoutPreferencesTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), "meridian-layout-prefs-" + Guid.NewGuid().ToString("N"));
    private readonly string _preferencesPath;

    public DesktopLayoutPreferencesTests()
    {
        Directory.CreateDirectory(_tempRoot);
        _preferencesPath = Path.Combine(_tempRoot, "desktop-shell-preferences.json");
        SettingsConfigurationService.SetDesktopPreferencesFilePathOverrideForTests(_preferencesPath);
    }

    [Fact]
    public void BuiltInLayoutPresets_ShouldCoverCommonPersonasWithoutChangingRootNavigation()
    {
        var presets = SettingsConfigurationService.Instance.GetDesktopLayoutPresets();

        presets.Select(preset => preset.Name).Should().BeSupersetOf(new[]
        {
            "Portfolio Manager",
            "Fund Accountant",
            "Data Operator",
            "Evaluator Demo"
        });
        presets.Select(preset => preset.StartupWorkspace).Should().OnlyContain(workspace =>
            workspace is "Trading" or "Portfolio" or "Accounting" or "Reporting" or "Strategy" or "Data" or "Settings");
    }

    [Fact]
    public void ApplyDesktopLayoutPreset_ShouldPersistSelectedPresetAndBasicPreferences()
    {
        var service = SettingsConfigurationService.Instance;

        service.ApplyDesktopLayoutPreset("fund-accountant");
        SettingsConfigurationService.SetDesktopPreferencesFilePathOverrideForTests(_preferencesPath);

        var reloaded = SettingsConfigurationService.Instance.GetDesktopShellPreferences();

        reloaded.SelectedLayoutPresetId.Should().Be("fund-accountant");
        reloaded.StartupWorkspace.Should().Be("Accounting");
        reloaded.ShellDensityMode.Should().Be(ShellDensityMode.Compact);
        reloaded.GridDensity.Should().Be("Dense");
        reloaded.DefaultFilterSet.Should().Be("Close blockers");
        reloaded.IsSummaryPanelVisible.Should().BeTrue();
        reloaded.IsInspectorPanelVisible.Should().BeTrue();
        reloaded.IsActivityPanelVisible.Should().BeTrue();
    }

    [Fact]
    public void SetDesktopShellPreferences_ShouldPersistManualLayoutOverrides()
    {
        var service = SettingsConfigurationService.Instance;
        var preferences = new DesktopShellPreferences(
            ShellDensityMode.Standard,
            "custom-operator",
            "Data",
            IsSummaryPanelVisible: false,
            IsInspectorPanelVisible: true,
            IsActivityPanelVisible: false,
            "Dense",
            "Provider warnings");

        service.SetDesktopShellPreferences(preferences);
        var json = File.ReadAllText(_preferencesPath);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        root.GetProperty("selectedLayoutPresetId").GetString().Should().Be("custom-operator");
        root.GetProperty("startupWorkspace").GetString().Should().Be("Data");
        root.GetProperty("gridDensity").GetString().Should().Be("Dense");
        root.GetProperty("defaultFilterSet").GetString().Should().Be("Provider warnings");
        root.GetProperty("isSummaryPanelVisible").GetBoolean().Should().BeFalse();
        root.GetProperty("isInspectorPanelVisible").GetBoolean().Should().BeTrue();
        root.GetProperty("isActivityPanelVisible").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public void ResetDesktopShellPreferences_ShouldRestorePortfolioManagerDefaults()
    {
        var service = SettingsConfigurationService.Instance;
        service.ApplyDesktopLayoutPreset("data-operator");

        service.ResetDesktopShellPreferences();
        var preferences = service.GetDesktopShellPreferences();

        preferences.SelectedLayoutPresetId.Should().Be("portfolio-manager");
        preferences.StartupWorkspace.Should().Be("Portfolio");
        preferences.ShellDensityMode.Should().Be(ShellDensityMode.Standard);
        preferences.GridDensity.Should().Be("Comfortable");
        preferences.DefaultFilterSet.Should().Be("Open work");
    }

    [Fact]
    public void SettingsPage_ShouldExposeRolePresetControlsAndResetAction()
    {
        var xaml = File.ReadAllText(Path.Combine("src", "Meridian.Wpf", "Views", "SettingsPage.xaml"));

        xaml.Should().Contain("Role Layout Presets");
        xaml.Should().Contain("SettingsRoleLayoutPresetCombo");
        xaml.Should().Contain("SettingsStartupWorkspaceCombo");
        xaml.Should().Contain("SettingsGridDensityCombo");
        xaml.Should().Contain("Reset to Defaults");
        xaml.Should().Contain("Trading, Portfolio, Accounting, Reporting, Strategy, Data, and Settings");
    }

    public void Dispose()
    {
        SettingsConfigurationService.SetDesktopPreferencesFilePathOverrideForTests(null);
        if (Directory.Exists(_tempRoot))
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
    }
}
