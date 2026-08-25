using FluentAssertions;
using Meridian.Backtesting.Engine;
using Meridian.Backtesting.Metrics;
using Meridian.Backtesting.Portfolio;

namespace Meridian.Backtesting.Tests;

/// <summary>
/// Regression coverage for the Milestone 1 accounting and portfolio invariants.
/// </summary>
public sealed class MilestoneOneAccountingTests
{
    [Fact]
    public void BuildTradeTickets_AssetEvent_PreservesBrokerageAccount()
    {
        var cashFlow = new AssetEventCashFlow(
            DateTimeOffset.UtcNow,
            10m,
            "SPY",
            AssetEventType.Dividend,
            10L,
            1m)
        {
            AccountId = "broker-a"
        };

        var ticket = BacktestEngine.BuildTradeTickets([cashFlow]).Should().ContainSingle().Subject;

        ticket.AccountId.Should().Be("broker-a");
    }

    [Fact]
    public void Compute_ExplicitAccounts_UsesTheirCombinedOpeningCapital()
    {
        var from = new DateOnly(2023, 1, 1);
        var accounts = new[]
        {
            new FinancialAccount("broker-a", "Broker A", FinancialAccountKind.Brokerage, InitialCash: 60_000m),
            new FinancialAccount("broker-b", "Broker B", FinancialAccountKind.Brokerage, InitialCash: 40_000m),
        };
        var request = new BacktestRequest(
            from,
            from.AddYears(1),
            InitialCash: 1m,
            Accounts: accounts,
            DefaultBrokerageAccountId: "broker-a",
            RiskFreeRate: 0d);

        var metrics = BacktestMetricsEngine.Compute(
            [Snapshot(from, 100_000m), Snapshot(from.AddYears(1), 110_000m)],
            [],
            [],
            request);

        metrics.InitialCapital.Should().Be(100_000m);
        metrics.NetPnl.Should().Be(10_000m);
        metrics.TotalReturn.Should().Be(0.10m);
    }

