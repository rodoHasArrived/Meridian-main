using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Meridian.Backtesting.Sdk;
using Meridian.Contracts.Operations;
using Meridian.Contracts.Workstation;
using Meridian.Storage.Operations;
using Meridian.Strategies.Interfaces;
using Meridian.Strategies.Models;
using Meridian.Strategies.Storage;
using Xunit;

namespace Meridian.Tests.Strategies;

public sealed class StrategyRunStoreTests
{
    [Fact]
    public async Task GetRunByIdAsync_ReturnsRecordedRun()
    {
        var store = new StrategyRunStore();
        var expected = CreateRun("run-a", "strategy-1", RunType.Backtest, startedAt: new DateTimeOffset(2026, 4, 20, 14, 0, 0, TimeSpan.Zero));

        await store.RecordRunAsync(expected);

        var actual = await store.GetRunByIdAsync("run-a");

        actual.Should().Be(expected);
    }

    [Fact]
    public async Task GetRunsByIdsAsync_ReturnsRequestedRunsInInputOrder()
    {
        var store = new StrategyRunStore();
        var runA = CreateRun("run-a", "strategy-1", RunType.Backtest, new DateTimeOffset(2026, 4, 20, 10, 0, 0, TimeSpan.Zero));
        var runB = CreateRun("run-b", "strategy-1", RunType.Paper, new DateTimeOffset(2026, 4, 20, 11, 0, 0, TimeSpan.Zero));
        var runC = CreateRun("run-c", "strategy-2", RunType.Live, new DateTimeOffset(2026, 4, 20, 12, 0, 0, TimeSpan.Zero), endedAt: null);

        await store.RecordRunAsync(runA);
        await store.RecordRunAsync(runB);
        await store.RecordRunAsync(runC);

        var results = await store.GetRunsByIdsAsync(["run-c", "missing", "run-a"]);

        results.Select(static run => run.RunId).Should().Equal("run-c", "run-a");
    }

    [Fact]
    public async Task QueryRunsAsync_FiltersAndOrdersByLastUpdatedDescending()
    {
        var store = new StrategyRunStore();
        var olderCompleted = CreateRun(
            "run-old",
            "strategy-1",
            RunType.Backtest,
            new DateTimeOffset(2026, 4, 20, 8, 0, 0, TimeSpan.Zero),
            endedAt: new DateTimeOffset(2026, 4, 20, 9, 0, 0, TimeSpan.Zero));
        var newestCompleted = CreateRun(
            "run-new",
            "strategy-1",
            RunType.Backtest,
            new DateTimeOffset(2026, 4, 20, 10, 0, 0, TimeSpan.Zero),
            endedAt: new DateTimeOffset(2026, 4, 20, 12, 0, 0, TimeSpan.Zero));
        var runningPaper = CreateRun(
            "run-running",
            "strategy-1",
            RunType.Paper,
            new DateTimeOffset(2026, 4, 20, 13, 0, 0, TimeSpan.Zero),
            endedAt: null);

        await store.RecordRunAsync(olderCompleted);
        await store.RecordRunAsync(newestCompleted);
        await store.RecordRunAsync(runningPaper);

        var results = await store.QueryRunsAsync(new StrategyRunRepositoryQuery(
            StrategyId: "strategy-1",
            RunTypes: [RunType.Backtest],
            Status: StrategyRunStatus.Completed,
            Limit: 10));

        results.Select(static run => run.RunId).Should().Equal("run-new", "run-old");
    }

    [Fact]
    public async Task QueryVisibleRunsAsync_AppliesExactScopeBeforeLimit_AndPreservesLegacyCompatibility()
    {
        var store = new StrategyRunStore();
        var startedAt = new DateTimeOffset(2026, 8, 3, 10, 0, 0, TimeSpan.Zero);

        for (var index = 0; index < 64; index++)
        {
            await store.RecordRunAsync(CreateRun(
                $"foreign-{index:D2}",
                $"covered-call-overwrite:foreign-{index:D2}",
                RunType.Backtest,
                startedAt.AddMinutes(100 + index),
                parameterSet: ScopeParameters("tenant-b", "company-b")));
        }

        await store.RecordRunAsync(CreateRun(
            "partial-scope",
            "covered-call-overwrite:partial",
            RunType.Backtest,
            startedAt.AddMinutes(90),
            parameterSet: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["workstationTenantId"] = "tenant-a"
            }));
        await store.RecordRunAsync(CreateRun(
            "blank-scope",
            "strategy-with-blank-scope",
            RunType.Backtest,
            startedAt.AddMinutes(80),
            parameterSet: ScopeParameters("tenant-a", " ")));
        await store.RecordRunAsync(CreateRun(
            "legacy-global-covered-call",
            "covered-call-overwrite",
            RunType.Backtest,
            startedAt.AddMinutes(70)));
        await store.RecordRunAsync(CreateRun(
            "local-global-covered-call",
            "covered-call-overwrite",
            RunType.Backtest,
            startedAt.AddMinutes(3),
            parameterSet: ScopeParameters(" tenant-a ", " company-a ")));
        var localScopedCoveredCall = CreateRun(
            "local-scoped-covered-call",
            "covered-call-overwrite:local",
            RunType.Backtest,
            startedAt.AddMinutes(2),
            parameterSet: ScopeParameters("tenant-a", "company-a"));
        await store.RecordRunAsync(localScopedCoveredCall);
        await store.RecordRunAsync(localScopedCoveredCall with
        {
            EndedAt = startedAt.AddMinutes(4)
        });
        await store.RecordRunAsync(CreateRun(
            "legacy-unscoped",
            "legacy-strategy",
            RunType.Backtest,
            startedAt.AddMinutes(1)));

        var scoped = await store.QueryVisibleRunsAsync(
            new StrategyRunRepositoryQuery(Limit: 3),
            new StrategyRunRepositoryScope("tenant-a", "company-a"));
        var foreignScope = await store.QueryVisibleRunsAsync(
            new StrategyRunRepositoryQuery(Limit: 3),
            new StrategyRunRepositoryScope("tenant-a", "company-other"));
        var unscoped = await store.QueryVisibleRunsAsync(
            new StrategyRunRepositoryQuery(Limit: 10),
            scope: null);

