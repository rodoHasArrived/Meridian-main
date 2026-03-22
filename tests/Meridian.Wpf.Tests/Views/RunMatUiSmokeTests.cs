using System.IO;
using System.Windows.Automation;
using System.Windows.Controls;
using Meridian.Ui.Services;
using Meridian.Wpf.Services;
using Meridian.Wpf.Tests.Support;
using Meridian.Wpf.Views;

namespace Meridian.Wpf.Tests.Views;

public sealed class RunMatUiSmokeTests
{
    [Fact]
    public void MainPage_RunMatNavigation_ShouldLoadRunMatPageAndExposeAutomationHooks()
    {
        WpfTestThread.Run(() =>
        {
            RunMatUiAutomationFacade.EnsureApplicationResources();

            var navigationService = NavigationService.Instance;
            navigationService.SetServiceProvider(RunMatUiAutomationFacade.CreateMainPageServiceProvider());
            var page = new MainPage(
                navigationService,
                ConnectionService.Instance,
                SearchService.Instance,
                MessagingService.Instance);

            RunMatUiAutomationFacade.InvokeMainPageLoaded(page);

            var commandPaletteInput = page.FindName("CommandPaletteTextBox").Should().BeOfType<TextBox>().Subject;
            var commandPaletteResults = page.FindName("CommandPaletteResults").Should().BeOfType<ListBox>().Subject;
            var researchNavList = page.FindName("ResearchNavList").Should().BeOfType<ListBox>().Subject;
            var pageTitle = page.FindName("PageTitleText").Should().BeOfType<TextBlock>().Subject;
            var contentFrame = page.FindName("ContentFrame").Should().BeOfType<Frame>().Subject;
            var shellAutomationState = page.FindName("ShellAutomationStateText").Should().BeOfType<TextBlock>().Subject;

            AutomationProperties.GetAutomationId(page).Should().Be("MainPage");
            AutomationProperties.GetAutomationId(commandPaletteInput).Should().Be("CommandPaletteInput");
            AutomationProperties.GetAutomationId(commandPaletteResults).Should().Be("CommandPaletteResults");
            AutomationProperties.GetAutomationId(contentFrame).Should().Be("ContentFrame");
            shellAutomationState.Text.Should().Contain("PageTag=Dashboard");

            var runMatNavItem = researchNavList.Items
                .OfType<ListBoxItem>()
                .Single(item => string.Equals(item.Tag as string, "RunMat", StringComparison.Ordinal));

            AutomationProperties.GetAutomationId(runMatNavItem).Should().Be("ResearchNavRunMat");
            AutomationProperties.GetName(runMatNavItem).Should().Be("RunMat Lab Navigation");

            RunMatUiAutomationFacade.InvokeNavigateToPage(page, "RunMat");

            contentFrame.Content.Should().BeOfType<RunMatPage>();
            pageTitle.Text.Should().Be("RunMat Lab");
            shellAutomationState.Text.Should().Contain("PageTag=RunMat").And.Contain("PageTitle=RunMat Lab");

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
