using System.IO;
using System.Windows.Automation;
using Meridian.Wpf.Models;
using Meridian.Wpf.Tests.Support;
using Meridian.Wpf.Views;

namespace Meridian.Wpf.Tests.Views;

[Collection("NavigationServiceSerialCollection")]
public sealed class RunMatUiSmokeTests
{
    [Fact]
    public void MainPage_RunMatNavigation_ShouldLoadRunMatPageAndExposeAutomationHooks()
    {
        WpfTestThread.Run(() =>
        {
            using var facade = new MainPageUiAutomationFacade();

            AutomationProperties.GetAutomationId(facade.Page).Should().Be("MainPage");
            AutomationProperties.GetAutomationId(facade.CommandPaletteTextBox).Should().Be("CommandPaletteInput");
            AutomationProperties.GetAutomationId(facade.CommandPaletteResults).Should().Be("CommandPaletteResults");
            AutomationProperties.GetAutomationId(facade.ContentFrame).Should().Be("ContentFrame");

            var initialDescriptor = ShellNavigationCatalog.GetPage(facade.ShellAutomationStateText.Text);
            initialDescriptor.Should().NotBeNull();
            facade.ViewModel.CurrentPageTitle.Should().Be(initialDescriptor!.Title);

            facade.ViewModel.CommandPalettePages
                .Select(item => item.PageTag)
                .Should()
                .Contain("RunMat");

            facade.ShowCommandPalette();
            facade.SetText(facade.CommandPaletteTextBox, "mat");
            facade.SelectCommandPalettePage("RunMat");
            facade.OpenSelectedCommandPalettePage();

            facade.ContentFrame.Content.Should().BeOfType<WorkspaceDeepPageHostPage>();
            NavigationHostInspector.ResolveInnermostPage(facade.ContentFrame.Content).Should().BeOfType<RunMatPage>();
            facade.ShellAutomationStateText.Text.Should().Be("RunMat");
            facade.ViewModel.CurrentPageTitle.Should().Be("Run Mat");

            var runMatXaml = File.ReadAllText(RunMatUiAutomationFacade.GetRepoFilePath(@"src\Meridian.Wpf\Views\RunMatPage.xaml"));
            runMatXaml.Should().Contain("AutomationProperties.AutomationId=\"RunMatPageTitle\"");
            runMatXaml.Should().Contain("AutomationProperties.AutomationId=\"RunMatExecutablePathInput\"");
            runMatXaml.Should().Contain("AutomationProperties.AutomationId=\"RunMatWorkingDirectoryInput\"");
            runMatXaml.Should().Contain("AutomationProperties.AutomationId=\"RunMatRunScriptButton\"");
            runMatXaml.Should().Contain("AutomationProperties.AutomationId=\"RunMatResolvedExecutablePath\"");
            runMatXaml.Should().Contain("AutomationProperties.AutomationId=\"RunMatOutputList\"");
            runMatXaml.Should().Contain("AutomationProperties.AutomationId=\"RunMatAutomationStatus\"");
            runMatXaml.Should().Contain("AutomationProperties.AutomationId=\"RunMatAutomationLastRun\"");
            runMatXaml.Should().Contain("AutomationProperties.AutomationId=\"RunMatAutomationResolvedExecutable\"");
        });
    }
}
