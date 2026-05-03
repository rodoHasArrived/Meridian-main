using Meridian.Ui.Services;
using Meridian.Wpf.Models;
using Meridian.Wpf.Services;
using Meridian.Wpf.Tests.Support;
using Meridian.Wpf.ViewModels;
using WpfServices = Meridian.Wpf.Services;

namespace Meridian.Wpf.Tests.ViewModels;

public sealed class SymbolsPageViewModelTests
{
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
    public void SymbolsPageSource_BindsFilterAndEmptyStateThroughViewModel()
    {
        var xaml = File.ReadAllText(RunMatUiAutomationFacade.GetRepoFilePath(@"src\Meridian.Wpf\Views\SymbolsPage.xaml"));
        var codeBehind = File.ReadAllText(RunMatUiAutomationFacade.GetRepoFilePath(@"src\Meridian.Wpf\Views\SymbolsPage.xaml.cs"));

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
        codeBehind.Should().Contain("_vm.SearchText");
        codeBehind.Should().Contain("_vm.SelectedSubscriptionFilter");
        codeBehind.Should().Contain("_vm.SelectedExchangeFilter");
    }

    private static SymbolsPageViewModel CreateViewModel() =>
        new(
            WpfServices.ConfigService.Instance,
            WpfServices.WatchlistService.Instance,
            WpfServices.LoggingService.Instance,
            WpfServices.NotificationService.Instance,
            WpfServices.NavigationService.Instance,
            SymbolManagementService.Instance,
            CommandPaletteService.Instance);

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
}
