using FluentAssertions;
using Meridian.Execution.Margin;
using Meridian.Execution.Models;
using Meridian.Execution.Sdk;
using Meridian.Execution.Services;
using Meridian.Ledger;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Workstation;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Meridian.Tests.Execution;

public sealed class PaperLedgerIntegrityTests
{
    [Fact]
    public void MultiAccountFills_KeepCashBasisAndRealizedGainWithTheirOwner()
    {
        var ledger = new Meridian.Ledger.Ledger();
        var portfolio = new PaperTradingPortfolio([
            new("alpha", "Alpha", AccountKind.Brokerage, 1_000m),
            new("beta", "Beta", AccountKind.Brokerage, 2_000m)
        ], ledger);
        portfolio.ApplyFill("alpha", Fill(OrderSide.Buy, 3m, 10m, 1m));
        portfolio.ApplyFill("beta", Fill(OrderSide.Buy, 2m, 20m));
        portfolio.ApplyFill("alpha", Fill(OrderSide.Sell, 1m, 15m));

        ledger.GetBalance(LedgerAccounts.CashAccount("alpha")).Should().Be(984m);
        ledger.GetBalance(LedgerAccounts.CashAccount("beta")).Should().Be(1_960m);
        ledger.GetBalance(LedgerAccounts.Securities("XYZ", "alpha")).Should().Be(20m);
        ledger.GetBalance(LedgerAccounts.Securities("XYZ", "beta")).Should().Be(40m);
        ledger.GetBalance(LedgerAccounts.RealizedGainFor("alpha")).Should().Be(5m);
        ledger.GetBalance(LedgerAccounts.CommissionExpenseFor("alpha")).Should().Be(1m);
        ledger.TrialBalance("beta").Keys.Should().OnlyContain(a => a.FinancialAccountId == "beta");
        portfolio.CheckLedgerIntegrity().Should().BeEmpty();
    }

    [Fact]
    public void RegTShort_CollateralCashAndBasisReconcileThroughPartialAndFullCover()
    {
        var ledger = new Meridian.Ledger.Ledger();
        var portfolio = new PaperTradingPortfolio([
            new("margin", "Margin", AccountKind.Brokerage, 10_000m, MarginAccountType.RegT)
        ], ledger);
        portfolio.ApplyFill("margin", Fill(OrderSide.Sell, 10m, 100m, 2m));
        portfolio.CheckLedgerIntegrity().Should().BeEmpty();
        portfolio.GetAccount("margin")!.Cash.Should().Be(9_498m);
        portfolio.ApplyFill("margin", Fill(OrderSide.Buy, 4m, 90m, 1m));
        portfolio.CheckLedgerIntegrity().Should().BeEmpty();
        portfolio.ApplyFill("margin", Fill(OrderSide.Buy, 6m, 110m));
        portfolio.CheckLedgerIntegrity().Should().BeEmpty();
        portfolio.GetAccount("margin")!.Cash.Should().Be(9_977m);
    }

    [Fact]
    public void PaidMarginInterest_DoesNotAlsoCapitalizeIntoLoan()
    {
        var ledger = new Meridian.Ledger.Ledger();
        var portfolio = new PaperTradingPortfolio([
            new("margin", "Margin", AccountKind.Brokerage, 10_000m, MarginAccountType.RegT)
        ], ledger);
        portfolio.ApplyFill("margin", Fill(OrderSide.Buy, 10m, 100m));
        portfolio.AccrueMarginInterest(0.365m);

        portfolio.MarginBalance.Should().Be(500m);
        portfolio.Cash.Should().Be(9_499.5m);
        ledger.GetBalance(LedgerAccounts.MarginInterestExpenseFor("margin")).Should().Be(0.5m);
        portfolio.CheckLedgerIntegrity().Should().BeEmpty();
        portfolio.ApplyFill("margin", Fill(OrderSide.Sell, 10m, 100m));
        portfolio.Cash.Should().Be(9_999.5m);
        portfolio.CheckLedgerIntegrity().Should().BeEmpty();
    }

