using System.Windows.Input;
using Meridian.Wpf.Services;
using Meridian.Wpf.Tests.Support;
using Meridian.Wpf.ViewModels;
using Meridian.Wpf.ViewModels.Accounting;

namespace Meridian.Wpf.Tests.Views;

/// <summary>
/// The shell owns the one Refresh control. Pages reload through their view model's refresh command
/// and keep a local "Retry" only inside an error state.
/// </summary>
public sealed class PageLevelRefreshRemovalTests
{
    [Theory]
    [InlineData("AccountPortfolioPage.xaml", "AccountPortfolioRefreshButton")]
    [InlineData("AccountPortfolioPage.xaml", "AccountPortfolioInspectorRefreshButton")]
    [InlineData("AggregatePortfolioPage.xaml", "AggregatePortfolioRefreshButton")]
    [InlineData("AggregatePortfolioPage.xaml", "AggregatePortfolioInspectorRefreshButton")]
    [InlineData("DirectLendingPage.xaml", "DirectLendingRefreshButton")]
    [InlineData("EvidenceWorkbenchPage.xaml", "EvidenceWorkbenchRefreshButton")]
    [InlineData("FinancialRecordExplorerPage.xaml", "FinancialRecordExplorerRefreshButton")]
    [InlineData("LifecycleControlPage.xaml", "LifecycleRefreshButton")]
    [InlineData("OperationsContinuityPage.xaml", "OperationsContinuityRefreshButton")]
    [InlineData("OperationsRecordReleasePage.xaml", "OperationsRecordReleaseRefreshButton")]
    [InlineData("OperatorReadinessConsolePage.xaml", "OperatorReadinessConsoleRefreshButton")]
    [InlineData("PostedLedgerPage.xaml", "PostedLedgerRefreshButton")]
    [InlineData("SystemHealthPage.xaml", "SystemHealthRefreshButton")]
    [InlineData("SystemHealthPage.xaml", "SystemHealthInspectorRefreshButton")]
    [InlineData("AccountingConfigurePage.xaml", "AccountingConfigureRefreshButton")]
    [InlineData("ClusterStatusPage.xaml", "ClusterStatusRefreshButton")]
    [InlineData("MessagingHubPage.xaml", "MessagingHubRefreshButton")]
    [InlineData("StrategyRunsPage.xaml", "StrategyRunsRefreshButton")]
    [InlineData("SymbolStoragePage.xaml", "SymbolStorageRefreshButton")]
    public void PageSource_ShouldNotCarryItsOwnRefreshButton(string pageFile, string removedAutomationId)
    {
        ReadView(pageFile).Should().NotContain(removedAutomationId);
    }

    [Theory]
    [InlineData("DirectLendingPage.xaml")]
    [InlineData("FinancialRecordExplorerPage.xaml")]
    [InlineData("FundLedgerPage.xaml")]
    [InlineData("HomeWorkspacePage.xaml")]
    [InlineData("LifecycleControlPage.xaml")]
    [InlineData("OperationsContinuityPage.xaml")]
    [InlineData("OperatorReadinessConsolePage.xaml")]
    [InlineData("PluginManagementPage.xaml")]
    [InlineData("ProviderHealthPage.xaml")]
    [InlineData("SystemHealthPage.xaml")]
    [InlineData("AccountingConfigurePage.xaml")]
    [InlineData("ClusterStatusPage.xaml")]
    [InlineData("MessagingHubPage.xaml")]
    [InlineData("StrategyRunsPage.xaml")]
    [InlineData("DashboardPage.xaml")]
    public void PageSource_ShouldNotBindAButtonToThePageRefreshCommand(string pageFile)
    {
        var xaml = ReadView(pageFile);

        xaml.Should().NotContain("Command=\"{Binding RefreshCommand}\"");
        xaml.Should().NotContain("Command=\"{Binding RefreshStatusCommand}\"");
        xaml.Should().NotContain("Content=\"Refresh\"");
    }

