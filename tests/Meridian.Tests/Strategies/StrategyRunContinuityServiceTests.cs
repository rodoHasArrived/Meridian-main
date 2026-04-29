using FluentAssertions;
using Meridian.Backtesting.Sdk;
using Meridian.Contracts.Workstation;
using Meridian.Ledger;
using Meridian.Strategies.Interfaces;
using Meridian.Strategies.Models;
using Meridian.Strategies.Promotions;
using Meridian.Strategies.Services;
using Meridian.Strategies.Storage;
using Xunit;

namespace Meridian.Tests.Strategies;

public sealed class StrategyRunContinuityServiceTests
{
    [Fact]
    public async Task GetRunContinuityAsync_WithParentAndMismatchedPromotionSource_EmitsLineageMismatchWarning()
    {
        var store = new StrategyRunStore();
        var run = BuildContinuityRun("lineage-source-mismatch") with { ParentRunId = "expected-parent" };
        await store.RecordRunAsync(run);

        var continuityService = BuildContinuityService(
            store,
            [BuildPromotionRecord("lineage-source-mismatch", "different-parent", "paper-target")]);
        var continuity = await continuityService.GetRunContinuityAsync("lineage-source-mismatch");

        continuity.Should().NotBeNull();
        continuity!.ContinuityStatus.Warnings.Select(static warning => warning.Code)
            .Should()
            .Contain("lineage-parent-source-mismatch");
    }

    [Fact]
    public async Task GetRunContinuityAsync_WithoutParentAndPromotionSource_EmitsMissingParentWarning()
    {
        var store = new StrategyRunStore();
        await store.RecordRunAsync(BuildContinuityRun("lineage-parent-missing"));

        var continuityService = BuildContinuityService(
            store,
            [BuildPromotionRecord("lineage-parent-missing", "claimed-parent", "paper-target")]);
        var continuity = await continuityService.GetRunContinuityAsync("lineage-parent-missing");

        continuity.Should().NotBeNull();
        continuity!.ContinuityStatus.Warnings.Select(static warning => warning.Code)
            .Should()
            .Contain("lineage-missing-parent-with-source");
    }

    [Fact]
    public async Task GetRunContinuityAsync_PromotionCandidateWithoutTarget_EmitsMissingTargetWarning()
    {
        var store = new StrategyRunStore();
        await store.RecordRunAsync(BuildContinuityRun("lineage-target-missing"));

        var continuityService = BuildContinuityService(
            store,
            [BuildPromotionRecord("lineage-target-missing", "lineage-target-missing", targetRunId: null)]);
        var continuity = await continuityService.GetRunContinuityAsync("lineage-target-missing");

        continuity.Should().NotBeNull();
        continuity!.ContinuityStatus.Warnings.Select(static warning => warning.Code)
            .Should()
            .Contain("promotion-target-run-missing");
    }

    [Fact]
    public async Task GetRunContinuityAsync_CandidateForLiveWithoutParent_EmitsLineageShapeWarning()
    {
        var store = new StrategyRunStore();
        var paperRun = BuildContinuityRun("lineage-shape-paper") with { RunType = RunType.Paper };
        await store.RecordRunAsync(paperRun);

        var continuityService = BuildContinuityService(store);
        var continuity = await continuityService.GetRunContinuityAsync("lineage-shape-paper");

        continuity.Should().NotBeNull();
        continuity!.Run.Summary.Promotion!.State.Should().Be(StrategyRunPromotionState.CandidateForLive);
        continuity.ContinuityStatus.Warnings.Select(static warning => warning.Code)
            .Should()
            .Contain("promotion-lineage-shape-inconsistent");
    }

