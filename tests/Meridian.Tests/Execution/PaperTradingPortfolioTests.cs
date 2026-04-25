using FluentAssertions;
using Meridian.Execution.Margin;
using Meridian.Execution.Models;
using Meridian.Execution.Sdk;
using Meridian.Execution.Services;
using Meridian.Ledger;
using OrderStatus = Meridian.Execution.Sdk.OrderStatus;

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

// ---------------------------------------------------------------------------
// Reg T margin account tests
// ---------------------------------------------------------------------------

public sealed class PaperTradingPortfolioRegTMarginTests
{
    // The default Reg T model requires 50 % initial margin for long positions,
    // so buying power equals 2× the available cash equity.

    [Fact]
    public void RegTAccount_BuyLong_DeductsOnlyInitialMarginFromCash()
    {
        // Arrange: $100 000 cash, 50 % initial margin required
        var portfolio = BuildRegTPortfolio(100_000m);

        // Act: buy $20 000 notional → cash required = $10 000 (50 % initial margin)
        portfolio.ApplyFill("margin-1", BuildFill("AAPL", OrderSide.Buy, qty: 100, price: 200m));

        // Assert
        portfolio.Cash.Should().Be(90_000m, because: "only $10 000 initial margin is debited");
        portfolio.MarginBalance.Should().Be(10_000m, because: "broker loaned the other $10 000");
    }

    [Fact]
    public void RegTAccount_BuyingPower_IsDoubleCashEquity()
    {
        var portfolio = BuildRegTPortfolio(100_000m);

        var snapshot = portfolio.GetAccountSnapshot("margin-1");

        snapshot.Should().NotBeNull();
        snapshot!.BuyingPower.Should().Be(200_000m,
            because: "at 50 % initial margin rate, buying power = cash / 0.5");
        snapshot.MarginType.Should().Be(MarginAccountType.RegT);
    }

    [Fact]
    public void RegTAccount_SellLong_RepaysProportionalMarginLoan()
    {
        // Arrange: open a $20 000 position with 50 % margin → $10 000 borrowed
        var portfolio = BuildRegTPortfolio(100_000m);
        portfolio.ApplyFill("margin-1", BuildFill("AAPL", OrderSide.Buy, qty: 100, price: 200m));

        // Cash after buy: $90 000; margin borrowed: $10 000
        portfolio.Cash.Should().Be(90_000m);
        portfolio.MarginBalance.Should().Be(10_000m);

        // Act: sell all 100 shares at $220 (proceeds = $22 000)
        // Loan repaid: $10 000 × (100/100) = $10 000
        // Net cash received by trader: $22 000 − $10 000 = $12 000
        portfolio.ApplyFill("margin-1", BuildFill("AAPL", OrderSide.Sell, qty: 100, price: 220m));

        portfolio.Cash.Should().Be(102_000m,
            because: "$90 000 + $12 000 net proceeds after loan repayment");
        portfolio.MarginBalance.Should().Be(0m, because: "position fully closed; loan fully repaid");
        portfolio.RealisedPnl.Should().Be(2_000m, because: "($220 − $200) × 100 shares");
    }

    [Fact]
    public void RegTAccount_PartialSell_RepaysProportionalLoan()
    {
        // Arrange: buy 100 shares @ $200 on 50 % margin → $10 000 borrowed
        var portfolio = BuildRegTPortfolio(100_000m);
        portfolio.ApplyFill("margin-1", BuildFill("AAPL", OrderSide.Buy, qty: 100, price: 200m));

        // Act: sell 50 shares @ $200 (break-even)
        // Loan repaid: $10 000 × (50/100) = $5 000
        // Net cash received: $10 000 proceeds − $5 000 loan = $5 000
        portfolio.ApplyFill("margin-1", BuildFill("AAPL", OrderSide.Sell, qty: 50, price: 200m));

        portfolio.Cash.Should().Be(95_000m,
            because: "$90 000 + $5 000 net proceeds");
        portfolio.MarginBalance.Should().Be(5_000m,
            because: "half the loan remains for the remaining 50-share position");
    }