    [Theory]
    [InlineData("EvidenceWorkbenchPage.xaml", "EvidenceWorkbenchRetryButton", "{Binding HasError, Converter={StaticResource BoolToVisibilityConverter}}")]
    [InlineData("OperationsRecordReleasePage.xaml", "OperationsRecordReleaseRetryButton", "{Binding HasWorkflowError, Converter={StaticResource BoolToVisibilityConverter}}")]
    [InlineData("PostedLedgerPage.xaml", "PostedLedgerRetryButton", "{Binding HasLoadError, Converter={StaticResource BoolToVisibilityConverter}}")]
    [InlineData("SymbolStoragePage.xaml", "SymbolStorageRetryButton", "{Binding HasError, Converter={StaticResource BoolToVisibilityConverter}}")]
    [InlineData("ServiceManagerPage.xaml", "ServiceManagerStatusRetryButton", "{Binding IsStatusLoadFailed, Converter={StaticResource BoolToVisibilityConverter}}")]
    public void PageSource_ShouldKeepRetryOnlyInsideTheErrorState(string pageFile, string retryAutomationId, string errorVisibilityBinding)
    {
        var xaml = ReadView(pageFile);

        xaml.Should().Contain(retryAutomationId);
        xaml.Should().Contain(errorVisibilityBinding);
        xaml.Should().NotContain("Content=\"Refresh\"");
        CountOccurrences(xaml, "Content=\"Retry\"").Should().Be(1);
        CountOccurrences(xaml, "Command=\"{Binding RefreshCommand}\"").Should().Be(
            1,
            "the only local control bound to the page reload is the error-state Retry");
    }

    [Theory]
    [InlineData("PositionBlotterPage.xaml", "PositionBlotterEmptyStateRefreshButton")]
    [InlineData("AccountPortfolioPage.xaml", "AccountPositionsEmptyStateRefreshButton")]
    [InlineData("AggregatePortfolioPage.xaml", "AggregatePositionsEmptyStateRefreshButton")]
    [InlineData("WorkflowLibraryPage.xaml", "WorkflowLibraryEmptyStateRefreshButton")]
    public void PageSource_ShouldKeepEmptyStateAction(string pageFile, string emptyStateAutomationId)
    {
        var xaml = ReadView(pageFile);

        xaml.Should().Contain(emptyStateAutomationId);
        CountOccurrences(xaml, "{Binding RefreshCommand}").Should().Be(
            1,
            "only the empty-state action stays bound to the page reload");
    }

    [Theory]
    [InlineData(typeof(AccountPortfolioViewModel))]
    [InlineData(typeof(AggregatePortfolioViewModel))]
    [InlineData(typeof(DirectLendingViewModel))]
    [InlineData(typeof(EvidenceWorkbenchViewModel))]
    [InlineData(typeof(FinancialRecordExplorerViewModel))]
    [InlineData(typeof(FundLedgerViewModel))]
    [InlineData(typeof(HomeWorkspaceViewModel))]
    [InlineData(typeof(LifecycleControlViewModel))]
    [InlineData(typeof(OperationsContinuityViewModel))]
    [InlineData(typeof(OperationsRecordReleaseViewModel))]
    [InlineData(typeof(OperatorReadinessConsoleViewModel))]
    [InlineData(typeof(PositionBlotterViewModel))]
    [InlineData(typeof(PostedLedgerViewModel))]
    [InlineData(typeof(SystemHealthViewModel))]
    [InlineData(typeof(WorkflowLibraryViewModel))]
    [InlineData(typeof(AccountingConfigureViewModel))]
    [InlineData(typeof(ClusterStatusViewModel))]
    [InlineData(typeof(MessagingHubViewModel))]
    [InlineData(typeof(StrategyRunBrowserViewModel))]
    [InlineData(typeof(SymbolStorageViewModel))]
    [InlineData(typeof(ServiceManagerViewModel))]
    [InlineData(typeof(ProviderHealthViewModel))]
    [InlineData(typeof(DataQualityViewModel))]
    public void PageViewModel_ShouldStillExposeTheRefreshCommandTheShellInvokes(Type viewModelType)
    {
        var property = viewModelType.GetProperty(PageRefreshResolver.ConventionalCommandPropertyName);

        property.Should().NotBeNull();
        typeof(ICommand).IsAssignableFrom(property!.PropertyType).Should().BeTrue();
        typeof(Meridian.Wpf.Contracts.IPageRefreshOptOut).IsAssignableFrom(viewModelType).Should().BeFalse();
    }

    [Fact]
    public void PluginManagementViewModel_ShouldOptOutBecauseItsRefreshReloadsPluginAssemblies()
    {
        typeof(Meridian.Wpf.Contracts.IPageRefreshOptOut)
            .IsAssignableFrom(typeof(PluginManagementViewModel))
            .Should().BeTrue("shell Refresh must fall back to re-navigation instead of re-running plugin loading");
        typeof(Meridian.Wpf.Contracts.IPageRefreshTarget)
            .IsAssignableFrom(typeof(PluginManagementViewModel))
            .Should().BeFalse();
    }

    [Theory]
    [InlineData(typeof(ProviderHealthViewModel))]
    [InlineData(typeof(DataQualityViewModel))]
    [InlineData(typeof(DashboardViewModel))]
    public void ActionBarPageViewModel_ShouldBeAnExplicitRefreshTarget(Type viewModelType)
    {
        typeof(Meridian.Wpf.Contracts.IPageRefreshTarget).IsAssignableFrom(viewModelType).Should().BeTrue();
    }

