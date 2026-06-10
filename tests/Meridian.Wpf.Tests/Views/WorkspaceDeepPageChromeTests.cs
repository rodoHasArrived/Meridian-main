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
    [InlineData("ProviderHealth", "Provider health", "Data")]
    [InlineData("Diagnostics", "Diagnostics", "Settings")]
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
    [InlineData(@"src\Meridian.Wpf\Views\SymbolMappingPage.xaml", "EmbeddedShellHeaderGridStyle")]
    [InlineData(@"src\Meridian.Wpf\Views\RetentionAssurancePage.xaml", "EmbeddedShellHeaderStackPanelStyle")]
    [InlineData(@"src\Meridian.Wpf\Views\SettingsPage.xaml", "EmbeddedShellHeroCardStyle")]
    [InlineData(@"src\Meridian.Wpf\Views\WelcomePage.xaml", "EmbeddedShellHeroCardStyle")]
    [InlineData(@"src\Meridian.Wpf\Views\MessagingHubPage.xaml", "EmbeddedShellCompactHeaderCardStyle")]
    [InlineData(@"src\Meridian.Wpf\Views\NotificationCenterPage.xaml", "EmbeddedShellCompactHeaderCardStyle")]
    [InlineData(@"src\Meridian.Wpf\Views\SecurityMasterPage.xaml", "EmbeddedShellCompactHeaderCardStyle")]
    [InlineData(@"src\Meridian.Wpf\Views\DiagnosticsPage.xaml", "EmbeddedShellHeroCardStyle")]
    [InlineData(@"src\Meridian.Wpf\Views\DataQualityPage.xaml", "WorkspaceCommandBarControl")]
    [InlineData(@"src\Meridian.Wpf\Views\DataQualityPage.xaml", "WorkspaceInteractionPanel")]
    [InlineData(@"src\Meridian.Wpf\Views\PositionBlotterPage.xaml", "EmbeddedShellCompactHeaderCardStyle")]
    public void LegacyDeepPages_ShouldOptIntoSharedEmbeddedShellChrome(string relativePath, string expectedMarker)
    {
        var absolutePath = RunMatUiAutomationFacade.GetRepoFilePath(relativePath);
        File.ReadAllText(absolutePath).Should().Contain(expectedMarker);
    }

    [Fact]
    public void ProviderHealthPage_ShouldUseCompactFreshnessChrome()
    {
        var absolutePath = RunMatUiAutomationFacade.GetRepoFilePath(@"src\Meridian.Wpf\Views\ProviderHealthPage.xaml");
        var xaml = File.ReadAllText(absolutePath);

        xaml.Should().NotContain("EmbeddedShellHeroCardStyle");
        xaml.Should().Contain("ProviderHealthInlineRefreshButton");
        xaml.Should().Contain("ProviderManagementCenter");
        xaml.Should().Contain("DenseDataGridControl");
        xaml.Should().Contain("InspectorPanelControl");
    }

    [Fact]
    public void ProviderPage_ShouldUseBackfillDenseTableInspectorChrome()
    {
        var absolutePath = RunMatUiAutomationFacade.GetRepoFilePath(@"src\Meridian.Wpf\Views\ProviderPage.xaml");
        var xaml = File.ReadAllText(absolutePath);

        xaml.Should().NotContain("EmbeddedShellHeroCardStyle");
        xaml.Should().NotContain("Active Streaming Provider");
        xaml.Should().Contain("ProviderBackfillActionStrip");
        xaml.Should().Contain("ProviderBackfillPostureCard");
        xaml.Should().Contain("ProviderBackfillWorkbench");
        xaml.Should().Contain("ProviderBackfillSettingsGrid");
        xaml.Should().Contain("ProviderBackfillSettingsInspector");
        xaml.Should().Contain("ProviderBackfillActionInspector");
        xaml.Should().Contain("ProviderFallbackChainGrid");
        xaml.Should().Contain("ProviderDryRunPlanGrid");
        xaml.Should().Contain("ProviderAuditTrailGrid");
        xaml.Should().Contain("ToolTipService.ShowOnDisabled=\"True\"");
    }

    [Fact]
    public void ServiceManagerPage_ShouldUseCompactWorkbenchChrome()
    {
        var absolutePath = RunMatUiAutomationFacade.GetRepoFilePath(@"src\Meridian.Wpf\Views\ServiceManagerPage.xaml");
        var xaml = File.ReadAllText(absolutePath);

        xaml.Should().NotContain("EmbeddedShellHeroCardStyle");
        xaml.Should().Contain("ServiceManagerPostureStrip");
        xaml.Should().Contain("ServiceManagerControlWorkbench");
        xaml.Should().Contain("ServiceManagerRuntimeInspector");
        xaml.Should().Contain("ServiceManagerActionInspector");
    }

    [Fact]
    public void SystemHealthPage_ShouldUseCompactDenseTablesAndInspectorChrome()
    {
        var absolutePath = RunMatUiAutomationFacade.GetRepoFilePath(@"src\Meridian.Wpf\Views\SystemHealthPage.xaml");
        var xaml = File.ReadAllText(absolutePath);

        xaml.Should().NotContain("EmbeddedShellHeroCardStyle");
        xaml.Should().NotContain("<ListView");
        xaml.Should().Contain("SystemHealthActionStrip");
        xaml.Should().Contain("SystemHealthWorkbench");
        xaml.Should().Contain("SystemHealthProviderHealthGrid");
        xaml.Should().Contain("SystemHealthEventsGrid");
        xaml.Should().Contain("SystemHealthTriageBriefingCard");
        xaml.Should().Contain("SystemHealthProviderInspector");
        xaml.Should().Contain("SystemHealthEventInspector");
        xaml.Should().Contain("ToolTipService.ShowOnDisabled=\"True\"");
    }

    [Fact]
    public void DirectLendingPage_ShouldUseCompactDenseTablesAndInspectorChrome()
    {
        var absolutePath = RunMatUiAutomationFacade.GetRepoFilePath(@"src\Meridian.Wpf\Views\DirectLendingPage.xaml");
        var xaml = File.ReadAllText(absolutePath);

        xaml.Should().NotContain("<DataGrid");
        xaml.Should().NotContain("SectionCardStyle");
        xaml.Should().Contain("DirectLendingActionStrip");
        xaml.Should().Contain("DirectLendingWorkbench");
        xaml.Should().Contain("DirectLendingLoansGrid");
        xaml.Should().Contain("DirectLendingAccrualsGrid");
        xaml.Should().Contain("DirectLendingCashTransactionsGrid");
        xaml.Should().Contain("DirectLendingLoanInspector");
        xaml.Should().Contain("DirectLendingLoanActionInspector");
        xaml.Should().Contain("ToolTipService.ShowOnDisabled=\"True\"");
    }

    [Fact]
    public void AnalysisExportPage_ShouldUseCompactDenseTableAndInspectorChrome()
    {
        var absolutePath = RunMatUiAutomationFacade.GetRepoFilePath(@"src\Meridian.Wpf\Views\AnalysisExportPage.xaml");
        var xaml = File.ReadAllText(absolutePath);

        xaml.Should().NotContain("EmbeddedShellHeaderStackPanelStyle");
        xaml.Should().NotContain("<DataGrid");
        xaml.Should().Contain("AnalysisExportActionStrip");
        xaml.Should().Contain("AnalysisExportWorkbench");
        xaml.Should().Contain("AnalysisExportRecentExportsGrid");
        xaml.Should().Contain("AnalysisExportReadinessCard");
        xaml.Should().Contain("AnalysisExportRecentExportInspector");
        xaml.Should().Contain("AnalysisExportActionInspector");
        xaml.Should().Contain("ToolTipService.ShowOnDisabled=\"True\"");
    }

    [Fact]
    public void ClusterStatusPage_ShouldUseCompactDenseTableAndInspectorChrome()
    {
        var absolutePath = RunMatUiAutomationFacade.GetRepoFilePath(@"src\Meridian.Wpf\Views\ClusterStatusPage.xaml");
        var xaml = File.ReadAllText(absolutePath);

        xaml.Should().NotContain("<DataGrid");
        xaml.Should().Contain("ClusterStatusActionStrip");
        xaml.Should().Contain("ClusterStatusPostureCard");
        xaml.Should().Contain("ClusterStatusWorkbench");
        xaml.Should().Contain("WorkstationTableInspectorControl");
        xaml.Should().Contain("ClusterStatusNodesGrid");
        xaml.Should().Contain("ClusterStatusSummaryInspector");
        xaml.Should().Contain("ClusterStatusSelectionInspector");
        xaml.Should().Contain("ClusterStatusActionInspector");
        xaml.Should().Contain("ToolTipService.ShowOnDisabled=\"True\"");
    }

    [Fact]
    public void DataExportPage_ShouldUseCompactReadinessChrome()
    {
        var absolutePath = RunMatUiAutomationFacade.GetRepoFilePath(@"src\Meridian.Wpf\Views\DataExportPage.xaml");
        var xaml = File.ReadAllText(absolutePath);

        xaml.Should().NotContain("EmbeddedShellHeroCardStyle");
        xaml.Should().Contain("DataExportReadinessCard");
        xaml.Should().Contain("DataExportRunButton");
        xaml.Should().Contain("DataExportScheduleReadinessCard");
        xaml.Should().Contain("ToolTip=\"{Binding ExportReadinessDetail}\"");
        xaml.Should().Contain("ToolTip=\"{Binding ScheduleReadinessDetail}\"");
    }

    [Fact]
    public void DataSourcesPage_ShouldUseCompactSourceSetupChrome()
    {
        var absolutePath = RunMatUiAutomationFacade.GetRepoFilePath(@"src\Meridian.Wpf\Views\DataSourcesPage.xaml");
        var xaml = File.ReadAllText(absolutePath);

        xaml.Should().NotContain("EmbeddedShellHeroCardStyle");
        xaml.Should().Contain("DataSourcesEditReadinessCard");
        xaml.Should().Contain("DataSourcesSaveSourceButton");
        xaml.Should().Contain("ToolTip=\"{Binding SourceSetupReadinessDetail}\"");
        xaml.Should().Contain("ToolTipService.ShowOnDisabled=\"True\"");
    }

    [Fact]
    public void ActivityLogPage_ShouldUseCompactActionChrome()
    {
        var absolutePath = RunMatUiAutomationFacade.GetRepoFilePath(@"src\Meridian.Wpf\Views\ActivityLogPage.xaml");
        var xaml = File.ReadAllText(absolutePath);

        xaml.Should().NotContain("EmbeddedShellHeroCardStyle");
        xaml.Should().Contain("ActivityLogActionStrip");
        xaml.Should().Contain("ActivityLogExportButton");
        xaml.Should().Contain("ActivityLogClearButton");
        xaml.Should().Contain("ToolTip=\"{Binding ExportVisibleLogsTooltip}\"");
        xaml.Should().Contain("ToolTip=\"{Binding ClearLogTooltip}\"");
        xaml.Should().Contain("ToolTipService.ShowOnDisabled=\"True\"");
    }

    [Fact]
    public void OrderBookPage_ShouldUseCompactLadderChrome()
    {
        var absolutePath = RunMatUiAutomationFacade.GetRepoFilePath(@"src\Meridian.Wpf\Views\OrderBookPage.xaml");
        var xaml = File.ReadAllText(absolutePath);

        xaml.Should().NotContain("EmbeddedShellHeroCardStyle");
        xaml.Should().Contain("OrderBookActionStrip");
        xaml.Should().Contain("OrderBookSymbolSelector");
        xaml.Should().Contain("OrderBookDepthSelector");
        xaml.Should().Contain("OrderBookPostureCard");
        xaml.Should().Contain("OrderBookEmptyStatePanel");
    }

    [Fact]
    public void WatchlistPage_ShouldUseDenseTableAndInspectorChrome()
    {
        var absolutePath = RunMatUiAutomationFacade.GetRepoFilePath(@"src\Meridian.Wpf\Views\WatchlistPage.xaml");
        var xaml = File.ReadAllText(absolutePath);

        xaml.Should().NotContain("EmbeddedShellHeroCardStyle");
        xaml.Should().NotContain("WatchlistCardStyle");
        xaml.Should().Contain("WatchlistActionStrip");
        xaml.Should().Contain("WatchlistWorkbench");
        xaml.Should().Contain("WorkstationTableInspectorControl");
        xaml.Should().Contain("WatchlistLibraryGrid");
        xaml.Should().Contain("WatchlistSelectionInspector");
        xaml.Should().Contain("WatchlistActionInspector");
    }

    [Fact]
    public void BackfillPage_ShouldUseCompactActionChrome()
    {
        var absolutePath = RunMatUiAutomationFacade.GetRepoFilePath(@"src\Meridian.Wpf\Views\BackfillPage.xaml");
        var xaml = File.ReadAllText(absolutePath);

        xaml.Should().NotContain("EmbeddedShellHeroCardStyle");
        xaml.Should().Contain("BackfillActionStrip");
        xaml.Should().Contain("BackfillWizardButton");
        xaml.Should().Contain("BackfillValidateDataButton");
        xaml.Should().Contain("BackfillStartReadinessCard");
        xaml.Should().Contain("ToolTip=\"{Binding StartReadinessDetail}\"");
        xaml.Should().Contain("ToolTipService.ShowOnDisabled=\"True\"");
    }

    [Fact]
    public void DataQualityPage_ShouldUseCompactFreshnessAndCommandChrome()
    {
        var absolutePath = RunMatUiAutomationFacade.GetRepoFilePath(@"src\Meridian.Wpf\Views\DataQualityPage.xaml");
        var xaml = File.ReadAllText(absolutePath);

        xaml.Should().NotContain("EmbeddedShellHeroCardStyle");
        xaml.Should().Contain("DataQualityFreshnessStrip");
        xaml.Should().Contain("WorkspaceCommandBarControl");
        xaml.Should().Contain("WorkspaceInteractionPanel");
        xaml.Should().Contain("DataQualitySymbolQualityGrid");
    }

    [Fact]
    public void AccountingConfigurePage_ShouldUseCompactActionChrome()
    {
        var absolutePath = RunMatUiAutomationFacade.GetRepoFilePath(@"src\Meridian.Wpf\Views\AccountingConfigurePage.xaml");
        var xaml = File.ReadAllText(absolutePath);

        xaml.Should().NotContain("EmbeddedShellHeroCardStyle");
        xaml.Should().Contain("AccountingConfigureActionStrip");
        xaml.Should().Contain("AccountingConfigureRefreshButton");
        xaml.Should().Contain("AccountingConfigureSeedBaselineButton");
        xaml.Should().Contain("AccountingConfigureActivateButton");
        xaml.Should().Contain("AccountingConfigureTabs");
    }

    [Fact]
    public void StrategyRunsPage_ShouldUseCompactRunWorkbenchChrome()
    {
        var absolutePath = RunMatUiAutomationFacade.GetRepoFilePath(@"src\Meridian.Wpf\Views\StrategyRunsPage.xaml");
        var xaml = File.ReadAllText(absolutePath);

        xaml.Should().NotContain("EmbeddedShellHeroCardStyle");
        xaml.Should().Contain("StrategyRunsActionStrip");
        xaml.Should().Contain("StrategyRunsGrid");
        xaml.Should().Contain("StrategyRunsSelectionInspector");
        xaml.Should().Contain("StrategyRunsInspectorTabs");
        xaml.Should().Contain("ToolTipService.ShowOnDisabled=\"True\"");
    }

    [Fact]
    public void RunPortfolioPage_ShouldUseCompactDenseTableAndInspectorChrome()
    {
        var absolutePath = RunMatUiAutomationFacade.GetRepoFilePath(@"src\Meridian.Wpf\Views\RunPortfolioPage.xaml");
        var xaml = File.ReadAllText(absolutePath);

        xaml.Should().NotContain("EmbeddedShellHeroCardStyle");
        xaml.Should().Contain("RunPortfolioActionStrip");
        xaml.Should().Contain("RunPortfolioWorkbench");
        xaml.Should().Contain("WorkstationTableInspectorControl");
        xaml.Should().Contain("RunPortfolioPositionsGrid");
        xaml.Should().Contain("RunPortfolioSelectionInspector");
        xaml.Should().Contain("RunPortfolioActionInspector");
        xaml.Should().Contain("ToolTipService.ShowOnDisabled=\"True\"");
    }

    [Fact]
    public void AccountPortfolioPage_ShouldUseCompactDenseTableAndInspectorChrome()
    {
        var absolutePath = RunMatUiAutomationFacade.GetRepoFilePath(@"src\Meridian.Wpf\Views\AccountPortfolioPage.xaml");
        var xaml = File.ReadAllText(absolutePath);

        xaml.Should().NotContain("EmbeddedShellHeroCardStyle");
        xaml.Should().NotContain("<DataGrid");
        xaml.Should().Contain("AccountPortfolioActionStrip");
        xaml.Should().Contain("AccountPortfolioWorkbench");
        xaml.Should().Contain("WorkstationTableInspectorControl");
        xaml.Should().Contain("AccountPortfolioPositionsGrid");
        xaml.Should().Contain("AccountPortfolioSelectionInspector");
        xaml.Should().Contain("AccountPortfolioActionInspector");
        xaml.Should().Contain("ToolTipService.ShowOnDisabled=\"True\"");
    }

    [Fact]
    public void AggregatePortfolioPage_ShouldUseCompactDenseTableAndInspectorChrome()
    {
        var absolutePath = RunMatUiAutomationFacade.GetRepoFilePath(@"src\Meridian.Wpf\Views\AggregatePortfolioPage.xaml");
        var xaml = File.ReadAllText(absolutePath);

        xaml.Should().NotContain("EmbeddedShellHeroCardStyle");
        xaml.Should().NotContain("<DataGrid");
        xaml.Should().Contain("AggregatePortfolioActionStrip");
        xaml.Should().Contain("AggregatePortfolioWorkbench");
        xaml.Should().Contain("WorkstationTableInspectorControl");
        xaml.Should().Contain("AggregatePortfolioPositionsGrid");
        xaml.Should().Contain("AggregatePortfolioSelectionInspector");
        xaml.Should().Contain("AggregatePortfolioActionInspector");
        xaml.Should().Contain("ToolTipService.ShowOnDisabled=\"True\"");
    }

    [Fact]
    public void FundAccountsPage_ShouldUseCompactDenseTableAndInspectorChrome()
    {
        var absolutePath = RunMatUiAutomationFacade.GetRepoFilePath(@"src\Meridian.Wpf\Views\FundAccountsPage.xaml");
        var codePath = RunMatUiAutomationFacade.GetRepoFilePath(@"src\Meridian.Wpf\Views\FundAccountsPage.xaml.cs");
        var xaml = File.ReadAllText(absolutePath);
        var code = File.ReadAllText(codePath);

        xaml.Should().NotContain("EmbeddedShellHeroCardStyle");
        xaml.Should().NotContain("<ListBox");
        xaml.Should().Contain("FundAccountsActionStrip");
        xaml.Should().Contain("FundAccountsWorkbench");
        xaml.Should().Contain("WorkstationTableInspectorControl");
        xaml.Should().Contain("FundAccountsQueueGrid");
        xaml.Should().Contain("FundAccountsSelectionInspector");
        xaml.Should().Contain("FundAccountsRoutingReadinessCard");
        xaml.Should().Contain("ToolTipService.ShowOnDisabled=\"True\"");
        code.Should().NotContain("AccountSelectionChanged");
    }

    [Fact]
    public void FundLedgerPage_ShouldUseCompactActionAndAccountWorkbenchChrome()
    {
        var absolutePath = RunMatUiAutomationFacade.GetRepoFilePath(@"src\Meridian.Wpf\Views\FundLedgerPage.xaml");
        var xaml = File.ReadAllText(absolutePath);

        xaml.Should().NotContain("EmbeddedShellHeroCardStyle");
        xaml.Should().Contain("FundLedgerActionStrip");
        xaml.Should().Contain("FundLedgerActionStripState");
        xaml.Should().Contain("FundLedgerAccountWorkbench");
        xaml.Should().Contain("WorkstationTableInspectorControl");
        xaml.Should().Contain("FundLedgerAccountQueueGrid");
        xaml.Should().Contain("FundLedgerAccountSelectionInspector");
        xaml.Should().Contain("FundLedgerAccountActionInspector");
        xaml.Should().Contain("FundLedgerOpenAccountPortfolioButton");
        xaml.Should().Contain("ToolTipService.ShowOnDisabled=\"True\"");
    }

    [Fact]
    public void RunLedgerPage_ShouldUseCompactDenseTablesAndInspectorChrome()
    {
        var absolutePath = RunMatUiAutomationFacade.GetRepoFilePath(@"src\Meridian.Wpf\Views\RunLedgerPage.xaml");
        var xaml = File.ReadAllText(absolutePath);

        xaml.Should().NotContain("EmbeddedShellHeroCardStyle");
        xaml.Should().Contain("RunLedgerActionStrip");
        xaml.Should().Contain("RunLedgerWorkbench");
        xaml.Should().Contain("RunLedgerTrialBalanceGrid");
        xaml.Should().Contain("RunLedgerJournalGrid");
        xaml.Should().Contain("RunLedgerSelectionInspector");
        xaml.Should().Contain("RunLedgerActionInspector");
        xaml.Should().Contain("ToolTipService.ShowOnDisabled=\"True\"");
    }

    [Fact]
    public void FinancialRecordExplorerPage_ShouldUseCompactDenseTableInspectorChrome()
    {
        var absolutePath = RunMatUiAutomationFacade.GetRepoFilePath(@"src\Meridian.Wpf\Views\FinancialRecordExplorerPage.xaml");
        var codePath = RunMatUiAutomationFacade.GetRepoFilePath(@"src\Meridian.Wpf\Views\FinancialRecordExplorerPage.xaml.cs");
        var xaml = File.ReadAllText(absolutePath);
        var code = File.ReadAllText(codePath);

        xaml.Should().NotContain("EmbeddedShellHeroCardStyle");
        xaml.Should().NotContain("<DataGrid");
        xaml.Should().Contain("FinancialRecordExplorerActionStrip");
        xaml.Should().Contain("FinancialRecordExplorerSummaryStrip");
        xaml.Should().Contain("WorkstationTableInspectorControl");
        xaml.Should().Contain("FinancialRecordExplorerGrid");
        xaml.Should().Contain("FinancialRecordExplorerSelectionInspector");
        xaml.Should().Contain("FinancialRecordExplorerProofActionsCard");
        xaml.Should().Contain("FinancialRecordExplorerUsedInRelationships");
        xaml.Should().Contain("FinancialRecordExplorerImpactRelationships");
        xaml.Should().Contain("ToolTipService.ShowOnDisabled=\"True\"");
        code.Should().Contain("IWorkspaceShellPageContextAware");
    }

    [Fact]
    public void RunRiskPage_ShouldUseCompactAttributionWorkbenchChrome()
    {
        var absolutePath = RunMatUiAutomationFacade.GetRepoFilePath(@"src\Meridian.Wpf\Views\RunRiskPage.xaml");
        var xaml = File.ReadAllText(absolutePath);

        xaml.Should().NotContain("EmbeddedShellHeroCardStyle");
        xaml.Should().Contain("RunRiskActionStrip");
        xaml.Should().Contain("RunRiskAttributionWorkbench");
        xaml.Should().Contain("RunRiskAttributionGrid");
        xaml.Should().Contain("RunRiskAttributionInspector");
        xaml.Should().Contain("RunRiskAttributionActionInspector");
        xaml.Should().Contain("ToolTipService.ShowOnDisabled=\"True\"");
    }

    [Fact]
    public void RunDetailPage_ShouldUseCompactParameterWorkbenchChrome()
    {
        var absolutePath = RunMatUiAutomationFacade.GetRepoFilePath(@"src\Meridian.Wpf\Views\RunDetailPage.xaml");
        var xaml = File.ReadAllText(absolutePath);

        xaml.Should().NotContain("EmbeddedShellHeroCardStyle");
        xaml.Should().Contain("RunDetailActionStrip");
        xaml.Should().Contain("RunDetailWorkbench");
        xaml.Should().Contain("RunDetailParametersGrid");
        xaml.Should().Contain("RunDetailSummaryInspector");
        xaml.Should().Contain("RunDetailSelectedParameterInspector");
        xaml.Should().Contain("RunDetailActionInspector");
        xaml.Should().Contain("ToolTipService.ShowOnDisabled=\"True\"");
    }

    [Fact]
    public void RunCashFlowPage_ShouldUseCompactDenseTablesAndInspectorChrome()
    {
        var absolutePath = RunMatUiAutomationFacade.GetRepoFilePath(@"src\Meridian.Wpf\Views\RunCashFlowPage.xaml");
        var xaml = File.ReadAllText(absolutePath);

        xaml.Should().NotContain("EmbeddedShellHeroCardStyle");
        xaml.Should().NotContain("<DataGrid");
        xaml.Should().Contain("RunCashFlowActionStrip");
        xaml.Should().Contain("RunCashFlowWorkbench");
        xaml.Should().Contain("RunCashFlowLadderGrid");
        xaml.Should().Contain("RunCashFlowEntriesGrid");
        xaml.Should().Contain("RunCashFlowEntryInspector");
        xaml.Should().Contain("RunCashFlowLadderInspector");
        xaml.Should().Contain("RunCashFlowContinuityPosture");
        xaml.Should().Contain("RunCashFlowActionInspector");
        xaml.Should().Contain("ToolTipService.ShowOnDisabled=\"True\"");
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
