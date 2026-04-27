using System.IO;
using System.Windows;
using System.Windows.Controls;
using Meridian.Wpf.Models;
using Meridian.Wpf.Tests.Support;
using Meridian.Wpf.Views;

namespace Meridian.Wpf.Tests.Views;

public sealed class WorkspaceDeepPageChromeTests
{
    [Fact]
    public void WorkspaceDeepPageHostPage_ShouldToggleHostedShellState()
    {
            WpfTestThread.Run(() =>
            {
                RunMatUiAutomationFacade.EnsureApplicationResources();

            var navigationService = (Meridian.Wpf.Services.NavigationService)Activator.CreateInstance(
                typeof(Meridian.Wpf.Services.NavigationService),
                nonPublic: true)!;

            var hostedPage = new Page();
            var hostPage = new WorkspaceDeepPageHostPage(
                navigationService,
                shellContextService: null,
                pageTag: "EventReplay",
                hostedPage,
                navigationParameter: null,
                presentationMode: WorkspaceChromePresentationMode.Docked);

            var window = new Window
            {
                Width = 960,
                Height = 720,
                Content = hostPage
            };

            try
            {
                WorkspaceShellChromeState.GetIsHostedInWorkspaceShell(hostedPage).Should().BeFalse();

                window.Show();
                RunMatUiAutomationFacade.DrainDispatcher();
                window.UpdateLayout();

                WorkspaceShellChromeState.GetIsHostedInWorkspaceShell(hostedPage).Should().BeTrue();
            }
            finally
            {
                window.Close();
                RunMatUiAutomationFacade.DrainDispatcher();
                WorkspaceShellChromeState.GetIsHostedInWorkspaceShell(hostedPage).Should().BeFalse();
            }
        });
    }

