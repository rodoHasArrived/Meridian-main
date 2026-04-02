using FluentAssertions;
using Meridian.Execution.Sdk;
using Meridian.Execution.Services;
using Meridian.Ledger;

namespace Meridian.Tests.Execution;

public sealed class PaperTradingPortfolioTests
{
    // -------------------------------------------------------------------------
    // Constructor / initial capital
    // -------------------------------------------------------------------------

    [Fact]
    public void Constructor_SetsInitialCash()
    {
        var portfolio = new PaperTradingPortfolio(50_000m);

        portfolio.Cash.Should().Be(50_000m);
        portfolio.Positions.Should().BeEmpty();
        portfolio.RealisedPnl.Should().Be(0m);
    }

    [Fact]
    public void Constructor_WithLedger_PostsOpeningCapitalEntry()
    {
        var ledger = new Meridian.Ledger.Ledger();

        _ = new PaperTradingPortfolio(100_000m, ledger);

        ledger.Journal.Should().ContainSingle();
        var entry = ledger.Journal[0];
        entry.Description.Should().Contain("Initial capital");
        entry.Lines.Should().HaveCount(2);
        var debit = entry.Lines.Single(l => l.Debit > 0);
        debit.Account.Should().Be(LedgerAccounts.Cash);
        debit.Debit.Should().Be(100_000m);
    }

    [Fact]
    public void Constructor_WithLedger_ZeroCash_DoesNotPostOpeningEntry()
    {
        var ledger = new Meridian.Ledger.Ledger();
        _ = new PaperTradingPortfolio(0m, ledger);

        ledger.Journal.Should().BeEmpty();
    }

    // -------------------------------------------------------------------------
    // ApplyFill – buy
    // -------------------------------------------------------------------------

    [Fact]
    public void ApplyFill_Buy_ReducesCashAndOpensLongPosition()
    {
        var portfolio = new PaperTradingPortfolio(100_000m);
        portfolio.ApplyFill(BuildFill("AAPL", OrderSide.Buy, qty: 10, price: 200m));

        portfolio.Cash.Should().Be(100_000m - 2_000m);
        portfolio.Positions.Should().ContainKey("AAPL");
        portfolio.Positions["AAPL"].Quantity.Should().Be(10);
        portfolio.Positions["AAPL"].AverageCostBasis.Should().Be(200m);
    }

    [Fact]
    public void ApplyFill_Buy_PostsSecuritiesAndCashEntriesToLedger()
    {
        var ledger = new Meridian.Ledger.Ledger();
        var portfolio = new PaperTradingPortfolio(100_000m, ledger);
        portfolio.ApplyFill(BuildFill("AAPL", OrderSide.Buy, qty: 10, price: 200m));

        // Opening capital + buy entry
        ledger.Journal.Should().HaveCount(2);
        var buyEntry = ledger.Journal.Last(e => e.Description.Contains("Buy"));
        buyEntry.Lines.Should().HaveCount(2);
        buyEntry.Lines.Single(l => l.Account == LedgerAccounts.Securities("AAPL")).Debit.Should().Be(2_000m);
        buyEntry.Lines.Single(l => l.Account == LedgerAccounts.Cash).Credit.Should().Be(2_000m);
    }

    [Fact]
    public void ApplyFill_Buy_DeductsCommissionFromCash_AndPostsExpenseEntry()
    {
        var ledger = new Meridian.Ledger.Ledger();
        var portfolio = new PaperTradingPortfolio(100_000m, ledger);
        portfolio.ApplyFill(BuildFill("AAPL", OrderSide.Buy, qty: 10, price: 200m, commission: 5m));

        portfolio.Cash.Should().Be(100_000m - 2_000m - 5m);
        // capital + buy + commission entries
        ledger.Journal.Should().HaveCount(3);
        var commEntry = ledger.Journal.Single(e => e.Description.Contains("Commission"));
        commEntry.Lines.Single(l => l.Account == LedgerAccounts.CommissionExpense).Debit.Should().Be(5m);
    }

