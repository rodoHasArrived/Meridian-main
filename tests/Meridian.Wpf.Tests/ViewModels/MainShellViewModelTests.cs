using System.IO;
using System.Windows;
using System.Windows.Controls;
using Meridian.Contracts.Workstation;
using Meridian.Ui.Services.Contracts;
using Meridian.Ui.Services.Services;
using Meridian.Wpf.Models;
using Meridian.Wpf.Services;
using Meridian.Wpf.Tests.Support;
using Meridian.Wpf.ViewModels;

namespace Meridian.Wpf.Tests.ViewModels;

public sealed class MainShellViewModelTests
{
    private static MainPageViewModel CreateMainPageViewModel(
        FundContextService? fundContextService = null,
        WorkstationOperatingContextService? operatingContextService = null)
    {
        var navigationService = NavigationService.Instance;
        navigationService.ResetForTests();
        navigationService.Initialize(new Frame());
        WorkspaceService.Instance.ResetForTests();

        var fixtureModeDetector = FixtureModeDetector.Instance;
        fixtureModeDetector.SetFixtureMode(false);
        fixtureModeDetector.UpdateBackendReachability(true);

        return new MainPageViewModel(navigationService, fixtureModeDetector, fundContextService, operatingContextService);
    }

    private static MainWindowViewModel CreateMainWindowViewModel()
    {
        var navigationService = NavigationService.Instance;
        navigationService.ResetForTests();
        navigationService.Initialize(new Frame());
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
            WorkspaceService.Instance.ResetForTests();
            var detector = FixtureModeDetector.Instance;
            detector.SetFixtureMode(false);
            detector.UpdateBackendReachability(true);

            using var vm = new MainPageViewModel(NavigationService.Instance, detector);

            detector.SetFixtureMode(true);

            vm.FixtureModeBannerVisibility.Should().Be(Visibility.Visible);
            vm.FixtureModeBannerText.Should().Contain("FIXTURE MODE");

            detector.SetFixtureMode(false);
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
}
