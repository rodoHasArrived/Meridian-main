using Meridian.Contracts.Api;
using Meridian.Contracts.Workstation;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using Meridian.Wpf.Models;
using Meridian.Wpf.Tests.Support;
using Meridian.Ui.Services.Services;
using Meridian.Wpf.Services;
using Meridian.Wpf.ViewModels;

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
            AutomationProperties.GetName(facade.ShellAutomationStateText).Should().Be("RunMat");
            facade.ShellAutomationStateText.Visibility.Should().Be(Visibility.Visible);
            facade.ShellAutomationStateText.Opacity.Should().Be(0);
            facade.PageTitleText.Text.Should().Be("Run scripts");
            AutomationProperties.GetName(facade.PageTitleText).Should().Be("Run scripts");
            facade.PageTitleText.Visibility.Should().Be(Visibility.Visible);
            facade.PageSubtitleText.Visibility.Should().Be(Visibility.Visible);
            facade.PageSubtitleText.Text.Should().Be(facade.ViewModel.CurrentPageSubtitle);
            facade.CommandPaletteOverlay.Visibility.Should().Be(Visibility.Collapsed);
            AutomationProperties.GetAutomationId(facade.CommandPaletteTextBox).Should().Be("CommandPaletteInput");
        });
    }

    [Fact]
    public void MainPage_CommandPaletteArrowKeys_ShouldMoveSelectionWithinSearchResults()
    {
        WpfTestThread.Run(() =>
        {
            using var facade = new MainPageUiAutomationFacade();

            facade.ShowCommandPalette();
            facade.CommandPaletteResults.Items.Count.Should().BeGreaterThan(2);
            facade.ViewModel.SelectedCommandPalettePage?.PageTag.Should().Be("StrategyShell");
            facade.CommandPaletteTextBox.Text.Should().BeEmpty();
            facade.TryHandleCommandPaletteDirectionalKey(Key.Down).Should().BeTrue();

            facade.ViewModel.SelectedCommandPalettePage?.PageTag.Should().Be("Backtest");
            facade.CommandPaletteResults.SelectedItem.Should().BeSameAs(facade.ViewModel.SelectedCommandPalettePage);
            facade.CommandPaletteTextBox.Text.Should().BeEmpty();
            facade.TryHandleCommandPaletteDirectionalKey(Key.Up).Should().BeTrue();

            facade.ViewModel.SelectedCommandPalettePage?.PageTag.Should().Be("StrategyShell");
            facade.CommandPaletteResults.SelectedItem.Should().BeSameAs(facade.ViewModel.SelectedCommandPalettePage);
            facade.CommandPaletteTextBox.Text.Should().BeEmpty();
            facade.CommandPaletteSummaryText.Text.Should().Contain("pages across all workspaces");
        });
    }

    [Fact]
    public void MainPage_CommandPaletteEmptyState_ShouldExposeHelpfulRecoveryAction()
    {
        WpfTestThread.Run(() =>
        {
            using var facade = new MainPageUiAutomationFacade();

            facade.ShowCommandPalette();
            facade.SetText(facade.CommandPaletteTextBox, "zzzz-unmatched-query");

            facade.CommandPaletteResults.Items.Count.Should().Be(0);
            facade.CommandPaletteEmptyState.Visibility.Should().Be(Visibility.Visible);
            facade.CommandPaletteSummaryText.Text.Should().Contain("No matches");
            facade.CommandPaletteEmptyTitleText.Text.Should().Contain("zzzz-unmatched-query");

            facade.Click(facade.CommandPaletteClearButton);

            facade.CommandPaletteTextBox.Text.Should().BeEmpty();
            facade.CommandPaletteEmptyState.Visibility.Should().Be(Visibility.Collapsed);
            facade.CommandPaletteResults.Items.Count.Should().BeGreaterThan(0);
        });
    }

    [Fact]
    public void MainPage_CommandPaletteArrowKeys_ShouldNoOpWhenQueryHasNoMatches()
    {
        WpfTestThread.Run(() =>
        {
            using var facade = new MainPageUiAutomationFacade();

            facade.ShowCommandPalette();
            facade.SetText(facade.CommandPaletteTextBox, "zzzz-unmatched-query");

            facade.CommandPaletteResults.Items.Count.Should().Be(0);
            facade.ViewModel.SelectedCommandPalettePage.Should().BeNull();

            facade.TryHandleCommandPaletteDirectionalKey(Key.Down).Should().BeFalse();
            facade.CommandPaletteEmptyState.Visibility.Should().Be(Visibility.Visible);
            facade.ViewModel.SelectedCommandPalettePage.Should().BeNull();
        });
    }

    [Fact]
    public void MainPage_WorkspaceTileWorkflow_ShouldExposeStableWorkspaceLaunchContracts()
    {
        WpfTestThread.Run(() =>
        {
            using var facade = new MainPageUiAutomationFacade();

            AutomationProperties.GetAutomationId(facade.TradingWorkspaceButton).Should().Be("WorkspaceTradingButton");
            AutomationProperties.GetAutomationId(facade.PortfolioWorkspaceButton).Should().Be("WorkspacePortfolioButton");
            AutomationProperties.GetAutomationId(facade.AccountingWorkspaceButton).Should().Be("WorkspaceAccountingButton");
            AutomationProperties.GetAutomationId(facade.ReportingWorkspaceButton).Should().Be("WorkspaceReportingButton");
            AutomationProperties.GetAutomationId(facade.StrategyWorkspaceButton).Should().Be("WorkspaceStrategyButton");
            AutomationProperties.GetAutomationId(facade.DataWorkspaceButton).Should().Be("WorkspaceDataButton");
            AutomationProperties.GetAutomationId(facade.SettingsWorkspaceButton).Should().Be("WorkspaceSettingsButton");

            facade.TradingWorkspaceButton.Command.Should().BeSameAs(facade.ViewModel.NavigateToPageCommand);
            facade.PortfolioWorkspaceButton.Command.Should().BeSameAs(facade.ViewModel.NavigateToPageCommand);
            facade.AccountingWorkspaceButton.Command.Should().BeSameAs(facade.ViewModel.NavigateToPageCommand);
            facade.ReportingWorkspaceButton.Command.Should().BeSameAs(facade.ViewModel.NavigateToPageCommand);
            facade.StrategyWorkspaceButton.Command.Should().BeSameAs(facade.ViewModel.NavigateToPageCommand);
            facade.DataWorkspaceButton.Command.Should().BeSameAs(facade.ViewModel.NavigateToPageCommand);
            facade.SettingsWorkspaceButton.Command.Should().BeSameAs(facade.ViewModel.NavigateToPageCommand);

            ShellNavigationCatalog.GetWorkspace("trading")?.HomePageTag.Should().Be("TradingShell");
            ShellNavigationCatalog.GetWorkspace("portfolio")?.HomePageTag.Should().Be("PortfolioShell");
            ShellNavigationCatalog.GetWorkspace("accounting")?.HomePageTag.Should().Be("AccountingShell");
            ShellNavigationCatalog.GetWorkspace("reporting")?.HomePageTag.Should().Be("ReportingShell");
            ShellNavigationCatalog.GetWorkspace("strategy")?.HomePageTag.Should().Be("StrategyShell");
            ShellNavigationCatalog.GetWorkspace("data")?.HomePageTag.Should().Be("DataShell");
            ShellNavigationCatalog.GetWorkspace("settings")?.HomePageTag.Should().Be("SettingsShell");
        });
    }

    [Fact]
    public void MainPage_WorkspaceSecondaryNavigationSelection_ShouldNavigateToSecurityMaster()
    {
        WpfTestThread.Run(() =>
        {
            using var facade = new MainPageUiAutomationFacade();

            facade.Click(facade.DataWorkspaceButton);
            facade.SelectWorkspaceNavigationPage(facade.WorkspaceSecondaryNavList, "SecurityMaster");

            facade.ViewModel.CurrentPageTag.Should().Be("SecurityMaster");
            facade.ShellAutomationStateText.Text.Should().Be("SecurityMaster");
            facade.PageTitleText.Text.Should().Be("Security master");
            facade.WorkspaceSecondaryNavList.SelectedValue.Should().Be("SecurityMaster");
        });
    }

    [Fact]
    public void MainPage_WorkspaceNavigationSelection_WhenCleared_ShouldKeepCurrentPage()
    {
        WpfTestThread.Run(() =>
        {
            using var facade = new MainPageUiAutomationFacade();

            facade.Click(facade.DataWorkspaceButton);
            facade.SelectWorkspaceNavigationPage(facade.WorkspaceSecondaryNavList, "SecurityMaster");
            facade.ClearWorkspaceNavigationSelection(facade.WorkspaceSecondaryNavList);

            facade.ViewModel.CurrentPageTag.Should().Be("SecurityMaster");
            facade.PageTitleText.Text.Should().Be("Security master");
        });
    }

    [Fact]
    public void MainPage_FixtureIndicatorAndTickerToggle_ShouldRespondToUserActions()
    {
        WpfTestThread.Run(() =>
        {
            using var facade = new MainPageUiAutomationFacade();

            facade.SetFixtureMode(true);

            FixtureModeDetector.Instance.IsFixtureMode.Should().BeTrue();
            FixtureModeDetector.Instance.ModeLabel.Should().Contain("Demo data mode");
            facade.ViewModel.FixtureModeBannerVisibility.Should().Be(Visibility.Collapsed);
            facade.FixtureModeBanner.Visibility.Should().Be(Visibility.Collapsed);
            facade.ViewModel.ShellStatusText.Should().Be("Demo data");

            facade.ViewModel.TickerStripVisible.Should().BeFalse();
            facade.Click(facade.TickerStripToggleButton);
            facade.ViewModel.TickerStripVisible.Should().BeTrue();
            facade.TickerStripToggleLabelText.Text.Should().Be("Hide Ticker Strip");

            facade.Click(facade.TickerStripToggleButton);
            facade.ViewModel.TickerStripVisible.Should().BeFalse();
            facade.TickerStripToggleLabelText.Text.Should().Be("Ticker Strip");
        });
    }

    [Fact]
    public void MainPage_ShellDensityToggle_ShouldUpdateHeaderLabelAndPersistPreference()
    {
        WpfTestThread.Run(() =>
        {
            var preferencesPath = Path.Combine(
                Path.GetTempPath(),
                "mainpage-ui-test-" + Guid.NewGuid().ToString("N"),
                "desktop-shell-preferences.json");

            try
            {
                SettingsConfigurationService.SetDesktopPreferencesFilePathOverrideForTests(preferencesPath);
                SettingsConfigurationService.Instance.SetShellDensityMode(ShellDensityMode.Standard);

                using var facade = new MainPageUiAutomationFacade();

                AutomationProperties.GetAutomationId(facade.ShellDensityToggleButton).Should().Be("ShellDensityToggleButton");
                facade.PageTitleText.Visibility.Should().Be(Visibility.Visible);
                facade.PageSubtitleText.Visibility.Should().Be(Visibility.Visible);
                facade.PageSubtitleText.Text.Should().Be(facade.ViewModel.CurrentPageSubtitle);
                facade.ShellDensityButtonLabelText.Text.Should().Be("Density: Standard");
                ToolTipService.GetToolTip(facade.ShellDensityToggleButton).Should().Be("Switch to compact shell density");

                facade.Click(facade.ShellDensityToggleButton);

                facade.ViewModel.ShellDensityMode.Should().Be(ShellDensityMode.Compact);
                facade.PageTitleText.Visibility.Should().Be(Visibility.Visible);
                facade.PageSubtitleText.Visibility.Should().Be(Visibility.Collapsed);
                facade.ShellDensityButtonLabelText.Text.Should().Be("Density: Compact");
                ToolTipService.GetToolTip(facade.ShellDensityToggleButton).Should().Be("Switch to standard shell density");
                SettingsConfigurationService.Instance.GetShellDensityMode().Should().Be(ShellDensityMode.Compact);

                facade.Click(facade.ShellDensityToggleButton);

                facade.ViewModel.ShellDensityMode.Should().Be(ShellDensityMode.Standard);
                facade.PageTitleText.Visibility.Should().Be(Visibility.Visible);
                facade.PageSubtitleText.Visibility.Should().Be(Visibility.Visible);
                facade.ShellDensityButtonLabelText.Text.Should().Be("Density: Standard");
            }
            finally
            {
                SettingsConfigurationService.Instance.SetShellDensityMode(ShellDensityMode.Standard);
                SettingsConfigurationService.SetDesktopPreferencesFilePathOverrideForTests(null);
                if (File.Exists(preferencesPath))
                {
                    File.Delete(preferencesPath);
                }
            }
        });
    }

    [Fact]
    public void MainPage_CompactDensity_ShouldCollapseRepeatedContextOnWorkflowPages()
    {
        WpfTestThread.Run(async () =>
        {
            var preferencesPath = Path.Combine(
                Path.GetTempPath(),
                "mainpage-compact-context-test-" + Guid.NewGuid().ToString("N"),
                "desktop-shell-preferences.json");

            try
            {
                SettingsConfigurationService.SetDesktopPreferencesFilePathOverrideForTests(preferencesPath);
                SettingsConfigurationService.Instance.SetShellDensityMode(ShellDensityMode.Standard);

                using var facade = new MainPageUiAutomationFacade();

                facade.OpenCommandPalettePage("Backtest");
                await WaitForConditionAsync(() => facade.ViewModel.ShellContextVisibility == Visibility.Visible).ConfigureAwait(true);
                facade.Click(facade.ShellDensityToggleButton);

                facade.ViewModel.IsWorkflowPageActive.Should().BeTrue();
                facade.ViewModel.ShellDensityMode.Should().Be(ShellDensityMode.Compact);
                facade.ViewModel.ShellContextVisibility.Should().Be(Visibility.Collapsed);
                facade.WorkspaceShellContextStrip.Visibility.Should().Be(Visibility.Collapsed);
            }
            finally
            {
                SettingsConfigurationService.SetDesktopPreferencesFilePathOverrideForTests(null);
                if (File.Exists(preferencesPath))
                {
                    File.Delete(preferencesPath);
                }
            }
        });
    }

    [Fact]
    public void MainPage_RecentPagesEmptyState_ShouldOfferCommandPaletteShortcut()
    {
        WpfTestThread.Run(() =>
        {
            using var facade = new MainPageUiAutomationFacade();

            facade.RecentPagesEmptyText.Text.Should().Be("No recent pages yet.");
            facade.RecentPagesSummaryText.Text.Should().Contain("No recent");

            facade.Click(facade.RecentPagesEmptyActionButton);

            facade.CommandPaletteOverlay.Visibility.Should().Be(Visibility.Visible);
        });
    }

    [Fact]
    public void MainPage_RecentPagesRail_ShouldStayScopedToTheActiveWorkspace()
    {
        WpfTestThread.Run(() =>
        {
            using var facade = new MainPageUiAutomationFacade();

            facade.OpenCommandPalettePage("Backtest");
            facade.Click(facade.DataWorkspaceButton);
            facade.SelectWorkspaceNavigationPage(facade.WorkspaceSecondaryNavList, "SecurityMaster");

            facade.ViewModel.CurrentWorkspace.Should().Be("data");
            facade.RecentPagesSummaryText.Text.Should().Be("1 recent data workflow");
            facade.ViewModel.RecentPages.Select(page => page.PageTag).Should().Equal("DataShell");

            facade.Click(facade.StrategyWorkspaceButton);

            facade.ViewModel.CurrentWorkspace.Should().Be("strategy");
            facade.RecentPagesSummaryText.Text.Should().Be("1 recent strategy workflow");
            facade.ViewModel.RecentPages.Select(page => page.PageTag).Should().Equal("Backtest");
        });
    }

    [Fact]
    public void MainPage_WorkflowSummaryStrip_ShouldRenderAndUpdateAfterContextSelection()
    {
        WpfTestThread.Run(async () =>
        {
            var fundContextService = new FundContextService(Path.Combine(Path.GetTempPath(), $"mainpage-workflow-{Guid.NewGuid():N}.json"));
            using var facade = new MainPageUiAutomationFacade(fundContextService);

            facade.ViewModel.RefreshPageCommand.Execute(null);
            await WaitForConditionAsync(() =>
                facade.ViewModel.PrimaryWorkflowSummary is not null &&
                facade.ViewModel.SecondaryWorkflowSummaries.Count == 6,
                timeoutMs: 10000).ConfigureAwait(true);

            facade.Click(facade.TradingWorkspaceButton);
            await WaitForConditionAsync(() => facade.ViewModel.PrimaryWorkflowSummary?.WorkspaceId == "trading").ConfigureAwait(true);

            facade.WorkflowSummaryStrip.Visibility.Should().Be(Visibility.Visible);
            facade.ViewModel.WorkflowSummaries.Should().HaveCount(7);
            facade.ViewModel.PrimaryWorkflowSummary.Should().NotBeNull();
            facade.ViewModel.PrimaryWorkflowSummary!.WorkspaceId.Should().Be("trading");
            facade.ViewModel.SecondaryWorkflowSummaries.Should().HaveCount(6);
            facade.ViewModel.SecondaryWorkflowSummaries.Select(summary => summary.WorkspaceId).Should().NotContain("trading");
            facade.PrimaryWorkflowActionButton.Content.Should().Be("Choose Context");

            facade.Click(facade.SecondaryWorkflowToggleButton);
            facade.WorkflowSummaryItemsControl.Items.Count.Should().Be(6);

            await fundContextService.UpsertProfileAsync(new FundProfileDetail(
                FundProfileId: "alpha-fund",
                DisplayName: "Alpha Fund",
                LegalEntityName: "Alpha Fund LP",
                BaseCurrency: "USD",
                DefaultWorkspaceId: "trading",
                DefaultLandingPageTag: "TradingShell",
                DefaultLedgerScope: FundLedgerScope.Consolidated)).ConfigureAwait(true);
            await fundContextService.SelectFundProfileAsync("alpha-fund").ConfigureAwait(true);

            await WaitForConditionAsync(() =>
                facade.ViewModel.PrimaryWorkflowSummary?.NextAction.Label == "Open Strategy Runs").ConfigureAwait(true);

            facade.ViewModel.PrimaryWorkflowSummary!.NextAction.Label.Should().Be("Open Strategy Runs");
            facade.ViewModel.SecondaryWorkflowSummaries.Single(summary => summary.WorkspaceId == "accounting").NextAction.Label.Should().Be("Open Accounting Shell");
        });
    }

    [Fact]
    public void MainPage_ShellContextStrip_ShouldSurfaceCurrentPageAndAttentionState()
    {
        WpfTestThread.Run(async () =>
        {
            using var facade = new MainPageUiAutomationFacade();

            RunMatUiAutomationFacade.DrainDispatcher();
            facade.ViewModel.IsWorkspaceHomePageActive.Should().BeTrue();
            facade.ViewModel.ShellContextVisibility.Should().Be(Visibility.Collapsed);
            facade.WorkspaceShellContextStrip.Visibility.Should().Be(Visibility.Collapsed);

            facade.OpenCommandPalettePage("SecurityMaster");

            await WaitForConditionAsync(() =>
                facade.ViewModel.ShellContextVisibility == Visibility.Visible &&
                string.Equals(facade.WorkspaceContextTitleText.Text, "Security master", StringComparison.Ordinal)).ConfigureAwait(true);

            facade.WorkspaceShellContextStrip.Visibility.Should().Be(Visibility.Visible);
            facade.WorkspaceContextTitleText.Text.Should().Be("Security master");
            facade.WorkspaceContextSubtitleText.Text.Should().Be(facade.ViewModel.CurrentPageSubtitle);
            facade.WorkspaceContextAttentionBanner.Visibility.Should().Be(Visibility.Visible);
            facade.WorkspaceContextAttentionTitleText.Text.Should().NotBeNullOrWhiteSpace();
            var attentionDetail = facade.WorkspaceContextAttentionDetailText.Text;
            attentionDetail.Should().NotBeNullOrWhiteSpace();
            (attentionDetail.Contains("Environment", StringComparison.Ordinal)
                || attentionDetail.Contains("Operating Context", StringComparison.Ordinal)).Should().BeTrue();
        });
    }

    [Fact]
    public void MainPage_OperatorInboxQueueButton_ShouldOpenBrokerageSyncWorkbenchFromRoute()
    {
        WpfTestThread.Run(async () =>
        {
            var accountId = Guid.Parse("9f8f8f71-d9c8-4e2f-aef8-1393506296ef");
            var operatorInboxClient = new RecordingOperatorInboxApiClient(
                new OperatorInboxDto(
                    AsOf: new DateTimeOffset(2026, 4, 27, 19, 0, 0, TimeSpan.Zero),
                    Items:
                    [
                        new OperatorWorkItemDto(
                            WorkItemId: "brokerage-sync-attention-9f8f8f71",
                            Kind: OperatorWorkItemKindDto.BrokerageSync,
                            Label: "Brokerage sync attention",
                            Detail: "Brokerage sync failed for the active account.",
                            Tone: OperatorWorkItemToneDto.Critical,
                            CreatedAt: new DateTimeOffset(2026, 4, 27, 18, 59, 0, TimeSpan.Zero),
                            FundAccountId: accountId,
                            Workspace: "Trading",
                            TargetRoute: UiApiRoutes.FundAccountBrokerageSyncStatus.Replace("{accountId}", accountId.ToString("D"), StringComparison.Ordinal),
                            TargetPageTag: "TradingShell")
                    ],
                    CriticalCount: 1,
                    WarningCount: 0,
                    ReviewCount: 1,
                    Summary: "1 critical brokerage sync item needs review."));

            using var facade = new MainPageUiAutomationFacade(operatorInboxApiClient: operatorInboxClient);
            await WaitForConditionAsync(() => operatorInboxClient.RequestCount > 0).ConfigureAwait(true);

            facade.ViewModel.SelectedOperatingContext = new WorkstationOperatingContext
            {
                ScopeKind = OperatingContextScopeKind.Account,
                ScopeId = accountId.ToString("D"),
                AccountId = accountId.ToString("D"),
                DisplayName = "Prime brokerage account",
                BaseCurrency = "USD",
                DefaultWorkspaceId = "trading",
                DefaultLandingPageTag = "TradingShell"
            };

            var previousRequestCount = operatorInboxClient.RequestCount;
            facade.ViewModel.RefreshPageCommand.Execute(null);
            await WaitForConditionAsync(() =>
                operatorInboxClient.RequestCount > previousRequestCount &&
                operatorInboxClient.LastFundAccountId == accountId).ConfigureAwait(true);
            await WaitForConditionAsync(() =>
                facade.ViewModel.OperatorInboxReviewCount == 1,
                timeoutMs: 15000).ConfigureAwait(true);

            facade.ViewModel.OperatorInboxTargetText.Should().Be("AccountPortfolio");

            facade.ViewModel.OperatorInboxButtonText.Should().Be("Queue (1)");
            facade.OperatorInboxButtonLabelText.Text.Should().Be("Queue (1)");

            facade.Click(facade.OperatorInboxButton);

            facade.ViewModel.CurrentPageTag.Should().Be("AccountPortfolio");
            facade.ShellAutomationStateText.Text.Should().Be("AccountPortfolio");
        });
    }

    [Fact]
    public void MainPage_AccountingDeepLink_ShouldAnnounceWorkbenchTarget()
    {
        WpfTestThread.Run(async () =>
        {
            var fundContextService = new FundContextService(Path.Combine(Path.GetTempPath(), $"mainpage-accounting-{Guid.NewGuid():N}.json"));
            await fundContextService.UpsertProfileAsync(new FundProfileDetail(
                FundProfileId: "alpha-fund",
                DisplayName: "Alpha Fund",
                LegalEntityName: "Alpha Fund LP",
                BaseCurrency: "USD",
                DefaultWorkspaceId: "accounting",
                DefaultLandingPageTag: "FundLedger",
                DefaultLedgerScope: FundLedgerScope.Consolidated)).ConfigureAwait(true);
            await fundContextService.SelectFundProfileAsync("alpha-fund").ConfigureAwait(true);

            using var facade = new MainPageUiAutomationFacade(fundContextService);

            facade.OpenCommandPalettePage("FundReconciliation");

            await WaitForConditionAsync(() => facade.ViewModel.CurrentPageTag == "FundReconciliation").ConfigureAwait(true);
            var fundLedgerViewModel = facade.InnermostContentPage!.DataContext
                .Should()
                .BeOfType<FundLedgerViewModel>()
                .Subject;

            await WaitForConditionAsync(() =>
                fundLedgerViewModel.RouteBannerTitleText.Contains("Reconciliation", StringComparison.Ordinal)).ConfigureAwait(true);

            fundLedgerViewModel.RouteBannerTitleText.Should().Contain("Reconciliation");
            fundLedgerViewModel.CurrentWorkbenchTitleText.Should().Contain("Reconciliation");
        });
    }

    private static async Task WaitForConditionAsync(Func<bool> predicate, int timeoutMs = 5000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            RunMatUiAutomationFacade.DrainDispatcher();
            if (predicate())
            {
                return;
            }

            await Task.Delay(50).ConfigureAwait(true);
        }

        predicate().Should().BeTrue("expected condition to become true within the timeout window");
    }

    private sealed class RecordingOperatorInboxApiClient : IWorkstationOperatorInboxApiClient
    {
        private readonly OperatorInboxDto _inbox;

        public RecordingOperatorInboxApiClient(OperatorInboxDto inbox)
        {
            _inbox = inbox;
        }

        public int RequestCount { get; private set; }

        public Guid? LastFundAccountId { get; private set; }

        public Task<OperatorInboxDto?> GetInboxAsync(Guid? fundAccountId = null, CancellationToken ct = default)
        {
            RequestCount++;
            LastFundAccountId = fundAccountId;
            return Task.FromResult<OperatorInboxDto?>(_inbox);
        }
    }
}