    [Fact]
    public void RegTAccount_TakeSnapshot_ExposesMarginTypeAndBuyingPower()
    {
        var portfolio = BuildRegTPortfolio(50_000m);

        var snapshot = portfolio.GetAccountSnapshot("margin-1");

        snapshot.Should().NotBeNull();
        snapshot!.MarginType.Should().Be(MarginAccountType.RegT);
        snapshot.BuyingPower.Should().Be(100_000m);
    }

    [Fact]
    public void CashAccount_Buy_DeductsFullNotional_BuyingPowerEqualsCash()
    {
        var portfolio = new PaperTradingPortfolio(
        [
            new AccountDefinition("cash-1", "Cash Account", AccountKind.Brokerage, 100_000m,
                MarginType: MarginAccountType.Cash),
        ]);

        portfolio.ApplyFill("cash-1", BuildFill("MSFT", OrderSide.Buy, qty: 10, price: 300m));

        portfolio.Cash.Should().Be(97_000m, because: "full $3 000 notional deducted for cash account");
        portfolio.MarginBalance.Should().Be(0m);

        var snap = portfolio.GetAccountSnapshot("cash-1");
        snap.Should().NotBeNull();
        snap!.BuyingPower.Should().Be(97_000m, because: "cash account buying power equals cash");
    }

    [Fact]
    public void RegTAccount_AccountDefinition_CreatesDefaultRegTMarginModel()
    {
        var portfolio = BuildRegTPortfolio(100_000m);

        var snap = portfolio.GetAccountSnapshot("margin-1");

        snap.Should().NotBeNull();
        snap!.MarginType.Should().Be(MarginAccountType.RegT);
    }

    [Fact]
    public void MarginBalance_WithNoPositions_IsZero()
    {
        var portfolio = BuildRegTPortfolio(100_000m);

        portfolio.MarginBalance.Should().Be(0m);
    }

    // ---- Helpers ----

    private static PaperTradingPortfolio BuildRegTPortfolio(decimal cash) =>
        new(
        [
            new AccountDefinition("margin-1", "Reg T Margin Account", AccountKind.Brokerage, cash,
                MarginType: MarginAccountType.RegT),
        ]);

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
}

// ---------------------------------------------------------------------------
// Margin rules: short-sell margin, margin-call detection, interest accrual
// ---------------------------------------------------------------------------

public sealed class PaperTradingPortfolioMarginRulesTests
{
    // ── Short-sell Reg T ───────────────────────────────────────────────────

    [Fact]
    public void RegTAccount_ShortSell_HoldsAdditionalMarginAsCollateral()
    {
        // Arrange: $100 000 cash, Reg T short initial rate = 150 %
        // Short 100 shares @ $200 → notional = $20 000
        // Broker holds: 150 % × $20 000 = $30 000 collateral
        //   Proceeds ($20 000) stay with broker, trader must post extra $10 000 (50 %)
        //   Cash change: −$10 000 − $0 commission = $90 000
        var portfolio = BuildRegTPortfolio(100_000m);

        portfolio.ApplyFill("margin-1", BuildFill("AAPL", OrderSide.Sell, qty: 100, price: 200m));

        portfolio.Cash.Should().Be(90_000m,
            because: "Reg T requires 50 % additional margin posted on top of the proceeds held");
        portfolio.ShortMarginCollateral.Should().Be(30_000m,
            because: "broker holds 150 % of $20 000 notional as total collateral");
    }

