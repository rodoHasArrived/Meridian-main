using System.IO;
using System.Windows;
using System.Windows.Controls;
using Meridian.Contracts.Api;
using Meridian.Contracts.Workstation;
using Meridian.Ui.Services.Contracts;
using Meridian.Ui.Services.Services;
using Meridian.Wpf.Models;
using Meridian.Wpf.Services;
using Meridian.Wpf.Tests.Support;
using Meridian.Wpf.ViewModels;
using Xunit.Sdk;

namespace Meridian.Wpf.Tests.ViewModels;

public sealed class MainShellViewModelTests
{
    private static MainPageViewModel CreateMainPageViewModel(
        FundContextService? fundContextService = null,
        WorkstationOperatingContextService? operatingContextService = null,
        SettingsConfigurationService? settingsConfigurationService = null,
        IWorkstationOperatorInboxApiClient? operatorInboxClient = null)
    {
        var navigationService = NavigationService.Instance;
        navigationService.ResetForTests();
        navigationService.Initialize(new Frame());
        WorkspaceService.SetSettingsFilePathOverrideForTests(null);
        WorkspaceService.Instance.ResetForTests();

        var fixtureModeDetector = FixtureModeDetector.Instance;
        fixtureModeDetector.SetFixtureMode(false);
        fixtureModeDetector.UpdateBackendReachability(true);

        return new MainPageViewModel(
            navigationService,
            fixtureModeDetector,
            fundContextService,
            operatingContextService,
            operatorInboxApiClient: operatorInboxClient,
            settingsConfigurationService: settingsConfigurationService);
    }

    private static MainWindowViewModel CreateMainWindowViewModel()
    {
        var navigationService = NavigationService.Instance;
        navigationService.ResetForTests();
        navigationService.Initialize(new Frame());
        WorkspaceService.SetSettingsFilePathOverrideForTests(null);
        WorkspaceService.Instance.ResetForTests();

        var fixtureModeDetector = FixtureModeDetector.Instance;
        fixtureModeDetector.SetFixtureMode(false);
        fixtureModeDetector.UpdateBackendReachability(true);

        return new MainWindowViewModel(
            ConnectionService.Instance,
            navigationService,
            NotificationService.Instance,
            MessagingService.Instance,
            ThemeService.Instance,
            WatchlistService.Instance,
            fixtureModeDetector,
            Substitute.For<IStatusService>());
    }

    [Fact]
    public void ActivateShell_WhenHistoryIsEmpty_NavigatesToResearchWorkspace()
    {
        WpfTestThread.Run(() =>
        {
            using var vm = CreateMainPageViewModel();

            vm.ActivateShell();

            vm.CurrentPageTag.Should().Be("ResearchShell");
            vm.CurrentPageTitle.Should().Be("Research Workspace");
            vm.BackButtonVisibility.Should().Be(Visibility.Collapsed);
        });
    }

    [Fact]
    public void CommandPaletteQuery_FiltersRegisteredPages()
    {
        WpfTestThread.Run(() =>
        {
            using var vm = CreateMainPageViewModel();

            vm.CommandPaletteQuery = "sym";

            vm.CommandPalettePages.Select(page => page.PageTag).Should().Contain("Symbols");
            vm.CommandPalettePages.Select(page => page.PageTag).Should().Contain("SymbolMapping");
            vm.CommandPalettePages.Select(page => page.PageTag).Should().NotContain("Dashboard");
            vm.SelectedCommandPalettePage.Should().NotBeNull();
            vm.SelectedCommandPalettePage!.PageTag.Should().Be("Symbols");
        });
    }

    [Fact]
    public void CommandPaletteQuery_UsesTierOrderingWithinMatchingResults()
    {
        WpfTestThread.Run(() =>
        {
            using var vm = CreateMainPageViewModel();

            vm.CommandPaletteQuery = "workspace";

            vm.CommandPalettePages.Should().NotBeEmpty();
            vm.CommandPalettePages.First().PageTag.Should().Be("Workspaces");
        });
    }