    [Fact]
    public async Task GetRunContinuityAsync_BuildsRunCenteredSnapshotAcrossSharedSeams()
    {
        var store = new StrategyRunStore();
        var parentRun = BuildContinuityRun("continuity-root", includeFills: true, promotionState: StrategyRunPromotionState.CandidateForLive);
        var childRun = StrategyRunEntry.Start("continuity-strategy", "Continuity Strategy", RunType.Paper) with
        {
            RunId = "continuity-paper",
            StartedAt = parentRun.EndedAt!.Value.AddHours(1),
            EndedAt = parentRun.EndedAt.Value.AddHours(2),
            DatasetReference = parentRun.DatasetReference,
            FeedReference = parentRun.FeedReference,
            PortfolioId = "continuity-paper-portfolio",
            LedgerReference = "continuity-paper-ledger",
            AuditReference = "audit-continuity-paper",
            Engine = "BrokerPaper",
            ParentRunId = "continuity-root",
            FundProfileId = "alpha-credit",
            FundDisplayName = "Alpha Credit"
        };

        await store.RecordRunAsync(parentRun);
        await store.RecordRunAsync(childRun);

        var readService = new StrategyRunReadService(store, new PortfolioReadService(), new LedgerReadService());
        var reconciliationRepository = new InMemoryReconciliationRunRepository();
        var reconciliationService = new ReconciliationRunService(
            readService,
            new ReconciliationProjectionService(),
            reconciliationRepository);
        await reconciliationService.RunAsync(new ReconciliationRunRequest("continuity-root"));

        var continuityService = new StrategyRunContinuityService(
            readService,
            new CashFlowProjectionService(store),
            reconciliationService);

        var continuity = await continuityService.GetRunContinuityAsync("continuity-root");

        continuity.Should().NotBeNull();
        continuity!.Run.Summary.RunId.Should().Be("continuity-root");
        continuity.Run.Summary.FundProfileId.Should().Be("alpha-credit");
        continuity.Run.Summary.ParentRunId.Should().BeNull();
        continuity.Lineage.ParentRun.Should().BeNull();
        continuity.Lineage.ChildRuns.Should().ContainSingle();
        continuity.Lineage.ChildRuns[0].RunId.Should().Be("continuity-paper");
        continuity.Lineage.ChildRuns[0].Mode.Should().Be(StrategyRunMode.Paper);
        continuity.CashFlow.Should().NotBeNull();
        continuity.CashFlow!.TotalEntries.Should().Be(3);
        continuity.CashFlow.ProjectedNetPosition.Should().Be(-376m);
        continuity.Reconciliation.Should().NotBeNull();
        continuity.Reconciliation!.RunId.Should().Be("continuity-root");
        continuity.Reconciliation.OpenBreakCount.Should().Be(0);
        continuity.ContinuityStatus.HasPortfolio.Should().BeTrue();
        continuity.ContinuityStatus.HasLedger.Should().BeTrue();
        continuity.ContinuityStatus.HasCashFlow.Should().BeTrue();
        continuity.ContinuityStatus.HasFills.Should().BeFalse();
        continuity.ContinuityStatus.HasReconciliation.Should().BeTrue();
        continuity.ContinuityStatus.HasWarnings.Should().BeTrue();
        continuity.ContinuityStatus.Warnings.Select(static warning => warning.Code)
            .Should()
            .Contain(["security-coverage", "lineage-promotion-gap"]);
        continuity.ContinuityStatus.Warnings.Select(static warning => warning.Code)
            .Should()
            .NotContain(["missing-reconciliation", "as-of-drift", "open-reconciliation-breaks"]);
    }

    [Fact]
    public async Task GetRunContinuityAsync_WithoutCashFlowsOrReconciliation_ReturnsContinuityWarnings()
    {
        var store = new StrategyRunStore();
        await store.RecordRunAsync(BuildContinuityRun("continuity-warning", includeCashFlows: false));

        var readService = new StrategyRunReadService(store, new PortfolioReadService(), new LedgerReadService());
        var continuityService = new StrategyRunContinuityService(
            readService,
            new CashFlowProjectionService(store),
            new ReconciliationRunService(
                readService,
                new ReconciliationProjectionService(),
                new InMemoryReconciliationRunRepository()));

        var continuity = await continuityService.GetRunContinuityAsync("continuity-warning");

        continuity.Should().NotBeNull();
        continuity!.ContinuityStatus.HasCashFlow.Should().BeFalse();
        continuity.ContinuityStatus.HasReconciliation.Should().BeFalse();
        continuity.ContinuityStatus.HasWarnings.Should().BeTrue();
        continuity.ContinuityStatus.Warnings.Select(static warning => warning.Code)
            .Should()
            .Contain(["missing-cash-flow", "missing-fills", "missing-reconciliation"]);
    }