    [Fact]
    public void Compute_Xirr_ExcludesInternalTradeAndSettlementFlows()
    {
        var from = new DateOnly(2023, 1, 1);
        var to = from.AddYears(1).AddDays(-1);
        var request = new BacktestRequest(from, to, InitialCash: 100m, RiskFreeRate: 0d);
        var at = new DateTimeOffset(from.AddDays(100).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var internalFlows = new CashFlowEntry[]
        {
            new TradeCashFlow(at, -80m, "SPY", 1, 80m),
            new CommissionCashFlow(at, -1m, "SPY", Guid.NewGuid()),
            new DividendCashFlow(at.AddDays(20), 2m, "SPY", 1, 2m),
            new MarginInterestCashFlow(at.AddDays(30), -0.50m, 10m, 0.05),
        };

        var metrics = BacktestMetricsEngine.Compute(
            [Snapshot(from, 100m), Snapshot(to, 110m)],
            internalFlows,
            [],
            request);

        metrics.Xirr.Should().BeApproximately(0.10d, 0.000001d,
            "only investor opening capital and terminal value belong in money-weighted return");
    }

    [Fact]
    public void ProcessFill_ReturnsAuthoritativeCommission_AndSeparatesCashFlowTypes()
    {
        var portfolio = new SimulatedPortfolio(
            10_000m,
            new PerShareCommissionModel(perShare: 0.005m, minimumPerOrder: 1m),
            annualMarginRate: 0.05,
            annualShortRebateRate: 0.02);
        var proposed = new FillEvent(
            Guid.NewGuid(), Guid.NewGuid(), "SPY", 100L, 10m, 99m, DateTimeOffset.UtcNow);

        var accepted = portfolio.ProcessFill(proposed);
        var snapshot = portfolio.TakeSnapshot(DateTimeOffset.UtcNow, new DateOnly(2024, 1, 2));

        accepted.Commission.Should().Be(1m);
        accepted.AccountId.Should().Be(BacktestDefaults.DefaultBrokerageAccountId);
        snapshot.DayCashFlows.OfType<TradeCashFlow>().Should().ContainSingle()
            .Which.Amount.Should().Be(-1_000m);
        snapshot.DayCashFlows.OfType<CommissionCashFlow>().Should().ContainSingle()
            .Which.Amount.Should().Be(-1m);
        portfolio.Cash.Should().Be(8_999m);
    }

    [Fact]
    public void ProcessFill_RejectedFill_DoesNotConsumeCommissionAccumulator()
    {
        var account = new FinancialAccount(
            "cash-only",
            "Cash Only",
            FinancialAccountKind.Brokerage,
            InitialCash: 100m,
            Rules: new FinancialAccountRules(AllowMargin: false));
        var portfolio = new SimulatedPortfolio(
            [account],
            account.AccountId,
            new PerShareCommissionModel(perShare: 0.005m, minimumPerOrder: 1m));
        var orderId = Guid.NewGuid();

        var rejected = () => portfolio.ProcessFill(new FillEvent(
            Guid.NewGuid(), orderId, "SPY", 1L, 100m, 0m, DateTimeOffset.UtcNow, account.AccountId));
        rejected.Should().Throw<InvalidOperationException>().WithMessage("*margin borrowing*");

        var accepted = portfolio.ProcessFill(new FillEvent(
            Guid.NewGuid(), orderId, "SPY", 1L, 50m, 0m, DateTimeOffset.UtcNow, account.AccountId));

        accepted.Commission.Should().Be(1m,
            "a rejected quote must not make the next accepted slice look like a later slice");
        portfolio.Cash.Should().Be(49m);
    }

    [Fact]
    public void PerShareCommission_AccumulatesMinimumAndMaximumAcrossAcceptedSlices()
    {
        var model = new PerShareCommissionModel(
            perShare: 0.005m,
            minimumPerOrder: 1m,
            maximumPerOrder: 1.50m);
        var orderId = Guid.NewGuid();

        var first = model.Quote(orderId, "SPY", 100, 10m);
        model.Commit(first);
        var second = model.Quote(orderId, "SPY", 100, 10m);
        model.Commit(second);
        var third = model.Quote(orderId, "SPY", 100, 10m);
        model.Commit(third);
        var capped = model.Quote(orderId, "SPY", 100, 10m);

        first.Amount.Should().Be(1m);
        second.Amount.Should().Be(0m);
        third.Amount.Should().Be(0.50m);
        capped.Amount.Should().Be(0m);
    }

    [Fact]
    public void PerShareCommission_Release_DropsTerminalOrderState()
    {
        var model = new PerShareCommissionModel(perShare: 0.005m, minimumPerOrder: 1m);
        var orderId = Guid.NewGuid();
        var first = model.Quote(orderId, "SPY", 100, 10m);
        model.Commit(first);

        model.Release(orderId);
        var afterRelease = model.Quote(orderId, "SPY", 100, 10m);

        afterRelease.Amount.Should().Be(1m,
            "terminal-order state must no longer influence later accumulator lookups");
    }

    [Fact]
    public void BacktestContext_CancelOrder_ReleasesCommissionState()
    {
        var commission = new TrackingCommissionModel();
        var portfolio = new SimulatedPortfolio(10_000m, commission, 0.05, 0.02);
        var context = new BacktestContext(
            portfolio,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "SPY" },
            new BacktestLedger(),
            BacktestDefaults.DefaultBrokerageAccountId,
            commission);
        var orderId = context.PlaceMarketOrder("SPY", 1L);

        context.CancelOrder(orderId);

        commission.ReleasedOrderIds.Should().ContainSingle().Which.Should().Be(orderId);
    }

