using FluentAssertions;
using Meridian.Backtesting.Sdk;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Operations;
using Meridian.Ledger;
using Meridian.Storage.Archival;
using Meridian.Storage.Operations;
using Meridian.Strategies.Interfaces;
using Meridian.Strategies.Live;
using Meridian.Strategies.Models;
using Meridian.Strategies.Storage;
using Meridian.Strategies.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Meridian.Tests.Strategies;

public sealed class StrategyRunLedgerArchiveTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "meridian-ledger-archive-" + Guid.NewGuid().ToString("N"));
    private static readonly DateTimeOffset Timestamp = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private JsonlStrategyRunLedgerArchive Archive() => new(_root, new AtomicFileWriterAdapter());

    private static Meridian.Ledger.Ledger Ledger(decimal amount = 100m)
    {
        var ledger = new Meridian.Ledger.Ledger();
        var id = Guid.NewGuid();
        const string description = "Opening cash";
        var dimensions = new LedgerLineDimensionSet(FundId: "synthetic-fund", EntityId: "synthetic-entity")
        {
            AccountingArrangementId = "synthetic-arrangement",
            AccountingArrangementRevisionId = "synthetic-arrangement-revision-2",
            ChartAccountNodeId = "synthetic-chart-node",
            AccountBindingRevisionId = "synthetic-binding-revision-3",
            IntercompanyTransactionId = "synthetic-intercompany-transaction"
        };
        ledger.Post(new(id, Timestamp, description,
            [
                new(Guid.NewGuid(), id, Timestamp, LedgerAccounts.Cash, amount, 0m, description, dimensions),
                new(Guid.NewGuid(), id, Timestamp, LedgerAccounts.RetainedEarnings, 0m, amount, description, dimensions)
            ],
            new(ActivityType: "synthetic-capital", LedgerBook: "synthetic-book", IdempotencyKey: $"synthetic:{id:N}",
                Tags: new Dictionary<string, string> { ["source"] = "synthetic-archive-fixture" })));
        return ledger;
    }

    [Fact]
    public async Task Restart_RestoresExactJournalAndRejectsTamperedArchive()
    {
        var ledger = Ledger();
        var archive = Archive();
        var reference = await archive.SaveAsync("../run-1", ledger);
        var retried = await Archive().SaveAsync("../run-1", ledger);
        retried.Should().Be(reference);

        var replayed = await Archive().LoadAsync(reference);
        replayed.Journal.Should().BeEquivalentTo(ledger.Journal, options => options.WithStrictOrdering(),
            "journal and line identities, metadata and every operational dimension must survive replay");
        replayed.TrialBalance(lineDimensions: ledger.Journal.Single().Lines[0].Dimensions)
            .Should().BeEquivalentTo(ledger.TrialBalance(), "replay must rebuild the retained dimensional scope");
        replayed.GetBalanceAsOf(LedgerAccounts.Cash, Timestamp.AddTicks(-1)).Should().Be(0m);
        replayed.GetBalanceAsOf(LedgerAccounts.Cash, Timestamp).Should().Be(100m);
        var candidate = await Archive().SaveAsync("../run-1", Ledger(101m));
        candidate.Sha256.Should().NotBe(reference.Sha256);
        (await Archive().LoadAsync(candidate)).GetBalance(LedgerAccounts.Cash).Should().Be(101m);

        var file = Directory.GetFiles(Path.Combine(_root, "strategies", "run-ledgers"), "*-" + reference.Sha256 + ".jsonl").Single();
        await File.AppendAllTextAsync(file, " ");
        var load = () => Archive().LoadAsync(reference);
        await load.Should().ThrowAsync<InvalidDataException>().WithMessage("*checksum*");
    }

    [Fact]
    public async Task RunStore_RestartHydratesDetailWhileSummaryAndTerminalRetryStayBounded()
    {
        var history = new FileOperationalCaseHistoryStore(_root);
        var store = new StrategyRunStore(history, Archive());
        var start = StrategyRunEntry.Start("archive-strategy", "Archive Strategy", RunType.Backtest, "archive-run");
        await store.RecordRunAsync(start);
        start = (await store.GetRunByIdAsync(start.RunId))!;
        var metrics = new LiveRunMetricsTracker(100m, start.StartedAt).Build(
            "MeridianNative", new HashSet<string>(), 100m, Ledger(), start.StartedAt.AddMinutes(1));
        var completed = start.Complete(metrics);
        var receipt = await store.RecordLifecycleEventWithOutcomeAsync(completed, StrategyRunLifecycleEventType.Completed);

        var restarted = new StrategyRunStore(new FileOperationalCaseHistoryStore(_root), Archive());
        var retained = (await restarted.GetRunByIdAsync(start.RunId))!;
        retained.Metrics!.Ledger.Journal.Should().BeEquivalentTo(metrics.Ledger.Journal);
        retained.RetainedJournalEntryCount.Should().Be(1);
        retained.OutputMetadata[StrategyRunLedgerReference.HashMetadataKey].Should().HaveLength(64);

        var summary = (await restarted.QueryRunsAsync(new StrategyRunRepositoryQuery())).Single();
        summary.Metrics!.Ledger.JournalEntryCount.Should().Be(0);
        summary.RetainedJournalEntryCount.Should().Be(1);
        var repeatedReceipt = await restarted.RecordLifecycleEventWithOutcomeAsync(retained, StrategyRunLifecycleEventType.Completed);
        repeatedReceipt.Should().BeEquivalentTo(receipt);
        var records = await history.ReadAsync(new OperationalCaseHistoryQuery { CaseType = StrategyRunStore.CaseType });
        records.Should().HaveCount(2, "a hydrated detail retry must identify the original terminal snapshot");
        var altered = retained with { Metrics = retained.Metrics with { Ledger = Ledger(500m) } };
        var replace = () => restarted.RecordRunAsync(altered);
        await replace.Should().ThrowAsync<InvalidDataException>().WithMessage("*reference cannot be changed*");
    }

    [Fact]
    public async Task RejectedTerminalCandidate_CannotPoisonTheValidCompletionArchive()
    {
        var store = new StrategyRunStore(new FileOperationalCaseHistoryStore(_root), Archive());
        var start = StrategyRunEntry.Start("archive-strategy", "Archive Strategy", RunType.Backtest, "retry-run");
        await store.RecordRunAsync(start);
        start = (await store.GetRunByIdAsync(start.RunId))!;
        var metrics = new LiveRunMetricsTracker(100m, start.StartedAt).Build(
            "MeridianNative", new HashSet<string>(), 100m, Ledger(), start.StartedAt.AddMinutes(1));
        var invalid = start.Complete(metrics) with { StartedAt = start.StartedAt.AddTicks(1) };
        var reject = () => store.RecordRunAsync(invalid);
        await reject.Should().ThrowAsync<InvalidOperationException>().WithMessage("*immutable run identity*");

        await store.RecordRunAsync(start.Complete(metrics with { Ledger = Ledger(200m) }));
        var retained = (await store.GetRunByIdAsync(start.RunId))!;
        retained.Metrics!.Ledger.GetBalance(LedgerAccounts.Cash).Should().Be(200m);
    }

    [Fact]
    public async Task FailedDiagnosticMetrics_DoNotAcquireCompletionOnlyOutputMetadata()
    {
        var store = new StrategyRunStore(new FileOperationalCaseHistoryStore(_root), Archive());
        var start = StrategyRunEntry.Start("failed-strategy", "Failed Strategy", RunType.Backtest, "failed-run");
        await store.RecordRunAsync(start);
        start = (await store.GetRunByIdAsync(start.RunId))!;
        var metrics = new LiveRunMetricsTracker(100m, start.StartedAt).Build("MeridianNative", new HashSet<string>(),
            100m, Ledger(), start.StartedAt.AddMinutes(1));

        await store.RecordRunAsync(start.Fail(reason: "Synthetic failure") with { Metrics = metrics });

        var diagnostic = (await store.GetRunByIdAsync(start.RunId))!;
        diagnostic.LastLifecycleEvent.Should().Be(StrategyRunLifecycleEventType.Failed);
        diagnostic.OutputMetadata.Should().BeEmpty();
        diagnostic.Metrics!.Ledger.JournalEntryCount.Should().Be(1);
    }

    [Fact]
    public async Task ResearchRetention_ArchivesFinalLotEvidenceOutsideBoundedHistory()
    {
        var history = new FileOperationalCaseHistoryStore(_root);
        var archive = Archive();
        var store = new StrategyRunStore(history, archive);
        var openLot = new OpenLot(Guid.NewGuid(), "SPY", 2m, 50m, Timestamp, Guid.NewGuid(), "primary")
        { UnitKind = SecurityLotUnitKind.Shares, Multiplier = 1m, EntryCommission = 0.25m };
        var closedLot = new ClosedLot(Guid.NewGuid(), "SPY", 1m, 40m, Timestamp.AddDays(-2), Guid.NewGuid(),
            50m, Timestamp, Guid.NewGuid(), "primary")
        { UnitKind = SecurityLotUnitKind.Shares, Multiplier = 1m, CostBasis = 40m, ExitCommission = 0.1m };
        var account = new FinancialAccountSnapshot("primary", "Primary", FinancialAccountKind.Brokerage, null,
            100m, 0m, 100m, 0m, 200m, new Dictionary<string, Position>(), new FinancialAccountRules(), [openLot], [closedLot]);
        var final = new PortfolioSnapshot(Timestamp, new DateOnly(2026, 1, 1), 100m, 0m, 100m, 0m, 200m, 0m,
            new Dictionary<string, Position>(), new Dictionary<string, FinancialAccountSnapshot> { ["primary"] = account },
            [new DividendCashFlow(Timestamp, 1m, "SPY", 2, 0.5m, "primary")]);
        var result = new LiveRunMetricsTracker(100m, Timestamp).Build("MeridianNative", new HashSet<string>(),
            200m, Ledger(), Timestamp.AddHours(1)) with
        { Snapshots = [final] };
        var recorder = new StrategyRunResearchRecorder(store, NullLogger<StrategyRunResearchRecorder>.Instance, archive);

        var runId = await recorder.RecordAsync(new ResearchRunDescriptor("research", "Research"), result);

        runId.Should().NotBeNull();
        var restarted = new StrategyRunStore(new FileOperationalCaseHistoryStore(_root), Archive());
        var detail = (await restarted.GetRunByIdAsync(runId!))!;
        detail.Metrics!.Snapshots.Should().ContainSingle().Which.Should().BeEquivalentTo(final);
        detail.Metrics.Ledger.JournalEntryCount.Should().Be(1);
        var summary = (await restarted.QueryRunsAsync(new StrategyRunRepositoryQuery())).Single();
        summary.Metrics!.Snapshots.Should().BeEmpty();
        var retainedRecords = await history.ReadAsync(new OperationalCaseHistoryQuery { CaseType = StrategyRunStore.CaseType });
        retainedRecords.Single().Data["strategyRunSnapshotJson"].Should().NotContain(openLot.LotId.ToString());
    }

    [Fact]
    public async Task ConcurrentWriters_PreserveExactlyOneRunIdentity()
    {
        var sameLedger = Ledger();
        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Archive().SaveAsync("concurrent-run", sameLedger)));
        results.Should().OnlyContain(reference => reference == results[0]);
        (await Archive().LoadAsync(results[0])).JournalEntryCount.Should().Be(1);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, true);
    }
}