    [Fact]
    public void RegTAccount_CoverShort_ReleasesCollateralAndReturnsProfit()
    {
        // Arrange: short 100 @ $200, cash → $90 000, collateral = $30 000
        var portfolio = BuildRegTPortfolio(100_000m);
        portfolio.ApplyFill("margin-1", BuildFill("AAPL", OrderSide.Sell, qty: 100, price: 200m));

        // Act: cover @ $180 (profit)
        // Buy-to-cover cost: 100 × $180 = $18 000
        // Collateral released: full $30 000 (100 % of position closed)
        // Cash: $90 000 − $18 000 + $30 000 = $102 000
        portfolio.ApplyFill("margin-1", BuildFill("AAPL", OrderSide.Buy, qty: 100, price: 180m));

        portfolio.Cash.Should().Be(102_000m,
            because: "cover cost $18 000 paid; $30 000 collateral returned");
        portfolio.ShortMarginCollateral.Should().Be(0m,
            because: "position fully closed");
        portfolio.RealisedPnl.Should().Be(2_000m,
            because: "shorted at $200, covered at $180 → $20 × 100 shares profit");
    }

    [Fact]
    public void RegTAccount_PartialCoverShort_ReleasesProportionalCollateral()
    {
        // Arrange: short 100 @ $200
        var portfolio = BuildRegTPortfolio(100_000m);
        portfolio.ApplyFill("margin-1", BuildFill("AAPL", OrderSide.Sell, qty: 100, price: 200m));

        // Act: cover 50 shares @ $200 (break-even)
        // Collateral released: 50 % × $30 000 = $15 000
        // Buy-to-cover cost: 50 × $200 = $10 000
        // Cash: $90 000 − $10 000 + $15 000 = $95 000
        portfolio.ApplyFill("margin-1", BuildFill("AAPL", OrderSide.Buy, qty: 50, price: 200m));

        portfolio.Cash.Should().Be(95_000m,
            because: "half the collateral released; half the position covered at cost");
        portfolio.ShortMarginCollateral.Should().Be(15_000m,
            because: "50 % of the original $30 000 collateral remains for the open short");
    }

    [Fact]
    public void CashAccount_ShortSell_CreditsFullProceeds()
    {
        // A plain cash account does not hold margin against shorts.
        var portfolio = new PaperTradingPortfolio(
        [
            new AccountDefinition("cash-1", "Cash", AccountKind.Brokerage, 100_000m,
                MarginType: MarginAccountType.Cash),
        ]);

        portfolio.ApplyFill("cash-1", BuildFill("MSFT", OrderSide.Sell, qty: 50, price: 300m));

        portfolio.Cash.Should().Be(115_000m,
            because: "cash account: short proceeds ($15 000) credited immediately");
        portfolio.ShortMarginCollateral.Should().Be(0m);
    }

    // ── Margin-call detection (CheckMarginStatus) ──────────────────────────

    [Fact]
    public void CheckMarginStatus_NoMarginModel_ReturnsNoMarginRequired()
    {
        var portfolio = new PaperTradingPortfolio(100_000m); // default cash account

        var status = portfolio.CheckMarginStatus(
            PaperTradingPortfolio.DefaultAccountId,
            new Dictionary<string, decimal> { ["AAPL"] = 200m });

        status.IsMarginCall.Should().BeFalse();
        status.PortfolioRequirement.MaintenanceMargin.Should().Be(0m);
    }

    [Fact]
    public void CheckMarginStatus_LongPosition_NoMarginCall_WhenEquitySufficient()
    {
        // $100 000 cash, Reg T; buy 100 shares @ $200 on margin
        // After buy: cash = $90 000, margin borrowed = $10 000
        // Update price to $200 (no change) → maintenance = 25 % × $20 000 = $5 000
        // Equity = $90 000 + $20 000 MV = $110 000 → ExcessLiquidity = $105 000 → no call
        var portfolio = BuildRegTPortfolio(100_000m);
        portfolio.ApplyFill("margin-1", BuildFill("AAPL", OrderSide.Buy, qty: 100, price: 200m));

        var status = portfolio.CheckMarginStatus("margin-1",
            new Dictionary<string, decimal> { ["AAPL"] = 200m });

        status.IsMarginCall.Should().BeFalse();
        status.MarginDeficiency.Should().Be(0m);
        status.PortfolioRequirement.MaintenanceMargin.Should().Be(5_000m);
    }