    [Fact]
    public void ProcessFill_FixedCommission_IsChargedOnceAcrossAcceptedSlices()
    {
        var portfolio = new SimulatedPortfolio(10_000m, new FixedCommissionModel(2m), 0.05, 0.02);
        var orderId = Guid.NewGuid();

        var first = portfolio.ProcessFill(new FillEvent(
            Guid.NewGuid(), orderId, "SPY", 10L, 100m, 99m, DateTimeOffset.UtcNow));
        var second = portfolio.ProcessFill(new FillEvent(
            Guid.NewGuid(), orderId, "SPY", 10L, 101m, 99m, DateTimeOffset.UtcNow.AddSeconds(1)));
        var snapshot = portfolio.TakeSnapshot(DateTimeOffset.UtcNow.AddMinutes(1), new DateOnly(2024, 1, 2));

        first.Commission.Should().Be(2m);
        second.Commission.Should().Be(0m);
        snapshot.DayCashFlows.OfType<CommissionCashFlow>().Should().ContainSingle()
            .Which.Amount.Should().Be(-2m);
    }

    [Fact]
    public void ProcessFillsAtomically_RejectedBatchMutatesNeitherPortfolioNorCommissionState()
    {
        var account = new FinancialAccount(
            "cash-only",
            "Cash Only",
            FinancialAccountKind.Brokerage,
            InitialCash: 100m,
            Rules: new FinancialAccountRules(AllowMargin: false));
        var portfolio = new SimulatedPortfolio(
            [account],
            account.AccountId,
            new FixedCommissionModel(2m));
        var orderId = Guid.NewGuid();
        var proposed = new FillEvent[]
        {
            new(Guid.NewGuid(), orderId, "SPY", 1L, 40m, 99m, DateTimeOffset.UtcNow, account.AccountId),
            new(Guid.NewGuid(), orderId, "SPY", 1L, 70m, 99m, DateTimeOffset.UtcNow.AddMilliseconds(1), account.AccountId),
        };

        var rejected = () => portfolio.ProcessFillsAtomically(proposed);

        rejected.Should().Throw<InvalidOperationException>().WithMessage("*margin borrowing*");
        portfolio.Cash.Should().Be(100m);
        portfolio.GetCurrentPositions().Should().BeEmpty();

        var laterAccepted = portfolio.ProcessFill(new FillEvent(
            Guid.NewGuid(), orderId, "SPY", 1L, 10m, 0m, DateTimeOffset.UtcNow.AddSeconds(1), account.AccountId));
        laterAccepted.Commission.Should().Be(2m,
            "the rejected atomic batch must not commit its provisional fee chain");
    }

    [Fact]
    public void ProcessFillsAtomically_ReturnsChainedAuthoritativeCommissions()
    {
        var portfolio = new SimulatedPortfolio(10_000m, new FixedCommissionModel(2m), 0.05, 0.02);
        var orderId = Guid.NewGuid();
        var proposed = new FillEvent[]
        {
            new(Guid.NewGuid(), orderId, "SPY", 10L, 100m, 99m, DateTimeOffset.UtcNow),
            new(Guid.NewGuid(), orderId, "SPY", 10L, 101m, 99m, DateTimeOffset.UtcNow.AddMilliseconds(1)),
        };

        var accepted = portfolio.ProcessFillsAtomically(proposed);

        accepted.Select(static fill => fill.Commission).Should().Equal(2m, 0m);
        portfolio.GetCurrentPositions()["SPY"].Quantity.Should().Be(20L);
        portfolio.Cash.Should().Be(7_988m);
    }

    [Fact]
    public void ProcessFill_LegacyStatelessCommissionModel_RemainsCompatible()
    {
        var portfolio = new SimulatedPortfolio(10_000m, new LegacyCommissionModel(), 0.05, 0.02);

        var accepted = portfolio.ProcessFill(new FillEvent(
            Guid.NewGuid(), Guid.NewGuid(), "SPY", 10L, 100m, 0m, DateTimeOffset.UtcNow));

        accepted.Commission.Should().Be(1.25m);
        portfolio.Cash.Should().Be(8_998.75m);
    }