    [Fact]
    public void CommandPaletteQuery_WhenNoResults_ShowsHelpfulEmptyStateAndCanClear()
    {
        WpfTestThread.Run(() =>
        {
            using var vm = CreateMainPageViewModel();

            vm.CommandPaletteQuery = "zzzz-unmatched-query";

            vm.CommandPalettePages.Should().BeEmpty();
            vm.CommandPaletteEmptyVisibility.Should().Be(Visibility.Visible);
            vm.CommandPaletteResultSummary.Should().Contain("No matches");
            vm.CommandPaletteEmptyTitle.Should().Contain("zzzz-unmatched-query");

            vm.ClearCommandPaletteQueryCommand.Execute(null);

            vm.CommandPaletteQuery.Should().BeEmpty();
            vm.CommandPalettePages.Should().NotBeEmpty();
            vm.CommandPaletteEmptyVisibility.Should().Be(Visibility.Collapsed);
        });
    }

    [Fact]
    public void ShellNavigationCatalog_CoversEveryRegisteredPage()
    {
        WpfTestThread.Run(() =>
        {
            using var vm = CreateMainPageViewModel();

            var registeredPages = NavigationService.Instance.GetRegisteredPages().ToHashSet(StringComparer.OrdinalIgnoreCase);
            var catalogPages = ShellNavigationCatalog.GetRegisteredPageTags().ToHashSet(StringComparer.OrdinalIgnoreCase);

            registeredPages.Should().BeEquivalentTo(catalogPages);
        });
    }

    [Fact]
    public void WorkspaceSelection_RefreshesPrimaryOverflowAndRelatedNavigation()
    {
        WpfTestThread.Run(() =>
        {
            using var vm = CreateMainPageViewModel();

            vm.SelectWorkspaceCommand.Execute("governance");

            vm.CurrentWorkspace.Should().Be("governance");
            vm.CurrentPageTag.Should().Be("GovernanceShell");
            vm.PrimaryNavigationItems.Select(item => item.PageTag).Should().Contain(["GovernanceShell", "FundLedger", "FundReconciliation"]);
            vm.OverflowNavigationItems.Select(item => item.PageTag).Should().Contain("Settings");
            vm.RelatedWorkflowItems.Select(item => item.PageTag).Should().Contain(["FundLedger", "FundReconciliation", "SecurityMaster"]);
        });
    }

    [Fact]
    public void WorkspaceNavigation_UsesFriendlyContextTagsInsteadOfRawTierNames()
    {
        WpfTestThread.Run(() =>
        {
            using var vm = CreateMainPageViewModel();

            vm.SelectWorkspaceCommand.Execute("governance");

            vm.SecondaryNavigationItems.Should().OnlyContain(item => item.VisibilityLabel != "Secondary");
            vm.OverflowNavigationItems.Should().OnlyContain(item => item.VisibilityLabel == "Support");
        });
    }

    [Fact]
    public void RecentPages_AreScopedToTheActiveWorkspace()
    {
        WpfTestThread.Run(() =>
        {
            using var vm = CreateMainPageViewModel();

            vm.ActivateShell();
            vm.NavigateToPageCommand.Execute("Backtest");
            vm.NavigateToPageCommand.Execute("GovernanceShell");
            vm.NavigateToPageCommand.Execute("SecurityMaster");

            vm.CurrentWorkspace.Should().Be("governance");
            vm.RecentPages.Select(page => page.PageTag).Should().Equal("GovernanceShell");
            vm.RecentPagesSummaryText.Should().Be("1 recent governance workflow");

            vm.SelectWorkspaceCommand.Execute("research");

            vm.CurrentWorkspace.Should().Be("research");
            vm.CurrentPageTag.Should().Be("ResearchShell");
            vm.RecentPages.Select(page => page.PageTag).Should().Equal("Backtest");
            vm.RecentPagesSummaryText.Should().Be("1 recent research workflow");
        });
    }

    [Fact]
    public void NavigateToPageCommand_UpdatesCurrentPage()
    {
        WpfTestThread.Run(() =>
        {
            using var vm = CreateMainPageViewModel();

            vm.ActivateShell();
            vm.NavigateToPageCommand.Execute("Symbols");
            vm.NavigateToPageCommand.Execute("Backfill");

            vm.CurrentPageTag.Should().Be("Backfill");
            vm.CurrentPageTitle.Should().Be("Backfill");
        });
    }

