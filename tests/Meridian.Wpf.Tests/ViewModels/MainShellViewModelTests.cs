using System.IO;
using System.Windows;
using System.Windows.Controls;
using Meridian.Ui.Services.Contracts;
using Meridian.Ui.Services.Services;
using Meridian.Contracts.Workstation;
using Meridian.Wpf.Models;
using Meridian.Wpf.Services;
using Meridian.Wpf.Tests.Support;
using Meridian.Wpf.ViewModels;

namespace Meridian.Wpf.Tests.ViewModels;

public sealed class MainShellViewModelTests
{
    private static MainPageViewModel CreateMainPageViewModel(FundContextService? fundContextService = null)
    {
        var navigationService = NavigationService.Instance;
        navigationService.Initialize(new Frame());
        navigationService.ClearHistory();

        var fixtureModeDetector = FixtureModeDetector.Instance;
        fixtureModeDetector.SetFixtureMode(false);
        fixtureModeDetector.UpdateBackendReachability(true);

        return new MainPageViewModel(navigationService, fixtureModeDetector, fundContextService);
    }

    private static MainWindowViewModel CreateMainWindowViewModel()
    {
        var navigationService = NavigationService.Instance;
        navigationService.Initialize(new Frame());
        navigationService.ClearHistory();

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
    public void ActivateShell_WhenHistoryIsEmpty_NavigatesToDashboard()
    {
        WpfTestThread.Run(() =>
        {
            using var vm = CreateMainPageViewModel();

            vm.ActivateShell();

            vm.CurrentPageTag.Should().Be("Dashboard");
            vm.CurrentPageTitle.Should().Be("Dashboard");
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

            vm.CommandPalettePages.Should().Contain("Symbols");
            vm.CommandPalettePages.Should().Contain("SymbolMapping");
            vm.CommandPalettePages.Should().NotContain("Dashboard");
            vm.SelectedCommandPalettePage.Should().Be("SymbolMapping");
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
    public void FixtureModeChange_UpdatesBannerVisibilityAndText()
    {
        WpfTestThread.Run(() =>
        {
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
}