    [Fact]
    public void CheckMarginStatus_LongPosition_MarginCall_WhenPricePlumeges()
    {
        // $20 000 cash, buy 200 shares @ $100 on 50 % Reg T margin
        // Cash deducted: 50 % × $20 000 = $10 000 → cash = $10 000; borrowed = $10 000
        // Price drops to $20 → MV = $4 000, maintenance = 25 % × $4 000 = $1 000
        // Equity = $10 000 cash + $4 000 MV = $14 000 → ExcessLiquidity = $13 000
        // Actually let's trigger a real call:
        //   Start with $500 cash (tight), buy 10 shares @ $100 (notional $1 000)
        //   Cash after buy = $500 − ($1 000 × 0.5) = $0
        //   Price falls to $10 → MV = $100, maintenance = 25 % × $100 = $25
        //   Equity = $0 + $100 = $100 → ExcessLiquidity = $100 − $25 = $75 → no call yet
        //   Price falls to $1 → MV = $10, maintenance = $2.50, equity = $10 → $7.50 still ok
        // For a real margin call:
        //   Short: short 100 @ $100, then price jumps to $200
        //   Cash = $100 000 − $5 000 = $95 000 (50 % additional margin posted)
        //   Collateral = 150 % × $10 000 = $15 000
        //   Cover MV = 100 × $200 = $20 000
        //   Maintenance = 130 % × $20 000 = $26 000
        //   Equity = $95 000 + 0 long MV = $95 000
        //   ExcessLiquidity = $95 000 − $26 000 = $69 000 → no call (too much cash)
        //
        // Simplest real margin call: start with minimal cash for long
        //   $1 000 cash, 50 % reg-T; buy 20 shares @ $100 → notional $2 000
        //   Cash after buy = $1 000 − $1 000 = $0; borrowed = $1 000
        //   Price drops to $1 → MV = $20; maintenance = 25 % × $20 = $5
        //   Equity = $0 + $20 = $20; ExcessLiquidity = $20 − $5 = $15 → no call
        //
        // For an actual call, equity must be < maintenance:
        //   $1 000 cash, buy 40 shares @ $50 on 50 % → cash = $0, borrowed = $1 000
        //   Price drops to $0.01 → MV ≈ $0.40; maintenance = 25 % × $0.40 = $0.10
        //   Equity ≈ $0.40; ExcessLiquidity = $0.40 − $0.10 = $0.30 → still no call
        //
        // For cash to go negative we need a leveraged scenario:
        //   $0 cash isn't possible. Let's do:
        //   Account starts with $10 000 cash.
        //   Buy 200 @ $100 (notional $20 000) on 50 % margin.
        //   Cash after buy = $10 000 − $10 000 = $0; borrowed = $10 000
        //   Price falls to $1 → MV = $200; maintenance = 25 % × $200 = $50
        //   Equity = $0 + $200 = $200; ExcessLiquidity = $200 − $50 = $150 → no call
        //   Still no call because equity > maintenance...
        //
        // Actually to trigger a call, equity < maintenance:
        //   Need MV < 33% of original (where 33% of 100 = 33) for a 25% maintenance:
        //   Actually ExcessLiquidity = (cash + MV) - maintenance
        //                             = (cash + qty*px) - qty*px*0.25
        //                             = cash + qty*px*0.75
        //   For ExcessLiquidity < 0:
        //     cash + qty*px*0.75 < 0
        //   Since cash ≥ 0, this can't happen unless cash is negative.
        //   But with margin, cash CAN go negative if the position loses so much that:
        //     initial_cash - (notional * initial_margin_rate) + additional cash = 0
        //     and the position value collapses.
        //
        //   Actually, the model tracks cash separately from loans. After a buy:
        //     cash_after_buy = initial_cash - notional*initial_margin_rate
        //   If initial_cash = notional*initial_margin_rate, cash = 0.
        //   Now price falls:
        //     equity = 0 + qty*px*0.75 which is always ≥ 0.
        //   
        //   The margin call triggers when equity < maintenance.
        //   With cash = 0:
        //     equity = qty*px; maintenance = qty*px*0.25
        //     ExcessLiquidity = qty*px - qty*px*0.25 = qty*px*0.75 ≥ 0
        //   So a long-only position with 0 cash can never trigger a margin call
        //   under the standard RegT model (which is by design - the loan itself
        //   has a floor). OK let me try with additional cash going negative via accrual.
        //   
        //   Actually let me just directly manipulate: inject interest accrual to
        //   drive cash negative, then check.
        //   
        //   OR: test the case where the portfolio has a big short that moved against it:
        //   $10 000 cash, short 100 @ $100 → cash = $10 000 - $5 000 = $5 000
        //                                       collateral = $15 000
        //   Price jumps to $500 → cover MV = $50 000
        //   maintenance = 130 % × $50 000 = $65 000
        //   equity = $5 000 + 0 long MV = $5 000
        //   ExcessLiquidity = $5 000 − $65 000 = −$60 000 → MARGIN CALL ✓
        var portfolio = new PaperTradingPortfolio(
        [
            new AccountDefinition("margin-1", "Reg T", AccountKind.Brokerage, 10_000m,
                MarginType: MarginAccountType.RegT),
        ]);
        // Short 100 @ $100: cash = $10k − $5k additional margin = $5k; collateral = $15k
        portfolio.ApplyFill("margin-1", BuildFill("AAPL", OrderSide.Sell, qty: 100, price: 100m));

        // Price explodes to $500 → short is massively underwater
        // maintenance = 130 % × $50 000 = $65 000; equity = $5 000 → call
        var status = portfolio.CheckMarginStatus("margin-1",
            new Dictionary<string, decimal> { ["AAPL"] = 500m });

        status.IsMarginCall.Should().BeTrue();
        status.MarginDeficiency.Should().BeGreaterThan(0m);
    }

