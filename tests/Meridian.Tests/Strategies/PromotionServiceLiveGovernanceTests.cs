using FluentAssertions;
using Meridian.Backtesting.Sdk;
using Meridian.Execution.Sdk;
using Meridian.Execution.Services;
using Meridian.Strategies.Interfaces;
using Meridian.Strategies.Models;
using Meridian.Strategies.Promotions;
using Meridian.Strategies.Services;
using Meridian.Strategies.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Meridian.Tests.Strategies;

public sealed class PromotionServiceLiveGovernanceTests
{
    [Theory]
    [InlineData("expired-trust", "trust evidence is stale")]
    [InlineData("replaced-override", "not active for live promotion")]
    public async Task ApproveAsync_RetainedApprovalWithoutTarget_RevalidatesTrustAndApprovalControlsBeforeRepair(
        string changedGate,
        string expectedReason)
    {
        var controls = new ExecutionOperatorControlService(
            new ExecutionOperatorControlOptions(Path.Combine(CreateTempRoot(), "controls")),
            NullLogger<ExecutionOperatorControlService>.Instance);
        var promotionStore = new JsonlPromotionRecordStore(
            Path.Combine(CreateTempRoot(), "promotions"), NullLogger<JsonlPromotionRecordStore>.Instance);
        var service = BuildService(
            out var store,
            controls,
            brokerageConfiguration: new BrokerageConfiguration { Gateway = "alpaca", LiveExecutionEnabled = true },
            promotionRecordStore: promotionStore,
            trustEvidenceProvider: new PromotionTrustEvidenceStub(run =>
                changedGate == "expired-trust"
                    ? PromotionTrustEvidenceStub.Passing(run) with { ValidUntil = DateTimeOffset.UtcNow.AddSeconds(-1) }
                    : PromotionTrustEvidenceStub.Passing(run)));
        var run = StrategyRunEntry.Start("repair-trust", "Repair Trust", RunType.Paper)
            .Complete(BuildPassingResult()) with
        { WalkForwardEvidence = PassingWalkForwardEvidence() };
        await store.RecordRunAsync(run);
        var manualOverride = await controls.CreateManualOverrideAsync(new ManualOverrideRequest(
            ExecutionManualOverrideKinds.AllowLivePromotion, "Live controls reviewed", "ops",
            StrategyId: run.StrategyId, RunId: run.RunId));
        var targetRunId = Guid.NewGuid().ToString("N");
        var record = new StrategyPromotionRecord(
            PromotionId: Guid.NewGuid().ToString("N"),
            StrategyId: run.StrategyId,
            StrategyName: run.StrategyName,
            SourceRunType: RunType.Paper,
            TargetRunType: RunType.Live,
            SourceRunId: run.RunId,
            TargetRunId: targetRunId,
            QualifyingSharpe: run.Metrics!.Metrics.SharpeRatio,
            QualifyingMaxDrawdownPercent: run.Metrics.Metrics.MaxDrawdownPercent,
            QualifyingTotalReturn: run.Metrics.Metrics.TotalReturn,
            Decision: PromotionDecisionKinds.Approved,
            PromotedAt: DateTimeOffset.UtcNow.AddMinutes(-1),
            ApprovalReason: "Approved before interrupted target persistence.",
            ApprovalChecklist: PromotionApprovalChecklist.CreateRequiredFor(RunType.Live),
            EvidenceReferences: CreateLiveEvidenceReferences(manualOverride.OverrideId),
            AuditReference: "retained-approval-audit",
            ApprovedBy: "ops",
            ManualOverrideId: manualOverride.OverrideId);
        await promotionStore.AppendAsync(record);

        if (changedGate == "replaced-override")
        {
            await controls.ClearManualOverrideAsync(manualOverride.OverrideId, "ops", "Approval override revoked.");
            await controls.CreateManualOverrideAsync(new ManualOverrideRequest(
                ExecutionManualOverrideKinds.AllowLivePromotion, "Different approval", "ops",
                StrategyId: run.StrategyId, RunId: run.RunId));
            (await service.EvaluateAsync(run.RunId)).IsEligible.Should().BeTrue(
                "the current policy accepts the replacement, but it cannot replace the retained approval's override identity");
        }

        var retry = await service.ApproveAsync(new PromotionApprovalRequest(
            run.RunId, ApprovedBy: "ops", ApprovalReason: "Repair retained approval."));

        retry.Success.Should().BeFalse();
        retry.PromotionId.Should().Be(record.PromotionId);
        retry.Reason.Should().Contain(expectedReason);
        (await store.GetRunByIdAsync(targetRunId)).Should().BeNull();
        (await service.GetPromotionHistoryAsync()).Should().ContainSingle().Which.PromotionId.Should().Be(record.PromotionId);
    }

