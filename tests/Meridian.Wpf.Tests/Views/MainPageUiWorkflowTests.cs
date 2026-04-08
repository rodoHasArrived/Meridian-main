using System.Windows;
using System.Windows.Automation;
using Meridian.Wpf.Tests.Support;

namespace Meridian.Wpf.Tests.Views;

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

            facade.CommandPaletteResults.Items.OfType<string>().Should().Contain("RunMat");

            facade.SelectCommandPalettePage("RunMat");
            facade.OpenSelectedCommandPalettePage();

            facade.ViewModel.CurrentPageTag.Should().Be("RunMat");
            facade.ShellAutomationStateText.Text.Should().Be("RunMat");
            facade.PageTitleText.Text.Should().Be("Run Mat");
            facade.CommandPaletteOverlay.Visibility.Should().Be(Visibility.Collapsed);
            AutomationProperties.GetAutomationId(facade.CommandPaletteTextBox).Should().Be("CommandPaletteInput");
        });
    }

    [Fact]
    public void MainPage_WorkspaceTileWorkflow_ShouldSwitchBetweenWorkspaceHomePages()
    {
        WpfTestThread.Run(() =>
        {
            using var facade = new MainPageUiAutomationFacade();

            facade.Click(facade.TradingWorkspaceButton);

            facade.ViewModel.CurrentWorkspace.Should().Be("trading");
            facade.ViewModel.CurrentPageTag.Should().Be("TradingShell");
            facade.ShellAutomationStateText.Text.Should().Be("TradingShell");
            facade.PageTitleText.Text.Should().Be("Trading Workspace");
            AutomationProperties.GetAutomationId(facade.TradingWorkspaceButton).Should().Be("WorkspaceTradingButton");

            facade.Click(facade.GovernanceWorkspaceButton);

            facade.ViewModel.CurrentWorkspace.Should().Be("governance");
            facade.ViewModel.CurrentPageTag.Should().Be("GovernanceShell");
            facade.ShellAutomationStateText.Text.Should().Be("GovernanceShell");
            facade.PageTitleText.Text.Should().Be("Governance Workspace");
            AutomationProperties.GetAutomationId(facade.GovernanceWorkspaceButton).Should().Be("WorkspaceGovernanceButton");
        });
    }

    [Fact]
    public void MainPage_FixtureBannerAndTickerToggle_ShouldRespondToUserActions()
    {
        WpfTestThread.Run(() =>
        {
            using var facade = new MainPageUiAutomationFacade();

            facade.SetFixtureMode(true);

            facade.FixtureModeBanner.Visibility.Should().Be(Visibility.Visible);
            facade.FixtureModeLabel.Text.Should().Contain("FIXTURE MODE");
            AutomationProperties.GetAutomationId(facade.FixtureModeDismissButton).Should().Be("FixtureModeDismissButton");

            facade.Click(facade.FixtureModeDismissButton);
            facade.FixtureModeBanner.Visibility.Should().Be(Visibility.Collapsed);

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
