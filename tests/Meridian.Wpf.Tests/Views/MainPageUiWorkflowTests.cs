using System.Windows;
using System.Windows.Automation;
using Meridian.Wpf.Models;
using Meridian.Wpf.Tests.Support;
using Meridian.Ui.Services.Services;

namespace Meridian.Wpf.Tests.Views;

[Collection("NavigationServiceSerialCollection")]
public sealed class MainPageUiWorkflowTests
{
    [Fact]
    public void MainPage_CommandPaletteWorkflow_ShouldFilterAndNavigateToRunMat()
    {
        WpfTestThread.Run(() =>
        {
            using var facade = new MainPageUiAutomationFacade();

            facade.ShowCommandPalette();
            facade.CommandPaletteOverlay.Visibility.Should().Be(Visibility.Visible);
            facade.SetText(facade.CommandPaletteTextBox, "mat");

            facade.CommandPaletteResults.Items
                .OfType<ShellCommandPaletteEntry>()
                .Select(item => item.PageTag)
                .Should()
                .Contain("RunMat");

            facade.OpenCommandPalettePage("RunMat");

            facade.ViewModel.CurrentPageTag.Should().Be("RunMat");
            facade.ShellAutomationStateText.Text.Should().Be("RunMat");
            facade.PageTitleText.Text.Should().Be("Run Mat");
            facade.CommandPaletteOverlay.Visibility.Should().Be(Visibility.Collapsed);
            AutomationProperties.GetAutomationId(facade.CommandPaletteTextBox).Should().Be("CommandPaletteInput");
        });
    }

    [Fact]
    public void MainPage_WorkspaceTileWorkflow_ShouldExposeStableWorkspaceLaunchContracts()
    {
        WpfTestThread.Run(() =>
        {
            using var facade = new MainPageUiAutomationFacade();

            AutomationProperties.GetAutomationId(facade.ResearchWorkspaceButton).Should().Be("WorkspaceResearchButton");
            AutomationProperties.GetAutomationId(facade.TradingWorkspaceButton).Should().Be("WorkspaceTradingButton");
            AutomationProperties.GetAutomationId(facade.DataOperationsWorkspaceButton).Should().Be("WorkspaceDataOperationsButton");
            AutomationProperties.GetAutomationId(facade.GovernanceWorkspaceButton).Should().Be("WorkspaceGovernanceButton");

            facade.ResearchWorkspaceButton.Command.Should().BeSameAs(facade.ViewModel.NavigateToPageCommand);
            facade.TradingWorkspaceButton.Command.Should().BeSameAs(facade.ViewModel.NavigateToPageCommand);
            facade.DataOperationsWorkspaceButton.Command.Should().BeSameAs(facade.ViewModel.NavigateToPageCommand);
            facade.GovernanceWorkspaceButton.Command.Should().BeSameAs(facade.ViewModel.NavigateToPageCommand);

            ShellNavigationCatalog.GetWorkspace("research")?.HomePageTag.Should().Be("ResearchShell");
            ShellNavigationCatalog.GetWorkspace("trading")?.HomePageTag.Should().Be("TradingShell");
            ShellNavigationCatalog.GetWorkspace("data-operations")?.HomePageTag.Should().Be("DataOperationsShell");
            ShellNavigationCatalog.GetWorkspace("governance")?.HomePageTag.Should().Be("GovernanceShell");
        });
    }

    [Fact]
    public void MainPage_FixtureBannerAndTickerToggle_ShouldRespondToUserActions()
    {
        WpfTestThread.Run(() =>
        {
            using var facade = new MainPageUiAutomationFacade();

            facade.SetFixtureMode(true);

            FixtureModeDetector.Instance.IsFixtureMode.Should().BeTrue();
            FixtureModeDetector.Instance.ModeLabel.Should().Contain("FIXTURE MODE");
            AutomationProperties.GetAutomationId(facade.FixtureModeDismissButton).Should().Be("FixtureModeDismissButton");

            facade.Click(facade.FixtureModeDismissButton);
            facade.ViewModel.FixtureModeBannerVisibility.Should().Be(Visibility.Collapsed);

            facade.ViewModel.TickerStripVisible.Should().BeFalse();
            facade.Click(facade.TickerStripToggleButton);
            facade.ViewModel.TickerStripVisible.Should().BeTrue();
            facade.TickerStripToggleLabelText.Text.Should().Be("Hide Ticker Strip");

            facade.Click(facade.TickerStripToggleButton);
            facade.ViewModel.TickerStripVisible.Should().BeFalse();
            facade.TickerStripToggleLabelText.Text.Should().Be("Ticker Strip");
        });
    }
}