    [Fact]
    public void NavigateToEventReplay_KeepsResearchWorkspaceActive()
    {
        WpfTestThread.Run(() =>
        {
            using var vm = CreateMainPageViewModel();

            vm.NavigateToPageCommand.Execute("EventReplay");

            vm.CurrentWorkspace.Should().Be("research");
            vm.CurrentPageTag.Should().Be("EventReplay");
        });
    }

    [Fact]
    public void NavigateToTradingRoutes_InfersTradingWorkspace()
    {
        WpfTestThread.Run(() =>
        {
            using var vm = CreateMainPageViewModel();

            vm.NavigateToPageCommand.Execute("OrderBook");
            vm.CurrentWorkspace.Should().Be("trading");

            vm.NavigateToPageCommand.Execute("PositionBlotter");
            vm.CurrentWorkspace.Should().Be("trading");

            vm.NavigateToPageCommand.Execute("RunRisk");
            vm.CurrentWorkspace.Should().Be("trading");
        });
    }

    [Fact]
    public void NavigateToAddProviderWizard_KeepsDataOperationsWorkspaceActive()
    {
        WpfTestThread.Run(() =>
        {
            using var vm = CreateMainPageViewModel();

            vm.NavigateToPageCommand.Execute("AddProviderWizard");

            vm.CurrentWorkspace.Should().Be("data-operations");
            vm.CurrentPageTag.Should().Be("AddProviderWizard");
        });
    }
    [Fact]
    public void FixtureModeChange_UpdatesBannerVisibilityAndText()
    {
        WpfTestThread.Run(() =>
        {
            NavigationService.Instance.ResetForTests();
            WorkspaceService.SetSettingsFilePathOverrideForTests(null);
            WorkspaceService.Instance.ResetForTests();
            var detector = FixtureModeDetector.Instance;
            detector.SetFixtureMode(false);
            detector.UpdateBackendReachability(true);

            using var vm = new MainPageViewModel(NavigationService.Instance, detector);

            detector.SetFixtureMode(true);

            vm.FixtureModeBannerVisibility.Should().Be(Visibility.Visible);
            vm.FixtureModeBannerText.Should().Contain("Demo data mode");
            vm.ShellStatusText.Should().Be("Demo data");
            vm.ShellStatusTone.Should().Be(WorkspaceTone.Info);

            detector.SetFixtureMode(false);
        });
    }

    [Fact]
    public void LaunchArgs_ShouldNormalizeAliasesAndSequentialForwardedNavigation()
    {
        WpfTestThread.Run(() =>
        {
            using var vm = CreateMainWindowViewModel();

            vm.HandleLaunchArgs(["--page=RunBrowser"]);

            NavigationService.Instance.GetCurrentPageTag().Should().Be("StrategyRuns");

            vm.HandleLaunchArgs(["--navigate", "Backtest"]);

            NavigationService.Instance.GetCurrentPageTag().Should().Be("Backtest");

            vm.HandleLaunchArgs(["--page", "DataOperationsShell"]);

            NavigationService.Instance.GetCurrentPageTag().Should().Be("DataOperationsShell");
        });
    }

    [Fact]
    public void ShowClipboardSymbols_TogglesBannerState()
    {
        WpfTestThread.Run(() =>
        {
            using var vm = CreateMainWindowViewModel();

            vm.ShowClipboardSymbols(["AAPL", "MSFT"]);

            vm.ClipboardBannerVisibility.Should().Be(Visibility.Visible);
            vm.ClipboardBannerText.Should().Contain("AAPL, MSFT");
            vm.AddClipboardSymbolsCommand.CanExecute(null).Should().BeTrue();

            vm.DismissClipboardBannerCommand.Execute(null);

            vm.ClipboardBannerVisibility.Should().Be(Visibility.Collapsed);
            vm.AddClipboardSymbolsCommand.CanExecute(null).Should().BeFalse();
        });
    }