        scoped.Select(static run => run.RunId).Should().Equal(
            "local-scoped-covered-call",
            "local-global-covered-call",
            "legacy-unscoped");
        foreignScope.Select(static run => run.RunId).Should().Equal("legacy-unscoped");
        unscoped.Select(static run => run.RunId).Should().Equal("legacy-unscoped");
    }

    [Fact]
    public async Task RecordRunAsync_ReplacesExistingRunAcrossIndexes()
    {
        var store = new StrategyRunStore();
        var original = CreateRun(
            "run-replace",
            "strategy-1",
            RunType.Backtest,
            new DateTimeOffset(2026, 4, 20, 8, 0, 0, TimeSpan.Zero),
            endedAt: new DateTimeOffset(2026, 4, 20, 9, 0, 0, TimeSpan.Zero));
        var updated = original with
        {
            EndedAt = new DateTimeOffset(2026, 4, 20, 11, 30, 0, TimeSpan.Zero),
            TerminalStatus = StrategyRunStatus.Failed
        };

        await store.RecordRunAsync(original);
        await store.RecordRunAsync(updated);

        var byId = await store.GetRunByIdAsync("run-replace");
        var byStrategy = new List<StrategyRunEntry>();
        await foreach (var run in store.GetRunsAsync("strategy-1"))
        {
            byStrategy.Add(run);
        }

        var queried = await store.QueryRunsAsync(new StrategyRunRepositoryQuery(
            StrategyId: "strategy-1",
            Status: StrategyRunStatus.Failed,
            Limit: 10));

        byId.Should().Be(updated);
        byStrategy.Should().ContainSingle();
        byStrategy[0].Should().Be(updated);
        queried.Should().ContainSingle().Which.Should().Be(updated);
    }

    [Fact]
    public async Task DurableStore_ReplaysStartedAndCompletedRunAfterRestart()
    {
        var dataRoot = CreateDataRoot();
        try
        {
            var history = new FileOperationalCaseHistoryStore(dataRoot);
            var store = new StrategyRunStore(history);
            var started = StrategyRunEntry.StartWithEvidence(
                "strategy-1",
                "Strategy One",
                RunType.Backtest,
                "run-durable",
                datasetReference: "dataset:prices",
                engine: "MeridianNative",
                parameterSet: new Dictionary<string, string> { ["lookback"] = "20" },
                operatorAcceptanceCriteria: ["Operator approved the retained backtest evidence."],
                retainedEvidenceReferences: ["evidence://strategy-runs/run-durable"],
                accountingRecordReferences: ["ledger://books/11111111-1111-1111-1111-111111111111/accounts/run-durable"],
                approvalReferences: ["approval://strategy-runs/run-durable"],
                paperValidationReferences: ["workflow://fund/22222222-2222-2222-2222-222222222222"],
                governedReportReferences: ["reporting-run://run-durable/manifest"]);

            await store.RecordRunAsync(started);
            await store.RecordRunAsync(started.Complete(metrics: null));

            var restarted = new StrategyRunStore(new FileOperationalCaseHistoryStore(dataRoot));
            var replayed = await restarted.GetRunByIdAsync("run-durable");
            var events = await history.ReadAsync(new OperationalCaseHistoryQuery
            {
                CaseType = StrategyRunStore.CaseType
            });

            replayed.Should().NotBeNull();
            replayed!.LastLifecycleEvent.Should().Be(StrategyRunLifecycleEventType.Completed);
            replayed.EndedAt.Should().NotBeNull();
            replayed.InputHashSha256.Should().Be(started.InputHashSha256);
            replayed.OperatorAcceptanceCriteria.Should()
                .ContainSingle("Operator approved the retained backtest evidence.");
            replayed.RetainedEvidenceReferences.Should().ContainSingle("evidence://strategy-runs/run-durable");
            replayed.AccountingRecordReferences.Should().ContainSingle("ledger://books/11111111-1111-1111-1111-111111111111/accounts/run-durable");
            replayed.ApprovalReferences.Should().ContainSingle("approval://strategy-runs/run-durable");
            replayed.PaperValidationReferences.Should().ContainSingle("workflow://fund/22222222-2222-2222-2222-222222222222");
            replayed.GovernedReportReferences.Should().ContainSingle("reporting-run://run-durable/manifest");
            events.Select(static item => item.EventType).Should().Equal("Started", "Completed");
            events.Should().OnlyContain(static item =>
                item.Data.ContainsKey("strategyRunSnapshotJson") &&
                item.Data["schemaVersion"] == StrategyRunStore.SnapshotSchemaVersion);
            events[0].Data[FileOperationalCaseHistoryStore.ExpectedPreviousCaseSequenceDataKey]
                .Should().Be("0");
            events[1].Data[FileOperationalCaseHistoryStore.ExpectedPreviousCaseSequenceDataKey]
                .Should().Be(events[0].Sequence.ToString(System.Globalization.CultureInfo.InvariantCulture));
            events[1].Data[FileOperationalCaseHistoryStore.ExpectedPreviousCaseRecordHashDataKey]
                .Should().Be(events[0].RecordHashSha256);
        }
        finally
        {
            Directory.Delete(dataRoot, recursive: true);
        }
    }

    [Theory]
    [InlineData(StrategyRunLifecycleEventType.Started, OperationTerminalState.Succeeded)]
    [InlineData(StrategyRunLifecycleEventType.Paused, OperationTerminalState.Succeeded)]
    [InlineData(StrategyRunLifecycleEventType.Completed, OperationTerminalState.Succeeded)]
    [InlineData(StrategyRunLifecycleEventType.StartFailed, OperationTerminalState.Failed)]
    [InlineData(StrategyRunLifecycleEventType.PauseFailed, OperationTerminalState.Failed)]
    [InlineData(StrategyRunLifecycleEventType.Failed, OperationTerminalState.Failed)]
    [InlineData(StrategyRunLifecycleEventType.StopFailed, OperationTerminalState.Failed)]
    [InlineData(StrategyRunLifecycleEventType.Cancelled, OperationTerminalState.Blocked)]
    [InlineData(StrategyRunLifecycleEventType.EvidencePersistenceFailed, OperationTerminalState.CompletedWithWarnings)]
    public async Task DurableStore_RetainsValidTerminalOutcome(
        StrategyRunLifecycleEventType eventType,
        OperationTerminalState expectedState)
    {
        var dataRoot = CreateDataRoot();
        try
        {
            var history = new FileOperationalCaseHistoryStore(dataRoot);
            var store = new StrategyRunStore(history);
            var started = StrategyRunEntry.Start("strategy-1", "Strategy One", RunType.Paper, $"run-{eventType}");
            var exception = new InvalidOperationException($"Simulated {eventType} failure.");
            var terminal = eventType switch
            {
                StrategyRunLifecycleEventType.Started => started,
                StrategyRunLifecycleEventType.Paused => started.Paused(),
                StrategyRunLifecycleEventType.Completed => started.Complete(metrics: null),
                StrategyRunLifecycleEventType.StartFailed => started.StartFailed(exception),
                StrategyRunLifecycleEventType.PauseFailed => started.PauseFailed(exception),
                StrategyRunLifecycleEventType.Failed => started.Fail(exception),
                StrategyRunLifecycleEventType.StopFailed => started.StopFailed(exception),
                StrategyRunLifecycleEventType.Cancelled => started.Cancel(exception),
                StrategyRunLifecycleEventType.EvidencePersistenceFailed => started.EvidencePersistenceFailed(
                    exception,
                    "External action completed; final evidence append failed."),
                _ => throw new ArgumentOutOfRangeException(nameof(eventType))
            };

            await store.RecordLifecycleEventAsync(terminal, eventType);

            var retained = await history.ReadAsync(new OperationalCaseHistoryQuery
            {
                CaseId = $"strategy-run:{started.RunId}"
            });
            retained.Should().ContainSingle();
            retained[0].TerminalOutcome.Should().NotBeNull();
            retained[0].TerminalOutcome!.State.Should().Be(expectedState);
            VerifiedOperationOutcomeValidator.Validate(retained[0].TerminalOutcome!).Should().BeEmpty();
            if (eventType == StrategyRunLifecycleEventType.EvidencePersistenceFailed)
            {
                retained[0].TerminalOutcome!.Recovery.Should().ContainSingle()
                    .Which.Should().Match<OperationRecoveryAction>(action =>
                        action.ActionId == "reconcile-external-state" &&
                        !action.Retryable &&
                        action.Guidance.Contains("Do not repeat", StringComparison.Ordinal));
            }
        }
        finally
        {
            Directory.Delete(dataRoot, recursive: true);
        }
    }

    [Fact]
    public void ComputeInputHash_IsDeterministicAcrossParameterInsertionOrder()
    {
        var first = StrategyRunEntry.ComputeInputHash(
            "strategy-1",
            "Strategy One",
            RunType.Backtest,
            "dataset",
            null,
            "MeridianNative",
            new Dictionary<string, string> { ["window"] = "20", ["threshold"] = "0.5" });
        var second = StrategyRunEntry.ComputeInputHash(
            "strategy-1",
            "Strategy One",
            RunType.Backtest,
            "dataset",
            null,
            "MeridianNative",
            new Dictionary<string, string> { ["threshold"] = "0.5", ["window"] = "20" });

        first.Should().Be(second).And.HaveLength(64);
    }

    [Fact]
    public void ComputeInputHash_BindsRunLineageAndOperationalScope()
    {
        var baseline = StrategyRunEntry.ComputeInputHash(
            "strategy-1",
            "Strategy One",
            RunType.Backtest,
            "dataset",
            "feed",
            "MeridianNative",
            new Dictionary<string, string> { ["window"] = "20" },
            parentRunId: "parent-1",
            portfolioId: "portfolio-1",
            ledgerReference: "ledger-1",
            auditReference: "audit-1",
            fundProfileId: "fund-1");

        var mutations = new[]
        {
            StrategyRunEntry.ComputeInputHash("strategy-1", "Strategy One", RunType.Backtest, "dataset", "feed", "MeridianNative", new Dictionary<string, string> { ["window"] = "20" }, "parent-2", "portfolio-1", "ledger-1", "audit-1", "fund-1"),
            StrategyRunEntry.ComputeInputHash("strategy-1", "Strategy One", RunType.Backtest, "dataset", "feed", "MeridianNative", new Dictionary<string, string> { ["window"] = "20" }, "parent-1", "portfolio-2", "ledger-1", "audit-1", "fund-1"),
            StrategyRunEntry.ComputeInputHash("strategy-1", "Strategy One", RunType.Backtest, "dataset", "feed", "MeridianNative", new Dictionary<string, string> { ["window"] = "20" }, "parent-1", "portfolio-1", "ledger-2", "audit-1", "fund-1"),
            StrategyRunEntry.ComputeInputHash("strategy-1", "Strategy One", RunType.Backtest, "dataset", "feed", "MeridianNative", new Dictionary<string, string> { ["window"] = "20" }, "parent-1", "portfolio-1", "ledger-1", "audit-2", "fund-1"),
            StrategyRunEntry.ComputeInputHash("strategy-1", "Strategy One", RunType.Backtest, "dataset", "feed", "MeridianNative", new Dictionary<string, string> { ["window"] = "20" }, "parent-1", "portfolio-1", "ledger-1", "audit-1", "fund-2")
        };

        mutations.Should().OnlyContain(hash => !string.Equals(hash, baseline, StringComparison.Ordinal));
    }

    [Fact]
    public void Start_WithoutEvidence_PreservesV2InputHash()
    {
        var started = StrategyRunEntry.Start(
            "strategy-compatibility",
            "Compatibility Strategy",
            RunType.Backtest,
            "run-v2-start",
            datasetReference: "provider-bars/equities/daily",
            feedReference: "provider-feed:daily",
            engine: "MeridianNative",
            parameterSet: new Dictionary<string, string> { ["lookback"] = "20" });

        started.InputHashSha256.Should().Be(ComputeV2Hash(started));
        started.InputHashSha256.Should().NotBe(ComputeV3Hash(started));
    }

    [Fact]
    public async Task Store_WithOnlyBlankEvidence_RetainsV2InputHash()
    {
        var dataRoot = CreateDataRoot();
        try
        {
            var history = new FileOperationalCaseHistoryStore(dataRoot);
            var store = new StrategyRunStore(history);
            var started = StrategyRunEntry.StartWithEvidence(
                "strategy-blank-evidence",
                "Blank Evidence Strategy",
                RunType.Backtest,
                "run-blank-evidence",
                engine: "MeridianNative",
                operatorAcceptanceCriteria: [" ", "\t"],
                retainedEvidenceReferences: [string.Empty],
                accountingRecordReferences: ["  "],
                approvalReferences: ["\r\n"],
                paperValidationReferences: ["\t"],
                governedReportReferences: [" "]);
            var expectedV2Hash = ComputeV2Hash(started);

            await store.RecordRunAsync(started);
            var retained = await store.GetRunByIdAsync(started.RunId);
            var retainedHistory = await history.ReadAsync(new OperationalCaseHistoryQuery
            {
                CaseId = $"strategy-run:{started.RunId}",
                CaseType = StrategyRunStore.CaseType
            });

            started.InputHashSha256.Should().Be(expectedV2Hash);
            retained.Should().NotBeNull();
            retained!.InputHashSha256.Should().Be(expectedV2Hash);
            retained.InputHashSha256.Should().NotBe(ComputeV3Hash(retained));
            retainedHistory.Should().ContainSingle();
            retainedHistory[0].Approvals.Should().BeEmpty();
        }
        finally
        {
            Directory.Delete(dataRoot, recursive: true);
        }
    }

    [Fact]
    public async Task StartWithEvidence_AndStore_RetainV3InputHash()
    {
        var dataRoot = CreateDataRoot();
        try
        {
            var store = new StrategyRunStore(new FileOperationalCaseHistoryStore(dataRoot));
            var started = CreateEvidenceBoundRun("run-v3-evidence");
            var expectedV3Hash = ComputeV3Hash(started);

            await store.RecordRunAsync(started);
            var retained = await store.GetRunByIdAsync(started.RunId);

            started.InputHashSha256.Should().Be(expectedV3Hash);
            started.InputHashSha256.Should().NotBe(ComputeV2Hash(started));
            retained.Should().NotBeNull();
            retained!.InputHashSha256.Should().Be(expectedV3Hash);
        }
        finally
        {
            Directory.Delete(dataRoot, recursive: true);
        }
    }

    [Fact]
    public async Task DurableStore_ReplaysInterimEmptyV3_ThenCanonicalizesNextAppendToV2()
    {
        var dataRoot = CreateDataRoot();
        try
        {
            var history = new FileOperationalCaseHistoryStore(dataRoot);
            var startRequested = StrategyRunEntry.Start(
                    "strategy-interim-v3",
                    "Interim V3 Strategy",
                    RunType.Paper,
                    "run-interim-empty-v3",
                    engine: "BrokerPaper")
                .RequestStart(actorId: "compatibility-operator");
            var interimEmptyV3Hash = ComputeV3Hash(startRequested);
            var interim = startRequested with { InputHashSha256 = interimEmptyV3Hash };
            await AppendRawSnapshotAsync(
                history,
                interim,
                StrategyRunLifecycleEventType.StartRequested);

            var store = new StrategyRunStore(new FileOperationalCaseHistoryStore(dataRoot));
            var replayed = await store.GetRunByIdAsync(interim.RunId);

            replayed.Should().NotBeNull();
            replayed!.InputHashSha256.Should().Be(interimEmptyV3Hash);
            interimEmptyV3Hash.Should().NotBe(ComputeV2Hash(interim));

            await store.RecordRunAsync(interim.Started(actorId: "compatibility-operator"));
            var canonical = await store.GetRunByIdAsync(interim.RunId);
            var retainedEvents = await history.ReadAsync(new OperationalCaseHistoryQuery
            {
                CaseId = $"strategy-run:{interim.RunId}",
                CaseType = StrategyRunStore.CaseType
            });

            canonical.Should().NotBeNull();
            canonical!.InputHashSha256.Should().Be(ComputeV2Hash(canonical));
            retainedEvents.Should().HaveCount(2);
            retainedEvents[0].InputHashSha256.Should().Be(interimEmptyV3Hash);
            retainedEvents[1].InputHashSha256.Should().Be(ComputeV2Hash(canonical));
        }
        finally
        {
            Directory.Delete(dataRoot, recursive: true);
        }
    }

    [Fact]
    public void ComputeInputHash_BindsEvidenceLoopWithStableCollectionOrdering()
    {
        var baseline = StrategyRunEntry.ComputeEvidenceBoundInputHash(
            "strategy-1",
            "Strategy One",
            RunType.Backtest,
            "dataset",
            "feed",
            "MeridianNative",
            parameterSet: null,
            operatorAcceptanceCriteria: ["Criterion B", "Criterion A"],
            retainedEvidenceReferences: ["evidence://strategy-runs/run-1", "evidence://strategy-runs/run-2"],
            accountingRecordReferences: ["ledger://books/book-1/accounts/run-1"],
            approvalReferences: ["approval://strategy-runs/run-1"],
            paperValidationReferences: ["workflow://fund/fund-1"],
            governedReportReferences: ["reporting-run://run-1/manifest"]);
        var reordered = StrategyRunEntry.ComputeEvidenceBoundInputHash(
            "strategy-1",
            "Strategy One",
            RunType.Backtest,
            "dataset",
            "feed",
            "MeridianNative",
            parameterSet: null,
            operatorAcceptanceCriteria: ["Criterion A", "Criterion B"],
            retainedEvidenceReferences: ["evidence://strategy-runs/run-2", "evidence://strategy-runs/run-1"],
            accountingRecordReferences: ["ledger://books/book-1/accounts/run-1"],
            approvalReferences: ["approval://strategy-runs/run-1"],
            paperValidationReferences: ["workflow://fund/fund-1"],
            governedReportReferences: ["reporting-run://run-1/manifest"]);
        var changedCriterion = StrategyRunEntry.ComputeEvidenceBoundInputHash(
            "strategy-1",
            "Strategy One",
            RunType.Backtest,
            "dataset",
            "feed",
            "MeridianNative",
            parameterSet: null,
            operatorAcceptanceCriteria: ["Criterion A", "Criterion C"],
            retainedEvidenceReferences: ["evidence://strategy-runs/run-1", "evidence://strategy-runs/run-2"],
            accountingRecordReferences: ["ledger://books/book-1/accounts/run-1"],
            approvalReferences: ["approval://strategy-runs/run-1"],
            paperValidationReferences: ["workflow://fund/fund-1"],
            governedReportReferences: ["reporting-run://run-1/manifest"]);

        reordered.Should().Be(baseline);
        changedCriterion.Should().NotBe(baseline);
    }

    [Theory]
    [InlineData("criteria")]
    [InlineData("retained-evidence")]
    [InlineData("accounting")]
    [InlineData("approval")]
    [InlineData("paper-validation")]
    [InlineData("governed-report")]
    public async Task DurableStore_RejectsEvidenceLoopMutationBetweenStartedAndCompleted(string changedField)
    {
        var dataRoot = CreateDataRoot();
        try
        {
            var store = new StrategyRunStore(new FileOperationalCaseHistoryStore(dataRoot));
            var started = CreateEvidenceBoundRun("run-evidence-mutation");
            await store.RecordRunAsync(started);

            var completed = started.Complete(metrics: null);
            var changed = changedField switch
            {
                "criteria" => completed with { OperatorAcceptanceCriteria = ["Changed criterion."] },
                "retained-evidence" => completed with { RetainedEvidenceReferences = ["evidence://strategy-runs/changed"] },
                "accounting" => completed with { AccountingRecordReferences = ["ledger://books/book-1/accounts/changed"] },
                "approval" => completed with { ApprovalReferences = ["approval://strategy-runs/changed"] },
                "paper-validation" => completed with { PaperValidationReferences = ["workflow://fund/changed"] },
                "governed-report" => completed with { GovernedReportReferences = ["reporting-run://changed/manifest"] },
                _ => throw new ArgumentOutOfRangeException(nameof(changedField), changedField, null)
            };
            changed = changed with { InputHashSha256 = null };

            var action = () => store.RecordRunAsync(changed);

            await action.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("*immutable run identity changed*");
        }
        finally
        {
            Directory.Delete(dataRoot, recursive: true);
        }
    }

    [Fact]
    public async Task DurableStore_AcceptsAndReplaysUnchangedEvidenceLoopCompletion()
    {
        var dataRoot = CreateDataRoot();
        try
        {
            var history = new FileOperationalCaseHistoryStore(dataRoot);
            var store = new StrategyRunStore(history);
            var started = CreateEvidenceBoundRun("run-evidence-stable");
            var completed = started.Complete(metrics: null);

            await store.RecordRunAsync(started);
            await store.RecordRunAsync(completed);
            await store.RecordRunAsync(completed);

            var restarted = new StrategyRunStore(new FileOperationalCaseHistoryStore(dataRoot));
            var replayed = await restarted.GetRunByIdAsync(started.RunId);
            var retained = await history.ReadAsync(new OperationalCaseHistoryQuery
            {
                CaseId = $"strategy-run:{started.RunId}"
            });

            replayed.Should().NotBeNull();
            replayed!.LastLifecycleEvent.Should().Be(StrategyRunLifecycleEventType.Completed);
            replayed.OperatorAcceptanceCriteria.Should().Equal(started.OperatorAcceptanceCriteria);
            replayed.RetainedEvidenceReferences.Should().Equal(started.RetainedEvidenceReferences);
            replayed.AccountingRecordReferences.Should().Equal(started.AccountingRecordReferences);
            replayed.ApprovalReferences.Should().Equal(started.ApprovalReferences);
            replayed.PaperValidationReferences.Should().Equal(started.PaperValidationReferences);
            replayed.GovernedReportReferences.Should().Equal(started.GovernedReportReferences);
            retained.Should().HaveCount(2);
        }
        finally
        {
            Directory.Delete(dataRoot, recursive: true);
        }
    }

    [Fact]
    public async Task DurableStore_RejectsOperationalScopeMutationAfterFirstLifecycleEvent()
    {
        var dataRoot = CreateDataRoot();
        try
        {
            var store = new StrategyRunStore(new FileOperationalCaseHistoryStore(dataRoot));
            var started = StrategyRunEntry.Start(
                "strategy-1",
                "Strategy One",
                RunType.Paper,
                "run-scope-mutation") with
            {
                ParentRunId = "parent-1",
                FundProfileId = "fund-1",
                InputHashSha256 = null
            };
            await store.RecordRunAsync(started);

            var changed = started.Complete(metrics: null) with
            {
                PortfolioId = "different-portfolio",
                InputHashSha256 = null
            };
            var action = () => store.RecordRunAsync(changed);

            await action.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("*immutable run identity changed*");
        }
        finally
        {
            Directory.Delete(dataRoot, recursive: true);
        }
    }

    [Theory]
    [InlineData("strategy-id")]
    [InlineData("strategy-name")]
    [InlineData("run-type")]
    [InlineData("dataset")]
    [InlineData("feed")]
    [InlineData("engine")]
    [InlineData("parameters")]
    [InlineData("parent-run")]
    [InlineData("portfolio")]
    [InlineData("ledger")]
    [InlineData("audit")]
    [InlineData("fund-profile")]
    public async Task RecordRunAsync_CompatibleHashCanonicalization_RejectsImmutableInputMutation(
        string changedField)
    {
        var dataRoot = CreateDataRoot();
        try
        {
            var history = new FileOperationalCaseHistoryStore(dataRoot);
            var requested = CreateEvidenceBoundRun($"run-compatible-hash-{changedField}")
                .RequestStart(actorId: "compatibility-operator");
            var retainedV2 = requested with { InputHashSha256 = ComputeV2Hash(requested) };
            await AppendRawSnapshotAsync(
                history,
                retainedV2,
                StrategyRunLifecycleEventType.StartRequested);

            var started = retainedV2.Started(actorId: "compatibility-operator");
            var changed = MutateCanonicalInput(started, changedField) with { InputHashSha256 = null };
            var store = new StrategyRunStore(new FileOperationalCaseHistoryStore(dataRoot));

            var action = () => store.RecordRunAsync(changed);

            await action.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("*immutable run identity changed*");
        }
        finally
        {
            Directory.Delete(dataRoot, recursive: true);
        }
    }

    [Theory]
    [InlineData("v2", "dataset")]
    [InlineData("v2", "feed")]
    [InlineData("v2", "engine")]
    [InlineData("v2", "parameters")]
    [InlineData("v3", "dataset")]
    [InlineData("v3", "feed")]
    [InlineData("v3", "engine")]
    [InlineData("v3", "parameters")]
    public async Task RecordRunAsync_SameVersionCanonicalRehash_RejectsImmutableInputMutation(
        string hashVersion,
        string changedField)
    {
        var dataRoot = CreateDataRoot();
        try
        {
            var runId = $"run-{hashVersion}-rehash-{changedField}";
            var requested = hashVersion switch
            {
                "v2" => StrategyRunEntry.Start(
                        "strategy-compatibility",
                        "Compatibility Strategy",
                        RunType.Backtest,
                        runId,
                        datasetReference: "provider-bars/equities/daily",
                        feedReference: "provider-feed:daily",
                        engine: "MeridianNative",
                        parameterSet: new Dictionary<string, string>(StringComparer.Ordinal)
                        {
                            ["lookback"] = "20"
                        })
                    .RequestStart(actorId: "compatibility-operator"),
                "v3" => CreateEvidenceBoundRun(runId)
                    .RequestStart(actorId: "compatibility-operator"),
                _ => throw new ArgumentOutOfRangeException(nameof(hashVersion), hashVersion, null)
            };
            var store = new StrategyRunStore(new FileOperationalCaseHistoryStore(dataRoot));
            await store.RecordRunAsync(requested);
            var changed = MutateCanonicalInput(
                requested.Started(actorId: "compatibility-operator"),
                changedField) with
            {
                InputHashSha256 = null
            };

            var action = () => store.RecordRunAsync(changed);

            await action.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("*immutable run identity changed*");
        }
        finally
        {
            Directory.Delete(dataRoot, recursive: true);
        }
    }

    [Fact]
    public async Task DurableStore_RejectsSuppliedInputHashThatDoesNotMatchCanonicalInputs()
    {
        var dataRoot = CreateDataRoot();
        try
        {
            var store = new StrategyRunStore(new FileOperationalCaseHistoryStore(dataRoot));
            var run = StrategyRunEntry.Start(
                "strategy-1",
                "Strategy One",
                RunType.Paper,
                "run-forged-hash") with
            {
                InputHashSha256 = new string('a', 64)
            };

            var action = () => store.RecordRunAsync(run);

            await action.Should().ThrowAsync<ArgumentException>()
                .WithMessage("*does not match the canonical hash*");
        }
        finally
        {
            Directory.Delete(dataRoot, recursive: true);
        }
    }

    [Fact]
    public async Task QueryRunsAsync_ProjectsPausedAndStoppedLifecycleStates()
    {
        var store = new StrategyRunStore();
        var paused = StrategyRunEntry.Start(
            "strategy-paused",
            "Paused Strategy",
            RunType.Paper,
            "run-paused").Paused();
        var stopped = StrategyRunEntry.Start(
            "strategy-stopped",
            "Stopped Strategy",
            RunType.Paper,
            "run-stopped").Complete(metrics: null) with
        {
            TerminalStatus = StrategyRunStatus.Stopped
        };
        await store.RecordRunAsync(paused);
        await store.RecordRunAsync(stopped);

        var pausedRuns = await store.QueryRunsAsync(new StrategyRunRepositoryQuery(
            Status: StrategyRunStatus.Paused));
        var stoppedRuns = await store.QueryRunsAsync(new StrategyRunRepositoryQuery(
            Status: StrategyRunStatus.Stopped));

        pausedRuns.Should().ContainSingle().Which.RunId.Should().Be("run-paused");
        stoppedRuns.Should().ContainSingle().Which.RunId.Should().Be("run-stopped");
    }

    [Fact]
    public async Task Replay_RejectsSucceededReceiptBoundToFailedLifecycleEvent()
    {
        var dataRoot = CreateDataRoot();
        try
        {
            var history = new FileOperationalCaseHistoryStore(dataRoot);
            var failed = StrategyRunEntry.Start(
                "strategy-1",
                "Strategy One",
                RunType.Paper,
                "run-outcome-binding").StartFailed(new InvalidOperationException("start failed"));
            var snapshotJson = JsonSerializer.Serialize(failed);
            var snapshotHash = Convert.ToHexStringLower(
                SHA256.HashData(Encoding.UTF8.GetBytes(snapshotJson)));
            var eventId = $"strategy-run:{failed.RunId}:{failed.LastLifecycleEvent}:{snapshotHash}";
            var eventAt = failed.LifecycleEventAtUtc!.Value;
            var evidence = new OperationEvidenceReference(
                "outcome-binding-evidence",
                "test",
                "Synthetic receipt used to verify lifecycle binding.",
                ContentHashSha256: failed.InputHashSha256,
                CapturedAtUtc: eventAt);
            var wronglySucceeded = new VerifiedOperationOutcome(
                eventId,
                $"strategy-run.{StrategyRunLifecycleEventType.StartFailed}",
                OperationTerminalState.Succeeded,
                failed.StartedAt,
                eventAt,
                1,
                failed.CorrelationId,
                failed.InputHashSha256,
                [new OperationPostcondition(
                    "synthetic-success",
                    "Synthetic success receipt.",
                    OperationPostconditionState.Satisfied,
                    Required: true,
                    EvidenceIds: [evidence.EvidenceId])],
                [evidence],
                [],
                [],
                []);
            await history.AppendAsync(new OperationalCaseHistoryAppendRequest
            {
                CaseId = $"strategy-run:{failed.RunId}",
                CaseType = StrategyRunStore.CaseType,
                HistoryEventId = eventId,
                EventType = failed.LastLifecycleEvent.ToString(),
                OccurredAtUtc = eventAt,
                ActorId = failed.ActorId!,
                Reason = failed.Reason!,
                CorrelationId = failed.CorrelationId!,
                InputHashSha256 = failed.InputHashSha256!,
                Data = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["schemaVersion"] = StrategyRunStore.SnapshotSchemaVersion,
                    ["strategyRunSnapshotJson"] = snapshotJson,
                    [FileOperationalCaseHistoryStore.ExpectedPreviousCaseSequenceDataKey] = "0"
                },
                Transition = new OperationalCaseStateTransition
                {
                    PreviousState = null,
                    CurrentState = failed.LastLifecycleEvent.ToString(),
                    TransitionedAtUtc = eventAt
                },
                Evidence = [evidence],
                TerminalOutcome = wronglySucceeded
            });
            var restarted = new StrategyRunStore(new FileOperationalCaseHistoryStore(dataRoot));

            var action = () => restarted.GetRunByIdAsync(failed.RunId);

            await action.Should().ThrowAsync<InvalidDataException>()
                .WithMessage("*terminal receipt is not bound*lifecycle event 'StartFailed'*");
        }
        finally
        {
            Directory.Delete(dataRoot, recursive: true);
        }
    }

    [Fact]
    public async Task RecordRunAsync_RejectsRegressionAfterCompletedRun()
    {
        var dataRoot = CreateDataRoot();
        try
        {
            var history = new FileOperationalCaseHistoryStore(dataRoot);
            var store = new StrategyRunStore(history);
            var startedAt = new DateTimeOffset(2026, 7, 19, 12, 0, 0, TimeSpan.Zero);
            var started = StrategyRunEntry.Start("strategy-1", "Strategy One", RunType.Paper, "run-regression") with
            {
                StartedAt = startedAt,
                LifecycleEventAtUtc = startedAt
            };
            var completedAt = startedAt.AddMinutes(5);
            var completed = started.Complete(metrics: null) with
            {
                EndedAt = completedAt,
                LifecycleEventAtUtc = completedAt
            };
            await store.RecordRunAsync(started);
            await store.RecordRunAsync(completed);
            var regressed = started.Paused() with
            {
                LifecycleEventAtUtc = completedAt.AddMinutes(1)
            };

            var action = () => store.RecordLifecycleEventAsync(
                regressed,
                StrategyRunLifecycleEventType.Paused);

            await action.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("*Completed*Paused*not allowed*");
            (await history.ReadAsync(new OperationalCaseHistoryQuery
            {
                CaseId = $"strategy-run:{started.RunId}"
            })).Should().HaveCount(2);
        }
        finally
        {
            Directory.Delete(dataRoot, recursive: true);
        }
    }

    [Fact]
    public async Task Replay_RejectsHashValidCompletedToPausedRegression()
    {
        var dataRoot = CreateDataRoot();
        try
        {
            var history = new FileOperationalCaseHistoryStore(dataRoot);
            var writer = new StrategyRunStore(history);
            var startedAt = new DateTimeOffset(2026, 7, 19, 13, 0, 0, TimeSpan.Zero);
            var started = StrategyRunEntry.Start("strategy-1", "Strategy One", RunType.Paper, "run-forged-regression") with
            {
                StartedAt = startedAt,
                LifecycleEventAtUtc = startedAt
            };
            var completedAt = startedAt.AddMinutes(5);
            var completed = started.Complete(metrics: null) with
            {
                EndedAt = completedAt,
                LifecycleEventAtUtc = completedAt
            };
            await writer.RecordRunAsync(started);
            await writer.RecordRunAsync(completed);
            var regressed = started.Paused() with
            {
                LifecycleEventAtUtc = completedAt.AddMinutes(1)
            };
            await AppendRawSnapshotAsync(
                history,
                regressed,
                StrategyRunLifecycleEventType.Completed);

            var restarted = new StrategyRunStore(new FileOperationalCaseHistoryStore(dataRoot));
            var action = () => restarted.GetRunByIdAsync(started.RunId);

            await action.Should().ThrowAsync<InvalidDataException>()
                .WithMessage("*Completed*Paused*not allowed*");
        }
        finally
        {
            Directory.Delete(dataRoot, recursive: true);
        }
    }

    [Fact]
    public async Task Replay_RejectsHashValidLifecycleTimestampBeforeRunStart()
    {
        var dataRoot = CreateDataRoot();
        try
        {
            var history = new FileOperationalCaseHistoryStore(dataRoot);
            var writer = new StrategyRunStore(history);
            var startedAt = new DateTimeOffset(2026, 7, 19, 14, 0, 0, TimeSpan.Zero);
            var started = StrategyRunEntry.Start("strategy-1", "Strategy One", RunType.Paper, "run-forged-time") with
            {
                StartedAt = startedAt,
                LifecycleEventAtUtc = startedAt
            };
            await writer.RecordRunAsync(started);
            var outOfOrder = started.Paused() with
            {
                LifecycleEventAtUtc = startedAt.AddSeconds(-1)
            };
            await AppendRawSnapshotAsync(
                history,
                outOfOrder,
                StrategyRunLifecycleEventType.Started);

            var restarted = new StrategyRunStore(new FileOperationalCaseHistoryStore(dataRoot));
            var action = () => restarted.GetRunByIdAsync(started.RunId);

            await action.Should().ThrowAsync<InvalidDataException>()
                .WithMessage("*timestamp precedes the run start*");
        }
        finally
        {
            Directory.Delete(dataRoot, recursive: true);
        }
    }

    [Fact]
    public async Task RecordRunAsync_RepeatedCanonicalTerminalSnapshotIsIdempotent()
    {
        var dataRoot = CreateDataRoot();
        try
        {
            var history = new FileOperationalCaseHistoryStore(dataRoot);
            var store = new StrategyRunStore(history);
            var started = StrategyRunEntry.Start("strategy-1", "Strategy One", RunType.Paper, "run-idempotent");
            var completed = started.Complete(metrics: null);
            await store.RecordRunAsync(started);
            await store.RecordRunAsync(completed);

            await store.RecordRunAsync(completed);

            (await history.ReadAsync(new OperationalCaseHistoryQuery
            {
                CaseId = $"strategy-run:{started.RunId}"
            })).Should().HaveCount(2);
        }
        finally
        {
            Directory.Delete(dataRoot, recursive: true);
        }
    }

    [Fact]
    public async Task RecordRunAsync_RejectsChangedRepeatedTerminalSnapshot()
    {
        var dataRoot = CreateDataRoot();
        try
        {
            var history = new FileOperationalCaseHistoryStore(dataRoot);
            var store = new StrategyRunStore(history);
            var started = StrategyRunEntry.Start("strategy-1", "Strategy One", RunType.Paper, "run-terminal-mutation");
            var completed = started.Complete(metrics: null);
            await store.RecordRunAsync(started);
            await store.RecordRunAsync(completed);
            var changed = completed with
            {
                Reason = "Changed after terminal retention."
            };

            var action = () => store.RecordRunAsync(changed);

            await action.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("*Completed*Completed*not allowed*");
            (await history.ReadAsync(new OperationalCaseHistoryQuery
            {
                CaseId = $"strategy-run:{started.RunId}"
            })).Should().HaveCount(2);
        }
        finally
        {
            Directory.Delete(dataRoot, recursive: true);
        }
    }

    [Fact]
    public async Task Replay_RejectsHashValidChangedRepeatedTerminalSnapshot()
    {
        var dataRoot = CreateDataRoot();
        try
        {
            var history = new FileOperationalCaseHistoryStore(dataRoot);
            var writer = new StrategyRunStore(history);
            var started = StrategyRunEntry.Start("strategy-1", "Strategy One", RunType.Paper, "run-forged-terminal");
            var completed = started.Complete(metrics: null);
            await writer.RecordRunAsync(started);
            await writer.RecordRunAsync(completed);
            var changed = completed with
            {
                Reason = "Forged changed terminal snapshot."
            };
            await AppendRawSnapshotAsync(
                history,
                changed,
                StrategyRunLifecycleEventType.Completed);

            var restarted = new StrategyRunStore(new FileOperationalCaseHistoryStore(dataRoot));
            var action = () => restarted.GetRunByIdAsync(started.RunId);

            await action.Should().ThrowAsync<InvalidDataException>()
                .WithMessage("*Completed*Completed*not allowed*");
        }
        finally
        {
            Directory.Delete(dataRoot, recursive: true);
        }
    }

    [Fact]
    public async Task RecordRunAsync_RejectsChangedRepeatedRecoveryAttemptSnapshot()
    {
        var dataRoot = CreateDataRoot();
        try
        {
            var history = new FileOperationalCaseHistoryStore(dataRoot);
            var store = new StrategyRunStore(history);
            var started = StrategyRunEntry.Start("strategy-1", "Strategy One", RunType.Paper, "run-recovery-mutation");
            var paused = started.Paused();
            var recovery = paused.RecoveryAttempted(started.RunId, attemptNumber: 2);
            await store.RecordRunAsync(started);
            await store.RecordRunAsync(paused);
            await store.RecordRunAsync(recovery);
            var changed = recovery with { Reason = "Changed recovery evidence." };

            var action = () => store.RecordRunAsync(changed);

            await action.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("*RecoveryAttempted*RecoveryAttempted*not allowed*");
        }
        finally
        {
            Directory.Delete(dataRoot, recursive: true);
        }
    }

    [Fact]
    public async Task RecordRunAsync_AllowsTerminalOutputMetadataWithoutChangingInputHash()
    {
        var store = new StrategyRunStore();
        var started = StrategyRunEntry.Start(
            "strategy-output",
            "Output Strategy",
            RunType.Backtest,
            "run-output");
        await store.RecordRunAsync(started);

        var completed = (started with
        {
            OutputMetadata = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["coveredCallResult"] = "{\"runId\":\"run-output\"}",
                ["sharpe"] = "1.25"
            }
        }).Complete(metrics: null);

        await store.RecordRunAsync(completed);

        var retained = await store.GetRunByIdAsync(started.RunId);
        retained.Should().NotBeNull();
        retained!.LastLifecycleEvent.Should().Be(StrategyRunLifecycleEventType.Completed);
        retained.InputHashSha256.Should().Be(started.InputHashSha256);
        retained.OutputMetadata.Should().ContainKey("coveredCallResult");
    }

    [Fact]
    public async Task RecordRunAsync_RejectsOutputMetadataBeforeCompletionAndAfterTerminalRetention()
    {
        var store = new StrategyRunStore();
        var started = StrategyRunEntry.Start(
            "strategy-output",
            "Output Strategy",
            RunType.Backtest,
            "run-output-guard");
        var premature = started with
        {
            OutputMetadata = new Dictionary<string, string> { ["result"] = "premature" }
        };

        var prematureAction = () => store.RecordRunAsync(premature);
        await prematureAction.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*output metadata*completed lifecycle event*");

        await store.RecordRunAsync(started);
        var completed = (started with
        {
            OutputMetadata = new Dictionary<string, string> { ["result"] = "original" }
        }).Complete(metrics: null);
        await store.RecordRunAsync(completed);

        var changed = completed with
        {
            OutputMetadata = new Dictionary<string, string> { ["result"] = "overwritten" }
        };
        var changedAction = () => store.RecordRunAsync(changed);
        await changedAction.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*output metadata changed after the terminal lifecycle event*");
    }

    private static async Task AppendRawSnapshotAsync(
        FileOperationalCaseHistoryStore history,
        StrategyRunEntry entry,
        StrategyRunLifecycleEventType previousEvent)
    {
        var eventAt = entry.LifecycleEventAtUtc!.Value;
        var snapshotJson = JsonSerializer.Serialize(entry);
        var snapshotHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(snapshotJson)));
        var caseId = $"strategy-run:{entry.RunId}";
        var predecessor = (await history.ReadAsync(new OperationalCaseHistoryQuery
        {
            CaseId = caseId,
            CaseType = StrategyRunStore.CaseType
        })).LastOrDefault();
        var data = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["schemaVersion"] = StrategyRunStore.SnapshotSchemaVersion,
            ["strategyRunSnapshotJson"] = snapshotJson,
            [FileOperationalCaseHistoryStore.ExpectedPreviousCaseSequenceDataKey] =
                (predecessor?.Sequence ?? 0).ToString(System.Globalization.CultureInfo.InvariantCulture)
        };
        if (predecessor is not null)
        {
            data[FileOperationalCaseHistoryStore.ExpectedPreviousCaseRecordHashDataKey] =
                predecessor.RecordHashSha256;
        }

        await history.AppendAsync(new OperationalCaseHistoryAppendRequest
        {
            CaseId = caseId,
            CaseType = StrategyRunStore.CaseType,
            HistoryEventId = $"strategy-run:{entry.RunId}:{entry.LastLifecycleEvent}:{snapshotHash}",
            EventType = entry.LastLifecycleEvent.ToString(),
            OccurredAtUtc = eventAt,
            ActorId = entry.ActorId!,
            Reason = entry.Reason!,
            CorrelationId = entry.CorrelationId!,
            InputHashSha256 = entry.InputHashSha256!,
            Data = data,
            Transition = new OperationalCaseStateTransition
            {
                PreviousState = predecessor is null ? null : previousEvent.ToString(),
                CurrentState = entry.LastLifecycleEvent.ToString(),
                TransitionedAtUtc = eventAt
            }
        });
    }

    private static StrategyRunEntry CreateRun(
        string runId,
        string strategyId,
        RunType runType,
        DateTimeOffset startedAt,
        DateTimeOffset? endedAt = null,
        StrategyRunStatus? terminalStatus = null,
        IReadOnlyDictionary<string, string>? parameterSet = null)
    {
        return new StrategyRunEntry(
            RunId: runId,
            StrategyId: strategyId,
            StrategyName: strategyId,
            RunType: runType,
            StartedAt: startedAt,
            EndedAt: endedAt,
            Metrics: null,
            PortfolioId: $"{strategyId}-{runId}-portfolio",
            LedgerReference: $"{strategyId}-{runId}-ledger",
            AuditReference: $"{runId}-audit",
            Engine: runType.ToString(),
            TerminalStatus: terminalStatus,
            ParameterSet: parameterSet);
    }

    private static IReadOnlyDictionary<string, string> ScopeParameters(
        string tenantId,
        string companyId) =>
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["workstationTenantId"] = tenantId,
            ["workstationCompanyId"] = companyId
        };

    private static string ComputeV2Hash(StrategyRunEntry entry) =>
        StrategyRunEntry.ComputeInputHash(
            entry.StrategyId,
            entry.StrategyName,
            entry.RunType,
            entry.DatasetReference,
            entry.FeedReference,
            entry.Engine,
            entry.ParameterSet,
            entry.ParentRunId,
            entry.PortfolioId,
            entry.LedgerReference,
            entry.AuditReference,
            entry.FundProfileId);

    private static string ComputeV3Hash(StrategyRunEntry entry) =>
        StrategyRunEntry.ComputeEvidenceBoundInputHash(
            entry.StrategyId,
            entry.StrategyName,
            entry.RunType,
            entry.DatasetReference,
            entry.FeedReference,
            entry.Engine,
            entry.ParameterSet,
            entry.ParentRunId,
            entry.PortfolioId,
            entry.LedgerReference,
            entry.AuditReference,
            entry.FundProfileId,
            entry.OperatorAcceptanceCriteria,
            entry.RetainedEvidenceReferences,
            entry.AccountingRecordReferences,
            entry.ApprovalReferences,
            entry.PaperValidationReferences,
            entry.GovernedReportReferences);

    private static StrategyRunEntry CreateEvidenceBoundRun(string runId) =>
        StrategyRunEntry.StartWithEvidence(
            "strategy-evidence",
            "Evidence Strategy",
            RunType.Backtest,
            runId,
            datasetReference: "provider-bars/equities/daily",
            feedReference: "provider-feed:daily",
            engine: "MeridianNative",
            operatorAcceptanceCriteria: ["Operator reviewed the retained backtest evidence."],
            retainedEvidenceReferences: [$"evidence://strategy-runs/{runId}"],
            accountingRecordReferences: ["ledger://books/book-1/accounts/strategy-evidence"],
            approvalReferences: [$"approval://strategy-runs/{runId}"],
            paperValidationReferences: ["workflow://fund/fund-1"],
            governedReportReferences: [$"reporting-run://{runId}/manifest"]);

    private static StrategyRunEntry MutateCanonicalInput(
        StrategyRunEntry entry,
        string changedField) =>
        changedField switch
        {
            "strategy-id" => entry with { StrategyId = "changed-strategy" },
            "strategy-name" => entry with { StrategyName = "Changed Strategy" },
            "run-type" => entry with { RunType = RunType.Paper },
            "dataset" => entry with { DatasetReference = "provider-bars/changed" },
            "feed" => entry with { FeedReference = "provider-feed:changed" },
            "engine" => entry with { Engine = "ChangedEngine" },
            "parameters" => entry with
            {
                ParameterSet = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["lookback"] = "99"
                }
            },
            "parent-run" => entry with { ParentRunId = "changed-parent" },
            "portfolio" => entry with { PortfolioId = "changed-portfolio" },
            "ledger" => entry with { LedgerReference = "changed-ledger" },
            "audit" => entry with { AuditReference = "changed-audit" },
            "fund-profile" => entry with { FundProfileId = "changed-fund" },
            _ => throw new ArgumentOutOfRangeException(nameof(changedField), changedField, null)
        };

    [Fact]
    public async Task DurableStore_RetainsRealismBoundInputHash()
    {
        var dataRoot = CreateDataRoot();
        try
        {
            var store = new StrategyRunStore(new FileOperationalCaseHistoryStore(dataRoot));
            var realism = new ExecutionRealismDescriptor(
                DefaultExecutionModel: ExecutionModel.BarMidpoint,
                FillTiming: FillTiming.NextBar,
                FillConservatism: FillConservatism.Conservative,
                DelistingPolicy: DelistingPolicy.LiquidateAtLastPrice,
                DelistingHaircutPercent: 0m,
                DelistingGraceDays: 5,
                CommissionKind: BacktestCommissionKind.PerShare,
                CommissionRate: 0.005m,
                CommissionMinimum: 1.00m,
                CommissionMaximum: decimal.MaxValue,
                SlippageBasisPoints: 5m,
                MaxParticipationRate: 0m,
                MarketImpactCoefficient: 0.1m,
                OrderBookQueueAheadFraction: 0m,
                AdjustForCorporateActions: true,
                RiskFreeRate: 0.04);

            var entry = StrategyRunEntry.Start(
                "strategy-realism",
                "Realism Strategy",
                RunType.Backtest,
                "run-realism",
                datasetReference: "dataset:prices",
                feedReference: null,
                engine: "MeridianNative",
                parameterSet: null) with
            {
                ExecutionRealism = realism
            };

            var hash = StrategyRunEntry.ComputeRealismBoundInputHash(entry);
            entry = entry with { InputHashSha256 = hash };

            // The durable store must accept a v4 realism-bound hash and retain it verbatim. Before
            // this fix it threw, because the hash matched none of the v3/v2/evidence/legacy
            // recomputations - and the research recorder swallowed that exception, so lineage
            // vanished silently in production while in-memory-store tests stayed green.
            await store.RecordRunAsync(entry);

            var replayed = await new StrategyRunStore(new FileOperationalCaseHistoryStore(dataRoot))
                .GetRunByIdAsync("run-realism");

            replayed.Should().NotBeNull();
            replayed!.InputHashSha256.Should().Be(hash.ToLowerInvariant());
            replayed.ExecutionRealism.Should().Be(realism);
        }
        finally
        {
            Directory.Delete(dataRoot, recursive: true);
        }
    }

    private static string CreateDataRoot()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            "meridian-strategy-run-store-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
