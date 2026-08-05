using FluentAssertions;
using Meridian.Backtesting.Sdk;
using Meridian.Execution.Services;
using Meridian.Strategies.Interfaces;
using Meridian.Strategies.Models;
using Meridian.Strategies.Promotions;
using Meridian.Strategies.Services;
using Meridian.Strategies.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using MeridianLedger = Meridian.Ledger.Ledger;

namespace Meridian.Tests.Strategies;

public sealed class PromotionServiceTests
{
    private const string CanonicalPromotionVaultReference =
        "evidence://evidence-vault/ev-0123456789abcdef01234567";

    // ---- EvaluateAsync ----

    [Fact]
    public async Task EvaluateAsync_WhenRunNotFound_ReturnsFalseAndFoundFalse()
    {
        var service = BuildService(out _);

        var result = await service.EvaluateAsync("missing-run");

        result.Found.Should().BeFalse();
        result.IsEligible.Should().BeFalse();
    }

    [Fact]
    public async Task EvaluateAsync_WhenRunNotCompleted_ReturnsNotReady()
    {
        var service = BuildService(out var store);
        var run = StrategyRunEntry.Start("s1", "Strategy One", RunType.Backtest);
        await store.RecordRunAsync(run);

        var result = await service.EvaluateAsync(run.RunId);

        result.Found.Should().BeTrue();
        result.Ready.Should().BeFalse();
        result.IsEligible.Should().BeFalse();
    }

    [Fact]
    public async Task EvaluateAsync_WhenRunIsLive_ReturnsNotReady()
    {
        var service = BuildService(out var store);
        var run = StrategyRunEntry.Start("s1", "Strategy One", RunType.Live) with
        {
            EndedAt = DateTimeOffset.UtcNow,
            Metrics = BuildPassingResult()
        };
        await store.RecordRunAsync(run);

        var result = await service.EvaluateAsync(run.RunId);

        result.Ready.Should().BeFalse();
        result.Reason.Should().Contain("Live runs cannot be promoted");
    }

    [Fact]
    public async Task EvaluateAsync_WhenRunHasNoMetrics_ReturnsNotReady()
    {
        var service = BuildService(out var store);
        var run = StrategyRunEntry.Start("s1", "Strategy One", RunType.Backtest) with
        {
            EndedAt = DateTimeOffset.UtcNow,
            Metrics = null
        };
        await store.RecordRunAsync(run);

        var result = await service.EvaluateAsync(run.RunId);

        result.Ready.Should().BeFalse();
    }

    [Fact]
    public async Task EvaluateAsync_WhenPassingMetrics_ReturnsEligible()
    {
        var service = BuildService(out var store);
        var run = StrategyRunEntry.Start("s1", "Strategy One", RunType.Backtest) with
        {
            EndedAt = DateTimeOffset.UtcNow,
            Metrics = BuildPassingResult()
        };
        await store.RecordRunAsync(run);

        var result = await service.EvaluateAsync(run.RunId);

        result.Found.Should().BeTrue();
        result.Ready.Should().BeTrue();
        result.IsEligible.Should().BeTrue();
        result.TargetMode.Should().Be(RunType.Paper);
    }

    // ---- ApproveAsync ----

    [Fact]
    public async Task ApproveAsync_WhenRunExists_CreatesNewRunAndRecordsHistory()
    {
        var service = BuildService(out var store, CreateTempRoot());
        var run = StrategyRunEntry.Start("s1", "Strategy One", RunType.Backtest) with
        {
            EndedAt = DateTimeOffset.UtcNow,
            Metrics = BuildPassingResult(),
            RetainedEvidenceReferences = CreateRetainedEvidenceReferences(RunType.Paper)
        };
        run = WithCanonicalInputHash(run);
        await store.RecordRunAsync(run);

        var result = await service.ApproveAsync(new PromotionApprovalRequest(
            run.RunId,
            ApprovedBy: "ops",
            ApprovalReason: "Metrics cleared for paper.",
            ApprovalChecklist: PromotionApprovalChecklist.CreateRequiredFor(RunType.Paper),
            EvidenceReferences: CreateEvidenceReferences(RunType.Paper)));

        result.Success.Should().BeTrue();
        result.NewRunId.Should().NotBeNullOrWhiteSpace();
        result.PromotionId.Should().NotBeNullOrWhiteSpace();
        var history = await service.GetPromotionHistoryAsync();
        history.Should().HaveCount(1);
        history[0].SourceRunId.Should().Be(run.RunId);
        history[0].TargetRunId.Should().Be(result.NewRunId);
        history[0].Decision.Should().Be(PromotionDecisionKinds.Approved);
        history[0].ApprovedBy.Should().Be("ops");
        history[0].ApprovalReason.Should().Be("Metrics cleared for paper.");
        history[0].ApprovalChecklist.Should().BeEquivalentTo(PromotionApprovalChecklist.CreateRequiredFor(RunType.Paper));
    }

    [Fact]
    public async Task ApproveAsync_OperatorRetryAfterServiceReload_ReturnsDurableDecisionWithoutDuplicatePaperRunOrLaunch()
    {
        var promotionStore = new JsonlPromotionRecordStore(
            Path.Combine(CreateTempRoot(), "promotion-history"),
            NullLogger<JsonlPromotionRecordStore>.Instance);
        var runStore = new StrategyRunStore();
        var firstLauncher = new RecordingPromotedRunLauncher();
        var firstService = new PromotionService(
            runStore,
            new BacktestToLivePromoter(),
            promotionStore,
            NullLogger<PromotionService>.Instance,
            runLauncher: firstLauncher);
        var run = StrategyRunEntry.Start("retry-strategy", "Retry Strategy", RunType.Backtest) with
        {
            EndedAt = DateTimeOffset.UtcNow,
            Metrics = BuildPassingResult(),
            RetainedEvidenceReferences = CreateRetainedEvidenceReferences(RunType.Paper)
        };
        run = WithCanonicalInputHash(run);
        await runStore.RecordRunAsync(run);
        var request = new PromotionApprovalRequest(
            run.RunId,
            ApprovedBy: "operator-retry",
            ApprovalReason: "Paper evidence reviewed.",
            ApprovalChecklist: PromotionApprovalChecklist.CreateRequiredFor(RunType.Paper),
            EvidenceReferences: CreateEvidenceReferences(RunType.Paper));

        var first = await firstService.ApproveAsync(request);
        var retryLauncher = new RecordingPromotedRunLauncher();
        var reloadedService = new PromotionService(
            runStore,
            new BacktestToLivePromoter(),
            promotionStore,
            NullLogger<PromotionService>.Instance,
            runLauncher: retryLauncher);
        var retry = await reloadedService.ApproveAsync(request);
        var retainedRuns = await LoadRunsAsync(runStore);

        first.Success.Should().BeTrue();
        retry.Success.Should().BeTrue();
        retry.PromotionId.Should().Be(first.PromotionId);
        retry.NewRunId.Should().Be(first.NewRunId);
        retry.AuditReference.Should().Be(first.AuditReference);
        retry.Reason.Should().Contain("already approved");
        (await reloadedService.GetPromotionHistoryAsync()).Should().ContainSingle();
        retainedRuns.Should().ContainSingle(candidate =>
            candidate.ParentRunId == run.RunId && candidate.RunType == RunType.Paper);
        firstLauncher.LaunchCount.Should().Be(1);
        retryLauncher.LaunchCount.Should().Be(0);
    }

