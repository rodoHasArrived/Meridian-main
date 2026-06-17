using Meridian.Contracts.Api;
using Meridian.Contracts.Configuration;
using Meridian.Contracts.Workstation;
using Meridian.Ui.Services;
using Meridian.Wpf.Contracts;
using Meridian.Wpf.Models;
using Meridian.Wpf.Services;
using Meridian.Wpf.Tests.Support;
using Meridian.Wpf.ViewModels;
using WpfServices = Meridian.Wpf.Services;

namespace Meridian.Wpf.Tests.ViewModels;

public sealed class SymbolsPageViewModelTests
{
    [Fact]
    public void ActivationLifetime_DeactivateCancelsVisiblePageLifetimeWithoutClearingLoadedState()
    {
        var viewModel = File.ReadAllText(RunMatUiAutomationFacade.GetRepoFilePath(@"src\Meridian.Wpf\ViewModels\SymbolsPageViewModel.cs"));

        viewModel.Should().Contain("IPageActivationLifetime");
        viewModel.Should().Contain("public bool IsActive");
        viewModel.Should().Contain("public CancellationToken ActivationToken");
        viewModel.Should().Contain("public Task StartAsync(CancellationToken ct = default) => ActivateAsync(ct)");
        viewModel.Should().Contain("public void Stop() => Deactivate()");
        viewModel.Should().Contain("CancellationTokenSource.CreateLinkedTokenSource(ActivationToken, ct)");
        viewModel.Should().Contain("CancelWithoutDisposing(Interlocked.Exchange(ref _loadCts, null))");
        viewModel.Should().Contain("CancelWithoutDisposing(Interlocked.Exchange(ref _activationCts, null))");
        viewModel.Should().Contain("_getConfiguredSymbolsAsync(loadCts.Token)");
        viewModel.Should().Contain("activationCts.Dispose();");
    }

    [Fact]
    public async Task Deactivate_CancelsInFlightSymbolLoadWithoutClearingLoadedSymbols()
    {
        var loadStarted = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);

        using var viewModel = CreateViewModel(
            getConfiguredSymbolsAsync: async ct =>
            {
                loadStarted.SetResult(ct);
                await Task.Delay(Timeout.InfiniteTimeSpan, ct);
                return Array.Empty<SymbolConfigDto>();
            });
        AddSymbol(viewModel, "SPY", exchange: "SMART", trades: true, depth: false);
        viewModel.ApplyFilters();

        var activationTask = viewModel.ActivateAsync();
        var activeLoadToken = await loadStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        viewModel.Deactivate();

        await WaitForConditionAsync(() => activeLoadToken.IsCancellationRequested);
        await activationTask.WaitAsync(TimeSpan.FromSeconds(5));