    // -------------------------------------------------------------------------
    // ApplyFill – sell (close long)
    // -------------------------------------------------------------------------

    [Fact]
    public void ApplyFill_Sell_WithGain_RealisesGainAndUpdatesLedger()
    {
        var ledger = new Meridian.Ledger.Ledger();
        var portfolio = new PaperTradingPortfolio(100_000m, ledger);
        portfolio.ApplyFill(BuildFill("AAPL", OrderSide.Buy, qty: 10, price: 200m));
        portfolio.ApplyFill(BuildFill("AAPL", OrderSide.Sell, qty: 10, price: 220m));

        portfolio.Cash.Should().Be(100_000m - 2_000m + 2_200m);  // bought at 200, sold at 220
        portfolio.RealisedPnl.Should().Be(200m);                   // 10 * (220 – 200)
        portfolio.Positions.Should().NotContainKey("AAPL");        // flat after full close

        var sellEntry = ledger.Journal.Single(e => e.Description.Contains("Sell"));
        sellEntry.Lines.Should().Contain(l => l.Account == LedgerAccounts.RealizedGain && l.Credit == 200m);
    }

    [Fact]
    public void ApplyFill_Sell_WithLoss_RealisesLossAndUpdatesLedger()
    {
        var ledger = new Meridian.Ledger.Ledger();
        var portfolio = new PaperTradingPortfolio(100_000m, ledger);
        portfolio.ApplyFill(BuildFill("AAPL", OrderSide.Buy, qty: 10, price: 200m));
        portfolio.ApplyFill(BuildFill("AAPL", OrderSide.Sell, qty: 10, price: 180m));

        portfolio.RealisedPnl.Should().Be(-200m);  // 10 * (180 – 200)
        portfolio.Positions.Should().NotContainKey("AAPL");

        var sellEntry = ledger.Journal.Single(e => e.Description.Contains("Sell"));
        sellEntry.Lines.Should().Contain(l => l.Account == LedgerAccounts.RealizedLoss && l.Debit == 200m);
    }

    [Fact]
    public void ApplyFill_Sell_AtCost_PostsSimpleTwoLineEntry()
    {
        var ledger = new Meridian.Ledger.Ledger();
        var portfolio = new PaperTradingPortfolio(100_000m, ledger);
        portfolio.ApplyFill(BuildFill("AAPL", OrderSide.Buy, qty: 10, price: 200m));
        portfolio.ApplyFill(BuildFill("AAPL", OrderSide.Sell, qty: 10, price: 200m));

        portfolio.RealisedPnl.Should().Be(0m);

        var sellEntry = ledger.Journal.Single(e => e.Description.Contains("Sell"));
        sellEntry.Lines.Should().HaveCount(2);  // no gain/loss line
        sellEntry.Lines.Should().NotContain(l => l.Account == LedgerAccounts.RealizedGain);
        sellEntry.Lines.Should().NotContain(l => l.Account == LedgerAccounts.RealizedLoss);
    }

    [Fact]
    public void ApplyFill_CoverShort_WithGain_RealisesGainAndPostsLedgerEntries()
    {
        var ledger = new Meridian.Ledger.Ledger();
        var portfolio = new PaperTradingPortfolio(100_000m, ledger);
        portfolio.ApplyFill(BuildFill("AAPL", OrderSide.Sell, qty: 10, price: 200m));
        portfolio.ApplyFill(BuildFill("AAPL", OrderSide.Buy, qty: 10, price: 180m));

        portfolio.Cash.Should().Be(100_000m + 2_000m - 1_800m);
        portfolio.RealisedPnl.Should().Be(200m);
        portfolio.Positions.Should().NotContainKey("AAPL");

        var coverEntry = ledger.Journal.Single(e => e.Description.Contains("Cover"));
        var shortPayable = LedgerAccounts.ShortSecuritiesPayable("AAPL", financialAccountId: null);
        coverEntry.Lines.Should().Contain(l => l.Account == shortPayable && l.Debit == 2_000m);
        coverEntry.Lines.Should().Contain(l => l.Account == LedgerAccounts.Cash && l.Credit == 1_800m);
        coverEntry.Lines.Should().Contain(l => l.Account == LedgerAccounts.RealizedGain && l.Credit == 200m);
    }