    [Fact]
    public void ShowClipboardSymbols_WhenManySymbolsDetected_UsesCompactPreview()
    {
        WpfTestThread.Run(() =>
        {
            using var vm = CreateMainWindowViewModel();

            vm.ShowClipboardSymbols(["AAPL", "MSFT", "NVDA", "AMD", "TSLA", "META"]);

            vm.ClipboardBannerText.Should().Contain("6 symbols detected in clipboard");
            vm.ClipboardBannerText.Should().Contain("AAPL, MSFT, NVDA, AMD +2 more");
        });
    }

    [Fact]
    public void ActiveFundDisplay_WhenFundSelected_ShowsFundBadgeAndMetadata()
    {
        WpfTestThread.Run(async () =>
        {
            var fundContext = await CreateFundContextAsync();
            using var vm = CreateMainPageViewModel(fundContext);

            vm.ActiveFundVisibility.Should().Be(Visibility.Visible);
            vm.ActiveFundName.Should().Be("Alpha Credit");
            vm.ActiveFundSubtitle.Should().Contain("USD");
        });
    }

    [Fact]
    public void SwitchFundCommand_RaisesFundSwitchRequest()
    {
        WpfTestThread.Run(async () =>
        {
            var fundContext = await CreateFundContextAsync();
            using var vm = CreateMainPageViewModel(fundContext);
            var raised = false;
            fundContext.FundSwitchRequested += (_, _) => raised = true;

            vm.SwitchFundCommand.Execute(null);

            raised.Should().BeTrue();
        });
    }

    [Fact]
    public void ActiveFundDisplay_WhenOperatingContextSelected_ShowsContextMetadataAndWindowMode()
    {
        WpfTestThread.Run(async () =>
        {
            var fundContext = await CreateFundContextAsync();
            var operatingContextService = await CreateOperatingContextServiceAsync(fundContext);
            await operatingContextService.SetWindowModeAsync(Meridian.Ui.Services.BoundedWindowMode.WorkbenchPreset, "accounting-review");

            using var vm = CreateMainPageViewModel(fundContext, operatingContextService);

            vm.ActiveFundVisibility.Should().Be(Visibility.Visible);
            vm.ActiveFundName.Should().Be("Alpha Credit");
            vm.ActiveFundSubtitle.Should().Contain("Fund");
            vm.SelectedOperatingContext.Should().NotBeNull();
            vm.CurrentModeName.Should().Contain("Accounting Review");
        });
    }

    [Fact]
    public void SwitchFundCommand_WhenOperatingContextServicePresent_RaisesContextSwitchRequest()
    {
        WpfTestThread.Run(async () =>
        {
            var fundContext = await CreateFundContextAsync();
            var operatingContextService = await CreateOperatingContextServiceAsync(fundContext);
            using var vm = CreateMainPageViewModel(fundContext, operatingContextService);
            var raised = false;
            operatingContextService.ContextSwitchRequested += (_, _) => raised = true;

            vm.SwitchFundCommand.Execute(null);

            raised.Should().BeTrue();
        });
    }

    [Fact]
    public void OperatingContextUpdates_FromBackgroundThread_MarshalBackIntoShellState()
    {
        WpfTestThread.Run(async () =>
        {
            var fundContext = await CreateFundContextAsync();
            var operatingContextService = await CreateOperatingContextServiceAsync(fundContext);
            using var vm = CreateMainPageViewModel(fundContext, operatingContextService);

            await Task.Run(() => operatingContextService.SetWindowModeAsync(Meridian.Ui.Services.BoundedWindowMode.WorkbenchPreset, "accounting-review"));
            await WaitForConditionAsync(
                () => vm.SelectedWindowMode == Meridian.Ui.Services.BoundedWindowMode.WorkbenchPreset
                      && vm.CurrentModeName.Contains("Accounting Review", StringComparison.Ordinal));

            vm.SelectedWindowMode.Should().Be(Meridian.Ui.Services.BoundedWindowMode.WorkbenchPreset);
            vm.CurrentModeName.Should().Contain("Accounting Review");
        });
    }