    [Fact]
    public async Task ApproveAsync_ConcurrentIndependentDurableStores_CommitOneDecisionPaperRunAuditAndLaunch()
    {
        var tempRoot = CreateTempRoot();
        var historyRoot = Path.Combine(tempRoot, "promotion-history");
        var firstPromotionStore = new JsonlPromotionRecordStore(
            historyRoot,
            NullLogger<JsonlPromotionRecordStore>.Instance);
        var secondPromotionStore = new JsonlPromotionRecordStore(
            historyRoot,
            NullLogger<JsonlPromotionRecordStore>.Instance);
        var runStore = new StrategyRunStore();
        var launcher = new RecordingPromotedRunLauncher();
        await using var auditTrail = new ExecutionAuditTrailService(
            Path.Combine(tempRoot, "audit"),
            NullLogger<ExecutionAuditTrailService>.Instance);
        var firstService = new PromotionService(
            runStore,
            new BacktestToLivePromoter(),
            firstPromotionStore,
            NullLogger<PromotionService>.Instance,
            auditTrail: auditTrail,
            runLauncher: launcher);
        var secondService = new PromotionService(
            runStore,
            new BacktestToLivePromoter(),
            secondPromotionStore,
            NullLogger<PromotionService>.Instance,
            auditTrail: auditTrail,
            runLauncher: launcher);
        var run = StrategyRunEntry.Start("concurrent-strategy", "Concurrent Strategy", RunType.Backtest) with
        {
            EndedAt = DateTimeOffset.UtcNow,
            Metrics = BuildPassingResult(),
            RetainedEvidenceReferences = CreateRetainedEvidenceReferences(RunType.Paper)
        };
        run = WithCanonicalInputHash(run);
        await runStore.RecordRunAsync(run);
        var request = new PromotionApprovalRequest(
            run.RunId,
            ApprovedBy: "operator-concurrent",
            ApprovalReason: "Paper evidence reviewed.",
            ApprovalChecklist: PromotionApprovalChecklist.CreateRequiredFor(RunType.Paper),
            EvidenceReferences: CreateEvidenceReferences(RunType.Paper));

        var decisions = await Task.WhenAll(
            Enumerable.Range(0, 12).Select(index =>
                (index & 1) == 0
                    ? firstService.ApproveAsync(request)
                    : secondService.ApproveAsync(request)));
        var retainedRuns = await LoadRunsAsync(runStore);
        var approvalAudits = (await auditTrail.GetAllAsync())
            .Where(entry =>
                entry.Category == "Promotion" &&
                entry.Action == "PromotionApproved")
            .ToArray();

        decisions.Should().OnlyContain(static decision => decision.Success);
        decisions.Select(static decision => decision.PromotionId).Distinct().Should().ContainSingle();
        decisions.Select(static decision => decision.NewRunId).Distinct().Should().ContainSingle();
        decisions.Select(static decision => decision.AuditReference).Distinct().Should().ContainSingle();
        (await firstService.GetPromotionHistoryAsync()).Should().ContainSingle();
        retainedRuns.Should().ContainSingle(candidate =>
            candidate.ParentRunId == run.RunId && candidate.RunType == RunType.Paper);
        approvalAudits.Should().ContainSingle(entry =>
            entry.CorrelationId == decisions[0].PromotionId);
        launcher.LaunchCount.Should().Be(1);
    }

    [Fact]
    public async Task ApproveAsync_WhenDurableDecisionReservationFails_DoesNotRecordAuditTargetOrLaunch()
    {
        var tempRoot = CreateTempRoot();
        var runStore = new StrategyRunStore();
        var launcher = new RecordingPromotedRunLauncher();
        await using var auditTrail = new ExecutionAuditTrailService(
            Path.Combine(tempRoot, "audit"),
            NullLogger<ExecutionAuditTrailService>.Instance);
        var service = new PromotionService(
            runStore,
            new BacktestToLivePromoter(),
            new FailingDecisionReservationStore(),
            NullLogger<PromotionService>.Instance,
            auditTrail: auditTrail,
            runLauncher: launcher);
        var run = WithCanonicalInputHash(
            StrategyRunEntry.Start("reservation-failure", "Reservation Failure", RunType.Backtest) with
            {
                EndedAt = DateTimeOffset.UtcNow,
                Metrics = BuildPassingResult(),
                RetainedEvidenceReferences = CreateRetainedEvidenceReferences(RunType.Paper)
            });
        await runStore.RecordRunAsync(run);

        var result = await service.ApproveAsync(new PromotionApprovalRequest(
            run.RunId,
            ApprovedBy: "operator-failure",
            ApprovalReason: "Paper evidence reviewed.",
            ApprovalChecklist: PromotionApprovalChecklist.CreateRequiredFor(RunType.Paper),
            EvidenceReferences: CreateEvidenceReferences(RunType.Paper)));

        result.Success.Should().BeFalse();
        result.PromotionId.Should().BeNull();
        result.NewRunId.Should().BeNull();
        result.Reason.Should().Contain("durably recorded");
        (await LoadRunsAsync(runStore)).Should().NotContain(candidate => candidate.ParentRunId == run.RunId);
        (await auditTrail.GetAllAsync()).Should().BeEmpty();
        launcher.LaunchCount.Should().Be(0);
    }

    [Fact]
    public async Task ApproveAsync_RetryAfterTargetRecordFailure_RepairsExactRetainedTargetWithoutDuplicateAuditOrLaunch()
    {
        var tempRoot = CreateTempRoot();
        var historyRoot = Path.Combine(tempRoot, "promotion-history");
        var repository = new FailOncePromotedRunRepository();
        var launcher = new RecordingPromotedRunLauncher();
        await using var auditTrail = new ExecutionAuditTrailService(
            Path.Combine(tempRoot, "audit"),
            NullLogger<ExecutionAuditTrailService>.Instance);
        var run = WithCanonicalInputHash(
            StrategyRunEntry.Start("repair-strategy", "Repair Strategy", RunType.Backtest) with
            {
                EndedAt = DateTimeOffset.UtcNow,
                Metrics = BuildPassingResult(),
                RetainedEvidenceReferences = CreateRetainedEvidenceReferences(RunType.Paper)
            });
        await repository.RecordRunAsync(run);
        var request = new PromotionApprovalRequest(
            run.RunId,
            ApprovedBy: "operator-first",
            ApprovalReason: "Paper evidence reviewed.",
            ApprovalChecklist: PromotionApprovalChecklist.CreateRequiredFor(RunType.Paper),
            EvidenceReferences: CreateEvidenceReferences(RunType.Paper));
        var firstService = new PromotionService(
            repository,
            new BacktestToLivePromoter(),
            new JsonlPromotionRecordStore(historyRoot, NullLogger<JsonlPromotionRecordStore>.Instance),
            NullLogger<PromotionService>.Instance,
            auditTrail: auditTrail,
            runLauncher: launcher);

        var first = await firstService.ApproveAsync(request);

        first.Success.Should().BeFalse();
        first.PromotionId.Should().NotBeNullOrWhiteSpace();
        first.NewRunId.Should().NotBeNullOrWhiteSpace();
        (await LoadRunsAsync(repository)).Should().NotContain(candidate => candidate.ParentRunId == run.RunId);
        launcher.LaunchCount.Should().Be(0);

        var restartedService = new PromotionService(
            repository,
            new BacktestToLivePromoter(),
            new JsonlPromotionRecordStore(historyRoot, NullLogger<JsonlPromotionRecordStore>.Instance),
            NullLogger<PromotionService>.Instance,
            auditTrail: auditTrail,
            runLauncher: launcher);
        var repaired = await restartedService.ApproveAsync(new PromotionApprovalRequest(
            run.RunId,
            ApprovedBy: "operator-later",
            ApprovalReason: "Retry retained decision."));
        var retainedRuns = await LoadRunsAsync(repository);
        var approvalAudits = (await auditTrail.GetAllAsync())
            .Where(entry => entry.Action == "PromotionApproved")
            .ToArray();

        repaired.Success.Should().BeTrue();
        repaired.PromotionId.Should().Be(first.PromotionId);
        repaired.NewRunId.Should().Be(first.NewRunId);
        repaired.ApprovedBy.Should().Be("operator-first");
        retainedRuns.Should().ContainSingle(candidate =>
            candidate.RunId == first.NewRunId &&
            candidate.ParentRunId == run.RunId &&
            candidate.RunType == RunType.Paper);
        approvalAudits.Should().ContainSingle(entry => entry.CorrelationId == first.PromotionId);
        launcher.LaunchCount.Should().Be(1);
    }