    [Fact]
    public void ApplyFill_CoverShort_WithLoss_RealisesLossAndPostsLedgerEntries()
    {
        var ledger = new Meridian.Ledger.Ledger();
        var portfolio = new PaperTradingPortfolio(100_000m, ledger);
        portfolio.ApplyFill(BuildFill("AAPL", OrderSide.Sell, qty: 10, price: 200m));
        portfolio.ApplyFill(BuildFill("AAPL", OrderSide.Buy, qty: 10, price: 220m));

        portfolio.Cash.Should().Be(100_000m + 2_000m - 2_200m);
        portfolio.RealisedPnl.Should().Be(-200m);

        var coverEntry = ledger.Journal.Single(e => e.Description.Contains("Cover"));
        var shortPayable = LedgerAccounts.ShortSecuritiesPayable("AAPL", financialAccountId: null);
        coverEntry.Lines.Should().Contain(l => l.Account == shortPayable && l.Debit == 2_000m);
        coverEntry.Lines.Should().Contain(l => l.Account == LedgerAccounts.Cash && l.Credit == 2_200m);
        coverEntry.Lines.Should().Contain(l => l.Account == LedgerAccounts.RealizedLoss && l.Debit == 200m);
    }

    // -------------------------------------------------------------------------
    // UpdateMarketPrice / unrealised P&L
    // -------------------------------------------------------------------------

    [Fact]
    public void UpdateMarketPrice_UpdatesUnrealisedPnl()
    {
        var portfolio = new PaperTradingPortfolio(100_000m);
        portfolio.ApplyFill(BuildFill("AAPL", OrderSide.Buy, qty: 10, price: 200m));

        portfolio.UpdateMarketPrice("AAPL", 250m);

        portfolio.UnrealisedPnl.Should().Be(500m);  // 10 * (250 – 200)
    }

    // -------------------------------------------------------------------------
    // Ignore non-fill reports
    // -------------------------------------------------------------------------

    [Fact]
    public void ApplyFill_AcceptedReport_IsNoOp()
    {
        var portfolio = new PaperTradingPortfolio(50_000m);
        portfolio.ApplyFill(new ExecutionReport
        {
            OrderId = "ord-1",
            ReportType = ExecutionReportType.New,
            Symbol = "AAPL",
            Side = OrderSide.Buy,
            OrderStatus = OrderStatus.Accepted,
        });

        portfolio.Cash.Should().Be(50_000m);
        portfolio.Positions.Should().BeEmpty();
    }

    // -------------------------------------------------------------------------
    // Ledger balance integrity (assets = liabilities + equity + revenue - expenses)
    // -------------------------------------------------------------------------