    [Fact]
    public void FreshOperatingContextCatalog_DoesNotPretendFirstEntryIsActive()
    {
        WpfTestThread.Run(async () =>
        {
            var fundContext = await CreateFundContextAsync();
            var storagePath = Path.Combine(
                Path.GetTempPath(),
                "meridian-main-shell-tests",
                $"{Guid.NewGuid():N}.fresh-operating-context.json");

            var operatingContextService = new WorkstationOperatingContextService(fundContext, storagePath: storagePath);
            await operatingContextService.LoadAsync();
            using var vm = CreateMainPageViewModel(fundContext, operatingContextService);

            vm.OperatingContexts.Should().NotBeEmpty();
            vm.SelectedOperatingContext.Should().BeNull();
            operatingContextService.CurrentContext.Should().BeNull();

            var firstContext = vm.OperatingContexts[0];
            vm.SelectedOperatingContext = firstContext;

            await WaitForConditionAsync(() =>
                string.Equals(
                    operatingContextService.CurrentContext?.ContextKey,
                    firstContext.ContextKey,
                    StringComparison.OrdinalIgnoreCase));

            vm.SelectedOperatingContext.Should().NotBeNull();
            operatingContextService.CurrentContext.Should().NotBeNull();
            operatingContextService.CurrentContext!.ContextKey.Should().Be(firstContext.ContextKey);
        });
    }