    [Fact]
    public void CheckMarginStatus_UnknownAccount_ReturnsNoMarginRequired()
    {
        var portfolio = BuildRegTPortfolio(100_000m);

        var status = portfolio.CheckMarginStatus("does-not-exist",
            new Dictionary<string, decimal>());

        status.IsMarginCall.Should().BeFalse();
        status.AccountId.Should().Be("does-not-exist");
    }

    // ── GetMarginRequirement convenience method ────────────────────────────

    [Fact]
    public void GetMarginRequirement_ReturnsPortfolioRequirement()
    {
        var portfolio = BuildRegTPortfolio(100_000m);
        portfolio.ApplyFill("margin-1", BuildFill("AAPL", OrderSide.Buy, qty: 100, price: 200m));

        var req = portfolio.GetMarginRequirement("margin-1",
            new Dictionary<string, decimal> { ["AAPL"] = 200m });

        req.Symbol.Should().BeNull("portfolio-level requirement has no single symbol");
        req.MaintenanceMargin.Should().Be(5_000m, because: "25 % × $20 000 MV");
        req.IsMarginCall.Should().BeFalse();
    }

    // ── AccrueMarginInterest ───────────────────────────────────────────────

    [Fact]
    public void AccrueMarginInterest_ZeroRate_NoChange()
    {
        var portfolio = BuildRegTPortfolio(100_000m);
        portfolio.ApplyFill("margin-1", BuildFill("AAPL", OrderSide.Buy, qty: 100, price: 200m));

        var cashBefore = portfolio.Cash;
        var balanceBefore = portfolio.MarginBalance;

        portfolio.AccrueMarginInterest(annualRate: 0m, days: 1);

        portfolio.Cash.Should().Be(cashBefore);
        portfolio.MarginBalance.Should().Be(balanceBefore);
    }