    [Fact]
    public void IntegrityCheck_ReportsStrandedBasisAndWrongCash()
    {
        var ledger = new Meridian.Ledger.Ledger();
        var portfolio = new PaperTradingPortfolio(100m, ledger);
        ledger.PostLines(DateTimeOffset.UtcNow, "Unexpected posting", [
            (LedgerAccounts.Securities("GHOST"), 10m, 0m),
            (LedgerAccounts.Cash, 0m, 10m)
        ]);

        var breaks = portfolio.CheckLedgerIntegrity();
        breaks.Should().Contain(b => b.Check == "Cash" && b.Expected == 100m && b.Actual == 90m);
        breaks.Should().Contain(b => b.Check == "Securities:GHOST" && b.Expected == 0m && b.Actual == 10m);
    }

    [Fact]
    public void DuplicateAccountDefinitions_AreRejectedBeforeAnyJournalEntry()
    {
        var ledger = new Meridian.Ledger.Ledger();
        Action create = () => _ = new PaperTradingPortfolio([
            new("alpha", "One", AccountKind.Brokerage, 1_000m),
            new("ALPHA", "Two", AccountKind.Brokerage, 2_000m)
        ], ledger);
        create.Should().Throw<ArgumentException>();
        ledger.Journal.Should().BeEmpty();
    }

    [Fact]
    public void DeepLossMarginSale_PostsCashDebitAsCreditAndReconciles()
    {
        var ledger = new Meridian.Ledger.Ledger();
        var portfolio = new PaperTradingPortfolio([
            new("margin", "Margin", AccountKind.Brokerage, 1_000m, MarginAccountType.RegT)
        ], ledger);
        portfolio.ApplyFill("margin", Fill(OrderSide.Buy, 1m, 100m));
        portfolio.ApplyFill("margin", Fill(OrderSide.Sell, 1m, 10m));
        portfolio.Cash.Should().Be(910m);
        portfolio.CheckLedgerIntegrity().Should().BeEmpty();
        ledger.Journal.SelectMany(e => e.Lines).Should().OnlyContain(l => l.Debit >= 0m && l.Credit >= 0m);
    }

    [Fact]
    public void MultiAccountIntegrity_DetectsUnscopedCashAndAllocationResiduals()
    {
        var ledger = new Meridian.Ledger.Ledger();
        var portfolio = new PaperTradingPortfolio([
            new("alpha", "Alpha", AccountKind.Brokerage, 1_000m)
        ], ledger);
        ledger.PostLines(DateTimeOffset.UtcNow, "Orphan opening", [
            (LedgerAccounts.Cash, 2m, 0m), (LedgerAccounts.CapitalAccount, 0m, 2m)
        ]);
        ledger.PostLines(DateTimeOffset.UtcNow, "Uncleared allocation", [
            (new LedgerAccount("Allocation Control", LedgerAccountType.Asset, FinancialAccountId: "alpha"), 1m, 0m),
            (LedgerAccounts.CapitalAccountFor("alpha"), 0m, 1m)
        ]);
        portfolio.CheckLedgerIntegrity().Should().Contain(b => b.Check == "OrphanAccount:Cash");
        portfolio.CheckLedgerIntegrity().Should().Contain(b => b.Check == "TransferClearing:Allocation Control");
    }

    [Fact]
    public async Task QuoteOnlyBasisChange_WithLedger_IsRefusedBeforePositionMutation()
    {
        var ledger = new Meridian.Ledger.Ledger();
        var portfolio = new PaperTradingPortfolio(1_000m, ledger,
            (ILivePositionCorporateActionAdjuster)new BasisChangingAdjuster());
        portfolio.ApplyFill(Fill(OrderSide.Buy, 10m, 10m));
        var before = portfolio.GetPositions().Single();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            portfolio.ApplyCorporateActionsAsync("XYZ", DateTimeOffset.UtcNow.AddDays(-1)));

