using Meridian.Wpf.Services;
using Meridian.Wpf.Tests.Support;
using Meridian.Wpf.ViewModels;

namespace Meridian.Wpf.Tests.ViewModels;

public sealed class SettingsViewModelTests
{
    [Fact]
    public void AppVersionText_ReportsRealAssemblyVersion()
    {
        WpfTestThread.Run(() =>
        {
            var viewModel = CreateViewModel();
            var expected = typeof(SettingsViewModel).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

            DiagnosticsPageViewModel.AppVersion.Should().Be(expected);
            viewModel.AppVersionText.Should().Be($"Version {expected}");
        });
    }

    [Fact]
    public void RecentActivity_IsEmpty_WithNoFabricatedEntries()
    {
        WpfTestThread.Run(() =>
        {
            var viewModel = CreateViewModel();

            viewModel.RecentActivity.Should().BeEmpty();
        });
    }

    [Fact]
    public void SettingsSurfaces_DisplayAssemblyVersionWithoutFabricatedContent()
    {
        var viewModelSource = File.ReadAllText(RunMatUiAutomationFacade.GetRepoFilePath(@"src\Meridian.Wpf\ViewModels\SettingsViewModel.cs"));
        var settingsXaml = File.ReadAllText(RunMatUiAutomationFacade.GetRepoFilePath(@"src\Meridian.Wpf\Views\SettingsPage.xaml"));
        var diagnosticsXaml = File.ReadAllText(RunMatUiAutomationFacade.GetRepoFilePath(@"src\Meridian.Wpf\Views\DiagnosticsPage.xaml"));
        var diagnosticsViewModelSource = File.ReadAllText(RunMatUiAutomationFacade.GetRepoFilePath(@"src\Meridian.Wpf\ViewModels\DiagnosticsPageViewModel.cs"));

        viewModelSource.Should().NotContain("1.6.1");
        viewModelSource.Should().NotContain("You are running the latest version");
        viewModelSource.Should().NotContain("Cloud sync");
        settingsXaml.Should().NotContain("1.6.1");
        settingsXaml.Should().Contain("{Binding AppVersionText}");
        diagnosticsXaml.Should().NotContain("1.6.1");
        diagnosticsXaml.Should().Contain("x:Name=\"AppVersionText\"");
        diagnosticsViewModelSource.Should().NotContain("1.6.1");
        diagnosticsViewModelSource.Should().Contain("Assembly.GetName().Version");
    }

    private static SettingsViewModel CreateViewModel()
        => new(
            ConfigService.Instance,
            NotificationService.Instance,
            StatusService.Instance);
}