    [Fact]
    public void AccrueMarginInterest_OneDay_DeductsInterestFromCashAndAddsToLoan()
    {
        // $100 000 cash, buy $20 000 notional on 50 % margin → $10 000 borrowed.
        // Annual rate 7.3 % → daily = 7.3 / 365 = 0.02 %.
        // Interest for 1 day on $10 000 = $10 000 × 0.0002 = $2.
        var portfolio = BuildRegTPortfolio(100_000m);
        portfolio.ApplyFill("margin-1", BuildFill("AAPL", OrderSide.Buy, qty: 100, price: 200m));

        // Cash before accrual: $90 000, MarginBalance: $10 000
        portfolio.AccrueMarginInterest(annualRate: 0.073m, days: 1);

        var expectedInterest = 10_000m * (0.073m / 365m);
        portfolio.Cash.Should().BeApproximately(90_000m - expectedInterest, precision: 0.0001m);
        portfolio.MarginBalance.Should().BeApproximately(10_000m + expectedInterest, precision: 0.0001m);
    }

    [Fact]
    public void AccrueMarginInterest_MultiDay_ScalesWithDays()
    {
        var portfolio = BuildRegTPortfolio(100_000m);
        portfolio.ApplyFill("margin-1", BuildFill("AAPL", OrderSide.Buy, qty: 100, price: 200m));

        portfolio.AccrueMarginInterest(annualRate: 0.073m, days: 30);

        var expectedInterest = 10_000m * (0.073m / 365m) * 30m;
        portfolio.MarginBalance.Should().BeApproximately(10_000m + expectedInterest, precision: 0.001m);
        portfolio.Cash.Should().BeApproximately(90_000m - expectedInterest, precision: 0.001m);
    }

    [Fact]
    public void AccrueMarginInterest_CashAccount_NoChange()
    {
        // Cash accounts have no margin model; interest accrual should be a no-op.
        var portfolio = new PaperTradingPortfolio(100_000m);
        portfolio.ApplyFill(BuildFill("AAPL", OrderSide.Buy, qty: 10, price: 100m));

        var cashBefore = portfolio.Cash;

        portfolio.AccrueMarginInterest(annualRate: 0.10m, days: 30);

        portfolio.Cash.Should().Be(cashBefore);
    }

    [Fact]
    public void AccrueMarginInterest_NegativeRate_ThrowsArgumentOutOfRange()
    {
        var portfolio = BuildRegTPortfolio(100_000m);

        var act = () => portfolio.AccrueMarginInterest(annualRate: -0.01m);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void AccrueMarginInterest_ZeroDays_ThrowsArgumentOutOfRange()
    {
        var portfolio = BuildRegTPortfolio(100_000m);

        var act = () => portfolio.AccrueMarginInterest(annualRate: 0.07m, days: 0);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    // ── Snapshot includes ShortMarginCollateral ───────────────────────────

    [Fact]
    public void RegTAccount_Snapshot_IncludesShortMarginCollateral()
    {
        var portfolio = BuildRegTPortfolio(100_000m);
        portfolio.ApplyFill("margin-1", BuildFill("AAPL", OrderSide.Sell, qty: 100, price: 200m));

        var snap = portfolio.GetAccountSnapshot("margin-1");

        snap.Should().NotBeNull();
        snap!.ShortMarginCollateral.Should().Be(30_000m,
            because: "150 % × $20 000 notional = $30 000 collateral");
        snap.MarginBalance.Should().Be(0m,
            because: "no long positions in this account, so no broker loan");
    }

    // ---- Helpers ----

    private static PaperTradingPortfolio BuildRegTPortfolio(decimal cash) =>
        new(
        [
            new AccountDefinition("margin-1", "Reg T Margin", AccountKind.Brokerage, cash,
                MarginType: MarginAccountType.RegT),
        ]);

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
}