    private static StrategyRunEntry BuildContinuityRun(string runId, bool includeCashFlows = true, bool includeFills = false, StrategyRunPromotionState promotionState = StrategyRunPromotionState.None)
    {
        var startedAt = new DateTimeOffset(2026, 4, 2, 9, 30, 0, TimeSpan.Zero);
        var completedAt = startedAt.AddHours(2);
        var positions = new Dictionary<string, Position>(StringComparer.OrdinalIgnoreCase)
        {
            ["AAPL"] = new("AAPL", 10, 40m, 0m, 0m)
        };
        var accountSnapshot = new FinancialAccountSnapshot(
            AccountId: BacktestDefaults.DefaultBrokerageAccountId,
            DisplayName: "Primary Brokerage",
            Kind: FinancialAccountKind.Brokerage,
            Institution: "Simulated Broker",
            Cash: 600m,
            MarginBalance: 0m,
            LongMarketValue: 400m,
            ShortMarketValue: 0m,
            Equity: 1_000m,
            Positions: positions,
            Rules: new FinancialAccountRules());
        var snapshot = new PortfolioSnapshot(
            Timestamp: completedAt,
            Date: DateOnly.FromDateTime(completedAt.UtcDateTime),
            Cash: 600m,
            MarginBalance: 0m,
            LongMarketValue: 400m,
            ShortMarketValue: 0m,
            TotalEquity: 1_000m,
            DailyReturn: 0m,
            Positions: positions,
            Accounts: new Dictionary<string, FinancialAccountSnapshot>(StringComparer.OrdinalIgnoreCase)
            {
                [accountSnapshot.AccountId] = accountSnapshot
            },
            DayCashFlows: []);

        var cashFlows = includeCashFlows
            ? new CashFlowEntry[]
            {
                new TradeCashFlow(startedAt.AddMinutes(15), -400m, "AAPL", 10L, 40m),
                new CommissionCashFlow(startedAt.AddMinutes(15), -1m, "AAPL", Guid.NewGuid()),
                new DividendCashFlow(startedAt.AddDays(5), 25m, "AAPL", 10L, 2.5m)
            }
            : [];

        var metrics = new BacktestMetrics(
            InitialCapital: 1_000m,
            FinalEquity: 1_000m,
            GrossPnl: 0m,
            NetPnl: 0m,
            TotalReturn: 0m,
            AnnualizedReturn: 0m,
            SharpeRatio: 0.8d,
            SortinoRatio: 0.8d,
            CalmarRatio: 0.8d,
            MaxDrawdown: 0m,
            MaxDrawdownPercent: 0m,
            MaxDrawdownRecoveryDays: 0,
            ProfitFactor: 1d,
            WinRate: 1d,
            TotalTrades: 1,
            WinningTrades: 1,
            LosingTrades: 0,
            TotalCommissions: 1m,
            TotalMarginInterest: 0m,
            TotalShortRebates: 0m,
            Xirr: 0d,
            SymbolAttribution: new Dictionary<string, SymbolAttribution>());
        var result = new BacktestResult(
            Request: new BacktestRequest(
                From: new DateOnly(2026, 4, 1),
                To: new DateOnly(2026, 4, 2),
                Symbols: ["AAPL"],
                InitialCash: 1_000m,
                DataRoot: "./data"),
            Universe: new HashSet<string>(["AAPL"], StringComparer.OrdinalIgnoreCase),
            Snapshots: [snapshot],
            CashFlows: cashFlows,
            Fills: includeFills
                ? [new FillEvent(Guid.NewGuid(), "AAPL", "buy", 10L, 40m, 0.5m, startedAt.AddMinutes(15), "ORD-1")]
                : [],
            Metrics: metrics,
            Ledger: CreateLedger(startedAt, completedAt),
            ElapsedTime: TimeSpan.FromHours(2),
            TotalEventsProcessed: 32);

        return new StrategyRunEntry(
            RunId: runId,
            StrategyId: "continuity-strategy",
            StrategyName: "Continuity Strategy",
            RunType: RunType.Backtest,
            StartedAt: startedAt,
            EndedAt: completedAt,
            Metrics: result,
            DatasetReference: "dataset/us-equities/2026-q2",
            FeedReference: "synthetic:equities",
            PortfolioId: "continuity-portfolio",
            LedgerReference: "continuity-ledger",
            AuditReference: $"audit-{runId}",
            Engine: "MeridianNative",
            ParameterSet: new Dictionary<string, string>
            {
                ["rebalance"] = "weekly"
            },
            FundProfileId: "alpha-credit",
            FundDisplayName: "Alpha Credit",
            PromotionState: promotionState);
    }