        activeLoadToken.IsCancellationRequested.Should().BeTrue();
        viewModel.IsActive.Should().BeFalse();
        viewModel.Symbols.Should().ContainSingle(symbol => symbol.Symbol == "SPY");
        viewModel.FilteredSymbols.Should().ContainSingle(symbol => symbol.Symbol == "SPY");
        viewModel.VisibleSymbolScopeText.Should().Be("1 configured symbols");
    }

    private static async Task WaitForConditionAsync(Func<bool> predicate)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!predicate())
        {
            await Task.Delay(25, timeout.Token);
        }
    }

    [Fact]
    public void ApplyFilters_WithNoConfiguredSymbols_ShowsSetupGuidance()
    {
        using var viewModel = CreateViewModel();

        viewModel.ApplyFilters();

        viewModel.FilteredSymbols.Should().BeEmpty();
        viewModel.IsSymbolsEmptyStateVisible.Should().BeTrue();
        viewModel.HasVisibleSymbols.Should().BeFalse();
        viewModel.HasActiveFilters.Should().BeFalse();
        viewModel.ClearFiltersCommand.CanExecute(null).Should().BeFalse();
        viewModel.VisibleSymbolScopeText.Should().Be("No configured symbols");
        viewModel.SymbolsEmptyStateTitle.Should().Be("No symbols configured yet");
        viewModel.SymbolsEmptyStateDetail.Should().Contain("Add a symbol");
    }

    [Fact]
    public void SearchMiss_ShowsFilterRecoveryAndClearCommandRestoresRows()
    {
        using var viewModel = CreateViewModel();
        AddSymbol(viewModel, "SPY", exchange: "SMART", trades: true, depth: false);
        AddSymbol(viewModel, "QQQ", exchange: "NASDAQ", trades: true, depth: true);
        viewModel.ApplyFilters();

        viewModel.SearchText = "no-match";

        viewModel.FilteredSymbols.Should().BeEmpty();
        viewModel.IsSymbolsEmptyStateVisible.Should().BeTrue();
        viewModel.HasActiveFilters.Should().BeTrue();
        viewModel.ClearFiltersCommand.CanExecute(null).Should().BeTrue();
        viewModel.VisibleSymbolScopeText.Should().Be("0 visible of 2 configured symbols");
        viewModel.SymbolsEmptyStateTitle.Should().Be("No symbols match the current filters");
        viewModel.SymbolsEmptyStateDetail.Should().Contain("Clear filters");

        viewModel.ClearFiltersCommand.Execute(null);

        viewModel.SearchText.Should().BeEmpty();
        viewModel.SelectedSubscriptionFilter.Should().Be("All");
        viewModel.SelectedExchangeFilter.Should().Be("All");
        viewModel.FilteredSymbols.Should().HaveCount(2);
        viewModel.IsSymbolsEmptyStateVisible.Should().BeFalse();
        viewModel.ClearFiltersCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public void SubscriptionAndExchangeFilters_AreOwnedByViewModelState()
    {
        using var viewModel = CreateViewModel();
        AddSymbol(viewModel, "AAPL", exchange: "SMART", trades: true, depth: false);
        AddSymbol(viewModel, "MSFT", exchange: "NASDAQ", trades: true, depth: true);
        AddSymbol(viewModel, "DIA", exchange: "NYSE", trades: false, depth: true);
        viewModel.ApplyFilters();

        viewModel.SelectedSubscriptionFilter = "Depth";
        viewModel.SelectedExchangeFilter = "NASDAQ";

        viewModel.FilteredSymbols.Should().ContainSingle(item => item.Symbol == "MSFT");
        viewModel.VisibleSymbolScopeText.Should().Be("1 visible of 3 configured symbols");
        viewModel.IsSymbolsEmptyStateVisible.Should().BeFalse();
        viewModel.HasActiveFilters.Should().BeTrue();
    }

    [Fact]
    public void SelectionState_EnablesBulkActionsAndRefreshesVisibleRows()
    {
        using var viewModel = CreateViewModel();
        AddSymbol(viewModel, "SPY", exchange: "SMART", trades: false, depth: true);
        AddSymbol(viewModel, "QQQ", exchange: "NASDAQ", trades: false, depth: true);
        viewModel.SelectedSubscriptionFilter = "Trades";
        viewModel.FilteredSymbols.Should().BeEmpty();

        viewModel.ClearFiltersCommand.Execute(null);
        viewModel.FilteredSymbols[0].IsSelected = true;
        viewModel.UpdateSelectionCount();

        viewModel.CanBulkAction.Should().BeTrue();
        viewModel.SelectionCountText.Should().Be("1 selected");

        viewModel.BulkEnableTrades();

        viewModel.FilteredSymbols.Should().HaveCount(2);
        viewModel.FilteredSymbols[0].SubscribeTrades.Should().BeTrue();
        viewModel.VisibleSymbolScopeText.Should().Be("2 configured symbols");
    }

    [Fact]
    public async Task SelectedSymbolTicker_LoadsSecurityMasterStatusThroughRemoteClient()
    {
        var securityId = Guid.NewGuid();
        var remoteClient = new FakeRemoteWorkstationClient
        {
            Securities =
            [
                new SecurityMasterWorkstationDto(
                    securityId,
                    "Apple Inc.",
                    Meridian.Contracts.SecurityMaster.SecurityStatusDto.Active,
                    new SecurityClassificationSummaryDto("Equity", null, "Ticker", "AAPL"),
                    new SecurityEconomicDefinitionSummaryDto("USD", 1, DateTimeOffset.UtcNow, null))
            ]
        };
        using var viewModel = CreateViewModel(remoteClient: remoteClient);

        viewModel.SelectedSymbolTicker = "AAPL";

        await WaitForConditionAsync(() => remoteClient.LastGetEndpoint is not null);

        remoteClient.LastGetEndpoint.Should().StartWith(UiApiRoutes.WorkstationSecurityMasterSearch);
        remoteClient.LastGetEndpoint.Should().Contain("query=AAPL");
        viewModel.SelectedSymbolSecurityId.Should().Be(securityId);
        viewModel.CanViewInSecurityMaster.Should().BeTrue();
        viewModel.CanAddToSecurityMaster.Should().BeFalse();
    }

    [Fact]
    public async Task SelectedSymbolTicker_WhenRemoteLookupFails_AllowsSecurityMasterCreate()
    {
        var remoteClient = new FakeRemoteWorkstationClient { IsSuccess = false };
        using var viewModel = CreateViewModel(remoteClient: remoteClient);

        viewModel.SelectedSymbolTicker = "MSFT";

        await WaitForConditionAsync(() => remoteClient.LastGetEndpoint is not null);

        viewModel.SelectedSymbolSecurityId.Should().BeNull();
        viewModel.CanViewInSecurityMaster.Should().BeFalse();
        viewModel.CanAddToSecurityMaster.Should().BeTrue();
    }

    [Fact]
    public void SymbolsPageSource_BindsFilterAndEmptyStateThroughViewModel()
    {
        var xaml = File.ReadAllText(RunMatUiAutomationFacade.GetRepoFilePath(@"src\Meridian.Wpf\Views\SymbolsPage.xaml"));
        var codeBehind = File.ReadAllText(RunMatUiAutomationFacade.GetRepoFilePath(@"src\Meridian.Wpf\Views\SymbolsPage.xaml.cs"));
        var viewModel = File.ReadAllText(RunMatUiAutomationFacade.GetRepoFilePath(@"src\Meridian.Wpf\ViewModels\SymbolsPageViewModel.cs"));

        xaml.Should().Contain("AutomationProperties.AutomationId=\"SymbolsSearchBox\"");
        xaml.Should().Contain("Text=\"{Binding SearchText, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}\"");
        xaml.Should().Contain("{Binding SelectedSubscriptionFilter, Mode=TwoWay");
        xaml.Should().Contain("{Binding SelectedExchangeFilter, Mode=TwoWay");
        xaml.Should().Contain("Command=\"{Binding ClearFiltersCommand}\"");
        xaml.Should().Contain("SymbolsEmptyStatePanel");
        xaml.Should().Contain("{Binding SymbolsEmptyStateTitle}");
        xaml.Should().Contain("{Binding SymbolsEmptyStateDetail}");
        xaml.Should().Contain("{Binding IsSymbolsEmptyStateVisible");
        xaml.Should().Contain("{Binding CanBulkAction}");
        xaml.Should().Contain("{Binding SelectionCountText}");
        xaml.Should().Contain("{Binding VisibleSymbolScopeText}");
        xaml.Should().NotContain("TextChanged=\"SymbolSearch_TextChanged\"");
        xaml.Should().NotContain("SelectionChanged=\"Filter_Changed\"");
        xaml.Should().NotContain("Click=\"ClearFilters_Click\"");
        xaml.Should().NotContain("IsEnabled=\"False\"");

        codeBehind.Should().NotContain("SymbolSearch_TextChanged");
        codeBehind.Should().NotContain("Filter_Changed");
        codeBehind.Should().NotContain("ClearFilters_Click");
        codeBehind.Should().NotContain("CallApplyFilters");
        codeBehind.Should().Contain("await _vm.ActivateAsync()");
        codeBehind.Should().Contain("_vm.Deactivate()");
        codeBehind.Should().NotContain("_vm.StartAsync()");
        codeBehind.Should().NotContain("_vm.Stop()");
        codeBehind.Should().Contain("_vm.SearchText");
        codeBehind.Should().Contain("_vm.SelectedSubscriptionFilter");
        codeBehind.Should().Contain("_vm.SelectedExchangeFilter");

        viewModel.Should().Contain("IPageActivationLifetime");
        viewModel.Should().Contain("public bool IsActive");
        viewModel.Should().Contain("public CancellationToken ActivationToken");
    }

    [Fact]
    public void SymbolsPageSection_ShouldOwnCollectionsFiltersAndSelectionWithAdapterBindings()
    {
        var sectionSource = File.ReadAllText(RunMatUiAutomationFacade.GetRepoFilePath(@"src\Meridian.Wpf\ViewModels\SymbolsPageViewModel.Sections.cs"));
        var viewModelSource = File.ReadAllText(RunMatUiAutomationFacade.GetRepoFilePath(@"src\Meridian.Wpf\ViewModels\SymbolsPageViewModel.cs"));

        sectionSource.Should().Contain("public ObservableCollection<SymbolViewModel> Symbols");
        sectionSource.Should().Contain("public ObservableCollection<SymbolViewModel> FilteredSymbols");
        sectionSource.Should().Contain("public ObservableCollection<WatchlistInfo> Watchlists");
        sectionSource.Should().Contain("public string SearchText");
        sectionSource.Should().Contain("public string SelectedSubscriptionFilter");
        sectionSource.Should().Contain("public SymbolViewModel? SelectedItem");
        viewModelSource.Should().Contain("public SymbolsPageSectionViewModel SymbolsSection");
        viewModelSource.Should().Contain("public ObservableCollection<SymbolViewModel> Symbols => SymbolsSection.Symbols");
        viewModelSource.Should().Contain("get => SymbolsSection.SearchText");
        viewModelSource.Should().Contain("get => SymbolsSection.SelectedItem");
        viewModelSource.Should().NotContain("_searchText");
        viewModelSource.Should().NotContain("_selectedSubscriptionFilter");
        viewModelSource.Should().NotContain("_selectedExchangeFilter");
        viewModelSource.Should().NotContain("_selectedItem");
    }

    private static SymbolsPageViewModel CreateViewModel(
        IRemoteWorkstationClient? remoteClient = null,
        Func<CancellationToken, Task<SymbolConfigDto[]>>? getConfiguredSymbolsAsync = null,
        Func<SymbolConfigDto[], CancellationToken, Task>? saveSymbolsAsync = null,
        Func<CancellationToken, Task<IReadOnlyList<WpfServices.Watchlist>>>? getAllWatchlistsAsync = null) =>
        new(
            WpfServices.ConfigService.Instance,
            WpfServices.WatchlistService.Instance,
            WpfServices.LoggingService.Instance,
            WpfServices.NotificationService.Instance,
            WpfServices.NavigationService.Instance,
            SymbolManagementService.Instance,
            CommandPaletteService.Instance,
            remoteClient,
            getConfiguredSymbolsAsync,
            saveSymbolsAsync,
            getAllWatchlistsAsync);

    private static void AddSymbol(
        SymbolsPageViewModel viewModel,
        string symbol,
        string exchange,
        bool trades,
        bool depth)
    {
        viewModel.Symbols.Add(new SymbolViewModel
        {
            Symbol = symbol,
            Exchange = exchange,
            SubscribeTrades = trades,
            SubscribeDepth = depth,
            DepthLevels = 10
        });
    }

    private sealed class FakeRemoteWorkstationClient : IRemoteWorkstationClient
    {
        public string BaseUrl { get; private set; } = "http://localhost:8080";
        public string? LastGetEndpoint { get; private set; }
        public bool IsSuccess { get; init; } = true;
        public SecurityMasterWorkstationDto[]? Securities { get; init; }

        public void Configure(string serviceUrl, int timeoutSeconds = 30, int backfillTimeoutMinutes = 60)
            => BaseUrl = serviceUrl;

        public Task<bool> CheckHealthEndpointAsync(CancellationToken ct = default)
            => Task.FromResult(true);

        public Task<ServiceHealthResult> CheckHealthAsync(CancellationToken ct = default)
            => Task.FromResult(new ServiceHealthResult { IsReachable = true, IsConnected = true });

        public Task<StatusResponse?> GetStatusAsync(CancellationToken ct = default)
            => Task.FromResult<StatusResponse?>(null);

        public Task<ApiResponse<StatusResponse>> GetStatusWithResponseAsync(CancellationToken ct = default)
            => Task.FromResult(new ApiResponse<StatusResponse> { Success = true });

        public Task<T?> GetAsync<T>(string endpoint, CancellationToken ct = default) where T : class
            => Task.FromResult<T?>(null);

        public Task<ApiResponse<T>> GetWithResponseAsync<T>(string endpoint, CancellationToken ct = default)
            where T : class
        {
            LastGetEndpoint = endpoint;
            if (!IsSuccess)
            {
                return Task.FromResult(new ApiResponse<T> { Success = false });
            }

            return Securities is T typed
                ? Task.FromResult(ApiResponse<T>.Ok(typed))
                : Task.FromResult(new ApiResponse<T> { Success = false });
        }

        public Task<T?> PostAsync<T>(string endpoint, object? body = null, CancellationToken ct = default)
            where T : class
            => Task.FromResult<T?>(null);

        public Task<ApiResponse<T>> PostWithResponseAsync<T>(
            string endpoint,
            object? body = null,
            CancellationToken ct = default) where T : class
            => Task.FromResult(new ApiResponse<T> { Success = false });

        public Task<ApiResponse<T>> DeleteWithResponseAsync<T>(string endpoint, CancellationToken ct = default)
            where T : class
            => Task.FromResult(new ApiResponse<T> { Success = false });

        public void Dispose()
        {
        }
    }
}
