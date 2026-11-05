using FluentAssertions;
using Meridian.Contracts.SecurityMaster;
using Meridian.Execution.Margin;
using Meridian.Execution.Models;
using Meridian.Execution.Sdk;
using Meridian.Execution.Services;
using Meridian.Ledger;
using OrderStatus = Meridian.Execution.Sdk.OrderStatus;

namespace Meridian.Tests.Execution;

/// <summary>
/// Paper economics for contracts: an option pays its premium in full, so one $2.50 contract on a
/// 100 multiplier moves $250 of cash — the same figure the risk layer sizes the order at.
/// Futures deliberately stay at 1x until the book has a variation-margin model.
/// </summary>
public sealed class PaperTradingPortfolioMultiplierTests
{
    private const string Option = "AAPL261218C00250000";

    [Fact]
    public void OptionBuy_PaysPremiumTimesMultiplier_AndKeepsPerUnitCostBasis()
    {
        var ledger = new Meridian.Ledger.Ledger();
        var portfolio = new PaperTradingPortfolio(100_000m, ledger);

        portfolio.ApplyFill(OptionFill(OrderSide.Buy, qty: 1m, price: 2.50m));

        portfolio.Cash.Should().Be(99_750m);
        var position = portfolio.Positions[Option];
        position.AverageCostBasis.Should().Be(2.50m, "cost basis stays a per-unit quote that readers multiply themselves");
        position.ContractMultiplier.Should().Be(100m);

        var buy = ledger.Journal.Single(e => e.Description.StartsWith("Buy"));
        buy.Lines.Single(l => l.Account == LedgerAccounts.Securities(Option)).Debit.Should().Be(250m);
        buy.Lines.Single(l => l.Account == LedgerAccounts.Cash).Credit.Should().Be(250m);
    }

    [Fact]
    public void OptionMark_ScalesMarketValueUnrealisedPnlAndPortfolioValue()
    {
        var portfolio = new PaperTradingPortfolio(100_000m);
        portfolio.ApplyFill(OptionFill(OrderSide.Buy, qty: 1m, price: 2.50m));

        portfolio.UpdateMarketPrice(Option, 3.00m);

        portfolio.UnrealisedPnl.Should().Be(50m);
        portfolio.PortfolioValue.Should().Be(100_050m);
        portfolio.Positions[Option].UnrealizedPnl.Should().Be(50m);
    }

    [Fact]
    public void OptionSell_RealisesPnlInDollars_AcrossFifoLots()
    {
        var portfolio = new PaperTradingPortfolio(100_000m);
        portfolio.ApplyFill(OptionFill(OrderSide.Buy, qty: 1m, price: 2.00m));
        portfolio.ApplyFill(OptionFill(OrderSide.Buy, qty: 1m, price: 3.00m));
        portfolio.Positions[Option].AverageCostBasis.Should().Be(2.50m);

        portfolio.ApplyFill(OptionFill(OrderSide.Sell, qty: 1m, price: 3.50m));

        // FIFO relieves the $200 lot: $350 proceeds - $200 basis.
        portfolio.RealisedPnl.Should().Be(150m);
        portfolio.Cash.Should().Be(100_000m - 200m - 300m + 350m);
        portfolio.Positions[Option].AverageCostBasis.Should().Be(3.00m);

        portfolio.ApplyFill(OptionFill(OrderSide.Sell, qty: 1m, price: 3.50m));

        portfolio.RealisedPnl.Should().Be(200m);
        portfolio.Cash.Should().Be(100_200m);
        portfolio.Positions.Should().BeEmpty();
    }

    [Fact]
    public void OptionShortThenCover_ScalesProceedsAndRealisedPnl()
    {
        var portfolio = new PaperTradingPortfolio(100_000m);

        portfolio.ApplyFill(OptionFill(OrderSide.Sell, qty: 2m, price: 4.00m));
        portfolio.Cash.Should().Be(100_800m);
        portfolio.UpdateMarketPrice(Option, 4.00m);
        portfolio.PortfolioValue.Should().Be(100_000m, "the $800 short liability offsets the $800 received");

        portfolio.ApplyFill(OptionFill(OrderSide.Buy, qty: 2m, price: 1.00m));

        portfolio.RealisedPnl.Should().Be(600m);
        portfolio.Cash.Should().Be(100_600m);
    }

    [Fact]
    public void OptionOnRegTAccount_ScalesLoanAndCollateral()
    {
        var portfolio = new PaperTradingPortfolio(
        [
            new AccountDefinition("margin-1", "Reg T Margin", AccountKind.Brokerage, 100_000m,
                MarginType: MarginAccountType.RegT),
        ]);

        portfolio.ApplyFill("margin-1", OptionFill(OrderSide.Buy, qty: 10m, price: 2.00m));

        // $2,000 premium on 50 % initial margin.
        portfolio.Cash.Should().Be(99_000m);
        portfolio.MarginBalance.Should().Be(1_000m);
    }

