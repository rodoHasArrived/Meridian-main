using Meridian.Wpf.Tests.Support;
using Meridian.Wpf.Models;
using Meridian.Wpf.ViewModels;
using WpfServices = Meridian.Wpf.Services;

namespace Meridian.Wpf.Tests.ViewModels;

public sealed class OrderBookViewModelTests
{
    [Fact]
    public void SelectorState_DefaultsAndUpdatesPostureFromViewModel()
    {
        WpfTestThread.Run(() =>
        {
            using var viewModel = CreateViewModel();

            viewModel.DepthLevelOptions.Should().Equal(5, 10, 20, 50);
            viewModel.SelectedDepthLevels.Should().Be(10);
            viewModel.SelectedSymbol.Should().BeEmpty();

            viewModel.SelectedDepthLevels = 20;
            viewModel.SelectedSymbol = " AAPL ";

            viewModel.SelectedSymbol.Should().Be("AAPL");
            viewModel.SelectedDepthLevels.Should().Be(20);
            viewModel.OrderFlowPostureTitle.Should().Be("Waiting for full depth");
            viewModel.OrderFlowPostureScopeText.Should().Be("AAPL - 20 levels - 0 bid / 0 ask rows");
            viewModel.OrderFlowPostureActionText.Should().Be("Check connection");
        });
    }

    [Fact]
    public void SelectedSymbol_WhenChanged_ClearsRetainedLadderAndTape()
    {
        WpfTestThread.Run(() =>
        {
            using var viewModel = CreateViewModel();
            viewModel.Bids.Add(new OrderBookDisplayLevel { Price = "100.00", Size = "10", Total = "10" });
            viewModel.Asks.Add(new OrderBookDisplayLevel { Price = "100.01", Size = "12", Total = "12" });
            viewModel.RecentTrades.Add(new RecentTradeModel { Time = "09:30:00", Price = "100.00", Size = "10" });

            viewModel.SelectedSymbol = "MSFT";

            viewModel.Bids.Should().BeEmpty();
            viewModel.Asks.Should().BeEmpty();
            viewModel.RecentTrades.Should().BeEmpty();
            viewModel.NoDataVisible.Should().BeTrue();
            viewModel.NoTradesVisible.Should().BeTrue();
        });
    }

    [Fact]
    public void BuildOrderFlowPosture_WithoutSymbol_AsksOperatorToChooseSymbol()
    {
        var posture = OrderBookViewModel.BuildOrderFlowPosture(
            selectedSymbol: "",
            depthLevels: 10,
            connectionStatus: "Connected",
            bidCount: 0,
            askCount: 0,
            recentTradeCount: 0,
            spread: null,
            imbalancePercent: null,
            cumulativeDeltaText: "--");

        posture.Title.Should().Be("Select a symbol");
        posture.Detail.Should().Contain("Choose a symbol");
        posture.ScopeText.Should().Be("No symbol - 10 levels");
        posture.ActionText.Should().Be("Choose symbol");
    }

    [Fact]
    public void BuildOrderFlowPosture_WhenDisconnectedWithoutDepth_TargetsConnection()
    {
        var posture = OrderBookViewModel.BuildOrderFlowPosture(
            selectedSymbol: "SPY",
            depthLevels: 20,
            connectionStatus: "Disconnected",
            bidCount: 0,
            askCount: 0,
            recentTradeCount: 0,
            spread: null,
            imbalancePercent: null,
            cumulativeDeltaText: "--");

        posture.Title.Should().Be("Waiting for full depth");
        posture.Detail.Should().Contain("Disconnected");
        posture.ScopeText.Should().Be("SPY - 20 levels - 0 bid / 0 ask rows");
        posture.ActionText.Should().Be("Check connection");
    }

    [Fact]
    public void BuildOrderFlowPosture_WithDepthButNoTape_TargetsTradeTape()
    {
        var posture = OrderBookViewModel.BuildOrderFlowPosture(
            selectedSymbol: "AAPL",
            depthLevels: 10,
            connectionStatus: "Connected",
            bidCount: 10,
            askCount: 10,
            recentTradeCount: 0,
            spread: 0.02m,
            imbalancePercent: 3.4m,
            cumulativeDeltaText: "--");

        posture.Title.Should().Be("Depth live, tape pending");
        posture.Detail.Should().Contain("20 ladder rows");
        posture.ActionText.Should().Be("Verify trade tape");
    }