    [Fact]
    public void ShellDensityPreference_RemainsIndependentFromBoundedWindowMode()
    {
        WpfTestThread.Run(async () =>
        {
            var preferencesPath = Path.Combine(
                Path.GetTempPath(),
                "meridian-main-shell-tests",
                $"{Guid.NewGuid():N}.desktop-shell-preferences.json");

            try
            {
                SettingsConfigurationService.SetDesktopPreferencesFilePathOverrideForTests(preferencesPath);
                var settingsConfigurationService = SettingsConfigurationService.Instance;
                settingsConfigurationService.SetShellDensityMode(ShellDensityMode.Compact);

                var fundContext = await CreateFundContextAsync();
                var operatingContextService = await CreateOperatingContextServiceAsync(fundContext);
                await operatingContextService.SetWindowModeAsync(Meridian.Ui.Services.BoundedWindowMode.WorkbenchPreset, "accounting-review");

                using var vm = CreateMainPageViewModel(fundContext, operatingContextService, settingsConfigurationService);

                vm.ShellDensityMode.Should().Be(ShellDensityMode.Compact);
                vm.IsCompactShellDensity.Should().BeTrue();
                vm.SelectedWindowMode.Should().Be(Meridian.Ui.Services.BoundedWindowMode.WorkbenchPreset);
                vm.CurrentModeName.Should().Contain("Accounting Review");

                settingsConfigurationService.SetShellDensityMode(ShellDensityMode.Standard);
                await WaitForConditionAsync(() => vm.ShellDensityMode == ShellDensityMode.Standard);

                vm.IsCompactShellDensity.Should().BeFalse();
                vm.SelectedWindowMode.Should().Be(Meridian.Ui.Services.BoundedWindowMode.WorkbenchPreset);
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
    public void ToggleShellDensityCommand_UpdatesShellStateAndPersistsPreference()
    {
        WpfTestThread.Run(() =>
        {
            var preferencesPath = Path.Combine(
                Path.GetTempPath(),
                "meridian-main-shell-tests",
                $"{Guid.NewGuid():N}.density-toggle.json");

            try
            {
                SettingsConfigurationService.SetDesktopPreferencesFilePathOverrideForTests(preferencesPath);
                var settingsConfigurationService = SettingsConfigurationService.Instance;
                settingsConfigurationService.SetShellDensityMode(ShellDensityMode.Standard);

                using var vm = CreateMainPageViewModel(settingsConfigurationService: settingsConfigurationService);

                vm.ShellDensityMode.Should().Be(ShellDensityMode.Standard);
                vm.ShellDensityButtonText.Should().Be("Density: Standard");
                vm.ShellDensityToggleTooltip.Should().Be("Switch to compact shell density");

                vm.ToggleShellDensityCommand.Execute(null);

                vm.ShellDensityMode.Should().Be(ShellDensityMode.Compact);
                vm.IsCompactShellDensity.Should().BeTrue();
                vm.ShellDensityButtonText.Should().Be("Density: Compact");
                vm.ShellDensityToggleTooltip.Should().Be("Switch to standard shell density");
                settingsConfigurationService.GetShellDensityMode().Should().Be(ShellDensityMode.Compact);

                vm.ToggleShellDensityCommand.Execute(null);

                vm.ShellDensityMode.Should().Be(ShellDensityMode.Standard);
                vm.IsCompactShellDensity.Should().BeFalse();
                settingsConfigurationService.GetShellDensityMode().Should().Be(ShellDensityMode.Standard);
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
    public void WorkflowSummaryPresentation_PrioritizesCurrentWorkspace()
    {
        WpfTestThread.Run(async () =>
        {
            using var vm = CreateMainPageViewModel();

            await WaitForConditionAsync(() =>
                vm.PrimaryWorkflowSummary is not null &&
                vm.SecondaryWorkflowSummaries.Count == 3);

            vm.PrimaryWorkflowSummary.Should().NotBeNull();
            vm.PrimaryWorkflowSummary!.WorkspaceId.Should().Be("research");
            vm.SecondaryWorkflowSummaries.Select(summary => summary.WorkspaceId).Should().NotContain("research");

            vm.SelectWorkspaceCommand.Execute("trading");

            await WaitForConditionAsync(() => vm.PrimaryWorkflowSummary?.WorkspaceId == "trading");

            vm.PrimaryWorkflowSummary!.WorkspaceId.Should().Be("trading");
            vm.SecondaryWorkflowSummaries.Should().HaveCount(3);
            vm.SecondaryWorkflowSummaries.Select(summary => summary.WorkspaceId).Should().NotContain("trading");
            vm.PrimaryWorkflowTargetText.Should().NotBe("Target page: -");
        });
    }

    [Fact]
    public void OperatorInboxPresentation_LoadsQueueAndNavigatesToPrimaryWorkItem()
    {
        WpfTestThread.Run(async () =>
        {
            var inbox = new OperatorInboxDto(
                DateTimeOffset.UtcNow,
                [
                    new OperatorWorkItemDto(
                        WorkItemId: "paper-replay-stale-session-1",
                        Kind: OperatorWorkItemKindDto.PaperReplay,
                        Label: "Replay verification is stale",
                        Detail: "Session changed after the latest replay audit.",
                        Tone: OperatorWorkItemToneDto.Critical,
                        CreatedAt: DateTimeOffset.UtcNow,
                        TargetPageTag: "TradingShell")
                ],
                CriticalCount: 1,
                WarningCount: 0,
                ReviewCount: 1,
                Summary: "1 critical work item needs review.");
            var inboxClient = new FakeOperatorInboxApiClient(inbox);

            using var vm = CreateMainPageViewModel(operatorInboxClient: inboxClient);

            await WaitForConditionAsync(() => vm.OperatorInboxReviewCount == 1);

            vm.OperatorInboxButtonText.Should().Be("Queue (1)");
            vm.OperatorInboxTone.Should().Be(WorkspaceTone.Danger);
            vm.OperatorInboxSummary.Should().Contain("critical");
            vm.OperatorInboxPrimaryLabel.Should().Be("Replay verification is stale");

            vm.OpenOperatorInboxCommand.Execute(null);

            vm.CurrentPageTag.Should().Be("TradingShell");
        });
    }

    [Fact]
    public void OperatorInboxPresentation_UsesRouteToOpenSpecificWorkbench()
    {
        WpfTestThread.Run(async () =>
        {
            var inbox = new OperatorInboxDto(
                DateTimeOffset.UtcNow,
                [
                    new OperatorWorkItemDto(
                        WorkItemId: "reconciliation-break-run-1-cash-mismatch",
                        Kind: OperatorWorkItemKindDto.ReconciliationBreak,
                        Label: "Reconciliation break requires review",
                        Detail: "Cash mismatch needs governance review.",
                        Tone: OperatorWorkItemToneDto.Warning,
                        CreatedAt: DateTimeOffset.UtcNow,
                        TargetRoute: UiApiRoutes.ReconciliationBreakQueue,
                        TargetPageTag: "GovernanceShell")
                ],
                CriticalCount: 0,
                WarningCount: 1,
                ReviewCount: 1,
                Summary: "1 warning work item needs review.");
            var inboxClient = new FakeOperatorInboxApiClient(inbox);

            using var vm = CreateMainPageViewModel(operatorInboxClient: inboxClient);

            await WaitForConditionAsync(() => vm.OperatorInboxReviewCount == 1);

            vm.OperatorInboxTargetText.Should().Be("FundReconciliation");

            vm.OpenOperatorInboxCommand.Execute(null);

            vm.CurrentWorkspace.Should().Be("governance");
            vm.CurrentPageTag.Should().Be("FundReconciliation");
        });
    }

    [Fact]
    public void OperatorInboxPresentation_WhenClientFails_DegradesToNotificationCenter()
    {
        WpfTestThread.Run(async () =>
        {
            var inboxClient = new FakeOperatorInboxApiClient(new InvalidOperationException("backend unavailable"));

            using var vm = CreateMainPageViewModel(operatorInboxClient: inboxClient);

            await WaitForConditionAsync(() =>
                vm.OperatorInboxSummary.Contains("awaiting backend", StringComparison.OrdinalIgnoreCase));

            vm.OperatorInboxReviewCount.Should().Be(0);
            vm.OperatorInboxTone.Should().Be(WorkspaceTone.Neutral);

            vm.OpenOperatorInboxCommand.Execute(null);

            vm.CurrentPageTag.Should().Be("NotificationCenter");
        });
    }

    private static async Task<FundContextService> CreateFundContextAsync()
    {
        var storagePath = Path.Combine(
            Path.GetTempPath(),
            "meridian-main-shell-tests",
            $"{Guid.NewGuid():N}.json");

        var service = new FundContextService(storagePath);
        await service.UpsertProfileAsync(new FundProfileDetail(
            FundProfileId: "alpha-credit",
            DisplayName: "Alpha Credit",
            LegalEntityName: "Alpha Credit Master Fund LP",
            BaseCurrency: "USD",
            DefaultWorkspaceId: "governance",
            DefaultLandingPageTag: "GovernanceShell",
            DefaultLedgerScope: FundLedgerScope.Consolidated,
            IsDefault: true));
        await service.SelectFundProfileAsync("alpha-credit");
        return service;
    }

    private static async Task<WorkstationOperatingContextService> CreateOperatingContextServiceAsync(FundContextService fundContextService)
    {
        var storagePath = Path.Combine(
            Path.GetTempPath(),
            "meridian-main-shell-tests",
            $"{Guid.NewGuid():N}.operating-context.json");

        var service = new WorkstationOperatingContextService(fundContextService, storagePath: storagePath);
        await service.LoadAsync();
        await service.SelectContextAsync(service.Contexts[0].ContextKey);
        return service;
    }

    private static async Task WaitForConditionAsync(Func<bool> predicate, int timeoutMs = 5000)
    {
        var start = DateTime.UtcNow;
        while (!predicate())
        {
            if ((DateTime.UtcNow - start).TotalMilliseconds > timeoutMs)
            {
                throw new XunitException("Timed out waiting for condition.");
            }

            await Task.Delay(25);
        }
    }

    private sealed class FakeOperatorInboxApiClient : IWorkstationOperatorInboxApiClient
    {
        private readonly OperatorInboxDto? _inbox;
        private readonly Exception? _exception;

        public FakeOperatorInboxApiClient(OperatorInboxDto inbox)
        {
            _inbox = inbox;
        }

        public FakeOperatorInboxApiClient(Exception exception)
        {
            _exception = exception;
        }

        public Task<OperatorInboxDto?> GetInboxAsync(Guid? fundAccountId = null, CancellationToken ct = default)
        {
            if (_exception is not null)
            {
                throw _exception;
            }

            return Task.FromResult(_inbox);
        }
    }
}