        portfolio.GetPositions().Single().ExactQuantity.Should().Be(before.ExactQuantity);
        portfolio.GetPositions().Single().AverageCostBasis.Should().Be(before.AverageCostBasis);
        portfolio.CheckLedgerIntegrity().Should().BeEmpty();
    }

    [Fact]
    public async Task Initialise_CorruptRetainedFillEconomics_RefusesToPublishSession()
    {
        // A corrupt historical fill charges a negative commission. Replay validates every
        // execution before mutation, so startup must not publish even a partial session.
        const string sessionId = "PAPER-CORRUPT-CASH";
        var fill = Fill(OrderSide.Buy, 1m, 10m, -5m);
        var store = Substitute.For<IPaperSessionStore>();
        store.LoadAllSessionsAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<PersistedSessionRecord>>([
            new(sessionId, "synthetic", "Synthetic", 100m, fill.Timestamp.AddMinutes(-1), null, true, ["XYZ"])
        ]));
        store.LoadFillRecordsAsync(sessionId, Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<PaperSessionFillRecord>>([
            PaperSessionFillRecord.CreateCanonical(fill, isApplied: true)
        ]));
        store.LoadFillsAsync(sessionId, Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<ExecutionReport>>([fill]));
        store.LoadOrderHistoryAsync(sessionId, Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<OrderState>>([]));
        store.LoadLedgerJournalAsync(sessionId, Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<PersistedJournalEntryDto>>([]));
        await using var service = new PaperSessionPersistenceService(NullLogger<PaperSessionPersistenceService>.Instance, store);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => service.InitialiseAsync());

        service.GetSessions().Should().BeEmpty();
        await store.DidNotReceive().SaveSessionMetadataAsync(Arg.Any<PersistedSessionRecord>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task MutatingDefensiveSessionProjection_DoesNotChangeAuthoritativeSessionCash()
    {
        await using var service = new PaperSessionPersistenceService(NullLogger<PaperSessionPersistenceService>.Instance);
        var session = await service.CreateSessionAsync(new CreatePaperSessionDto("synthetic", "Synthetic", 100m));
        var detached = service.GetActivePortfolio(session.SessionId)!;
        ((Meridian.Ledger.Ledger)detached.Ledger!).PostLines(DateTimeOffset.UtcNow, "Detached cash", [
            (LedgerAccounts.Cash, 5m, 0m), (LedgerAccounts.CapitalAccount, 0m, 5m)
        ]);
        detached.CheckLedgerIntegrity().Should().Contain(b => b.Check == "Cash");

        (await service.CloseSessionAsync(session.SessionId)).Should().BeTrue();
        service.GetLedger(session.SessionId)!.GetBalance(LedgerAccounts.Cash).Should().Be(100m);
    }

    private sealed class BasisChangingAdjuster : ILivePositionCorporateActionAdjuster
    {
        public Task<PositionCorporateActionAdjustment> AdjustPositionAsync(string ticker, decimal quantity,
            decimal costBasis, DateTimeOffset positionOpenedAt, CancellationToken ct = default)
            => Task.FromResult(new PositionCorporateActionAdjustment(ticker, quantity, quantity,
                costBasis, costBasis / 2m, 1)
            { AdjustedTotalCostBasis = quantity * costBasis / 2m });
    }

    [Fact]
    public void WhitespaceAccountIdentity_IsRejectedBeforeAnyPosting()
    {
        var ledger = new Meridian.Ledger.Ledger();
        Action create = () => _ = new PaperTradingPortfolio([
            new("alpha", "Alpha", AccountKind.Brokerage, 1_000m),
            new(" alpha ", "Ambiguous", AccountKind.Brokerage, 2_000m)
        ], ledger);
        create.Should().Throw<ArgumentException>();
        ledger.Journal.Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PartialFifoRelief_RecomputesRemainingAverageCostFromExactLots(bool shortBook)
    {
        var ledger = new Meridian.Ledger.Ledger();
        var portfolio = new PaperTradingPortfolio(1_000m, ledger);
        var open = shortBook ? OrderSide.Sell : OrderSide.Buy;
        var close = shortBook ? OrderSide.Buy : OrderSide.Sell;
        portfolio.ApplyFill(Fill(open, 1m, 10m));
        portfolio.ApplyFill(Fill(open, 2m, 20m));
        portfolio.ApplyFill(Fill(close, 1m, 15m));

        portfolio.GetPositions().Single().AverageCostBasis.Should().Be(20m);
        portfolio.GetPositions().Single().ExactQuantity.Should().Be(shortBook ? -2m : 2m);
        portfolio.CheckLedgerIntegrity().Should().BeEmpty();
    }

    [Theory]
    [InlineData(120, 20)]
    [InlineData(80, -20)]
    public void LongSaleCashFlow_ClassifiesGrossTradeCashAsInvestingAndCommissionsAsOperating(int salePrice, int tradePnl)
    {
        var ledger = new Meridian.Ledger.Ledger();
        var portfolio = new PaperTradingPortfolio(1_000m, ledger);
        var start = ledger.Journal.Single().Timestamp;
        portfolio.ApplyFill(Fill(OrderSide.Buy, 1m, 100m, 2m) with { Timestamp = start.AddMinutes(1) });
        portfolio.ApplyFill(Fill(OrderSide.Sell, 1m, salePrice, 3m) with { Timestamp = start.AddMinutes(2) });

        var cashFlow = LedgerFinancialStatementBuilder.BuildForPeriod(ledger, start, start.AddMinutes(3)).CashFlow!;

        cashFlow.InvestingCashFlow.Should().Be(tradePnl);
        cashFlow.OperatingCashFlow.Should().Be(-5m);
        cashFlow.FinancingCashFlow.Should().Be(0m);
        cashFlow.EndingCash.Should().Be(portfolio.Cash);
        cashFlow.IsReconciled.Should().BeTrue();
        cashFlow.Lines.Where(line => line.Description.StartsWith("Sell", StringComparison.Ordinal)).Sum(line => line.Amount).Should().Be(salePrice);
    }

    [Theory]
    [InlineData(80, 20)]
    [InlineData(120, -20)]
    public void ShortRoundTripCashFlow_ClassifiesBothProceedsAndCoverAsInvesting(int coverPrice, int tradePnl)
    {
        var ledger = new Meridian.Ledger.Ledger();
        var portfolio = new PaperTradingPortfolio(1_000m, ledger);
        var start = ledger.Journal.Single().Timestamp;
        portfolio.ApplyFill(Fill(OrderSide.Sell, 1m, 100m) with { Timestamp = start.AddMinutes(1) });
        portfolio.ApplyFill(Fill(OrderSide.Buy, 1m, coverPrice) with { Timestamp = start.AddMinutes(2) });

        var cashFlow = LedgerFinancialStatementBuilder.BuildForPeriod(ledger, start, start.AddMinutes(3)).CashFlow!;

        cashFlow.InvestingCashFlow.Should().Be(tradePnl);
        cashFlow.OperatingCashFlow.Should().Be(0m);
        cashFlow.FinancingCashFlow.Should().Be(0m);
        cashFlow.EndingCash.Should().Be(portfolio.Cash);
        cashFlow.IsReconciled.Should().BeTrue();
        cashFlow.Lines.Where(line => line.Description.StartsWith("Short sell", StringComparison.Ordinal)).Sum(line => line.Amount).Should().Be(100m);
        cashFlow.Lines.Where(line => line.Description.StartsWith("Cover", StringComparison.Ordinal)).Sum(line => line.Amount).Should().Be(-coverPrice);
    }

    [Theory]
    [InlineData(120)]
    [InlineData(10)]
    public void RegTMarginCashFlow_RetainsGrossInvestmentAndIndependentBorrowingRepayment(int salePrice)
    {
        var ledger = new Meridian.Ledger.Ledger();
        var portfolio = new PaperTradingPortfolio([
            new("margin", "Margin", AccountKind.Brokerage, 1_000m, MarginAccountType.RegT)
        ], ledger);
        var start = ledger.Journal.Single().Timestamp;
        portfolio.ApplyFill("margin", Fill(OrderSide.Buy, 1m, 100m) with { Timestamp = start.AddMinutes(1) });
        var acquisition = LedgerFinancialStatementBuilder.BuildForPeriod(ledger, start, start.AddMinutes(1),
            financialAccountId: "margin").CashFlow!;
        acquisition.InvestingCashFlow.Should().Be(-100m);
        acquisition.FinancingCashFlow.Should().Be(50m);
        acquisition.EndingCash.Should().Be(950m);
        acquisition.IsReconciled.Should().BeTrue();

        portfolio.ApplyFill("margin", Fill(OrderSide.Sell, 1m, salePrice) with { Timestamp = start.AddMinutes(2) });
        var disposal = LedgerFinancialStatementBuilder.BuildForPeriod(ledger, start.AddMinutes(1), start.AddMinutes(3),
            financialAccountId: "margin").CashFlow!;
        disposal.InvestingCashFlow.Should().Be(salePrice);
        disposal.FinancingCashFlow.Should().Be(-50m);
        disposal.OperatingCashFlow.Should().Be(0m);
        disposal.EndingCash.Should().Be(portfolio.Cash);
        disposal.IsReconciled.Should().BeTrue();
        portfolio.CheckLedgerIntegrity().Should().BeEmpty();
    }

    [Fact]
    public async Task ClosedSessionRestart_PreservesCashFlowAttributionBoundToOriginalLineIds()
    {
        var root = Path.Combine(Path.GetTempPath(), "meridian-paper-attribution", Guid.NewGuid().ToString("N"));
        try
        {
            string sessionId;
            DateTimeOffset periodStart;
            string[] originalLineIds;
            Dictionary<string, string>[] originalTags;
            await using (var service = new PaperSessionPersistenceService(NullLogger<PaperSessionPersistenceService>.Instance,
                new JsonlFilePaperSessionStore(root, NullLogger<JsonlFilePaperSessionStore>.Instance)))
            {
                var session = await service.CreateSessionAsync(new CreatePaperSessionDto("synthetic", "Synthetic", 1_000m));
                sessionId = session.SessionId;
                periodStart = service.GetLedger(sessionId)!.Journal.Single().Timestamp;
                periodStart.Should().Be(session.CreatedAt);
                await service.RecordFillAsync(sessionId, Fill(OrderSide.Buy, 1m, 100m) with { Timestamp = periodStart.AddSeconds(1) });
                service.GetLedger(sessionId)!.Journal[0].Timestamp.Should().Be(periodStart);
                service.GetLedger(sessionId)!.GetBalanceAsOf(LedgerAccounts.Cash, periodStart).Should().Be(1_000m);
                await service.RecordFillAsync(sessionId, Fill(OrderSide.Sell, 1m, 80m) with { Timestamp = periodStart.AddSeconds(2) });
                service.GetLedger(sessionId)!.Journal[0].Timestamp.Should().Be(periodStart);
                service.GetLedger(sessionId)!.GetBalanceAsOf(LedgerAccounts.Cash, periodStart).Should().Be(1_000m);
                await service.CloseSessionAsync(sessionId);
                var journal = service.GetLedger(sessionId)!.Journal;
                originalLineIds = journal.SelectMany(entry => entry.Lines).Select(line => line.EntryId.ToString()).ToArray();
                originalTags = journal.Select(entry => entry.Metadata.Tags?.ToDictionary(pair => pair.Key, pair => pair.Value)
                    ?? new Dictionary<string, string>()).ToArray();
            }
            await using var restored = new PaperSessionPersistenceService(NullLogger<PaperSessionPersistenceService>.Instance,
                new JsonlFilePaperSessionStore(root, NullLogger<JsonlFilePaperSessionStore>.Instance));
            await restored.InitialiseAsync();
            var retained = restored.GetLedger(sessionId)!;
            retained.Journal[0].Timestamp.Should().Be(periodStart);
            retained.GetBalanceAsOf(LedgerAccounts.Cash, periodStart).Should().Be(1_000m);
            retained.Journal.SelectMany(entry => entry.Lines).Select(line => line.EntryId.ToString()).Should().Equal(originalLineIds);
            for (var index = 0; index < retained.Journal.Count; index++)
                (retained.Journal[index].Metadata.Tags ?? new Dictionary<string, string>()).Should().BeEquivalentTo(originalTags[index]);
            var cashFlow = LedgerFinancialStatementBuilder.BuildForPeriod(retained, periodStart, periodStart.AddSeconds(3)).CashFlow!;
            cashFlow.BeginningCash.Should().Be(1_000m);
            cashFlow.InvestingCashFlow.Should().Be(-20m);
            cashFlow.OperatingCashFlow.Should().Be(0m);
            cashFlow.FinancingCashFlow.Should().Be(0m);
            cashFlow.EndingCash.Should().Be(980m);
            cashFlow.IsReconciled.Should().BeTrue();
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Theory]
    [InlineData(false, 200)]
    [InlineData(true, 200)]
    [InlineData(false, 1)]
    [InlineData(true, 1)]
    public void ContractPartialRelief_ReconcilesScaledMoneyAndRetainsUnitQuotes(bool shortBook, int doubledMultiplier)
    {
        var multiplier = doubledMultiplier / 2m;
        var ledger = new Meridian.Ledger.Ledger();
        var portfolio = new PaperTradingPortfolio(10_000m, ledger);
        var start = ledger.Journal.Single().Timestamp;
        var open = shortBook ? OrderSide.Sell : OrderSide.Buy;
        var close = shortBook ? OrderSide.Buy : OrderSide.Sell;
        portfolio.ApplyFill(ContractFill(open, 1m, 10m, start.AddSeconds(1)));
        portfolio.ApplyFill(ContractFill(open, 2m, 20m, start.AddSeconds(2)));
        portfolio.ApplyFill(ContractFill(close, 1m, 15m, start.AddSeconds(3)));

        portfolio.GetPositions().Single().AverageCostBasis.Should().Be(20m);
        portfolio.CheckLedgerIntegrity().Should().BeEmpty();
        portfolio.ApplyFill(ContractFill(close, 2m, 25m, start.AddSeconds(4)));

        var realized = (shortBook ? -15m : 15m) * multiplier;
        portfolio.RealisedPnl.Should().Be(realized);
        portfolio.Cash.Should().Be(10_000m + realized);
        portfolio.CheckLedgerIntegrity().Should().BeEmpty();
        var cashFlow = LedgerFinancialStatementBuilder.BuildForPeriod(ledger, start, start.AddSeconds(5)).CashFlow!;
        cashFlow.InvestingCashFlow.Should().Be(realized);
        cashFlow.IsReconciled.Should().BeTrue();

        ExecutionReport ContractFill(OrderSide side, decimal quantity, decimal price, DateTimeOffset at) =>
            Fill(side, quantity, price) with
            {
                AssetClass = "Option",
                LotUnitKind = SecurityLotUnitKind.Contracts,
                LotMultiplier = multiplier,
                BookedMoneyMultiplier = multiplier,
                BookedVariationMargin = false,
                Timestamp = at
            };
    }

    [Fact]
    public void MultiAccountVariationMargin_ReconcilesCashWithoutInventingNotionalSecuritiesBasis()
    {
        var ledger = new Meridian.Ledger.Ledger();
        var portfolio = new PaperTradingPortfolio([
            new("futures", "Futures", AccountKind.Brokerage, 1_000_000m),
            new("other", "Other", AccountKind.Brokerage, 2_000m)
        ], ledger);
        var start = ledger.Journal.Max(entry => entry.Timestamp);
        var future = Fill(OrderSide.Buy, 1m, 6_000m) with
        {
            Symbol = "ESZ6",
            AssetClass = "Future",
            LotUnitKind = SecurityLotUnitKind.Contracts,
            LotMultiplier = 50m,
            BookedMoneyMultiplier = 50m,
            BookedVariationMargin = true,
            Timestamp = start.AddSeconds(1)
        };
        portfolio.ApplyFill("futures", future);
        portfolio.CheckLedgerIntegrity().Should().BeEmpty();

        portfolio.ApplyVariationMarginSettlement("futures", "ESZ6", 6_010m, start.AddSeconds(2)).Should().Be(500m);
        portfolio.CheckLedgerIntegrity().Should().BeEmpty();
        portfolio.ApplyFill("futures", future with
        {
            OrderId = Guid.NewGuid().ToString("N"),
            Side = OrderSide.Sell,
            FillPrice = 6_020m,
            Timestamp = start.AddSeconds(3)
        });

        portfolio.CheckLedgerIntegrity().Should().BeEmpty();
        ledger.GetBalance(LedgerAccounts.CashAccount("futures")).Should().Be(1_001_000m);
        ledger.GetBalance(LedgerAccounts.FuturesMtmSettlement("ESZ6", "futures")).Should().Be(-1_000m);
        ledger.GetBalance(LedgerAccounts.CashAccount("other")).Should().Be(2_000m);
        ledger.Accounts.Should().NotContain(account => account.FinancialAccountId is null);
    }

    private static ExecutionReport Fill(OrderSide side, decimal quantity, decimal price, decimal commission = 0m) => new()
    {
        OrderId = Guid.NewGuid().ToString("N"),
        Symbol = "XYZ",
        Side = side,
        ReportType = ExecutionReportType.Fill,
        FilledQuantity = quantity,
        FillPrice = price,
        Commission = commission,
        OrderStatus = Meridian.Execution.Sdk.OrderStatus.Filled,
        Timestamp = DateTimeOffset.UtcNow
    };
}