    [Fact]
    public void BuildOrderFlowPosture_WithBidPressure_SurfacesLiquidityWall()
    {
        var posture = OrderBookViewModel.BuildOrderFlowPosture(
            selectedSymbol: "MSFT",
            depthLevels: 10,
            connectionStatus: "Connected",
            bidCount: 10,
            askCount: 10,
            recentTradeCount: 8,
            spread: 0.04m,
            imbalancePercent: 27.5m,
            cumulativeDeltaText: "+4.2K");

        posture.Title.Should().Be("Bid-side pressure building");
        posture.Detail.Should().Contain("27.5%");
        posture.Detail.Should().Contain("+4.2K cumulative delta");
        posture.ActionText.Should().Be("Monitor liquidity wall");
    }

    [Fact]
    public void BuildOrderFlowPosture_WithBalancedDepth_IsReady()
    {
        var posture = OrderBookViewModel.BuildOrderFlowPosture(
            selectedSymbol: "QQQ",
            depthLevels: 5,
            connectionStatus: "Connected",
            bidCount: 5,
            askCount: 5,
            recentTradeCount: 15,
            spread: 0.01m,
            imbalancePercent: -4.1m,
            cumulativeDeltaText: "-700");

        posture.Title.Should().Be("Order flow ready");
        posture.Detail.Should().Contain("spread is 0.01");
        posture.Detail.Should().Contain("imbalance is -4.1%");
        posture.ActionText.Should().Be("Monitor heatmap and tape");
    }

    [Fact]
    public void OrderBookPageSource_BindsOrderFlowPosture()
    {
        var xaml = File.ReadAllText(RunMatUiAutomationFacade.GetRepoFilePath(@"src\Meridian.Wpf\Views\OrderBookPage.xaml"));
        var codeBehind = File.ReadAllText(RunMatUiAutomationFacade.GetRepoFilePath(@"src\Meridian.Wpf\Views\OrderBookPage.xaml.cs"));
        var viewModel = File.ReadAllText(RunMatUiAutomationFacade.GetRepoFilePath(@"src\Meridian.Wpf\ViewModels\OrderBookViewModel.cs"));

        xaml.Should().Contain("OrderBookPostureCard");
        xaml.Should().Contain("OrderBookPostureTitleText");
        xaml.Should().Contain("OrderBookPostureDetailText");
        xaml.Should().Contain("OrderBookPostureScopeChip");
        xaml.Should().Contain("OrderBookPostureHandoffPanel");
        xaml.Should().Contain("OrderBookPostureActionText");
        xaml.Should().Contain("{Binding OrderFlowPostureTitle}");
        xaml.Should().Contain("{Binding OrderFlowPostureDetail}");
        xaml.Should().Contain("{Binding OrderFlowPostureScopeText}");
        xaml.Should().Contain("{Binding OrderFlowPostureActionText}");
        xaml.Should().Contain("OrderBookSymbolSelector");
        xaml.Should().Contain("OrderBookDepthSelector");
        xaml.Should().Contain("ItemsSource=\"{Binding AvailableSymbols}\"");
        xaml.Should().Contain("SelectedItem=\"{Binding SelectedSymbol, Mode=TwoWay}\"");
        xaml.Should().Contain("ItemsSource=\"{Binding DepthLevelOptions}\"");
        xaml.Should().Contain("SelectedItem=\"{Binding SelectedDepthLevels, Mode=TwoWay}\"");
        xaml.Should().NotContain("SelectionChanged=\"Symbol_SelectionChanged\"");
        xaml.Should().NotContain("SelectionChanged=\"Levels_SelectionChanged\"");
        codeBehind.Should().NotContain("FirstSymbolAutoSelected");
        codeBehind.Should().NotContain("Symbol_SelectionChanged");
        codeBehind.Should().NotContain("Levels_SelectionChanged");
        viewModel.Should().Contain("public string SelectedSymbol");
        viewModel.Should().Contain("public int SelectedDepthLevels");
        viewModel.Should().Contain("public IReadOnlyList<int> DepthLevelOptions");
    }

    private static OrderBookViewModel CreateViewModel()
        => new(
            WpfServices.StatusService.Instance,
            WpfServices.ConnectionService.Instance,
            WpfServices.LoggingService.Instance);
}