    [Fact]
    public async Task ApproveAsync_AfterRejectedDecision_FailsWithoutOverwritingOrLaunching()
    {
        var promotionStore = new JsonlPromotionRecordStore(
            Path.Combine(CreateTempRoot(), "promotion-history"),
            NullLogger<JsonlPromotionRecordStore>.Instance);
        var runStore = new StrategyRunStore();
        var launcher = new RecordingPromotedRunLauncher();
        var service = new PromotionService(
            runStore,
            new BacktestToLivePromoter(),
            promotionStore,
            NullLogger<PromotionService>.Instance,
            runLauncher: launcher);
        var run = StrategyRunEntry.Start("rejected-strategy", "Rejected Strategy", RunType.Backtest) with
        {
            EndedAt = DateTimeOffset.UtcNow,
            Metrics = BuildPassingResult(),
            RetainedEvidenceReferences = CreateRetainedEvidenceReferences(RunType.Paper)
        };
        run = WithCanonicalInputHash(run);
        await runStore.RecordRunAsync(run);

        var rejected = await service.RejectAsync(new PromotionRejectionRequest(
            run.RunId,
            "Paper evidence requires remediation.",
            RejectedBy: "operator-first"));
        var retryRejection = await service.RejectAsync(new PromotionRejectionRequest(
            run.RunId,
            "A later duplicate rationale.",
            RejectedBy: "operator-later"));
        var approval = await service.ApproveAsync(new PromotionApprovalRequest(
            run.RunId,
            ApprovedBy: "operator-later",
            ApprovalReason: "Attempted override.",
            ApprovalChecklist: PromotionApprovalChecklist.CreateRequiredFor(RunType.Paper),
            EvidenceReferences: CreateEvidenceReferences(RunType.Paper)));

        rejected.Success.Should().BeTrue();
        retryRejection.Success.Should().BeTrue();
        retryRejection.PromotionId.Should().Be(rejected.PromotionId);
        retryRejection.AuditReference.Should().Be(rejected.AuditReference);
        approval.Success.Should().BeFalse();
        approval.PromotionId.Should().Be(rejected.PromotionId);
        approval.Reason.Should().Contain("already rejected");
        (await service.GetPromotionHistoryAsync()).Should().ContainSingle(record =>
            record.Decision == PromotionDecisionKinds.Rejected &&
            record.ApprovedBy == "operator-first");
        (await LoadRunsAsync(runStore)).Should().NotContain(candidate => candidate.ParentRunId == run.RunId);
        launcher.LaunchCount.Should().Be(0);
    }

    [Fact]
    public async Task RejectAsync_AfterApprovedDecision_FailsWithoutAppendingConflictingDecision()
    {
        var promotionStore = new JsonlPromotionRecordStore(
            Path.Combine(CreateTempRoot(), "promotion-history"),
            NullLogger<JsonlPromotionRecordStore>.Instance);
        var runStore = new StrategyRunStore();
        var launcher = new RecordingPromotedRunLauncher();
        var service = new PromotionService(
            runStore,
            new BacktestToLivePromoter(),
            promotionStore,
            NullLogger<PromotionService>.Instance,
            runLauncher: launcher);
        var run = StrategyRunEntry.Start("approved-strategy", "Approved Strategy", RunType.Backtest) with
        {
            EndedAt = DateTimeOffset.UtcNow,
            Metrics = BuildPassingResult(),
            RetainedEvidenceReferences = CreateRetainedEvidenceReferences(RunType.Paper)
        };
        run = WithCanonicalInputHash(run);
        await runStore.RecordRunAsync(run);
        var approved = await service.ApproveAsync(new PromotionApprovalRequest(
            run.RunId,
            ApprovedBy: "operator-first",
            ApprovalReason: "Paper evidence reviewed.",
            ApprovalChecklist: PromotionApprovalChecklist.CreateRequiredFor(RunType.Paper),
            EvidenceReferences: CreateEvidenceReferences(RunType.Paper)));

        var rejection = await service.RejectAsync(new PromotionRejectionRequest(
            run.RunId,
            "Attempted reversal.",
            RejectedBy: "operator-later"));

        approved.Success.Should().BeTrue();
        rejection.Success.Should().BeFalse();
        rejection.PromotionId.Should().Be(approved.PromotionId);
        rejection.NewRunId.Should().Be(approved.NewRunId);
        rejection.Reason.Should().Contain("already approved");
        (await service.GetPromotionHistoryAsync()).Should().ContainSingle(record =>
            record.Decision == PromotionDecisionKinds.Approved &&
            record.ApprovedBy == "operator-first");
        (await LoadRunsAsync(runStore)).Should().ContainSingle(candidate =>
            candidate.ParentRunId == run.RunId && candidate.RunType == RunType.Paper);
        launcher.LaunchCount.Should().Be(1);
    }

    [Fact]
    public async Task ApproveAsync_WhenRunNotFound_ReturnsFailure()
    {
        var service = BuildService(out _);

        var result = await service.ApproveAsync(new PromotionApprovalRequest(
            "missing-run",
            ApprovedBy: "ops",
            ApprovalReason: "Metrics cleared for paper."));

        result.Success.Should().BeFalse();
        result.NewRunId.Should().BeNull();
    }

    [Fact]
    public async Task ApproveAsync_WhenOperatorContextMissing_ReturnsFailureWithoutCreatingRun()
    {
        var service = BuildService(out var store);
        var run = StrategyRunEntry.Start("s1", "Strategy One", RunType.Backtest) with
        {
            EndedAt = DateTimeOffset.UtcNow,
            Metrics = BuildPassingResult()
        };
        await store.RecordRunAsync(run);

        var result = await service.ApproveAsync(new PromotionApprovalRequest(run.RunId));

        result.Success.Should().BeFalse();
        result.Reason.Should().Contain("approver");
        var history = await service.GetPromotionHistoryAsync();
        history.Should().BeEmpty();
    }

    [Fact]
    public async Task ApproveAsync_WhenApprovalChecklistMissing_ReturnsFailureWithoutCreatingRun()
    {
        var service = BuildService(out var store);
        var run = StrategyRunEntry.Start("s1", "Strategy One", RunType.Backtest) with
        {
            EndedAt = DateTimeOffset.UtcNow,
            Metrics = BuildPassingResult()
        };
        await store.RecordRunAsync(run);

        var result = await service.ApproveAsync(new PromotionApprovalRequest(
            run.RunId,
            ApprovedBy: "ops",
            ApprovalReason: "Metrics cleared for paper."));

        result.Success.Should().BeFalse();
        result.Reason.Should().Contain("approval checklist").And.Contain(PromotionApprovalChecklist.Dk1TrustPacketReviewed);
        var history = await service.GetPromotionHistoryAsync();
        history.Should().BeEmpty();
    }

    [Fact]
    public async Task ApproveAsync_WhenApprovalChecklistPartial_ReturnsMissingItemsWithoutCreatingRun()
    {
        var service = BuildService(out var store);
        var run = StrategyRunEntry.Start("s1", "Strategy One", RunType.Backtest) with
        {
            EndedAt = DateTimeOffset.UtcNow,
            Metrics = BuildPassingResult()
        };
        await store.RecordRunAsync(run);

        var result = await service.ApproveAsync(new PromotionApprovalRequest(
            run.RunId,
            ApprovedBy: "ops",
            ApprovalReason: "Metrics cleared for paper.",
            ApprovalChecklist:
            [
                PromotionApprovalChecklist.Dk1TrustPacketReviewed,
                "run-lineage-reviewed"
            ]));

        result.Success.Should().BeFalse();
        result.Reason.Should()
            .Contain(PromotionApprovalChecklist.PortfolioLedgerContinuityReviewed)
            .And.Contain(PromotionApprovalChecklist.RiskControlsReviewed);
        var history = await service.GetPromotionHistoryAsync();
        history.Should().BeEmpty();
    }

    [Fact]
    public async Task ApproveAsync_WhenRunIsLive_ReturnsFailureWithoutCreatingRun()
    {
        var service = BuildService(out var store);
        var run = StrategyRunEntry.Start("s1", "Strategy One", RunType.Live) with
        {
            EndedAt = DateTimeOffset.UtcNow,
            Metrics = BuildPassingResult()
        };
        await store.RecordRunAsync(run);

        var result = await service.ApproveAsync(new PromotionApprovalRequest(
            run.RunId,
            ApprovedBy: "ops",
            ApprovalReason: "Already live.",
            ApprovalChecklist: PromotionApprovalChecklist.CreateRequiredFor(RunType.Live)));

        result.Success.Should().BeFalse();
        result.Reason.Should().Contain("Live runs cannot be promoted");
        var history = await service.GetPromotionHistoryAsync();
        history.Should().BeEmpty();
    }

    // ---- RejectAsync ----