    [Fact]
    public void AfterBuyAndSell_LedgerTrialBalanceIsBalanced()
    {
        var ledger = new Meridian.Ledger.Ledger();
        var portfolio = new PaperTradingPortfolio(100_000m, ledger);
        portfolio.ApplyFill(BuildFill("AAPL", OrderSide.Buy, qty: 5, price: 300m, commission: 2.5m));
        portfolio.ApplyFill(BuildFill("AAPL", OrderSide.Sell, qty: 5, price: 350m, commission: 2.5m));

        var trialBalance = ledger.SummarizeAccounts();
        var totalDebits = trialBalance.Sum(static a => a.TotalDebits);
        var totalCredits = trialBalance.Sum(static a => a.TotalCredits);

        totalDebits.Should().Be(totalCredits);
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static ExecutionReport BuildFill(
        string symbol,
        OrderSide side,
        decimal qty,
        decimal price,
        decimal commission = 0m) =>
        new()
        {
            OrderId = Guid.NewGuid().ToString("N"),
            ReportType = ExecutionReportType.Fill,
            Symbol = symbol,
            Side = side,
            OrderStatus = OrderStatus.Filled,
            OrderQuantity = qty,
            FilledQuantity = qty,
            FillPrice = price,
            Commission = commission,
            Timestamp = DateTimeOffset.UtcNow,
        };
}

// ---------------------------------------------------------------------------
// Corporate action adjustment tests
// ---------------------------------------------------------------------------

public sealed class PaperTradingPortfolioCorporateActionTests
{
    [Fact]
    public async Task ApplyCorporateActionsAsync_NoAdjuster_IsNoOp()
    {
        // Arrange — no adjuster supplied
        var portfolio = new PaperTradingPortfolio(100_000m);
        portfolio.ApplyFill(BuildFill("AAPL", OrderSide.Buy, 10, 200m));

        // Act
        await portfolio.ApplyCorporateActionsAsync("AAPL", DateTimeOffset.UtcNow.AddDays(-1));

        // Assert — position unchanged
        portfolio.Positions["AAPL"].Quantity.Should().Be(10L);
        portfolio.Positions["AAPL"].AverageCostBasis.Should().Be(200m);
    }

    [Fact]
    public async Task ApplyCorporateActionsAsync_WithSplitAdjuster_UpdatesPositionQuantityAndCostBasis()
    {
        // Arrange — adjuster applies a 2-for-1 split
        var adjuster = new StubCorporateActionAdjuster(splitRatio: 2m);
        var portfolio = new PaperTradingPortfolio(100_000m, ledger: null, corporateActionAdjuster: adjuster);
        portfolio.ApplyFill(BuildFill("TSLA", OrderSide.Buy, 10, 300m));

        // Act — simulate the ex-date being after the position was opened
        await portfolio.ApplyCorporateActionsAsync("TSLA", DateTimeOffset.UtcNow.AddDays(-30));

        // Assert — after a 2-for-1 split: quantity doubled, cost basis halved
        portfolio.Positions["TSLA"].Quantity.Should().Be(20L);
        portfolio.Positions["TSLA"].AverageCostBasis.Should().Be(150m);
    }

    [Fact]
    public async Task ApplyCorporateActionsAsync_SymbolNotHeld_IsNoOp()
    {
        var adjuster = new StubCorporateActionAdjuster(splitRatio: 2m);
        var portfolio = new PaperTradingPortfolio(100_000m, ledger: null, corporateActionAdjuster: adjuster);

        // Act — symbol not in portfolio
        await portfolio.ApplyCorporateActionsAsync("MSFT", DateTimeOffset.UtcNow.AddDays(-1));

        // Assert — adjuster was NOT called (position doesn't exist)
        adjuster.CallCount.Should().Be(0);
    }

    // ---- Helpers ----

    private static ExecutionReport BuildFill(string symbol, OrderSide side, decimal qty, decimal price) =>
        new()
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

    private sealed class StubCorporateActionAdjuster : Meridian.Application.SecurityMaster.ILivePositionCorporateActionAdjuster
    {
        private readonly decimal _splitRatio;
        public int CallCount { get; private set; }

        public StubCorporateActionAdjuster(decimal splitRatio) => _splitRatio = splitRatio;

        public Task<Meridian.Application.SecurityMaster.PositionCorporateActionAdjustment> AdjustPositionAsync(
            string ticker, decimal quantity, decimal costBasis, DateTimeOffset positionOpenedAt, CancellationToken ct = default)
        {
            CallCount++;
            var adjustedQty = quantity * _splitRatio;
            var adjustedCb = _splitRatio != 0 ? costBasis / _splitRatio : costBasis;
            return Task.FromResult(new Meridian.Application.SecurityMaster.PositionCorporateActionAdjustment(
                ticker, quantity, adjustedQty, costBasis, adjustedCb, ActionCount: 1));
        }
    }
}