    private static StrategyRunContinuityService BuildContinuityService(
        StrategyRunStore store,
        IReadOnlyList<StrategyPromotionRecord>? promotionRecords = null)
    {
        var readService = new StrategyRunReadService(
            store,
            new PortfolioReadService(),
            new LedgerReadService(),
            new StubPromotionRecordStore(promotionRecords ?? []));
        return new StrategyRunContinuityService(
            readService,
            new CashFlowProjectionService(store),
            new ReconciliationRunService(
                readService,
                new ReconciliationProjectionService(),
                new InMemoryReconciliationRunRepository()));
    }

    private static StrategyPromotionRecord BuildPromotionRecord(string runId, string sourceRunId, string? targetRunId)
        => new(
            PromotionId: $"promotion-{runId}",
            TimestampUtc: new DateTimeOffset(2026, 4, 2, 16, 0, 0, TimeSpan.Zero),
            StrategyId: "continuity-strategy",
            StrategyName: "Continuity Strategy",
            SourceRunId: sourceRunId,
            TargetRunId: targetRunId,
            SourceRunType: RunType.Backtest,
            TargetRunType: RunType.Paper,
            Decision: PromotionDecisionKinds.Approved,
            ApprovedBy: "test-operator",
            Reason: "test fixture");

    private sealed class StubPromotionRecordStore(IReadOnlyList<StrategyPromotionRecord> records) : IPromotionRecordStore
    {
        public Task<IReadOnlyList<StrategyPromotionRecord>> LoadAllAsync(CancellationToken ct = default) =>
            Task.FromResult(records);

        public Task AppendAsync(StrategyPromotionRecord record, CancellationToken ct = default) =>
            Task.CompletedTask;
    }

    private static global::Meridian.Ledger.Ledger CreateLedger(DateTimeOffset startedAt, DateTimeOffset completedAt)
    {
        var ledger = new global::Meridian.Ledger.Ledger();
        PostBalancedEntry(ledger, startedAt, "Initial capital",
        [
            (LedgerAccounts.Cash, 1_000m, 0m),
            (LedgerAccounts.CapitalAccount, 0m, 1_000m)
        ]);
        PostBalancedEntry(ledger, completedAt, "Buy AAPL",
        [
            (LedgerAccounts.Securities("AAPL"), 400m, 0m),
            (LedgerAccounts.Cash, 0m, 400m)
        ]);
        return ledger;
    }

    private static void PostBalancedEntry(
        global::Meridian.Ledger.Ledger ledger,
        DateTimeOffset timestamp,
        string description,
        IReadOnlyList<(LedgerAccount Account, decimal Debit, decimal Credit)> lines)
    {
        var journalId = Guid.NewGuid();
        var ledgerLines = lines
            .Select(line => new LedgerEntry(
                Guid.NewGuid(),
                journalId,
                timestamp,
                line.Account,
                line.Debit,
                line.Credit,
                description))
            .ToArray();
        ledger.Post(new JournalEntry(journalId, timestamp, description, ledgerLines));
    }
}