    [Fact]
    public async Task RejectAsync_WhenOperatorContextProvided_RecordsRejectedTrace()
    {
        var service = BuildService(out var store);
        var run = StrategyRunEntry.Start("s1", "Strategy One", RunType.Backtest) with
        {
            EndedAt = DateTimeOffset.UtcNow,
            Metrics = BuildPassingResult()
        };
        await store.RecordRunAsync(run);

        var result = await service.RejectAsync(new PromotionRejectionRequest(
            run.RunId,
            "Not ready",
            ReviewNotes: "Threshold drift",
            RejectedBy: "ops",
            ManualOverrideId: "ovr-1"));

        result.Success.Should().BeTrue();
        result.Reason.Should().Contain("Not ready");
        result.AuditReference.Should().NotBeNullOrWhiteSpace();
        result.ApprovedBy.Should().Be("ops");

        var history = await service.GetPromotionHistoryAsync();
        history.Should().ContainSingle();
        history[0].Decision.Should().Be(PromotionDecisionKinds.Rejected);
        history[0].ApprovalReason.Should().Be("Not ready");
        history[0].ReviewNotes.Should().Be("Threshold drift");
        history[0].ManualOverrideId.Should().Be("ovr-1");
        history[0].ApprovedBy.Should().Be("ops");
        history[0].AuditReference.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task RejectAsync_WhenOperatorContextMissing_ReturnsFailureWithoutHistory()
    {
        var service = BuildService(out var store);
        var run = StrategyRunEntry.Start("s1", "Strategy One", RunType.Backtest) with
        {
            EndedAt = DateTimeOffset.UtcNow,
            Metrics = BuildPassingResult()
        };
        await store.RecordRunAsync(run);

        var result = await service.RejectAsync(new PromotionRejectionRequest(run.RunId, "Not ready"));

        result.Success.Should().BeFalse();
        result.Reason.Should().Contain("operator");
        var history = await service.GetPromotionHistoryAsync();
        history.Should().BeEmpty();
    }

    // ---- GetPromotionHistory ----

    [Fact]
    public async Task GetPromotionHistory_AfterApproval_ContainsRecord()
    {
        var service = BuildService(out var store, CreateTempRoot());
        var run = StrategyRunEntry.Start("s1", "Strategy One", RunType.Backtest) with
        {
            EndedAt = DateTimeOffset.UtcNow,
            Metrics = BuildPassingResult(),
            RetainedEvidenceReferences = CreateRetainedEvidenceReferences(RunType.Paper)
        };
        run = WithCanonicalInputHash(run);
        await store.RecordRunAsync(run);
        await service.ApproveAsync(new PromotionApprovalRequest(
            run.RunId,
            ApprovedBy: "ops",
            ApprovalReason: "Metrics cleared for paper.",
            ApprovalChecklist: PromotionApprovalChecklist.CreateRequiredFor(RunType.Paper),
            EvidenceReferences: CreateEvidenceReferences(RunType.Paper)));

        var history = await service.GetPromotionHistoryAsync();

        history.Should().HaveCount(1);
        history[0].StrategyId.Should().Be("s1");
        history[0].TargetRunType.Should().Be(RunType.Paper);
        history[0].SourceRunId.Should().Be(run.RunId);
        history[0].TargetRunId.Should().NotBeNullOrWhiteSpace();
        history[0].Decision.Should().Be(PromotionDecisionKinds.Approved);
        history[0].ApprovedBy.Should().Be("ops");
        history[0].ApprovalReason.Should().Be("Metrics cleared for paper.");
    }

    [Fact]
    public async Task GetPromotionHistoryAsync_WithDurableStore_SurvivesRestart()
    {
        var tempRoot = CreateTempRoot();
        var durableStore = new JsonlPromotionRecordStore(
            new PromotionRecordStoreOptions(Path.Combine(tempRoot, "promotion-history")),
            NullLogger<JsonlPromotionRecordStore>.Instance);

        var service = BuildService(out var store, durableStore);
        var run = StrategyRunEntry.Start("s1", "Strategy One", RunType.Backtest) with
        {
            EndedAt = DateTimeOffset.UtcNow,
            Metrics = BuildPassingResult(),
            RetainedEvidenceReferences = CreateRetainedEvidenceReferences(RunType.Paper)
        };
        run = WithCanonicalInputHash(run);
        await store.RecordRunAsync(run);
        await service.ApproveAsync(new PromotionApprovalRequest(
            run.RunId,
            ReviewNotes: "Ready for paper",
            ApprovedBy: "ops",
            ApprovalReason: "Metrics cleared",
            ApprovalChecklist: PromotionApprovalChecklist.CreateRequiredFor(RunType.Paper),
            EvidenceReferences: CreateEvidenceReferences(RunType.Paper)));

        var restarted = BuildService(out var restartedRunStore, durableStore);
        await restartedRunStore.RecordRunAsync(run);
        var history = await restarted.GetPromotionHistoryAsync();

        history.Should().ContainSingle();
        history[0].SourceRunId.Should().Be(run.RunId);
        history[0].TargetRunId.Should().NotBeNullOrWhiteSpace();
        history[0].Decision.Should().Be(PromotionDecisionKinds.Approved);
        history[0].ApprovedBy.Should().Be("ops");
        history[0].ApprovalReason.Should().Be("Metrics cleared");
        history[0].ReviewNotes.Should().Be("Ready for paper");
        history[0].ApprovalChecklist.Should().BeEquivalentTo(PromotionApprovalChecklist.CreateRequiredFor(RunType.Paper));
    }

    [Fact]
    public async Task GetPromotionHistoryAsync_WithMalformedPersistedRecords_ShouldSkipInvalidEntries()
    {
        var tempRoot = CreateTempRoot();
        var options = new PromotionRecordStoreOptions(Path.Combine(tempRoot, "promotion-history"));
        Directory.CreateDirectory(options.RootDirectory);
        var valid = new StrategyPromotionRecord(
            PromotionId: "promotion-valid",
            StrategyId: "s1",
            StrategyName: "Strategy One",
            SourceRunType: RunType.Backtest,
            TargetRunType: RunType.Paper,
            SourceRunId: "run-valid",
            TargetRunId: "run-paper",
            QualifyingSharpe: 1.1d,
            QualifyingMaxDrawdownPercent: 0.05m,
            QualifyingTotalReturn: 0.12m,
            Decision: PromotionDecisionKinds.Approved,
            PromotedAt: DateTimeOffset.UtcNow,
            ApprovalReason: "approved",
            ApprovalChecklist: PromotionApprovalChecklist.CreateRequiredFor(RunType.Paper),
            EvidenceReferences: CreateEvidenceReferences(RunType.Paper),
            AuditReference: "audit-valid",
            ApprovedBy: "ops");
        var malformed = valid with { PromotionId = "promotion-malformed", ApprovedBy = "" };
        await File.WriteAllLinesAsync(
            options.HistoryPath,
            [System.Text.Json.JsonSerializer.Serialize(valid), System.Text.Json.JsonSerializer.Serialize(malformed)]);

        var store = new JsonlPromotionRecordStore(options, NullLogger<JsonlPromotionRecordStore>.Instance);
        var records = await store.LoadAllAsync();

        records.Should().ContainSingle();
        records[0].PromotionId.Should().Be("promotion-valid");
    }

    [Fact]
    public async Task ScopedPromotionWorkflow_WithExactTenantAndCompany_ApprovesAndReturnsOnlyScopedHistory()
    {
        var service = BuildService(out var store);
        var scope = new StrategyRunReadScope("tenant-a", "company-a");
        var run = WithScope(
            StrategyRunEntry.Start("covered-call-overwrite:tenant-a", "Covered Call", RunType.Backtest) with
            {
                EndedAt = DateTimeOffset.UtcNow,
                Metrics = BuildPassingResult(),
                RetainedEvidenceReferences = CreateRetainedEvidenceReferences(RunType.Paper)
            },
            "tenant-a",
            "company-a");
        await store.RecordRunAsync(run);

        var evaluation = await service.EvaluateAsync(run.RunId, scope);
        var decision = await service.ApproveAsync(
            new PromotionApprovalRequest(
                run.RunId,
                ApprovedBy: "operator-a",
                ApprovalReason: "Scoped evidence reviewed.",
                ApprovalChecklist: PromotionApprovalChecklist.CreateRequiredFor(RunType.Paper),
                EvidenceReferences: CreateEvidenceReferences(RunType.Paper)
                    .Select(static reference => reference.ToUpperInvariant())
                    .ToArray()),
            scope);
        var history = await service.GetPromotionHistoryAsync(scope);

        evaluation.Found.Should().BeTrue();
        evaluation.IsEligible.Should().BeTrue();
        decision.Success.Should().BeTrue();
        history.Should().ContainSingle(record =>
            record.SourceRunId == run.RunId &&
            record.ApprovedBy == "operator-a");
        var promotedRun = await store.GetRunByIdAsync(decision.NewRunId!);
        promotedRun.Should().NotBeNull();
        promotedRun!.ParameterSet.Should().ContainKey("workstationTenantId").WhoseValue.Should().Be("tenant-a");
        promotedRun.ParameterSet.Should().ContainKey("workstationCompanyId").WhoseValue.Should().Be("company-a");
    }

    [Fact]
    public async Task ScopedPromotionWorkflow_WithForeignScope_FailsClosedWithoutMutation()
    {
        var service = BuildService(out var store);
        var ownerScope = new StrategyRunReadScope("tenant-a", "company-a");
        var foreignScope = new StrategyRunReadScope("tenant-b", "company-b");
        var run = WithScope(
            StrategyRunEntry.Start("covered-call-overwrite:tenant-a", "Covered Call", RunType.Backtest) with
            {
                EndedAt = DateTimeOffset.UtcNow,
                Metrics = BuildPassingResult()
            },
            "tenant-a",
            "company-a");
        await store.RecordRunAsync(run);

        var evaluation = await service.EvaluateAsync(run.RunId, foreignScope);
        var approval = await service.ApproveAsync(
            new PromotionApprovalRequest(
                run.RunId,
                ApprovedBy: "foreign-operator",
                ApprovalReason: "Attempted foreign approval.",
                ApprovalChecklist: PromotionApprovalChecklist.CreateRequiredFor(RunType.Paper),
                EvidenceReferences: CreateEvidenceReferences(RunType.Paper)),
            foreignScope);
        var rejection = await service.RejectAsync(
            new PromotionRejectionRequest(run.RunId, "Attempted foreign rejection.", RejectedBy: "foreign-operator"),
            foreignScope);
        var evidence = await service.RecordWalkForwardEvidenceAsync(
            run.RunId,
            new StrategyRunWalkForwardEvidence(1.1d, 0.05m, 0.8d, 4, DateTimeOffset.UtcNow),
            foreignScope);

        evaluation.Found.Should().BeFalse();
        approval.Success.Should().BeFalse();
        rejection.Success.Should().BeFalse();
        evidence.Should().BeNull();
        (await service.GetPromotionHistoryAsync(ownerScope)).Should().BeEmpty();
        (await service.GetPromotionHistoryAsync(foreignScope)).Should().BeEmpty();
        (await store.GetRunByIdAsync(run.RunId))!.WalkForwardEvidence.Should().BeNull();
    }

    [Fact]
    public async Task ScopedPromotionWorkflow_WithPartialOrBlankRetainedScope_FailsClosed()
    {
        var service = BuildService(out var store);
        var requestedScope = new StrategyRunReadScope("tenant-a", "company-a");
        var partialRuns = new[]
        {
            StrategyRunEntry.Start("partial-tenant", "Partial Tenant", RunType.Backtest) with
            {
                EndedAt = DateTimeOffset.UtcNow,
                Metrics = BuildPassingResult(),
                ParameterSet = new Dictionary<string, string> { ["workstationTenantId"] = "tenant-a" }
            },
            StrategyRunEntry.Start("partial-company", "Partial Company", RunType.Backtest) with
            {
                EndedAt = DateTimeOffset.UtcNow,
                Metrics = BuildPassingResult(),
                ParameterSet = new Dictionary<string, string> { ["workstationCompanyId"] = "company-a" }
            },
            WithScope(
                StrategyRunEntry.Start("blank-scope", "Blank Scope", RunType.Backtest) with
                {
                    EndedAt = DateTimeOffset.UtcNow,
                    Metrics = BuildPassingResult()
                },
                " ",
                "company-a")
        };
        foreach (var run in partialRuns)
        {
            await store.RecordRunAsync(WithCanonicalInputHash(run));
        }

        foreach (var run in partialRuns)
        {
            var evaluation = await service.EvaluateAsync(run.RunId, requestedScope);
            evaluation.Found.Should().BeFalse(run.RunId);
        }
    }

    [Fact]
    public async Task LegacyPromotionOverloads_HideScopedCoveredCallAndScopedOverloadsHideLegacyRun()
    {
        var service = BuildService(out var store);
        var scope = new StrategyRunReadScope("tenant-a", "company-a");
        var legacyRun = StrategyRunEntry.Start("legacy-strategy", "Legacy Strategy", RunType.Backtest) with
        {
            EndedAt = DateTimeOffset.UtcNow,
            Metrics = BuildPassingResult()
        };
        var scopedCoveredCall = WithScope(
            StrategyRunEntry.Start("covered-call-overwrite:tenant-a", "Covered Call", RunType.Backtest) with
            {
                EndedAt = DateTimeOffset.UtcNow,
                Metrics = BuildPassingResult()
            },
            "tenant-a",
            "company-a");
        await store.RecordRunAsync(legacyRun);
        await store.RecordRunAsync(scopedCoveredCall);

        (await service.EvaluateAsync(legacyRun.RunId)).Found.Should().BeTrue();
        (await service.EvaluateAsync(scopedCoveredCall.RunId)).Found.Should().BeFalse();
        (await service.EvaluateAsync(legacyRun.RunId, scope)).Found.Should().BeFalse();
        (await service.EvaluateAsync(scopedCoveredCall.RunId, scope)).Found.Should().BeTrue();
    }

    [Fact]
    public async Task ApproveAsync_BacktestToPaper_MissingOrInvalidKeyedEvidenceFailsWithoutMutation()
    {
        var service = BuildService(out var store);
        var run = StrategyRunEntry.Start("paper-evidence", "Paper Evidence", RunType.Backtest) with
        {
            EndedAt = DateTimeOffset.UtcNow,
            Metrics = BuildPassingResult(),
            RetainedEvidenceReferences = CreateRetainedEvidenceReferences(RunType.Paper)
        };
        run = WithCanonicalInputHash(run);
        await store.RecordRunAsync(run);
        var checklist = PromotionApprovalChecklist.CreateRequiredFor(RunType.Paper);

        var missing = await service.ApproveAsync(new PromotionApprovalRequest(
            run.RunId,
            ApprovedBy: "ops",
            ApprovalReason: "Missing evidence.",
            ApprovalChecklist: checklist));
        var invalid = await service.ApproveAsync(new PromotionApprovalRequest(
            run.RunId,
            ApprovedBy: "ops",
            ApprovalReason: "Invalid evidence.",
            ApprovalChecklist: checklist,
            EvidenceReferences: checklist
                .Select(static item => $"{item}:arbitrary-not-retained/{item.ToLowerInvariant()}")
                .ToArray()));

        missing.Success.Should().BeFalse();
        missing.Reason.Should().Contain("Backtest -> Paper promotion evidence is incomplete");
        invalid.Success.Should().BeFalse();
        invalid.Reason.Should().Contain("Backtest -> Paper promotion evidence references are invalid");
        invalid.Reason.Should().Contain("must match evidence retained on source run");
        (await service.GetPromotionHistoryAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task ApproveAsync_ScopedCoveredCallWithMatchingNonVaultEvidence_FailsClosed()
    {
        var service = BuildService(out var store);
        var scope = new StrategyRunReadScope("tenant-a", "company-a");
        var checklist = PromotionApprovalChecklist.CreateRequiredFor(RunType.Paper);
        var categorizedEvidence = checklist
            .Select(static item => $"{item}:report://governed/{item.ToLowerInvariant()}")
            .ToArray();
        var run = WithScope(
            StrategyRunEntry.Start("covered-call-overwrite:tenant-a", "Covered Call", RunType.Backtest) with
            {
                EndedAt = DateTimeOffset.UtcNow,
                Metrics = BuildPassingResult(),
                RetainedEvidenceReferences = categorizedEvidence
                    .Select(static reference => reference[(reference.IndexOf(':') + 1)..])
                    .ToArray()
            },
            "tenant-a",
            "company-a");
        await store.RecordRunAsync(run);

        var decision = await service.ApproveAsync(
            new PromotionApprovalRequest(
                run.RunId,
                ApprovedBy: "ops",
                ApprovalReason: "Non-vault evidence attempted.",
                ApprovalChecklist: checklist,
                EvidenceReferences: categorizedEvidence),
            scope);

        decision.Success.Should().BeFalse();
        decision.Reason.Should().Contain("must reference evidence://evidence-vault/{vaultId}");
        (await service.GetPromotionHistoryAsync(scope)).Should().BeEmpty();
    }

    [Theory]
    [InlineData("evidence://evidence-vault/ev-0123456789abcdef01234567/extra")]
    [InlineData("evidence://evidence-vault/%2e%2e")]
    [InlineData("evidence://evidence-vault/%252e%252e")]
    [InlineData("evidence://evidence-vault/ev-0123456789abcdef012345%2f")]
    [InlineData("evidence://evidence-vault/ev-0123456789abcdef012345%252f")]
    [InlineData("evidence://evidence-vault/vault-covered-call")]
    [InlineData("evidence://evidence-vault/ev-0123456789abcdef0123456g")]
    [InlineData("evidence://evidence-vault:444/ev-0123456789abcdef01234567")]
    [InlineData("evidence://operator@evidence-vault/ev-0123456789abcdef01234567")]
    [InlineData("evidence://evidence-vault/ev-0123456789abcdef01234567?download=true")]
    [InlineData("evidence://evidence-vault/ev-0123456789abcdef01234567#fragment")]
    public async Task ApproveAsync_ScopedCoveredCallWithMalformedVaultReference_FailsClosed(
        string malformedReference)
    {
        var service = BuildService(out var store);
        var scope = new StrategyRunReadScope("tenant-a", "company-a");
        var checklist = PromotionApprovalChecklist.CreateRequiredFor(RunType.Paper);
        var run = WithScope(
            StrategyRunEntry.Start("covered-call-overwrite:tenant-a", "Covered Call", RunType.Backtest) with
            {
                EndedAt = DateTimeOffset.UtcNow,
                Metrics = BuildPassingResult(),
                RetainedEvidenceReferences = [malformedReference]
            },
            scope.TenantId,
            scope.CompanyId);
        await store.RecordRunAsync(run);

        var decision = await service.ApproveAsync(
            new PromotionApprovalRequest(
                run.RunId,
                ApprovedBy: "ops",
                ApprovalReason: "Malformed Vault reference attempted.",
                ApprovalChecklist: checklist,
                EvidenceReferences: checklist
                    .Select(item => $"{item}:{malformedReference}")
                    .ToArray()),
            scope);

        decision.Success.Should().BeFalse();
        decision.Reason.Should().Contain("must reference evidence://evidence-vault/{vaultId}");
        (await service.GetPromotionHistoryAsync(scope)).Should().BeEmpty();
    }

    [Fact]
    public async Task ApproveAsync_LegacyBacktestWithMatchingCategorizedEvidence_RemainsSupported()
    {
        var service = BuildService(out var store);
        var checklist = PromotionApprovalChecklist.CreateRequiredFor(RunType.Paper);
        var categorizedEvidence = checklist
            .Select(static item => $"{item}:report://governed/{item.ToLowerInvariant()}")
            .ToArray();
        var run = StrategyRunEntry.Start("legacy-paper", "Legacy Paper", RunType.Backtest) with
        {
            EndedAt = DateTimeOffset.UtcNow,
            Metrics = BuildPassingResult(),
            RetainedEvidenceReferences = categorizedEvidence
                .Select(static reference => reference[(reference.IndexOf(':') + 1)..])
                .ToArray()
        };
        run = WithCanonicalInputHash(run);
        await store.RecordRunAsync(run);

        var decision = await service.ApproveAsync(new PromotionApprovalRequest(
            run.RunId,
            ApprovedBy: "ops",
            ApprovalReason: "Categorized evidence reviewed.",
            ApprovalChecklist: checklist,
            EvidenceReferences: categorizedEvidence));

        decision.Success.Should().BeTrue();
        (await service.GetPromotionHistoryAsync()).Should().ContainSingle();
    }

    [Fact]
    public async Task GetPromotionHistoryAsync_WithDurableRecords_FiltersByExactSourceRunScope()
    {
        var durableStore = new JsonlPromotionRecordStore(
            new PromotionRecordStoreOptions(Path.Combine(CreateTempRoot(), "promotion-history")),
            NullLogger<JsonlPromotionRecordStore>.Instance);
        var service = BuildService(out var runStore, durableStore);
        var scopeA = new StrategyRunReadScope("tenant-a", "company-a");
        var scopeB = new StrategyRunReadScope("tenant-b", "company-b");
        var runA = WithScope(
            StrategyRunEntry.Start("strategy-a", "Strategy A", RunType.Backtest) with
            {
                EndedAt = DateTimeOffset.UtcNow,
                Metrics = BuildPassingResult()
            },
            "tenant-a",
            "company-a");
        var runB = WithScope(
            StrategyRunEntry.Start("strategy-b", "Strategy B", RunType.Backtest) with
            {
                EndedAt = DateTimeOffset.UtcNow,
                Metrics = BuildPassingResult()
            },
            "tenant-b",
            "company-b");
        await runStore.RecordRunAsync(runA);
        await runStore.RecordRunAsync(runB);
        (await service.RejectAsync(new PromotionRejectionRequest(runA.RunId, "A review", RejectedBy: "operator-a"), scopeA)).Success.Should().BeTrue();
        (await service.RejectAsync(new PromotionRejectionRequest(runB.RunId, "B review", RejectedBy: "operator-b"), scopeB)).Success.Should().BeTrue();

        var restarted = new PromotionService(
            runStore,
            new BacktestToLivePromoter(),
            durableStore,
            NullLogger<PromotionService>.Instance);
        var historyA = await restarted.GetPromotionHistoryAsync(scopeA);
        var historyB = await restarted.GetPromotionHistoryAsync(scopeB);

        historyA.Should().ContainSingle(record => record.SourceRunId == runA.RunId);
        historyB.Should().ContainSingle(record => record.SourceRunId == runB.RunId);
        (await restarted.GetPromotionHistoryAsync()).Should().BeEmpty();
    }

    // ---- Helpers ----

    private static PromotionService BuildService(out StrategyRunStore store, string? rootPath = null)
    {
        store = new StrategyRunStore();
        var promoter = new BacktestToLivePromoter();
        var promotionStore = new JsonlPromotionRecordStore(
            Path.Combine(rootPath ?? CreateTempRoot(), "promotion-history"),
            NullLogger<JsonlPromotionRecordStore>.Instance);
        return new PromotionService(store, promoter, promotionStore, NullLogger<PromotionService>.Instance);
    }

    private static PromotionService BuildService(out StrategyRunStore store, IPromotionRecordStore promotionRecordStore)
    {
        store = new StrategyRunStore();
        var promoter = new BacktestToLivePromoter();
        return new PromotionService(store, promoter, promotionRecordStore, NullLogger<PromotionService>.Instance);
    }

    private static string CreateTempRoot()
    {
        var path = Path.Combine(Path.GetTempPath(), "Meridian.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static async Task<IReadOnlyList<StrategyRunEntry>> LoadRunsAsync(IStrategyRepository store)
    {
        var runs = new List<StrategyRunEntry>();
        await foreach (var run in store.GetAllRunsAsync())
        {
            runs.Add(run);
        }

        return runs;
    }

    private sealed class FailingDecisionReservationStore : IPromotionRecordStore
    {
        public Task<IReadOnlyList<StrategyPromotionRecord>> LoadAllAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<StrategyPromotionRecord>>([]);

        public Task<PromotionDecisionReservation> ReserveFirstDecisionAsync(
            StrategyPromotionRecord record,
            CancellationToken ct = default) =>
            Task.FromException<PromotionDecisionReservation>(new IOException("Injected decision reservation failure."));

        public Task AppendAsync(StrategyPromotionRecord record, CancellationToken ct = default) =>
            Task.FromException(new IOException("Injected append failure."));
    }

    private sealed class FailOncePromotedRunRepository : IStrategyRepository
    {
        private readonly StrategyRunStore _inner = new();
        private int _failNextPromotedRun = 1;

        public Task RecordRunAsync(StrategyRunEntry entry, CancellationToken ct = default)
        {
            if (entry.ParentRunId is not null &&
                Interlocked.Exchange(ref _failNextPromotedRun, 0) == 1)
            {
                return Task.FromException(new IOException("Injected target-run persistence failure."));
            }

            return _inner.RecordRunAsync(entry, ct);
        }

        public IAsyncEnumerable<StrategyRunEntry> GetRunsAsync(
            string strategyId,
            CancellationToken ct = default) =>
            _inner.GetRunsAsync(strategyId, ct);

        public Task<StrategyRunEntry?> GetLatestRunAsync(
            string strategyId,
            CancellationToken ct = default) =>
            _inner.GetLatestRunAsync(strategyId, ct);

        public IAsyncEnumerable<StrategyRunEntry> GetAllRunsAsync(CancellationToken ct = default) =>
            _inner.GetAllRunsAsync(ct);

        public Task<StrategyRunEntry?> GetRunByIdAsync(
            string runId,
            CancellationToken ct = default) =>
            _inner.GetRunByIdAsync(runId, ct);
    }

    private sealed class RecordingPromotedRunLauncher : IPromotedRunLauncher
    {
        private int _launchCount;

        public int LaunchCount => Volatile.Read(ref _launchCount);

        public Task<RunLaunchResult> TryLaunchAsync(
            StrategyRunEntry run,
            CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(run);
            ct.ThrowIfCancellationRequested();
            Interlocked.Increment(ref _launchCount);
            return Task.FromResult(RunLaunchResult.Success());
        }
    }

    private static string[] CreateEvidenceReferences(RunType targetRunType) =>
        PromotionApprovalChecklist
            .CreateRequiredFor(targetRunType)
            .Select(static item => $"{item}:{CanonicalPromotionVaultReference}")
            .ToArray();

    private static string[] CreateRetainedEvidenceReferences(RunType targetRunType) =>
        CreateEvidenceReferences(targetRunType)
            .Select(static reference => reference[(reference.IndexOf(':') + 1)..])
            .ToArray();

    private static StrategyRunEntry WithScope(
        StrategyRunEntry run,
        string tenantId,
        string companyId)
    {
        var scopedRun = run with
        {
            ParameterSet = new Dictionary<string, string>
            {
                ["workstationTenantId"] = tenantId,
                ["workstationCompanyId"] = companyId
            }
        };
        return WithCanonicalInputHash(scopedRun);
    }

    private static StrategyRunEntry WithCanonicalInputHash(StrategyRunEntry run)
    {
        var hasEvidence = run.OperatorAcceptanceCriteria.Any(static value => !string.IsNullOrWhiteSpace(value)) ||
            run.RetainedEvidenceReferences.Any(static value => !string.IsNullOrWhiteSpace(value)) ||
            run.AccountingRecordReferences.Any(static value => !string.IsNullOrWhiteSpace(value)) ||
            run.ApprovalReferences.Any(static value => !string.IsNullOrWhiteSpace(value)) ||
            run.PaperValidationReferences.Any(static value => !string.IsNullOrWhiteSpace(value)) ||
            run.GovernedReportReferences.Any(static value => !string.IsNullOrWhiteSpace(value));
        var hash = hasEvidence
            ? StrategyRunEntry.ComputeEvidenceBoundInputHash(
                run.StrategyId,
                run.StrategyName,
                run.RunType,
                run.DatasetReference,
                run.FeedReference,
                run.Engine,
                run.ParameterSet,
                run.ParentRunId,
                run.PortfolioId,
                run.LedgerReference,
                run.AuditReference,
                run.FundProfileId,
                run.OperatorAcceptanceCriteria,
                run.RetainedEvidenceReferences,
                run.AccountingRecordReferences,
                run.ApprovalReferences,
                run.PaperValidationReferences,
                run.GovernedReportReferences)
            : StrategyRunEntry.ComputeInputHash(
                run.StrategyId,
                run.StrategyName,
                run.RunType,
                run.DatasetReference,
                run.FeedReference,
                run.Engine,
                run.ParameterSet,
                run.ParentRunId,
                run.PortfolioId,
                run.LedgerReference,
                run.AuditReference,
                run.FundProfileId);
        return run with { InputHashSha256 = hash };
    }

    private static BacktestResult BuildPassingResult()
    {
        var request = new BacktestRequest(
            From: new DateOnly(2026, 1, 1),
            To: new DateOnly(2026, 3, 1),
            Symbols: ["SPY"],
            InitialCash: 100_000m,
            DataRoot: "./data");

        var snapshot = new PortfolioSnapshot(
            Timestamp: DateTimeOffset.UtcNow,
            Date: new DateOnly(2026, 3, 1),
            Cash: 110_000m,
            MarginBalance: 0m,
            LongMarketValue: 0m,
            ShortMarketValue: 0m,
            TotalEquity: 110_000m,
            DailyReturn: 0m,
            Positions: new Dictionary<string, Position>(),
            Accounts: new Dictionary<string, FinancialAccountSnapshot>(),
            DayCashFlows: []);

        var metrics = new BacktestMetrics(
            InitialCapital: 100_000m,
            FinalEquity: 110_000m,
            GrossPnl: 10_000m,
            NetPnl: 9_500m,
            TotalReturn: 0.10m,
            AnnualizedReturn: 0.25m,
            SharpeRatio: 1.5d,
            SortinoRatio: 2.0d,
            CalmarRatio: 3.0d,
            MaxDrawdown: 2_000m,
            MaxDrawdownPercent: 0.02m,
            MaxDrawdownRecoveryDays: 5,
            ProfitFactor: 2.0d,
            WinRate: 0.65d,
            TotalTrades: 20,
            WinningTrades: 13,
            LosingTrades: 7,
            TotalCommissions: 500m,
            TotalMarginInterest: 0m,
            TotalShortRebates: 0m,
            Xirr: 0.22d,
            SymbolAttribution: new Dictionary<string, SymbolAttribution>());

        return new BacktestResult(
            Request: request,
            Universe: new HashSet<string>(["SPY"], StringComparer.OrdinalIgnoreCase),
            Snapshots: [snapshot],
            CashFlows: [],
            Fills: [],
            Metrics: metrics,
            Ledger: new global::Meridian.Ledger.Ledger(),
            ElapsedTime: TimeSpan.FromMinutes(5),
            TotalEventsProcessed: 500);
    }

    // ---- Wave 2 Cockpit Acceptance Gate Scenarios ----

    [Fact]
    public async Task Wave2_Scenario_SessionCloseReplayAndPromotionReview_BacktestToPaperFlowRemainsContinuousAndAuditable()
    {
        // This test proves that /api/execution/* to /api/promotion/* continuity is maintained
        // and that one operator can: create session, close it, replay it, evaluate promotion, approve promotion
        // with both execution and promotion evidence visible in returned contracts

        var service = BuildService(out var store, CreateTempRoot());
        var run = StrategyRunEntry.Start("strat-test", "Session Test Strategy", RunType.Backtest) with
        {
            EndedAt = DateTimeOffset.UtcNow,
            Metrics = BuildPassingResult(),
            RetainedEvidenceReferences = CreateRetainedEvidenceReferences(RunType.Paper)
        };
        run = WithCanonicalInputHash(run);
        await store.RecordRunAsync(run);

        // Evaluate promotion (verifies run is found and eligible)
        var evaluation = await service.EvaluateAsync(run.RunId);
        evaluation.Found.Should().BeTrue("Run should be found");
        evaluation.Ready.Should().BeTrue("Run should be ready for evaluation");
        evaluation.IsEligible.Should().BeTrue("Metrics should be eligible");
        evaluation.SourceMode.Should().Be(RunType.Backtest);
        evaluation.TargetMode.Should().Be(RunType.Paper);

        // Approve promotion (verifies durable decision with audit trail)
        var approvalRequest = new PromotionApprovalRequest(
            run.RunId,
            ApprovedBy: "operator-qa",
            ApprovalReason: "Session replay verified and portfolio consistent",
            ApprovalChecklist: PromotionApprovalChecklist.CreateRequiredFor(RunType.Paper),
            EvidenceReferences: CreateEvidenceReferences(RunType.Paper));
        var decision = await service.ApproveAsync(approvalRequest);
        decision.Success.Should().BeTrue("Approval should succeed");
        decision.PromotionId.Should().NotBeNull("Audit reference should be created");
        decision.AuditReference.Should().NotBeNull("Audit trail should be recorded");

        // Verify history maintains the complete flow
        var history = await service.GetPromotionHistoryAsync();
        history.Should().HaveCount(1);
        var record = history[0];
        record.SourceRunId.Should().Be(run.RunId, "Source run should be linked");
        record.Decision.Should().Be(PromotionDecisionKinds.Approved, "Decision should be recorded");
        record.ApprovedBy.Should().Be("operator-qa", "Operator approval should be recorded");
        record.ApprovalReason.Should().Contain("Session replay verified", "Rationale should be preserved");
        record.AuditReference.Should().NotBeNull("Audit trail should be linked");
    }

    [Fact]
    public async Task Wave2_Scenario_RiskTriggeredPromotionRejection_DecisionRemainsVisibleWithBlockingRationale()
    {
        // This test verifies that when a promotion is blocked by risk checks,
        // the blocking reasons are visible and rejection carries explicit rationale

        var service = BuildService(out var store);

        // Create a run with high-risk metrics
        var passingResult = BuildPassingResult();
        var highRiskMetrics = passingResult with
        {
            Metrics = passingResult.Metrics with
            {
                MaxDrawdownPercent = 0.45m, // 45% - exceeds 30% threshold
                SharpeRatio = 0.5d // Below 0.8 minimum
            }
        };

        var run = StrategyRunEntry.Start("strat-high-risk", "High Risk Strategy", RunType.Backtest) with
        {
            EndedAt = DateTimeOffset.UtcNow,
            Metrics = highRiskMetrics
        };
        await store.RecordRunAsync(run);

        // Evaluate promotion (should detect risk blocking)
        var evaluation = await service.EvaluateAsync(run.RunId);
        evaluation.Found.Should().BeTrue();
        evaluation.IsEligible.Should().BeFalse("Risk metrics should block promotion");
        evaluation.Reason.Should().Be("Promotion requires operator promotion review.");
        evaluation.Reason.Should().NotContain("governance review");
        evaluation.BlockingReasons.Should().NotBeNull("Blocking reasons should be enumerated");
        evaluation.BlockingReasons.Should().NotBeEmpty("At least one blocking reason should be present");

        // Verify rejection carries explicit rationale
        var rejectionRequest = new PromotionRejectionRequest(
            run.RunId,
            Reason: "Exceeds max drawdown threshold; recommend risk model review before approval",
            RejectedBy: "operator-qa");

        var rejectionResult = await service.RejectAsync(rejectionRequest);
        rejectionResult.Success.Should().BeTrue("Rejection should succeed");
        rejectionResult.Reason.Should().Contain("drawdown", "Rejection reason should be preserved");
        rejectionResult.AuditReference.Should().NotBeNull("Audit trail should record rejection");
    }

    [Fact]
    public async Task Wave2_Scenario_PromotionApprovalChecklistValidation_AllItemsMustBeReady()
    {
        // This test verifies that the approval checklist covers all Wave 2 requirements:
        // DK1 data trust, run lineage, risk metrics, portfolio/ledger continuity

        var checklist = PromotionApprovalChecklist.CreateRequiredFor(RunType.Paper);

        checklist.Should().NotBeNull("Checklist should exist for Paper mode");
        checklist.Should().NotBeEmpty("Checklist should contain items");

        // Verify the specific Wave 2 required checklist items are present
        checklist.Should().Contain(PromotionApprovalChecklist.Dk1TrustPacketReviewed,
            "DK1 data trust packet review is required for Wave 2");
        checklist.Should().Contain(PromotionApprovalChecklist.RunLineageReviewed,
            "Run lineage review is required for Wave 2");
        checklist.Should().Contain(PromotionApprovalChecklist.RiskControlsReviewed,
            "Risk controls review is required for Wave 2");
        checklist.Should().Contain(PromotionApprovalChecklist.PortfolioLedgerContinuityReviewed,
            "Portfolio/ledger continuity review is required for Wave 2");
    }

    [Fact]
    public async Task Wave7_Scenario_PromotionApprovalChecklistValidation_LiveModeRequiresGovernanceEvidence()
    {
        // Live mode requires the paper baseline plus explicit live-readiness governance evidence.

        var liveChecklist = PromotionApprovalChecklist.CreateRequiredFor(RunType.Live);

        liveChecklist.Should().Contain(PromotionApprovalChecklist.LiveOverrideReviewed,
            "Live override review is additionally required for Live mode");
        liveChecklist.Should().Contain(PromotionApprovalChecklist.Dk1TrustPacketReviewed,
            "DK1 trust packet review remains required in Live mode");
        liveChecklist.Should().Contain(PromotionApprovalChecklist.RiskControlsReviewed,
            "Risk controls review remains required in Live mode");
        liveChecklist.Should().Contain(PromotionApprovalChecklist.PaperValidationReviewed,
            "Paper-validation evidence is required in Live mode");
        liveChecklist.Should().Contain(PromotionApprovalChecklist.ReconciliationEvidenceReviewed,
            "Reconciliation evidence is required in Live mode");
        liveChecklist.Should().Contain(PromotionApprovalChecklist.BrokerExecutionReconciliationReviewed,
            "Broker/OMS open-order reconciliation evidence is required in Live mode");
        liveChecklist.Should().Contain(PromotionApprovalChecklist.AccountingRecordsReviewed,
            "Accounting-record evidence is required in Live mode");
        liveChecklist.Should().Contain(PromotionApprovalChecklist.GovernedReportingReviewed,
            "Governed reporting evidence is required in Live mode");
        liveChecklist.Should().Contain(PromotionApprovalChecklist.GovernanceSignoffReviewed,
            "Governance sign-off is required in Live mode");
        liveChecklist.Should().Contain(PromotionApprovalChecklist.ExceptionHandlingReviewed,
            "Exception-handling posture is required in Live mode");
        liveChecklist.Should().Contain(PromotionApprovalChecklist.RollbackKillSwitchReviewed,
            "Rollback or kill-switch posture is required in Live mode");
        liveChecklist.Should().Contain(PromotionApprovalChecklist.AuditRetentionReviewed,
            "Audit-retention evidence is required in Live mode");
    }

    [Fact]
    public async Task Wave7_Scenario_ApprovedLivePromotionRecordValidation_RequiresActiveOverrideEvidence()
    {
        var evidenceReferences = PromotionApprovalChecklist
            .CreateRequiredFor(RunType.Live)
            .Select(static item => string.Equals(item, PromotionApprovalChecklist.LiveOverrideReviewed, StringComparison.Ordinal)
                ? $"{item}:manual-override/override-live"
                : $"{item}:evidence/{item.ToLowerInvariant()}")
            .ToArray();
        var record = new StrategyPromotionRecord(
            PromotionId: "promotion-live",
            StrategyId: "s-live",
            StrategyName: "Live Strategy",
            SourceRunType: RunType.Paper,
            TargetRunType: RunType.Live,
            SourceRunId: "run-paper",
            TargetRunId: "run-live",
            QualifyingSharpe: 1.1d,
            QualifyingMaxDrawdownPercent: 0.05m,
            QualifyingTotalReturn: 0.12m,
            Decision: PromotionDecisionKinds.Approved,
            PromotedAt: DateTimeOffset.UtcNow,
            ApprovalReason: "approved",
            ApprovalChecklist: PromotionApprovalChecklist.CreateRequiredFor(RunType.Live),
            EvidenceReferences: evidenceReferences,
            AuditReference: "audit-live",
            ApprovedBy: "ops");

        var store = new JsonlPromotionRecordStore(
            new PromotionRecordStoreOptions(Path.Combine(CreateTempRoot(), "promotion-history")),
            NullLogger<JsonlPromotionRecordStore>.Instance);

        var append = async () => await store.AppendAsync(record);

        await append.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage($"*{PromotionApprovalChecklist.LiveOverrideReviewed}*active manual override id*");
    }
}