    [Fact]
    public void ProcessFill_AdministrativeExecutions_BypassCommissionWithoutSharingEmptyOrderState()
    {
        var portfolio = new SimulatedPortfolio(10_000m, new FixedCommissionModel(5m), 0.05, 0.02);
        portfolio.ProcessFill(new FillEvent(
            Guid.NewGuid(), Guid.NewGuid(), "AAPL", 1L, 100m, 0m, DateTimeOffset.UtcNow));
        portfolio.ProcessFill(new FillEvent(
            Guid.NewGuid(), Guid.NewGuid(), "MSFT", 1L, 200m, 0m, DateTimeOffset.UtcNow));

        var firstLiquidation = portfolio.ProcessFill(new FillEvent(
            Guid.NewGuid(), Guid.Empty, "AAPL", -1L, 110m, 0m, DateTimeOffset.UtcNow.AddDays(1)));
        var secondLiquidation = portfolio.ProcessFill(new FillEvent(
            Guid.NewGuid(), Guid.Empty, "MSFT", -1L, 210m, 0m, DateTimeOffset.UtcNow.AddDays(1)));
        var snapshot = portfolio.TakeSnapshot(
            DateTimeOffset.UtcNow.AddDays(1),
            DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)));

        firstLiquidation.Commission.Should().Be(0m);
        secondLiquidation.Commission.Should().Be(0m);
        snapshot.DayCashFlows.OfType<CommissionCashFlow>().Should().HaveCount(2)
            .And.OnlyContain(static flow => flow.OrderId != Guid.Empty);
        snapshot.DayCashFlows.OfType<CommissionCashFlow>().Sum(static flow => flow.Amount).Should().Be(-10m);
    }

    [Fact]
    public void ProcessFill_DisallowedShorting_RejectsLongToResidualShortCrossingAtomically()
    {
        var account = new FinancialAccount(
            "long-only",
            "Long Only",
            FinancialAccountKind.Brokerage,
            InitialCash: 10_000m,
            Rules: new FinancialAccountRules(AllowShortSelling: false));
        var portfolio = new SimulatedPortfolio([account], account.AccountId, new FixedCommissionModel());
        portfolio.ProcessFill(new FillEvent(
            Guid.NewGuid(), Guid.NewGuid(), "SPY", 5L, 100m, 0m, DateTimeOffset.UtcNow, account.AccountId));
        var cashBefore = portfolio.Cash;

        var crossingSell = () => portfolio.ProcessFill(new FillEvent(
            Guid.NewGuid(), Guid.NewGuid(), "SPY", -10L, 110m, 0m, DateTimeOffset.UtcNow, account.AccountId));

        crossingSell.Should().Throw<InvalidOperationException>().WithMessage("*short selling*");
        portfolio.Cash.Should().Be(cashBefore);
        portfolio.GetCurrentPositions()["SPY"].Quantity.Should().Be(5L);
    }

    [Fact]
    public void ProcessFillsAtomically_DisallowedShorting_DoesNotCloseLongBeforeResidualShortRejection()
    {
        var account = new FinancialAccount(
            "long-only",
            "Long Only",
            FinancialAccountKind.Brokerage,
            InitialCash: 10_000m,
            Rules: new FinancialAccountRules(AllowShortSelling: false));
        var portfolio = new SimulatedPortfolio([account], account.AccountId, new FixedCommissionModel());
        portfolio.ProcessFill(new FillEvent(
            Guid.NewGuid(), Guid.NewGuid(), "SPY", 5L, 100m, 0m, DateTimeOffset.UtcNow, account.AccountId));
        var cashBefore = portfolio.Cash;
        var orderId = Guid.NewGuid();
        var crossingSlices = new FillEvent[]
        {
            new(Guid.NewGuid(), orderId, "SPY", -5L, 110m, 0m, DateTimeOffset.UtcNow.AddSeconds(1), account.AccountId),
            new(Guid.NewGuid(), orderId, "SPY", -5L, 109m, 0m, DateTimeOffset.UtcNow.AddSeconds(1), account.AccountId),
        };

        var crossingSell = () => portfolio.ProcessFillsAtomically(crossingSlices);

        crossingSell.Should().Throw<InvalidOperationException>().WithMessage("*short selling*");
        portfolio.Cash.Should().Be(cashBefore);
        portfolio.GetCurrentPositions()["SPY"].Quantity.Should().Be(5L);
        portfolio.GetOpenLots("SPY").Should().ContainSingle().Which.Quantity.Should().Be(5L);
    }

    [Fact]
    public void ApplyAssetEvent_AppliesDividendToEveryBrokerageAccountHoldingSymbol()
    {
        var accounts = new[]
        {
            new FinancialAccount("broker-a", "Broker A", FinancialAccountKind.Brokerage, InitialCash: 10_000m),
            new FinancialAccount("broker-b", "Broker B", FinancialAccountKind.Brokerage, InitialCash: 20_000m),
        };
        var portfolio = new SimulatedPortfolio(accounts, "broker-a", new FixedCommissionModel());
        portfolio.ProcessFill(new FillEvent(Guid.NewGuid(), Guid.NewGuid(), "SPY", 10L, 100m, 0m, DateTimeOffset.UtcNow, "broker-a"));
        portfolio.ProcessFill(new FillEvent(Guid.NewGuid(), Guid.NewGuid(), "SPY", 20L, 100m, 0m, DateTimeOffset.UtcNow, "broker-b"));
        var effectiveAt = new DateTimeOffset(2024, 2, 1, 0, 0, 0, TimeSpan.Zero);

        portfolio.ApplyAssetEvent(new AssetEvent(effectiveAt, "SPY", AssetEventType.Dividend, CashPerShare: 1m));
        var snapshot = portfolio.TakeSnapshot(effectiveAt, new DateOnly(2024, 2, 1));

        snapshot.Accounts["broker-a"].Cash.Should().Be(9_010m);
        snapshot.Accounts["broker-b"].Cash.Should().Be(18_020m);
        snapshot.DayCashFlows.OfType<AssetEventCashFlow>()
            .Select(static flow => (flow.AccountId, flow.Amount))
            .Should().BeEquivalentTo([("broker-a", 10m), ("broker-b", 20m)]);
    }

    [Fact]
    public void ApplyAssetEvent_AppliesPositionTransformationToEveryBrokerageAccountHoldingSymbol()
    {
        var accounts = new[]
        {
            new FinancialAccount("broker-a", "Broker A", FinancialAccountKind.Brokerage, InitialCash: 10_000m),
            new FinancialAccount("broker-b", "Broker B", FinancialAccountKind.Brokerage, InitialCash: 20_000m),
        };
        var portfolio = new SimulatedPortfolio(accounts, "broker-a", new FixedCommissionModel());
        portfolio.ProcessFill(new FillEvent(Guid.NewGuid(), Guid.NewGuid(), "SPY", 10L, 100m, 0m, DateTimeOffset.UtcNow, "broker-a"));
        portfolio.ProcessFill(new FillEvent(Guid.NewGuid(), Guid.NewGuid(), "SPY", 20L, 100m, 0m, DateTimeOffset.UtcNow, "broker-b"));

        portfolio.ApplyAssetEvent(new AssetEvent(
            new DateTimeOffset(2024, 2, 1, 0, 0, 0, TimeSpan.Zero),
            "SPY",
            AssetEventType.Split,
            PositionFactor: 2m));
        var accountsAfterSplit = portfolio.GetAccountSnapshots();

        accountsAfterSplit["broker-a"].Positions["SPY"].Quantity.Should().Be(20L);
        accountsAfterSplit["broker-a"].Positions["SPY"].AverageCostBasis.Should().Be(50m);
        accountsAfterSplit["broker-b"].Positions["SPY"].Quantity.Should().Be(40L);
        accountsAfterSplit["broker-b"].Positions["SPY"].AverageCostBasis.Should().Be(50m);
        portfolio.LastPrices["SPY"].Should().Be(50m,
            "a same-symbol split must normalize the shared mark only once for every account");
    }

    [Fact]
    public void ApplyAssetEvent_SameSymbolTransformation_PreservesSecondaryAccountRealizedPnl()
    {
        var accounts = new[]
        {
            new FinancialAccount("broker-a", "Broker A", FinancialAccountKind.Brokerage, InitialCash: 10_000m),
            new FinancialAccount("broker-b", "Broker B", FinancialAccountKind.Brokerage, InitialCash: 20_000m),
        };
        var portfolio = new SimulatedPortfolio(accounts, "broker-a", new FixedCommissionModel());
        var openedAt = new DateTimeOffset(2024, 1, 2, 14, 30, 0, TimeSpan.Zero);
        portfolio.ProcessFill(new FillEvent(
            Guid.NewGuid(), Guid.NewGuid(), "SPY", 10L, 100m, 0m, openedAt, "broker-b"));
        portfolio.ProcessFill(new FillEvent(
            Guid.NewGuid(), Guid.NewGuid(), "SPY", -4L, 110m, 0m, openedAt.AddMinutes(1), "broker-b"));

        portfolio.ApplyAssetEvent(new AssetEvent(
            new DateTimeOffset(2024, 2, 1, 0, 0, 0, TimeSpan.Zero),
            "SPY",
            AssetEventType.Split,
            PositionFactor: 2m));

        var position = portfolio.GetAccountSnapshots()["broker-b"].Positions["SPY"];
        position.Quantity.Should().Be(12L);
        position.AverageCostBasis.Should().Be(50m);
        position.RealizedPnl.Should().Be(40m);
    }

    [Fact]
    public void ShortPosition_ExposesOpenLotsThroughPositionAccountAndPortfolioViews()
    {
        var portfolio = new SimulatedPortfolio(10_000m, new FixedCommissionModel(), 0.05, 0.02);
        portfolio.ProcessFill(new FillEvent(
            Guid.NewGuid(), Guid.NewGuid(), "SPY", -10L, 100m, 0m, DateTimeOffset.UtcNow));

        portfolio.GetOpenLots("SPY").Should().ContainSingle().Which.Quantity.Should().Be(10L);
        portfolio.GetCurrentPositions()["SPY"].OpenLots.Should().ContainSingle();
        portfolio.GetAccountSnapshots()[BacktestDefaults.DefaultBrokerageAccountId].OpenLots.Should().ContainSingle();
    }

    [Fact]
    public void AccrueDailyInterest_UsesCalendarDay365Basis()
    {
        var account = new FinancialAccount(
            "broker",
            "Broker",
            FinancialAccountKind.Brokerage,
            InitialCash: 1_000m,
            Rules: new FinancialAccountRules(AnnualMarginRate: 0.365));
        var portfolio = new SimulatedPortfolio([account], account.AccountId, new FixedCommissionModel());
        portfolio.ProcessFill(new FillEvent(
            Guid.NewGuid(), Guid.NewGuid(), "SPY", 20L, 100m, 0m, DateTimeOffset.UtcNow, account.AccountId));

        portfolio.AccrueDailyInterest(new DateOnly(2024, 1, 2));

        portfolio.Cash.Should().Be(-1_001m, "36.5% / 365 is exactly 0.1% for one calendar day");
    }

    private static PortfolioSnapshot Snapshot(DateOnly date, decimal equity) => new(
        new DateTimeOffset(date.ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero),
        date,
        Cash: equity,
        MarginBalance: 0m,
        LongMarketValue: 0m,
        ShortMarketValue: 0m,
        TotalEquity: equity,
        DailyReturn: 0m,
        Positions: new Dictionary<string, Position>(),
        Accounts: new Dictionary<string, FinancialAccountSnapshot>(),
        DayCashFlows: []);

    private sealed class LegacyCommissionModel : ICommissionModel
    {
        public decimal Calculate(string symbol, long quantity, decimal fillPrice) => 1.25m;
    }

    private sealed class TrackingCommissionModel : ICommissionModel
    {
        public List<Guid> ReleasedOrderIds { get; } = [];

        public decimal Calculate(string symbol, long quantity, decimal fillPrice) => 0m;

        public void Release(Guid orderId) => ReleasedOrderIds.Add(orderId);
    }
}