    [Theory]
    [InlineData(@"ViewModels\ProviderHealthViewModel.cs")]
    [InlineData(@"ViewModels\DataQualityViewModel.cs")]
    [InlineData(@"ViewModels\DashboardViewModel.cs")]
    [InlineData(@"Views\DataQualityPage.xaml.cs")]
    public void PageSource_ShouldNotOfferItsOwnRefreshAction(string relativePath)
    {
        var source = ReadSource(relativePath);

        source.Should().NotContain("new ActionEntry(\"Refresh\"");
        source.Should().NotContain("Label = \"Refresh\"");
        source.Should().NotContain("\"Refresh Status\"");
    }

    [Fact]
    public void SettingsShellFallbackCommand_ShouldReadRetryBecauseItOnlyShowsWhenTheSnapshotFailed()
    {
        var source = ReadSource(@"Features\Settings\Shell\SettingsWorkspaceShellViewModel.cs");

        source.Should().Contain("Id = \"Refresh\", Label = \"Retry\"");
        source.Should().NotContain("Label = \"Refresh\"");
    }

    [Theory]
    [InlineData(@"Views\StrategyWorkspaceShellPage.xaml.cs")]
    [InlineData(@"Views\AccountingWorkspaceShellPage.xaml.cs")]
    [InlineData(@"Features\Trading\Shell\TradingWorkspaceShellPage.xaml.cs")]
    [InlineData(@"Features\Settings\Shell\SettingsWorkspaceShellPage.xaml.cs")]
    [InlineData(@"Features\Data\Shell\DataWorkspaceShellPage.xaml.cs")]
    [InlineData(@"Features\Reporting\Shell\ReportingWorkspaceShellPage.xaml.cs")]
    [InlineData(@"Features\Portfolio\Shell\PortfolioWorkspaceShellPage.xaml.cs")]
    public void WorkspaceShellPage_ShouldRunItsOwnReloadPathFromTheShellRefresh(string relativePath)
    {
        ReadSource(relativePath).Should().Contain("protected override Task RefreshShellInPlaceAsync()");
    }

    [Theory]
    [InlineData(typeof(Meridian.Wpf.Views.StrategyWorkspaceShellPage))]
    [InlineData(typeof(Meridian.Wpf.Views.AccountingWorkspaceShellPage))]
    [InlineData(typeof(Meridian.Wpf.Views.TradingWorkspaceShellPage))]
    [InlineData(typeof(Meridian.Wpf.Features.Settings.Shell.SettingsWorkspaceShellPage))]
    [InlineData(typeof(Meridian.Wpf.Features.Data.Shell.DataWorkspaceShellPage))]
    [InlineData(typeof(Meridian.Wpf.Features.Reporting.Shell.ReportingWorkspaceShellPage))]
    [InlineData(typeof(Meridian.Wpf.Features.Portfolio.Shell.PortfolioWorkspaceShellPage))]
    public void WorkspaceShellPage_ShouldBeARefreshTargetSoDockedPagesReloadInPlace(Type shellPageType)
    {
        typeof(Meridian.Wpf.Contracts.IPageRefreshTarget).IsAssignableFrom(shellPageType).Should().BeTrue();
    }

    [Fact]
    public void WorkspaceShellPageBaseSource_ShouldFanOutToDockedPagesWithoutNavigating()
    {
        var source = ReadSource(@"Views\WorkspaceShellPageBase.cs");

        source.Should().Contain("DispatchRefresh(RefreshShellInPlaceAsync);");
        source.Should().Contain("PageRefreshResolver.RefreshAllInPlace(_refreshDockManager?.GetOpenPageContents());");
    }

    [Fact]
    public void DashboardViewModel_ShouldBeAnExplicitRefreshTarget()
    {
        typeof(Meridian.Wpf.Contracts.IPageRefreshTarget).IsAssignableFrom(typeof(DashboardViewModel)).Should().BeTrue();
    }

    [Fact]
    public void DeepPageHost_ShouldForwardShellRefreshToTheHostedPage()
    {
        typeof(Meridian.Wpf.Contracts.IPageRefreshContentHost)
            .IsAssignableFrom(typeof(Meridian.Wpf.Views.WorkspaceDeepPageHostPage))
            .Should().BeTrue();
    }

    private static string ReadSource(string relativePath)
        => File.ReadAllText(RunMatUiAutomationFacade.GetRepoFilePath(@"src\Meridian.Wpf\" + relativePath));

    private static string ReadView(string pageFile)
        => File.ReadAllText(RunMatUiAutomationFacade.GetRepoFilePath(@"src\Meridian.Wpf\Views\" + pageFile));

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }
}