    [Theory]
    [InlineData("ProviderHealth", "Provider Health", "Data Operations")]
    [InlineData("Diagnostics", "Diagnostics", "Governance")]
    public void WorkspaceDeepPageHostPage_ShouldKeepStandaloneChromeCompactAtReviewViewport(
        string pageTag,
        string expectedTitle,
        string expectedWorkspace)
    {
        WpfTestThread.Run(() =>
        {
            RunMatUiAutomationFacade.EnsureApplicationResources();

            var navigationService = (Meridian.Wpf.Services.NavigationService)Activator.CreateInstance(
                typeof(Meridian.Wpf.Services.NavigationService),
                nonPublic: true)!;

            var hostPage = new WorkspaceDeepPageHostPage(
                navigationService,
                shellContextService: null,
                pageTag,
                new Page(),
                navigationParameter: null,
                presentationMode: WorkspaceChromePresentationMode.Standalone);

            var window = new Window
            {
                Width = 1365,
                Height = 768,
                Content = hostPage
            };

            try
            {
                window.Show();
                RunMatUiAutomationFacade.DrainDispatcher();
                window.UpdateLayout();

                var title = hostPage.FindName("PageTitleText").Should().BeOfType<TextBlock>().Subject;
                var subtitle = hostPage.FindName("PageSubtitleText").Should().BeOfType<TextBlock>().Subject;
                var summaryCard = hostPage.FindName("SummaryCard").Should().BeOfType<Border>().Subject;
                var workspaceBadge = hostPage.FindName("WorkspaceBadgeText").Should().BeOfType<TextBlock>().Subject;
                var hostedFrame = hostPage.FindName("HostedFrame").Should().BeOfType<Frame>().Subject;

                title.Text.Should().Be(expectedTitle);
                subtitle.Text.Should().NotBeNullOrWhiteSpace();
                workspaceBadge.Text.Should().Be(expectedWorkspace);
                summaryCard.ActualHeight.Should().BeLessThan(180);
                hostedFrame.ActualHeight.Should().BeGreaterThan(360);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Theory]
    [InlineData(@"src\Meridian.Wpf\Views\AnalysisExportPage.xaml", "EmbeddedShellHeaderStackPanelStyle")]
    [InlineData(@"src\Meridian.Wpf\Views\SymbolMappingPage.xaml", "EmbeddedShellHeaderGridStyle")]
    [InlineData(@"src\Meridian.Wpf\Views\RetentionAssurancePage.xaml", "EmbeddedShellHeaderStackPanelStyle")]
    [InlineData(@"src\Meridian.Wpf\Views\DataExportPage.xaml", "EmbeddedShellHeroCardStyle")]
    [InlineData(@"src\Meridian.Wpf\Views\SettingsPage.xaml", "EmbeddedShellHeroCardStyle")]
    [InlineData(@"src\Meridian.Wpf\Views\WelcomePage.xaml", "EmbeddedShellHeroCardStyle")]
    [InlineData(@"src\Meridian.Wpf\Views\MessagingHubPage.xaml", "EmbeddedShellCompactHeaderCardStyle")]
    [InlineData(@"src\Meridian.Wpf\Views\NotificationCenterPage.xaml", "EmbeddedShellCompactHeaderCardStyle")]
    [InlineData(@"src\Meridian.Wpf\Views\SecurityMasterPage.xaml", "EmbeddedShellCompactHeaderCardStyle")]
    [InlineData(@"src\Meridian.Wpf\Views\ServiceManagerPage.xaml", "EmbeddedShellHeroCardStyle")]
    [InlineData(@"src\Meridian.Wpf\Views\ProviderHealthPage.xaml", "EmbeddedShellHeroCardStyle")]
    [InlineData(@"src\Meridian.Wpf\Views\DiagnosticsPage.xaml", "EmbeddedShellHeroCardStyle")]
    [InlineData(@"src\Meridian.Wpf\Views\DataQualityPage.xaml", "WorkspaceCommandBarControl")]
    [InlineData(@"src\Meridian.Wpf\Views\DataQualityPage.xaml", "WorkspaceInteractionPanel")]
    [InlineData(@"src\Meridian.Wpf\Views\PositionBlotterPage.xaml", "EmbeddedShellCompactHeaderCardStyle")]
    public void LegacyDeepPages_ShouldOptIntoSharedEmbeddedShellChrome(string relativePath, string expectedMarker)
    {
        var absolutePath = RunMatUiAutomationFacade.GetRepoFilePath(relativePath);
        File.ReadAllText(absolutePath).Should().Contain(expectedMarker);
    }

    [Theory]
    [InlineData(@"src\Meridian.Wpf\Views\PositionBlotterPage.xaml", "PositionBlotterWorkbench")]
    [InlineData(@"src\Meridian.Wpf\Views\PositionBlotterPage.xaml", "PositionBlotterSelectionInspector")]
    [InlineData(@"src\Meridian.Wpf\Views\PositionBlotterPage.xaml", "PositionBlotterActionInspector")]
    [InlineData(@"src\Meridian.Wpf\Views\SecurityMasterPage.xaml", "SecurityMasterResultsWorkbench")]
    [InlineData(@"src\Meridian.Wpf\Views\SecurityMasterPage.xaml", "SecurityMasterTrustPostureInspector")]
    [InlineData(@"src\Meridian.Wpf\Views\SecurityMasterPage.xaml", "SecurityMasterSelectionInspector")]
    [InlineData(@"src\Meridian.Wpf\Views\SecurityMasterPage.xaml", "SecurityMasterIdentityProvenanceTab")]
    [InlineData(@"src\Meridian.Wpf\Views\SecurityMasterPage.xaml", "SecurityMasterConflictTriagePanel")]
    [InlineData(@"src\Meridian.Wpf\Views\SecurityMasterPage.xaml", "SecurityMasterBulkResolveButton")]
    [InlineData(@"src\Meridian.Wpf\Views\SecurityMasterPage.xaml", "SecurityMasterRecommendedActionsList")]
    [InlineData(@"src\Meridian.Wpf\Views\ServiceManagerPage.xaml", "ServiceManagerControlWorkbench")]
    [InlineData(@"src\Meridian.Wpf\Views\ServiceManagerPage.xaml", "ServiceManagerRuntimeInspector")]
    public void HarmonizedDeepPages_ShouldExposeWorkbenchAndInspectorAutomationIds(string relativePath, string automationId)
    {
        var absolutePath = RunMatUiAutomationFacade.GetRepoFilePath(relativePath);
        File.ReadAllText(absolutePath).Should().Contain(automationId);
    }

    [Fact]
    public void DataQualityPage_ShouldAvoidBlockingPromptsInsideHostedWorkspaceExperience()
    {
        var pageCodePath = RunMatUiAutomationFacade.GetRepoFilePath(@"src\Meridian.Wpf\Views\DataQualityPage.xaml.cs");
        var code = File.ReadAllText(pageCodePath);

        code.Should().NotContain("MessageBox.Show");
        code.Should().NotContain("Interaction.InputBox");
        code.Should().Contain("OpenWorkflow(");
    }
}