    [Theory]
    [InlineData("missing", "Provider trust evidence is stale.")]
    [InlineData("stale", "Provider trust evidence is stale.")]
    [InlineData("incomplete", "Provider trust evidence is incomplete.")]
    [InlineData("future", "Provider trust evidence is stale.")]
    [InlineData("wrong-run", "Provider trust evidence is incomplete.")]
    [InlineData("wrong-input", "Provider trust evidence is incomplete.")]
    public async Task Promotion_WithAllOtherGatesPassing_RequiresValidRunBoundTrustEvidence(
        string evidenceState,
        string expectedReason)
    {
        var controls = new ExecutionOperatorControlService(
            new ExecutionOperatorControlOptions(Path.Combine(CreateTempRoot(), "controls")),
            NullLogger<ExecutionOperatorControlService>.Instance);
        var trustProvider = new PromotionTrustEvidenceStub(run =>
        {
            var evidence = PromotionTrustEvidenceStub.Passing(run);
            return evidenceState switch
            {
                "stale" => evidence with { ValidUntil = DateTimeOffset.UtcNow.AddSeconds(-1) },
                "incomplete" => evidence with { IsComplete = false },
                "future" => evidence with
                {
                    ObservedAt = DateTimeOffset.UtcNow.AddHours(1),
                    ValidUntil = DateTimeOffset.UtcNow.AddHours(2)
                },
                "wrong-run" => evidence with { RunId = "another-run" },
                "wrong-input" => evidence with { InputHashSha256 = "another-input-hash" },
                _ => null
            };
        });
        var service = BuildService(
            out var store,
            controls,
            brokerageConfiguration: new BrokerageConfiguration
            {
                Gateway = "alpaca",
                LiveExecutionEnabled = true,
                MaxPositionSize = 10_000m,
                MaxOrderNotional = 100_000m,
                MaxOpenOrders = 10
            },
            trustEvidenceProvider: trustProvider,
            includeTrustEvidence: evidenceState != "missing");
        var run = StrategyRunEntry.Start("trust-source", "Trust Evidence Strategy", RunType.Paper)
            .Complete(BuildPassingResult()) with
        {
            WalkForwardEvidence = PassingWalkForwardEvidence()
        };
        await store.RecordRunAsync(run);
        var manualOverride = await controls.CreateManualOverrideAsync(new ManualOverrideRequest(
            Kind: ExecutionManualOverrideKinds.AllowLivePromotion,
            Reason: "Other controls pass",
            CreatedBy: "ops",
            StrategyId: run.StrategyId,
            RunId: run.RunId));

        var evaluation = await service.EvaluateAsync(run.RunId);
        var approval = await service.ApproveAsync(new PromotionApprovalRequest(
            run.RunId,
            ApprovedBy: "ops",
            ApprovalReason: "Review valid trust evidence before approval.",
            ApprovalChecklist: PromotionApprovalChecklist.CreateRequiredFor(RunType.Live),
            EvidenceReferences: CreateLiveEvidenceReferences(manualOverride.OverrideId),
            ManualOverrideId: manualOverride.OverrideId));

        evaluation.IsEligible.Should().BeFalse();
        evaluation.RequiresManualOverride.Should().BeFalse("an override cannot repair missing trust evidence");
        evaluation.BlockingReasons.Should().Contain(expectedReason);
        if (evidenceState is "stale" or "incomplete" or "future")
        {
            evaluation.BlockingReasons.Should().ContainSingle();
        }
        approval.Success.Should().BeFalse();
        approval.NewRunId.Should().BeNull();
        (await service.GetPromotionHistoryAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task EvaluateAsync_WhenPaperRunTargetsLive_RequiresHumanApprovalControls()
    {
        var tempRoot = CreateTempRoot();
        var controls = new ExecutionOperatorControlService(
            new ExecutionOperatorControlOptions(Path.Combine(tempRoot, "controls")),
            NullLogger<ExecutionOperatorControlService>.Instance);

        var service = BuildService(
            out var store,
            controls,
            brokerageConfiguration: new BrokerageConfiguration
            {
                Gateway = "paper",
                LiveExecutionEnabled = false
            });

        var run = StrategyRunEntry.Start("s-live", "Strategy Live", RunType.Paper) with
        {
            LastLifecycleEvent = StrategyRunLifecycleEventType.Completed,
            EndedAt = DateTimeOffset.UtcNow,
            Metrics = BuildPassingResult(),
            WalkForwardEvidence = PassingWalkForwardEvidence()
        };
        await store.RecordRunAsync(run);

        var result = await service.EvaluateAsync(run.RunId);

        result.TargetMode.Should().Be(RunType.Live);
        result.RequiresHumanApproval.Should().BeTrue();
        result.RequiresManualOverride.Should().BeTrue();
        result.RequiredManualOverrideKind.Should().Be(ExecutionManualOverrideKinds.AllowLivePromotion);
        result.BlockingReasons.Should().NotBeNull();
        result.BlockingReasons!.Should().Contain(reason => reason.Contains("does not enable live execution", StringComparison.Ordinal));
    }

    [Fact]
    public async Task EvaluateAsync_WhenPaperRunTargetsLiveWithPaperGateway_RequiresHumanApprovalControls()
    {
        var service = BuildService(
            out var store,
            brokerageConfiguration: new BrokerageConfiguration
            {
                Gateway = "paper",
                LiveExecutionEnabled = true
            });

        var run = StrategyRunEntry.Start("s-live", "Strategy Live", RunType.Paper) with
        {
            LastLifecycleEvent = StrategyRunLifecycleEventType.Completed,
            EndedAt = DateTimeOffset.UtcNow,
            Metrics = BuildPassingResult(),
            WalkForwardEvidence = PassingWalkForwardEvidence()
        };
        await store.RecordRunAsync(run);

        var result = await service.EvaluateAsync(run.RunId);

        result.TargetMode.Should().Be(RunType.Live);
        result.RequiresHumanApproval.Should().BeTrue();
        result.RequiresManualOverride.Should().BeTrue();
        result.RequiredManualOverrideKind.Should().Be(ExecutionManualOverrideKinds.AllowLivePromotion);
        result.BlockingReasons.Should().NotBeNull();
        result.BlockingReasons!.Should().Contain(reason => reason.Contains("paper trading", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [MemberData(nameof(LivePolicyScenarios))]
    public async Task EvaluateAsync_LivePolicyScenarioMatrix_ReportsExpectedDecision(
        bool includeControls,
        bool liveEnabled,
        bool createAllowOverride,
        bool createConflictOverride,
        bool openCircuitBreaker,
        bool expectEligible,
        bool expectManualOverride,
        string expectedBlockingReason)
    {
        var tempRoot = CreateTempRoot();
        var controls = includeControls
            ? new ExecutionOperatorControlService(
                new ExecutionOperatorControlOptions(Path.Combine(tempRoot, "controls")),
                NullLogger<ExecutionOperatorControlService>.Instance)
            : null;

        var service = BuildService(
            out var store,
            controls,
            auditTrail: null,
            brokerageConfiguration: new BrokerageConfiguration
            {
                Gateway = liveEnabled ? "alpaca" : "paper",
                LiveExecutionEnabled = liveEnabled
            });

        var run = StrategyRunEntry.Start("s-edge", "Strategy Edge", RunType.Paper) with
        {
            LastLifecycleEvent = StrategyRunLifecycleEventType.Completed,
            EndedAt = DateTimeOffset.UtcNow,
            Metrics = BuildPassingResult(),
            WalkForwardEvidence = PassingWalkForwardEvidence()
        };
        await store.RecordRunAsync(run);

        if (controls is not null)
        {
            if (openCircuitBreaker)
            {
                await controls.SetCircuitBreakerAsync(true, "maintenance", "ops");
            }

            if (createAllowOverride)
            {
                await controls.CreateManualOverrideAsync(new ManualOverrideRequest(
                    Kind: ExecutionManualOverrideKinds.AllowLivePromotion,
                    Reason: "approved for live",
                    CreatedBy: "ops",
                    StrategyId: run.StrategyId,
                    RunId: run.RunId));
            }

            if (createConflictOverride)
            {
                await controls.CreateManualOverrideAsync(new ManualOverrideRequest(
                    Kind: ExecutionManualOverrideKinds.ForceBlockOrders,
                    Reason: "conflicting freeze",
                    CreatedBy: "ops",
                    StrategyId: run.StrategyId,
                    RunId: run.RunId));
            }
        }

        var result = await service.EvaluateAsync(run.RunId);

        result.IsEligible.Should().Be(expectEligible);
        result.RequiresManualOverride.Should().Be(expectManualOverride);
        result.BlockingReasons.Should().NotBeNull();
        result.BlockingReasons!.Should().Contain(reason => reason.Contains(expectedBlockingReason, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ApproveAsync_WhenPaperRunHasApprovalAndOverride_CreatesLiveRun()
    {
        var tempRoot = CreateTempRoot();
        await using var auditTrail = new ExecutionAuditTrailService(
            new ExecutionAuditTrailOptions(Path.Combine(tempRoot, "audit")),
            NullLogger<ExecutionAuditTrailService>.Instance);

        var controls = new ExecutionOperatorControlService(
            new ExecutionOperatorControlOptions(Path.Combine(tempRoot, "controls")),
            NullLogger<ExecutionOperatorControlService>.Instance,
            auditTrail);

        var brokerageConfiguration = new BrokerageConfiguration
        {
            Gateway = "alpaca",
            LiveExecutionEnabled = true
        };

        var service = BuildService(
            out var store,
            controls,
            auditTrail,
            brokerageConfiguration);

        var run = StrategyRunEntry.Start("s-live", "Strategy Live", RunType.Paper) with
        {
            LastLifecycleEvent = StrategyRunLifecycleEventType.Completed,
            EndedAt = DateTimeOffset.UtcNow,
            Metrics = BuildPassingResult(),
            WalkForwardEvidence = PassingWalkForwardEvidence()
        };
        await store.RecordRunAsync(run);

        var manualOverride = await controls.CreateManualOverrideAsync(new ManualOverrideRequest(
            Kind: ExecutionManualOverrideKinds.AllowLivePromotion,
            Reason: "Ready for live capital",
            CreatedBy: "ops",
            StrategyId: run.StrategyId,
            RunId: run.RunId));
        var evidenceReferences = CreateLiveEvidenceReferences(manualOverride.OverrideId);

        var result = await service.ApproveAsync(new PromotionApprovalRequest(
            RunId: run.RunId,
            ApprovedBy: "ops",
            ApprovalReason: "Ready for live capital",
            ApprovalChecklist: PromotionApprovalChecklist.CreateRequiredFor(RunType.Live),
            EvidenceReferences: evidenceReferences,
            ManualOverrideId: manualOverride.OverrideId));

        result.Success.Should().BeTrue();
        result.NewRunId.Should().NotBeNullOrWhiteSpace();
        result.AuditReference.Should().NotBeNullOrWhiteSpace();
        result.ApprovedBy.Should().Be("ops");

        var history = await service.GetPromotionHistoryAsync();
        history.Should().ContainSingle();
        history[0].ApprovedBy.Should().Be("ops");
        history[0].ManualOverrideId.Should().Be(manualOverride.OverrideId);
        history[0].AuditReference.Should().NotBeNullOrWhiteSpace();
        history[0].SourceRunId.Should().Be(run.RunId);
        history[0].TargetRunId.Should().Be(result.NewRunId);
        history[0].Decision.Should().Be(PromotionDecisionKinds.Approved);
        history[0].ApprovalReason.Should().Be("Ready for live capital");
        history[0].ApprovalChecklist.Should().BeEquivalentTo(PromotionApprovalChecklist.CreateRequiredFor(RunType.Live));
        history[0].EvidenceReferences.Should().BeEquivalentTo(evidenceReferences);

        var recordedRuns = new List<StrategyRunEntry>();
        await foreach (var entry in store.GetAllRunsAsync())
        {
            recordedRuns.Add(entry);
        }

        recordedRuns.Should().Contain(entry =>
            entry.RunId == result.NewRunId &&
            entry.RunType == RunType.Live &&
            entry.ParentRunId == run.RunId);

        var auditEntries = await auditTrail.GetRecentAsync(10);
        auditEntries.Should().Contain(entry =>
            entry.Category == "Promotion" &&
            entry.Action == "PromotionApproved" &&
            entry.Outcome == "Approved" &&
            entry.RunId == run.RunId &&
            entry.Reason == "HumanApprovedLivePromotion" &&
            entry.Scope == $"source:{run.RunId}/strategy:{run.StrategyId}/target:Live" &&
            entry.Metadata != null &&
            entry.Metadata["manualOverrideId"] == manualOverride.OverrideId &&
            entry.Metadata["requiredManualOverrideKind"] == ExecutionManualOverrideKinds.AllowLivePromotion &&
            entry.Metadata["approvalChecklistCount"] == PromotionApprovalChecklist.CreateRequiredFor(RunType.Live).Length.ToString(System.Globalization.CultureInfo.InvariantCulture) &&
            entry.Metadata["evidenceReferenceCount"] == evidenceReferences.Length.ToString(System.Globalization.CultureInfo.InvariantCulture) &&
            entry.Metadata["evidenceReferences"].Contains(manualOverride.OverrideId, StringComparison.Ordinal));
    }

    [Fact]
    public async Task ApproveAsync_WhenPaperRunMissingLiveEvidenceReferences_ReturnsFailureWithoutCreatingLiveRun()
    {
        var tempRoot = CreateTempRoot();
        await using var auditTrail = new ExecutionAuditTrailService(
            new ExecutionAuditTrailOptions(Path.Combine(tempRoot, "audit")),
            NullLogger<ExecutionAuditTrailService>.Instance);
        var controls = new ExecutionOperatorControlService(
            new ExecutionOperatorControlOptions(Path.Combine(tempRoot, "controls")),
            NullLogger<ExecutionOperatorControlService>.Instance,
            auditTrail);
        var service = BuildService(
            out var store,
            controls,
            auditTrail,
            brokerageConfiguration: new BrokerageConfiguration
            {
                Gateway = "alpaca",
                LiveExecutionEnabled = true
            });

        var run = StrategyRunEntry.Start("s-live", "Strategy Live", RunType.Paper) with
        {
            LastLifecycleEvent = StrategyRunLifecycleEventType.Completed,
            EndedAt = DateTimeOffset.UtcNow,
            Metrics = BuildPassingResult(),
            WalkForwardEvidence = PassingWalkForwardEvidence()
        };
        await store.RecordRunAsync(run);

        var manualOverride = await controls.CreateManualOverrideAsync(new ManualOverrideRequest(
            Kind: ExecutionManualOverrideKinds.AllowLivePromotion,
            Reason: "Ready for live capital",
            CreatedBy: "ops",
            StrategyId: run.StrategyId,
            RunId: run.RunId));

        var result = await service.ApproveAsync(new PromotionApprovalRequest(
            RunId: run.RunId,
            ApprovedBy: "ops",
            ApprovalReason: "Ready for live capital",
            ApprovalChecklist: PromotionApprovalChecklist.CreateRequiredFor(RunType.Live),
            ManualOverrideId: manualOverride.OverrideId));

        result.Success.Should().BeFalse();
        result.Reason.Should().Contain("promotion evidence").And.Contain(PromotionApprovalChecklist.LiveOverrideReviewed);

        var history = await service.GetPromotionHistoryAsync();
        history.Should().BeEmpty();

        var recordedRuns = new List<StrategyRunEntry>();
        await foreach (var entry in store.GetAllRunsAsync())
        {
            recordedRuns.Add(entry);
        }

        recordedRuns.Should().NotContain(entry => entry.RunType == RunType.Live);

        var auditEntries = await auditTrail.GetRecentAsync(10);
        auditEntries.Should().Contain(entry =>
            entry.Category == "Promotion" &&
            entry.Action == "PromotionBlocked" &&
            entry.Outcome == "Blocked" &&
            entry.RunId == run.RunId &&
            entry.Reason == "PromotionEvidenceIncomplete" &&
            entry.Scope == $"source:{run.RunId}/strategy:{run.StrategyId}/target:Live" &&
            entry.Metadata != null &&
            entry.Metadata["requiredManualOverrideKind"] == ExecutionManualOverrideKinds.AllowLivePromotion &&
            entry.Metadata["controlRejectReason"].Contains(PromotionApprovalChecklist.LiveOverrideReviewed, StringComparison.Ordinal));
    }

    [Fact]
    public async Task ApproveAsync_WhenPaperRunMissingLiveChecklistItem_RecordsBlockedAudit()
    {
        var tempRoot = CreateTempRoot();
        await using var auditTrail = new ExecutionAuditTrailService(
            new ExecutionAuditTrailOptions(Path.Combine(tempRoot, "audit")),
            NullLogger<ExecutionAuditTrailService>.Instance);
        var controls = new ExecutionOperatorControlService(
            new ExecutionOperatorControlOptions(Path.Combine(tempRoot, "controls")),
            NullLogger<ExecutionOperatorControlService>.Instance,
            auditTrail);
        var service = BuildService(
            out var store,
            controls,
            auditTrail,
            brokerageConfiguration: new BrokerageConfiguration
            {
                Gateway = "alpaca",
                LiveExecutionEnabled = true
            });

        var run = StrategyRunEntry.Start("s-live", "Strategy Live", RunType.Paper) with
        {
            LastLifecycleEvent = StrategyRunLifecycleEventType.Completed,
            EndedAt = DateTimeOffset.UtcNow,
            Metrics = BuildPassingResult(),
            WalkForwardEvidence = PassingWalkForwardEvidence()
        };
        await store.RecordRunAsync(run);

        var manualOverride = await controls.CreateManualOverrideAsync(new ManualOverrideRequest(
            Kind: ExecutionManualOverrideKinds.AllowLivePromotion,
            Reason: "Ready for live capital",
            CreatedBy: "ops",
            StrategyId: run.StrategyId,
            RunId: run.RunId));
        var checklist = PromotionApprovalChecklist.CreateRequiredFor(RunType.Live)
            .Where(static item => !string.Equals(item, PromotionApprovalChecklist.RollbackKillSwitchReviewed, StringComparison.Ordinal))
            .ToArray();

        var result = await service.ApproveAsync(new PromotionApprovalRequest(
            RunId: run.RunId,
            ApprovedBy: "ops",
            ApprovalReason: "Ready for live capital",
            ApprovalChecklist: checklist,
            EvidenceReferences: CreateLiveEvidenceReferences(manualOverride.OverrideId),
            ManualOverrideId: manualOverride.OverrideId));

        result.Success.Should().BeFalse();
        result.Reason.Should().Contain("approval checklist").And.Contain(PromotionApprovalChecklist.RollbackKillSwitchReviewed);

        var history = await service.GetPromotionHistoryAsync();
        history.Should().BeEmpty();

        var recordedRuns = new List<StrategyRunEntry>();
        await foreach (var entry in store.GetAllRunsAsync())
        {
            recordedRuns.Add(entry);
        }

        recordedRuns.Should().NotContain(entry => entry.RunType == RunType.Live);

        var auditEntries = await auditTrail.GetRecentAsync(10);
        auditEntries.Should().Contain(entry =>
            entry.Category == "Promotion" &&
            entry.Action == "PromotionBlocked" &&
            entry.Outcome == "Blocked" &&
            entry.RunId == run.RunId &&
            entry.Reason == "PromotionChecklistIncomplete" &&
            entry.Scope == $"source:{run.RunId}/strategy:{run.StrategyId}/target:Live" &&
            entry.Metadata != null &&
            entry.Metadata["requiredManualOverrideKind"] == ExecutionManualOverrideKinds.AllowLivePromotion &&
            entry.Metadata["approvalChecklist"].Contains(PromotionApprovalChecklist.RollbackKillSwitchReviewed, StringComparison.Ordinal) == false &&
            entry.Metadata["controlRejectReason"].Contains(PromotionApprovalChecklist.RollbackKillSwitchReviewed, StringComparison.Ordinal));
    }

    [Fact]
    public async Task ApproveAsync_WhenPaperRunMissingBrokerExecutionReconciliationEvidence_ReturnsFailureWithoutCreatingLiveRun()
    {
        var tempRoot = CreateTempRoot();
        await using var auditTrail = new ExecutionAuditTrailService(
            new ExecutionAuditTrailOptions(Path.Combine(tempRoot, "audit")),
            NullLogger<ExecutionAuditTrailService>.Instance);
        var controls = new ExecutionOperatorControlService(
            new ExecutionOperatorControlOptions(Path.Combine(tempRoot, "controls")),
            NullLogger<ExecutionOperatorControlService>.Instance,
            auditTrail);
        var service = BuildService(
            out var store,
            controls,
            auditTrail,
            brokerageConfiguration: new BrokerageConfiguration
            {
                Gateway = "alpaca",
                LiveExecutionEnabled = true
            });

        var run = StrategyRunEntry.Start("s-live", "Strategy Live", RunType.Paper) with
        {
            LastLifecycleEvent = StrategyRunLifecycleEventType.Completed,
            EndedAt = DateTimeOffset.UtcNow,
            Metrics = BuildPassingResult(),
            WalkForwardEvidence = PassingWalkForwardEvidence()
        };
        await store.RecordRunAsync(run);

        var manualOverride = await controls.CreateManualOverrideAsync(new ManualOverrideRequest(
            Kind: ExecutionManualOverrideKinds.AllowLivePromotion,
            Reason: "Ready for live capital",
            CreatedBy: "ops",
            StrategyId: run.StrategyId,
            RunId: run.RunId));
        var evidenceReferences = CreateLiveEvidenceReferences(manualOverride.OverrideId)
            .Where(static item => !item.StartsWith(PromotionApprovalChecklist.BrokerExecutionReconciliationReviewed, StringComparison.Ordinal))
            .ToArray();

        var result = await service.ApproveAsync(new PromotionApprovalRequest(
            RunId: run.RunId,
            ApprovedBy: "ops",
            ApprovalReason: "Ready for live capital",
            ApprovalChecklist: PromotionApprovalChecklist.CreateRequiredFor(RunType.Live),
            EvidenceReferences: evidenceReferences,
            ManualOverrideId: manualOverride.OverrideId));

        result.Success.Should().BeFalse();
        result.Reason.Should().Contain("promotion evidence").And.Contain(PromotionApprovalChecklist.BrokerExecutionReconciliationReviewed);

        var history = await service.GetPromotionHistoryAsync();
        history.Should().BeEmpty();

        var recordedRuns = new List<StrategyRunEntry>();
        await foreach (var entry in store.GetAllRunsAsync())
        {
            recordedRuns.Add(entry);
        }

        recordedRuns.Should().NotContain(entry => entry.RunType == RunType.Live);

        var auditEntries = await auditTrail.GetRecentAsync(10);
        auditEntries.Should().Contain(entry =>
            entry.Category == "Promotion" &&
            entry.Action == "PromotionBlocked" &&
            entry.Outcome == "Blocked" &&
            entry.RunId == run.RunId &&
            entry.Reason == "PromotionEvidenceIncomplete" &&
            entry.Scope == $"source:{run.RunId}/strategy:{run.StrategyId}/target:Live" &&
            entry.Metadata != null &&
            entry.Metadata["requiredManualOverrideKind"] == ExecutionManualOverrideKinds.AllowLivePromotion &&
            entry.Metadata["controlRejectReason"].Contains(PromotionApprovalChecklist.BrokerExecutionReconciliationReviewed, StringComparison.Ordinal));
    }

    [Fact]
    public async Task ApproveAsync_WhenLiveEvidenceReferenceHasNoRetainedValue_ReturnsFailureWithoutCreatingLiveRun()
    {
        var tempRoot = CreateTempRoot();
        await using var auditTrail = new ExecutionAuditTrailService(
            new ExecutionAuditTrailOptions(Path.Combine(tempRoot, "audit")),
            NullLogger<ExecutionAuditTrailService>.Instance);
        var controls = new ExecutionOperatorControlService(
            new ExecutionOperatorControlOptions(Path.Combine(tempRoot, "controls")),
            NullLogger<ExecutionOperatorControlService>.Instance,
            auditTrail);
        var service = BuildService(
            out var store,
            controls,
            auditTrail,
            brokerageConfiguration: new BrokerageConfiguration
            {
                Gateway = "alpaca",
                LiveExecutionEnabled = true
            });

        var run = StrategyRunEntry.Start("s-live", "Strategy Live", RunType.Paper) with
        {
            LastLifecycleEvent = StrategyRunLifecycleEventType.Completed,
            EndedAt = DateTimeOffset.UtcNow,
            Metrics = BuildPassingResult(),
            WalkForwardEvidence = PassingWalkForwardEvidence()
        };
        await store.RecordRunAsync(run);

        var manualOverride = await controls.CreateManualOverrideAsync(new ManualOverrideRequest(
            Kind: ExecutionManualOverrideKinds.AllowLivePromotion,
            Reason: "Ready for live capital",
            CreatedBy: "ops",
            StrategyId: run.StrategyId,
            RunId: run.RunId));
        var evidenceReferences = CreateLiveEvidenceReferences(manualOverride.OverrideId)
            .Select(static item => item.StartsWith(PromotionApprovalChecklist.AuditRetentionReviewed, StringComparison.Ordinal)
                ? $"{PromotionApprovalChecklist.AuditRetentionReviewed}:"
                : item)
            .ToArray();

        var result = await service.ApproveAsync(new PromotionApprovalRequest(
            RunId: run.RunId,
            ApprovedBy: "ops",
            ApprovalReason: "Ready for live capital",
            ApprovalChecklist: PromotionApprovalChecklist.CreateRequiredFor(RunType.Live),
            EvidenceReferences: evidenceReferences,
            ManualOverrideId: manualOverride.OverrideId));

        result.Success.Should().BeFalse();
        result.Reason.Should().Contain("evidence references are invalid")
            .And.Contain(PromotionApprovalChecklist.AuditRetentionReviewed)
            .And.Contain("retained evidence");

        var history = await service.GetPromotionHistoryAsync();
        history.Should().BeEmpty();

        var recordedRuns = new List<StrategyRunEntry>();
        await foreach (var entry in store.GetAllRunsAsync())
        {
            recordedRuns.Add(entry);
        }

        recordedRuns.Should().NotContain(entry => entry.RunType == RunType.Live);

        var auditEntries = await auditTrail.GetRecentAsync(10);
        auditEntries.Should().Contain(entry =>
            entry.Category == "Promotion" &&
            entry.Action == "PromotionBlocked" &&
            entry.Outcome == "Blocked" &&
            entry.RunId == run.RunId &&
            entry.Reason == "PromotionEvidenceInvalid" &&
            entry.Metadata != null &&
            entry.Metadata["controlRejectReason"].Contains(PromotionApprovalChecklist.AuditRetentionReviewed, StringComparison.Ordinal));
    }

    [Fact]
    public async Task ApproveAsync_WhenLiveOverrideEvidenceReferencesDifferentOverride_ReturnsFailureWithoutCreatingLiveRun()
    {
        var tempRoot = CreateTempRoot();
        await using var auditTrail = new ExecutionAuditTrailService(
            new ExecutionAuditTrailOptions(Path.Combine(tempRoot, "audit")),
            NullLogger<ExecutionAuditTrailService>.Instance);
        var controls = new ExecutionOperatorControlService(
            new ExecutionOperatorControlOptions(Path.Combine(tempRoot, "controls")),
            NullLogger<ExecutionOperatorControlService>.Instance,
            auditTrail);
        var service = BuildService(
            out var store,
            controls,
            auditTrail,
            brokerageConfiguration: new BrokerageConfiguration
            {
                Gateway = "alpaca",
                LiveExecutionEnabled = true
            });

        var run = StrategyRunEntry.Start("s-live", "Strategy Live", RunType.Paper) with
        {
            LastLifecycleEvent = StrategyRunLifecycleEventType.Completed,
            EndedAt = DateTimeOffset.UtcNow,
            Metrics = BuildPassingResult(),
            WalkForwardEvidence = PassingWalkForwardEvidence()
        };
        await store.RecordRunAsync(run);

        var manualOverride = await controls.CreateManualOverrideAsync(new ManualOverrideRequest(
            Kind: ExecutionManualOverrideKinds.AllowLivePromotion,
            Reason: "Ready for live capital",
            CreatedBy: "ops",
            StrategyId: run.StrategyId,
            RunId: run.RunId));
        var evidenceReferences = CreateLiveEvidenceReferences($"{manualOverride.OverrideId}-stale");

        var result = await service.ApproveAsync(new PromotionApprovalRequest(
            RunId: run.RunId,
            ApprovedBy: "ops",
            ApprovalReason: "Ready for live capital",
            ApprovalChecklist: PromotionApprovalChecklist.CreateRequiredFor(RunType.Live),
            EvidenceReferences: evidenceReferences,
            ManualOverrideId: manualOverride.OverrideId));

        result.Success.Should().BeFalse();
        result.Reason.Should().Contain("evidence references are invalid")
            .And.Contain(PromotionApprovalChecklist.LiveOverrideReviewed)
            .And.Contain(manualOverride.OverrideId);

        var history = await service.GetPromotionHistoryAsync();
        history.Should().BeEmpty();

        var recordedRuns = new List<StrategyRunEntry>();
        await foreach (var entry in store.GetAllRunsAsync())
        {
            recordedRuns.Add(entry);
        }

        recordedRuns.Should().NotContain(entry => entry.RunType == RunType.Live);

        var auditEntries = await auditTrail.GetRecentAsync(10);
        auditEntries.Should().Contain(entry =>
            entry.Category == "Promotion" &&
            entry.Action == "PromotionBlocked" &&
            entry.Outcome == "Blocked" &&
            entry.RunId == run.RunId &&
            entry.Reason == "PromotionEvidenceInvalid" &&
            entry.Metadata != null &&
            entry.Metadata["controlRejectReason"].Contains(manualOverride.OverrideId, StringComparison.Ordinal));
    }

    [Fact]
    public async Task ApproveAsync_WhenPaperRunMissingManualOverride_IsRejectedByExecutionControls()
    {
        var tempRoot = CreateTempRoot();
        await using var auditTrail = new ExecutionAuditTrailService(
            new ExecutionAuditTrailOptions(Path.Combine(tempRoot, "audit")),
            NullLogger<ExecutionAuditTrailService>.Instance);
        var controls = new ExecutionOperatorControlService(
            new ExecutionOperatorControlOptions(Path.Combine(tempRoot, "controls")),
            NullLogger<ExecutionOperatorControlService>.Instance,
            auditTrail);
        var service = BuildService(
            out var store,
            controls,
            auditTrail,
            brokerageConfiguration: new BrokerageConfiguration
            {
                Gateway = "alpaca",
                LiveExecutionEnabled = true
            });

        var run = StrategyRunEntry.Start("s-live", "Strategy Live", RunType.Paper) with
        {
            LastLifecycleEvent = StrategyRunLifecycleEventType.Completed,
            EndedAt = DateTimeOffset.UtcNow,
            Metrics = BuildPassingResult(),
            WalkForwardEvidence = PassingWalkForwardEvidence()
        };
        await store.RecordRunAsync(run);

        var result = await service.ApproveAsync(new PromotionApprovalRequest(
            run.RunId,
            ApprovedBy: "ops",
            ApprovalReason: "Ready for live capital.",
            ApprovalChecklist: PromotionApprovalChecklist.CreateRequiredFor(RunType.Live),
            EvidenceReferences: CreateLiveEvidenceReferences("override-under-review")));

        result.Success.Should().BeFalse();
        result.Reason.Should().NotBeNull();
        result.Reason!.Contains("requires an active AllowLivePromotion", StringComparison.OrdinalIgnoreCase).Should().BeTrue();

        var auditEntries = await auditTrail.GetRecentAsync(10);
        auditEntries.Should().Contain(entry =>
            entry.Category == "Promotion" &&
            entry.Action == "PromotionBlocked" &&
            entry.Outcome == "Blocked" &&
            entry.RunId == run.RunId &&
            entry.Reason == "LivePromotionPolicyBlocked" &&
            entry.Scope == $"source:{run.RunId}/strategy:{run.StrategyId}/target:Live" &&
            entry.Metadata != null &&
            entry.Metadata["requiredManualOverrideKind"] == ExecutionManualOverrideKinds.AllowLivePromotion &&
            entry.Metadata["controlRejectReason"].Contains("requires an active AllowLivePromotion", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task GetPromotionHistoryAsync_WithDurableStore_RetainsLiveApprovalAcrossRestart()
    {
        var tempRoot = CreateTempRoot();
        await using var auditTrail = new ExecutionAuditTrailService(
            new ExecutionAuditTrailOptions(Path.Combine(tempRoot, "audit")),
            NullLogger<ExecutionAuditTrailService>.Instance);

        var controls = new ExecutionOperatorControlService(
            new ExecutionOperatorControlOptions(Path.Combine(tempRoot, "controls")),
            NullLogger<ExecutionOperatorControlService>.Instance,
            auditTrail);

        var durableStore = new JsonlPromotionRecordStore(
            new PromotionRecordStoreOptions(Path.Combine(tempRoot, "promotion-history")),
            NullLogger<JsonlPromotionRecordStore>.Instance);

        var service = BuildService(
            out var store,
            controls,
            auditTrail,
            new BrokerageConfiguration
            {
                Gateway = "alpaca",
                LiveExecutionEnabled = true
            },
            durableStore);

        var run = StrategyRunEntry.Start("s-live", "Strategy Live", RunType.Paper) with
        {
            LastLifecycleEvent = StrategyRunLifecycleEventType.Completed,
            EndedAt = DateTimeOffset.UtcNow,
            Metrics = BuildPassingResult(),
            WalkForwardEvidence = PassingWalkForwardEvidence()
        };
        await store.RecordRunAsync(run);

        var manualOverride = await controls.CreateManualOverrideAsync(new ManualOverrideRequest(
            Kind: ExecutionManualOverrideKinds.AllowLivePromotion,
            Reason: "Ready for live capital",
            CreatedBy: "ops",
            StrategyId: run.StrategyId,
            RunId: run.RunId));
        var evidenceReferences = CreateLiveEvidenceReferences(manualOverride.OverrideId);

        await service.ApproveAsync(new PromotionApprovalRequest(
            RunId: run.RunId,
            ApprovedBy: "ops",
            ApprovalReason: "Ready for live capital",
            ReviewNotes: "All controls green",
            ApprovalChecklist: PromotionApprovalChecklist.CreateRequiredFor(RunType.Live),
            EvidenceReferences: evidenceReferences,
            ManualOverrideId: manualOverride.OverrideId));

        var restarted = BuildService(
            out _,
            controls,
            auditTrail,
            new BrokerageConfiguration
            {
                Gateway = "alpaca",
                LiveExecutionEnabled = true
            },
            durableStore);

        var history = await restarted.GetPromotionHistoryAsync();
        var scopedHistoryWithoutRetainedSource = await restarted.GetPromotionHistoryAsync(
            new StrategyRunReadScope("tenant-a", "company-a"));

        history.Should().ContainSingle();
        scopedHistoryWithoutRetainedSource.Should().BeEmpty(
            "scoped history must fail closed when the restarted run store cannot prove source ownership");
        history[0].SourceRunId.Should().Be(run.RunId);
        history[0].TargetRunId.Should().NotBeNullOrWhiteSpace();
        history[0].AuditReference.Should().NotBeNullOrWhiteSpace();
        history[0].ManualOverrideId.Should().Be(manualOverride.OverrideId);
        history[0].Decision.Should().Be(PromotionDecisionKinds.Approved);
        history[0].ApprovedBy.Should().Be("ops");
        history[0].ReviewNotes.Should().Be("All controls green");
        history[0].EvidenceReferences.Should().BeEquivalentTo(evidenceReferences);
    }

    private static PromotionService BuildService(
        out StrategyRunStore store,
        ExecutionOperatorControlService? controls = null,
        ExecutionAuditTrailService? auditTrail = null,
        BrokerageConfiguration? brokerageConfiguration = null,
        IPromotionRecordStore? promotionRecordStore = null,
        IPromotionTrustEvidenceProvider? trustEvidenceProvider = null,
        bool includeTrustEvidence = true)
    {
        store = new StrategyRunStore();
        var promoter = new BacktestToLivePromoter();
        var promotionStore = promotionRecordStore ?? new JsonlPromotionRecordStore(
            Path.Combine(CreateTempRoot(), "promotion-history"),
            NullLogger<JsonlPromotionRecordStore>.Instance);
        return new PromotionService(
            store,
            promoter,
            promotionStore,
            NullLogger<PromotionService>.Instance,
            controls,
            auditTrail,
            brokerageConfiguration,
            trustEvidenceProvider: includeTrustEvidence
                ? trustEvidenceProvider ?? new PromotionTrustEvidenceStub()
                : null);
    }

    public static IEnumerable<object[]> LivePolicyScenarios()
    {
        yield return [true, true, false, false, false, false, true, "requires an active AllowLivePromotion"];
        yield return [false, true, false, false, false, false, true, "requires an active AllowLivePromotion"];
        yield return [true, true, true, true, false, false, true, "conflicting manual override"];
        yield return [true, true, true, false, true, false, true, "circuit breaker is open"];
    }

    private static string[] CreateLiveEvidenceReferences(string manualOverrideId)
        =>
        [
            $"{PromotionApprovalChecklist.Dk1TrustPacketReviewed}:evidence-vault/dk1-trust-packet",
            $"{PromotionApprovalChecklist.RunLineageReviewed}:strategy-run/run-live-source",
            $"{PromotionApprovalChecklist.PortfolioLedgerContinuityReviewed}:ledger-continuity/live-book",
            $"{PromotionApprovalChecklist.RiskControlsReviewed}:risk-controls/pre-live",
            $"{PromotionApprovalChecklist.PaperValidationReviewed}:paper-validation/run-live-source",
            $"{PromotionApprovalChecklist.ReconciliationEvidenceReviewed}:reconciliation/live-readiness",
            $"{PromotionApprovalChecklist.BrokerExecutionReconciliationReviewed}:broker-execution-reconciliation/live-readiness",
            $"{PromotionApprovalChecklist.AccountingRecordsReviewed}:accounting-records/live-readiness",
            $"{PromotionApprovalChecklist.GovernedReportingReviewed}:report-pack/live-readiness",
            $"{PromotionApprovalChecklist.GovernanceSignoffReviewed}:governance-signoff/live-readiness",
            $"{PromotionApprovalChecklist.ExceptionHandlingReviewed}:exceptions/live-readiness",
            $"{PromotionApprovalChecklist.RollbackKillSwitchReviewed}:controls/kill-switch/live-readiness",
            $"{PromotionApprovalChecklist.AuditRetentionReviewed}:audit-retention/live-readiness",
            $"{PromotionApprovalChecklist.LiveOverrideReviewed}:manual-override/{manualOverrideId}",
            $"{PromotionApprovalChecklist.PaperExecutionModelReviewed}:paper-match/1+paper-cost/1"
        ];

    private static string CreateTempRoot()
    {
        var path = Path.Combine(Path.GetTempPath(), "Meridian.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    /// <summary>
    /// Walk-forward evidence that clears the out-of-sample promotion gates, so live-governance
    /// scenarios keep exercising their original concern (trust, overrides, brokerage, breaker).
    /// </summary>
    private static StrategyRunWalkForwardEvidence PassingWalkForwardEvidence() => new(
        OutOfSampleSharpeRatio: 1.1,
        OutOfSampleMaxDrawdownPercent: 0.08m,
        DegradationRatio: 0.85,
        WindowCount: 6,
        RecordedAt: DateTimeOffset.UtcNow);

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
            Ledger: new Meridian.Ledger.Ledger(),
            ElapsedTime: TimeSpan.FromMinutes(5),
            TotalEventsProcessed: 500);
    }
}