    [Fact]
    public void WarrantFill_PaysPriceTimesMultiplier()
    {
        // Warrants are listed premium contracts too: the catalog marks them with the same
        // UsesOccOptionSymbols fact as options, so the book pays price x quantity x multiplier.
        var portfolio = new PaperTradingPortfolio(100_000m);
        var report = Fill("XYZ.WS", OrderSide.Buy, qty: 1m, price: 2.00m) with
        {
            AssetClass = "Warrant",
            LotUnitKind = SecurityLotUnitKind.Contracts,
            LotMultiplier = 10m
        };

        portfolio.ApplyFill(report);

        portfolio.Cash.Should().Be(99_980m);
    }

    [Fact]
    public void UnclassifiedReport_WithOmsMultiplier_PaysTheMultipliedPremium()
    {
        // The order-management path: no asset class on the report, only the multiplier the OMS
        // resolved from the order's option contract.
        var portfolio = new PaperTradingPortfolio(100_000m);
        var report = Fill("AAPL", OrderSide.Buy, qty: 3m, price: 1.20m);

        portfolio.ApplyFill(report, ownerAccountId: null, contractMultiplier: 100m);

        portfolio.Cash.Should().Be(99_640m);
    }

    [Fact]
    public void OptionIdentityWithoutStampedMultiplier_UsesTheSuppliedMultiplier()
    {
        var portfolio = new PaperTradingPortfolio(100_000m);
        var report = Fill("AAPL", OrderSide.Buy, qty: 1m, price: 5m) with
        {
            OptionContract = new OptionContractIdentity { UnderlyingSymbol = "AAPL", Right = "C", StrikePrice = 250m }
        };

        portfolio.ApplyFill(report, ownerAccountId: null, contractMultiplier: 100m);

        portfolio.Cash.Should().Be(99_500m);
    }

    [Fact]
    public void FutureFill_KeepsOneTimesCash_UntilVariationMarginIsModelled()
    {
        var portfolio = new PaperTradingPortfolio(1_000_000m);
        var report = Fill("ESZ6", OrderSide.Buy, qty: 1m, price: 6_000m) with
        {
            AssetClass = "Future",
            LotUnitKind = SecurityLotUnitKind.Contracts,
            LotMultiplier = 50m
        };

        portfolio.ApplyFill(report);
        portfolio.UpdateMarketPrice("ESZ6", 6_010m);

        portfolio.Cash.Should().Be(994_000m, "futures cash is unchanged by this fix (deferred)");
        portfolio.UnrealisedPnl.Should().Be(10m);
        portfolio.Positions["ESZ6"].ContractMultiplier.Should().Be(1m,
            "the aggregate view reports the multiplier its P&L was computed with");
        portfolio.GetAccount(PaperTradingPortfolio.DefaultAccountId)!.Positions["ESZ6"].ContractMultiplier
            .Should().Be(50m, "the per-account exposure view still carries the contract multiplier");
    }

    [Fact]
    public void FaceValueFill_IgnoresAStrayMultiplier()
    {
        var portfolio = new PaperTradingPortfolio(1_000_000m);
        var report = Fill("T 4.5 2036", OrderSide.Buy, qty: 100_000m, price: 101.25m) with
        {
            UsesFaceValuePercentageOfPar = true
        };

        portfolio.ApplyFill(report, ownerAccountId: null, contractMultiplier: 1_000m);

        portfolio.Cash.Should().Be(1_000_000m - 101_250m);
    }

    [Fact]
    public void MixingMultipliersOnOneSymbol_Throws()
    {
        var portfolio = new PaperTradingPortfolio(100_000m);
        portfolio.ApplyFill(OptionFill(OrderSide.Buy, qty: 1m, price: 2.50m));

        var act = () => portfolio.ApplyFill(OptionFill(OrderSide.Buy, qty: 1m, price: 2.50m, multiplier: 10m));

        act.Should().Throw<InvalidOperationException>().WithMessage("*multiplier*");
    }

    [Fact]
    public void PositionTracker_RecoversThePerUnitMarkForOptions()
    {
        var portfolio = new PaperTradingPortfolio(100_000m);
        portfolio.ApplyFill(OptionFill(OrderSide.Buy, qty: 4m, price: 2.50m));
        portfolio.UpdateMarketPrice(Option, 3.00m);
        var tracker = new PortfolioStatePositionTracker(portfolio);

        var state = tracker.GetPosition(Option);

        state.MarketPrice.Should().Be(3.00m);
        state.AverageCostBasis.Should().Be(2.50m);
        tracker.GetPortfolioValue().Should().Be(100_200m);
    }

    private static ExecutionReport OptionFill(OrderSide side, decimal qty, decimal price, decimal multiplier = 100m) =>
        Fill(Option, side, qty, price) with
        {
            AssetClass = "Option",
            LotUnitKind = SecurityLotUnitKind.Contracts,
            LotMultiplier = multiplier
        };

    private static ExecutionReport Fill(string symbol, OrderSide side, decimal qty, decimal price) => new()
    {
        OrderId = Guid.NewGuid().ToString("N"),
        ReportType = ExecutionReportType.Fill,
        Symbol = symbol,
        Side = side,
        OrderStatus = OrderStatus.Filled,
        OrderQuantity = qty,
        FilledQuantity = qty,
        FillPrice = price,
        Commission = 0m,
        Timestamp = DateTimeOffset.UtcNow,
    };
}
