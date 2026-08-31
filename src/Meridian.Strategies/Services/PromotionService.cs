using Meridian.Backtesting.Sdk;
using Meridian.Contracts.Workstation;
using Meridian.Execution.Sdk;
using Meridian.Execution.Services;
using Meridian.Strategies.Interfaces;
using Meridian.Strategies.Models;
using Meridian.Strategies.Promotions;
using Microsoft.Extensions.Logging;
using Interop = Meridian.FSharp.Interop;

namespace Meridian.Strategies.Services;

/// <summary>
/// Orchestrates the strategy promotion workflow: evaluates eligibility,
/// records promotion decisions, and creates new run entries for the target mode.
/// Bridges the gap between promotion evaluation (F# policy) and operational execution.
/// </summary>
public sealed class PromotionService
{
    private readonly IStrategyRepository _repository;
    private readonly BacktestToLivePromoter _promoter;
    private readonly IPromotionRecordStore _promotionRecordStore;
    private readonly ILogger<PromotionService> _logger;
    private readonly ExecutionOperatorControlService? _operatorControls;
    private readonly ExecutionAuditTrailService? _auditTrail;
    private readonly BrokerageConfiguration? _brokerageConfiguration;
    private readonly IPromotedRunLauncher? _runLauncher;

    public PromotionService(
        IStrategyRepository repository,
        BacktestToLivePromoter promoter,
        IPromotionRecordStore promotionRecordStore,
        ILogger<PromotionService> logger,
        ExecutionOperatorControlService? operatorControls = null,
        ExecutionAuditTrailService? auditTrail = null,
        BrokerageConfiguration? brokerageConfiguration = null,
        IPromotedRunLauncher? runLauncher = null)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _promoter = promoter ?? throw new ArgumentNullException(nameof(promoter));
        _promotionRecordStore = promotionRecordStore ?? throw new ArgumentNullException(nameof(promotionRecordStore));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _operatorControls = operatorControls;
        _auditTrail = auditTrail;
        _brokerageConfiguration = brokerageConfiguration;
        _runLauncher = runLauncher;
    }

    /// <summary>
    /// Evaluates whether a completed run is eligible for promotion to the next mode.
    /// </summary>
    public Task<PromotionEvaluationResult> EvaluateAsync(
        string runId,
        PromotionCriteria? criteria = null,
        CancellationToken ct = default) =>
        EvaluateCoreAsync(runId, criteria, scope: null, ct);

    /// <summary>
    /// Evaluates a run only when its retained tenant and company exactly match the trusted
    /// workstation scope.
    /// </summary>
    public Task<PromotionEvaluationResult> EvaluateAsync(
        string runId,
        StrategyRunReadScope scope,
        PromotionCriteria? criteria = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        return EvaluateCoreAsync(runId, criteria, scope, ct);
    }

    private async Task<PromotionEvaluationResult> EvaluateCoreAsync(
        string runId,
        PromotionCriteria? criteria,
        StrategyRunReadScope? scope,
        CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);

        var run = await FindRunAsync(runId, scope, ct).ConfigureAwait(false);
        if (run is null)
        {
            return PromotionEvaluationResult.NotFound(runId);
        }

        if (!run.EndedAt.HasValue)
        {
            return PromotionEvaluationResult.NotReady(runId, "Run has not completed yet.");
        }

        if (run.RunType == RunType.Live)
        {
            return PromotionEvaluationResult.NotReady(runId, "Live runs cannot be promoted further.");
        }

        if (run.Metrics is null)
        {
            return PromotionEvaluationResult.NotReady(runId, "Run has no metrics available for evaluation.");
        }

        var effectiveCriteria = criteria ?? PromotionCriteria.Default;
        var metrics = run.Metrics.Metrics;
        var targetMode = run.RunType == RunType.Backtest ? RunType.Paper : RunType.Live;
        var controlsSnapshot = _operatorControls?.GetSnapshot();
        var brokerageValidation = targetMode == RunType.Live
            ? BrokerageValidationEvaluator.Evaluate(_brokerageConfiguration)
            : null;
        var hasConflictingOverride = false;
        var hasLivePromotionOverride = false;
        if (targetMode == RunType.Live && controlsSnapshot is not null)
        {
            foreach (var overrideEntry in controlsSnapshot.ManualOverrides)
            {
                var matchesStrategy = string.IsNullOrWhiteSpace(overrideEntry.StrategyId) ||
                                      string.Equals(overrideEntry.StrategyId, run.StrategyId, StringComparison.OrdinalIgnoreCase);
                var matchesRun = string.IsNullOrWhiteSpace(overrideEntry.RunId) ||
                                 string.Equals(overrideEntry.RunId, run.RunId, StringComparison.OrdinalIgnoreCase);
                if (!matchesStrategy || !matchesRun)
                {
                    continue;
                }

                if (string.Equals(overrideEntry.Kind, ExecutionManualOverrideKinds.ForceBlockOrders, StringComparison.OrdinalIgnoreCase))
                {
                    hasConflictingOverride = true;
                }

                if (string.Equals(overrideEntry.Kind, ExecutionManualOverrideKinds.AllowLivePromotion, StringComparison.OrdinalIgnoreCase))
                {
                    hasLivePromotionOverride = true;
                }
            }
        }

        var walkForwardEvidence = run.WalkForwardEvidence;
        var policyInput = new Meridian.FSharp.Promotion.PromotionPolicy.PromotionPolicyInput(
            run.EndedAt.HasValue,
            run.Metrics is not null,
            metrics.SharpeRatio,
            metrics.MaxDrawdownPercent,
            metrics.TotalReturn,
            effectiveCriteria.MinSharpeRatio,
            effectiveCriteria.MaxAllowedDrawdownPercent,
            effectiveCriteria.MinTotalReturn,
            targetMode == RunType.Live,
            controlsSnapshot is not null || targetMode != RunType.Live,
            controlsSnapshot is not null || targetMode != RunType.Live,
            _brokerageConfiguration?.LiveExecutionEnabled ?? false,
            controlsSnapshot?.CircuitBreaker.IsOpen ?? false,
            hasConflictingOverride,
            targetMode != RunType.Live || hasLivePromotionOverride,
            ExecutionManualOverrideKinds.AllowLivePromotion,
            requireWalkForwardEvidence: effectiveCriteria.RequireWalkForwardEvidenceForLive && targetMode == RunType.Live,
            hasWalkForwardEvidence: walkForwardEvidence is not null,
            outOfSampleSharpeRatio: walkForwardEvidence?.OutOfSampleSharpeRatio ?? 0.0,
            walkForwardDegradationRatio: walkForwardEvidence?.DegradationRatio ?? 0.0,
            minOutOfSampleSharpe: effectiveCriteria.MinOutOfSampleSharpe,
            minWalkForwardDegradationRatio: effectiveCriteria.MinWalkForwardDegradationRatio,
            outOfSampleMaxDrawdownPercent: walkForwardEvidence?.OutOfSampleMaxDrawdownPercent ?? 0m,
            maxOutOfSampleDrawdownPercent: effectiveCriteria.MaxOutOfSampleDrawdownPercent);
        var policyDecision = Interop.PromotionInterop.EvaluatePromotionPolicy(policyInput);
        var hasBrokerageGap = brokerageValidation?.HasBlockingGap == true;
        var eligible = policyDecision.Eligible && !hasBrokerageGap;
        var requiresManualOverride =
            string.Equals(policyDecision.Outcome, "requires_manual_override", StringComparison.OrdinalIgnoreCase) ||
            (targetMode == RunType.Live && hasBrokerageGap);
        var requiresHumanApproval =
            requiresManualOverride ||
            string.Equals(policyDecision.Outcome, "requires_human_review", StringComparison.OrdinalIgnoreCase);
        var blockingReasons = new List<string>();
        if (policyDecision.Reasons.Length > 0)
        {
            blockingReasons.AddRange(policyDecision.Reasons);
        }

        if (targetMode == RunType.Live && brokerageValidation is not null && brokerageValidation.Findings.Count > 0)
        {
            blockingReasons.AddRange(brokerageValidation.Findings);
        }

        var requiredManualOverrideKind = string.IsNullOrWhiteSpace(policyDecision.RequiredManualOverrideKind)
            ? requiresManualOverride && targetMode == RunType.Live
                ? ExecutionManualOverrideKinds.AllowLivePromotion
                : null
            : policyDecision.RequiredManualOverrideKind;
        var blockingReasonSet = blockingReasons.Count > 0
            ? blockingReasons
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray()
            : null;
        var reason = policyDecision.Outcome switch
        {
            "approved" when hasBrokerageGap && brokerageValidation is not null => brokerageValidation.Summary,
            "approved" => "Meets all promotion policy gates.",
            "requires_human_review" => "Promotion requires operator promotion review.",
            "requires_manual_override" => "Promotion requires a manual override.",
            "blocked" => "Promotion is blocked by policy.",
            _ when hasBrokerageGap && brokerageValidation is not null => brokerageValidation.Summary,
            _ => "Promotion policy decision unavailable."
        };

        _logger.LogInformation(
            "Promotion evaluation for run {RunId}: eligible={Eligible}, target={Target}, sharpe={Sharpe:F3}",
            runId, eligible, targetMode, metrics.SharpeRatio);

        return new PromotionEvaluationResult(
            RunId: runId,
            StrategyId: run.StrategyId,
            StrategyName: run.StrategyName,
            SourceMode: run.RunType,
            TargetMode: targetMode,
            IsEligible: eligible,
            SharpeRatio: metrics.SharpeRatio,
            MaxDrawdownPercent: metrics.MaxDrawdownPercent,
            TotalReturn: metrics.TotalReturn,
            Reason: reason,
            Found: true,
            Ready: true,
            RequiresHumanApproval: requiresHumanApproval,
            RequiresManualOverride: requiresManualOverride,
            RequiredManualOverrideKind: requiredManualOverrideKind,
            BlockingReasons: blockingReasonSet);
    }

    /// <summary>
    /// Approves a promotion: creates a new run entry for the target mode and records the audit trail.
    /// </summary>
    public Task<PromotionDecisionResult> ApproveAsync(
        PromotionApprovalRequest request,
        CancellationToken ct = default) =>
        ApproveCoreAsync(request, scope: null, ct);

    /// <summary>
    /// Approves a promotion only when the source run exactly matches the trusted tenant and
    /// company scope.
    /// </summary>
    public Task<PromotionDecisionResult> ApproveAsync(
        PromotionApprovalRequest request,
        StrategyRunReadScope scope,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        return ApproveCoreAsync(request, scope, ct);
    }

    private async Task<PromotionDecisionResult> ApproveCoreAsync(
        PromotionApprovalRequest request,
        StrategyRunReadScope? scope,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.ApprovedBy))
        {
            return new PromotionDecisionResult(
                Success: false,
                PromotionId: null,
                NewRunId: null,
                Reason: "Promotion approval requires an approver.");
        }

        if (string.IsNullOrWhiteSpace(request.ApprovalReason))
        {
            return new PromotionDecisionResult(
                Success: false,
                PromotionId: null,
                NewRunId: null,
                Reason: "Promotion approval requires an approval reason.");
        }

        return await ApproveSingleDecisionAsync(request, scope, ct).ConfigureAwait(false);
    }

    private async Task<PromotionDecisionResult> ApproveSingleDecisionAsync(
        PromotionApprovalRequest request,
        StrategyRunReadScope? scope,
        CancellationToken ct)
    {

        var run = await FindRunAsync(request.RunId, scope, ct).ConfigureAwait(false);
        if (run?.Metrics is null || !run.EndedAt.HasValue)
        {
            return new PromotionDecisionResult(
                Success: false,
                PromotionId: null,
                NewRunId: null,
                Reason: "Run not found, incomplete, or has no metrics.");
        }

        if (run.RunType == RunType.Live)
        {
            return new PromotionDecisionResult(
                Success: false,
                PromotionId: null,
                NewRunId: null,
                Reason: "Live runs cannot be promoted further.");
        }

        var targetRunType = run.RunType == RunType.Backtest ? RunType.Paper : RunType.Live;
        var existingDecision = await FindExistingDecisionAsync(run, targetRunType, ct).ConfigureAwait(false);
        if (existingDecision is not null)
        {
            if (!string.Equals(
                    existingDecision.Decision,
                    PromotionDecisionKinds.Approved,
                    StringComparison.OrdinalIgnoreCase))
            {
                return CreateApprovalRetryResult(existingDecision);
            }

            return await ReserveAndMaterializeApprovedDecisionAsync(
                    run,
                    existingDecision,
                    ct)
                .ConfigureAwait(false);
        }

        var approvalChecklist = PromotionApprovalChecklist.Normalize(request.ApprovalChecklist);
        var evidenceReferences = NormalizeEvidenceReferences(request.EvidenceReferences);
        var auditReference = Guid.NewGuid().ToString("N");

        var evaluation = await EvaluateCoreAsync(
                run.RunId,
                criteria: null,
                scope: scope,
                ct: ct)
            .ConfigureAwait(false);
        if (!evaluation.IsEligible)
        {
            if (targetRunType == RunType.Live)
            {
                var reason = evaluation.BlockingReasons?.FirstOrDefault()
                    ?? evaluation.Reason
                    ?? "Promotion gate is blocked.";
                await RecordPromotionAuditAsync(
                    action: "PromotionBlocked",
                    outcome: "Blocked",
                    actor: request.ApprovedBy,
                    runId: run.RunId,
                    promotionId: null,
                    message: reason,
                    reason: "LivePromotionPolicyBlocked",
                    scope: BuildPromotionAuditScope(run, targetRunType),
                    metadata: BuildPromotionControlMetadata(
                        run,
                        targetRunType,
                        request.ManualOverrideId,
                        approvalChecklist,
                        evidenceReferences,
                        auditReference,
                        reason),
                    ct).ConfigureAwait(false);
            }

            return new PromotionDecisionResult(
                Success: false,
                PromotionId: null,
                NewRunId: null,
                Reason: evaluation.BlockingReasons?.FirstOrDefault()
                    ?? evaluation.Reason
                    ?? "Promotion gate is blocked.");
        }

        // W9-TRUTH-001: a run whose figures derive from simulated, seeded, or sample data
        // carries a blocking simulation provenance mark and can never be approved into
        // promotion evidence — fail closed before any checklist or evidence evaluation.
        if (!string.IsNullOrWhiteSpace(run.DataProvenanceToken)
            && !string.Equals(run.DataProvenanceToken.Trim(), "real", StringComparison.OrdinalIgnoreCase))
        {
            var provenanceReason =
                $"Source run {run.RunId} is marked '{run.DataProvenanceToken.Trim().ToLowerInvariant()}': " +
                "figures derived from simulated or seeded data cannot enter promotion evidence.";
            await RecordPromotionAuditAsync(
                action: "PromotionBlocked",
                outcome: "Blocked",
                actor: request.ApprovedBy,
                runId: run.RunId,
                promotionId: null,
                message: provenanceReason,
                reason: "PromotionSimulatedProvenanceBlocked",
                scope: BuildPromotionAuditScope(run, targetRunType),
                metadata: BuildPromotionControlMetadata(
                    run,
                    targetRunType,
                    request.ManualOverrideId,
                    approvalChecklist,
                    evidenceReferences,
                    auditReference,
                    provenanceReason),
                ct).ConfigureAwait(false);

            return new PromotionDecisionResult(
                Success: false,
                PromotionId: null,
                NewRunId: null,
                Reason: provenanceReason);
        }

        var missingChecklistItems = PromotionApprovalChecklist.GetMissingRequiredItems(targetRunType, approvalChecklist);
        if (missingChecklistItems.Length > 0)
        {
            var reason = $"Promotion approval checklist is incomplete: {string.Join(", ", missingChecklistItems)}.";
            if (targetRunType == RunType.Live)
            {
                await RecordPromotionAuditAsync(
                    action: "PromotionBlocked",
                    outcome: "Blocked",
                    actor: request.ApprovedBy,
                    runId: run.RunId,
                    promotionId: null,
                    message: reason,
                    reason: "PromotionChecklistIncomplete",
                    scope: BuildPromotionAuditScope(run, targetRunType),
                    metadata: BuildPromotionControlMetadata(
                        run,
                        targetRunType,
                        request.ManualOverrideId,
                        approvalChecklist,
                        evidenceReferences,
                        auditReference,
                        reason),
                    ct).ConfigureAwait(false);
            }

            return new PromotionDecisionResult(
                Success: false,
                PromotionId: null,
                NewRunId: null,
                Reason: reason);
        }

        var missingEvidenceRequirements = GetMissingEvidenceRequirements(targetRunType, evidenceReferences);
        if (missingEvidenceRequirements.Length > 0)
        {
            var reason = $"{GetPromotionPath(targetRunType)} promotion evidence is incomplete: {string.Join(", ", missingEvidenceRequirements)}.";
            await RecordPromotionAuditAsync(
                action: "PromotionBlocked",
                outcome: "Blocked",
                actor: request.ApprovedBy,
                runId: run.RunId,
                promotionId: null,
                message: reason,
                reason: "PromotionEvidenceIncomplete",
                scope: BuildPromotionAuditScope(run, targetRunType),
                metadata: BuildPromotionControlMetadata(
                    run,
                    targetRunType,
                    request.ManualOverrideId,
                    approvalChecklist,
                    evidenceReferences,
                    auditReference,
                    reason),
                ct).ConfigureAwait(false);

            return new PromotionDecisionResult(
                Success: false,
                PromotionId: null,
                NewRunId: null,
                Reason: reason);
        }

        var invalidEvidenceReferences = GetInvalidEvidenceReferences(
                targetRunType,
                evidenceReferences,
                request.ManualOverrideId)
            .Concat(GetInvalidSourceRunEvidenceReferences(run, targetRunType, evidenceReferences))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (invalidEvidenceReferences.Length > 0)
        {
            var reason = $"{GetPromotionPath(targetRunType)} promotion evidence references are invalid: {string.Join(", ", invalidEvidenceReferences)}.";
            await RecordPromotionAuditAsync(
                action: "PromotionBlocked",
                outcome: "Blocked",
                actor: request.ApprovedBy,
                runId: run.RunId,
                promotionId: null,
                message: reason,
                reason: "PromotionEvidenceInvalid",
                scope: BuildPromotionAuditScope(run, targetRunType),
                metadata: BuildPromotionControlMetadata(
                    run,
                    targetRunType,
                    request.ManualOverrideId,
                    approvalChecklist,
                    evidenceReferences,
                    auditReference,
                    reason),
                ct).ConfigureAwait(false);

            return new PromotionDecisionResult(
                Success: false,
                PromotionId: null,
                NewRunId: null,
                Reason: reason);
        }

        var newRunId = Guid.NewGuid().ToString("N");

        if (targetRunType == RunType.Live && _operatorControls is not null)
        {
            var controlDecision = _operatorControls.EvaluateLivePromotion(run.RunId, run.StrategyId, request.ManualOverrideId);
            if (!controlDecision.IsAllowed)
            {
                await RecordPromotionAuditAsync(
                    action: "PromotionBlocked",
                    outcome: "Blocked",
                    actor: request.ApprovedBy,
                    runId: run.RunId,
                    promotionId: null,
                    message: controlDecision.RejectReason ?? "Promotion blocked by execution controls.",
                    reason: "ExecutionControlsBlocked",
                    scope: BuildPromotionAuditScope(run, targetRunType),
                    metadata: BuildPromotionControlMetadata(
                        run,
                        targetRunType,
                        request.ManualOverrideId,
                        approvalChecklist,
                        evidenceReferences,
                        auditReference,
                        controlDecision.RejectReason),
                    ct).ConfigureAwait(false);

                return new PromotionDecisionResult(
                    Success: false,
                    PromotionId: null,
                    NewRunId: null,
                    Reason: controlDecision.RejectReason ?? "Promotion blocked by execution controls.");
            }
        }

        var promotionRecord = _promoter.CreatePromotionRecord(
            run.Metrics,
            run.StrategyId,
            run.StrategyName,
            run.RunType,
            targetRunType,
            run.RunId,
            newRunId,
            PromotionDecisionKinds.Approved,
            approvedBy: request.ApprovedBy,
            approvalReason: request.ApprovalReason,
            reviewNotes: request.ReviewNotes,
            approvalChecklist: approvalChecklist,
            evidenceReferences: evidenceReferences,
            manualOverrideId: request.ManualOverrideId,
            auditReference: auditReference);
        if (!TryValidatePromotionRecord(promotionRecord, out var validationError))
        {
            return new PromotionDecisionResult(
                Success: false,
                PromotionId: null,
                NewRunId: null,
                Reason: validationError ?? "Promotion approval record is invalid.");
        }

        return await ReserveAndMaterializeApprovedDecisionAsync(run, promotionRecord, ct)
            .ConfigureAwait(false);
    }

    private async Task<PromotionDecisionResult> ReserveAndMaterializeApprovedDecisionAsync(
        StrategyRunEntry sourceRun,
        StrategyPromotionRecord candidate,
        CancellationToken ct)
    {
        PromotionDecisionReservation reservation;
        try
        {
            reservation = await _promotionRecordStore
                .ReserveFirstDecisionAsync(candidate, ct)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Promotion decision for source run {RunId} could not be durably reserved; no target run was created.",
                sourceRun.RunId);
            return new PromotionDecisionResult(
                Success: false,
                PromotionId: null,
                NewRunId: null,
                Reason: "Promotion decision could not be durably recorded; no target run was created or launched.");
        }

        await using (reservation)
        {
            var promotionRecord = reservation.Record;
            if (!string.Equals(
                    promotionRecord.Decision,
                    PromotionDecisionKinds.Approved,
                    StringComparison.OrdinalIgnoreCase))
            {
                return CreateApprovalRetryResult(promotionRecord);
            }

            if (string.IsNullOrWhiteSpace(promotionRecord.TargetRunId))
            {
                return CreateDurableApprovalCompletionFailure(
                    promotionRecord,
                    "The retained approval has no target run id; no runnable target was created.");
            }

            StrategyRunEntry? retainedTarget;
            try
            {
                retainedTarget = await _repository
                    .GetRunByIdAsync(promotionRecord.TargetRunId, CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Promotion {PromotionId} is durable, but target run {TargetRunId} could not be inspected.",
                    promotionRecord.PromotionId,
                    promotionRecord.TargetRunId);
                return CreateDurableApprovalCompletionFailure(
                    promotionRecord,
                    "The promotion decision is durable, but target-run state could not be inspected; no launch was attempted.");
            }

            if (retainedTarget is not null)
            {
                var retainedTargetError = GetRetainedTargetValidationError(
                    sourceRun,
                    promotionRecord,
                    retainedTarget);
                return retainedTargetError is null
                    ? CreateApprovalRetryResult(promotionRecord)
                    : CreateDurableApprovalCompletionFailure(promotionRecord, retainedTargetError);
            }

            // Once the decision is durable, complete its governance and target materialization
            // non-cancellably. Cancellation cannot be allowed to strand an approved decision after
            // commit while exposing an unaudited or request-owned target id.
            try
            {
                await EnsurePromotionApprovalAuditAsync(
                        sourceRun,
                        promotionRecord,
                        CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Promotion {PromotionId} is durable, but its approval audit could not be retained; target run {TargetRunId} was not created.",
                    promotionRecord.PromotionId,
                    promotionRecord.TargetRunId);
                return CreateDurableApprovalCompletionFailure(
                    promotionRecord,
                    "The promotion decision is durable, but approval audit retention failed; no target run was created or launched.");
            }

            var promotedRun = CreatePromotedRun(sourceRun, promotionRecord);
            try
            {
                await _repository
                    .RecordRunAsync(promotedRun, CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Promotion {PromotionId} is durable and audited, but target run {TargetRunId} could not be recorded.",
                    promotionRecord.PromotionId,
                    promotionRecord.TargetRunId);
                return CreateDurableApprovalCompletionFailure(
                    promotionRecord,
                    "The promotion decision is durable and audited, but its target run could not be recorded; no launch was attempted.");
            }

            _logger.LogInformation(
                "Promoted strategy {StrategyId} from {Source} to {Target}: promotionId={PromotionId}, newRunId={NewRunId}",
                sourceRun.StrategyId,
                sourceRun.RunType,
                promotionRecord.TargetRunType,
                promotionRecord.PromotionId,
                promotedRun.RunId);

            await ActivatePromotedRunAsync(promotedRun, CancellationToken.None).ConfigureAwait(false);

            return new PromotionDecisionResult(
                Success: true,
                PromotionId: promotionRecord.PromotionId,
                NewRunId: promotedRun.RunId,
                Reason: reservation.WasAppended
                    ? $"Strategy promoted from {sourceRun.RunType} to {promotionRecord.TargetRunType}."
                    : $"Durable promotion from {sourceRun.RunType} to {promotionRecord.TargetRunType} was repaired with its retained target run.",
                AuditReference: promotionRecord.AuditReference,
                ApprovedBy: promotionRecord.ApprovedBy);
        }
    }

    private async Task EnsurePromotionApprovalAuditAsync(
        StrategyRunEntry sourceRun,
        StrategyPromotionRecord promotionRecord,
        CancellationToken ct)
    {
        if (_auditTrail is null)
        {
            return;
        }

        var retainedAudit = await _auditTrail.GetAllAsync(ct).ConfigureAwait(false);
        if (retainedAudit.Any(entry =>
                string.Equals(entry.Category, "Promotion", StringComparison.Ordinal) &&
                string.Equals(entry.Action, "PromotionApproved", StringComparison.Ordinal) &&
                string.Equals(entry.CorrelationId, promotionRecord.PromotionId, StringComparison.Ordinal)))
        {
            return;
        }

        await RecordPromotionAuditAsync(
            action: "PromotionApproved",
            outcome: "Approved",
            actor: promotionRecord.ApprovedBy,
            runId: promotionRecord.SourceRunId,
            promotionId: promotionRecord.PromotionId,
            message: promotionRecord.ApprovalReason,
            reason: promotionRecord.TargetRunType == RunType.Live
                ? "HumanApprovedLivePromotion"
                : "HumanApprovedPromotion",
            scope: BuildPromotionAuditScope(sourceRun, promotionRecord.TargetRunType),
            metadata: BuildPromotionRecordMetadata(promotionRecord),
            ct).ConfigureAwait(false);
    }

    private static StrategyRunEntry CreatePromotedRun(
        StrategyRunEntry sourceRun,
        StrategyPromotionRecord promotionRecord) =>
        new(
            RunId: promotionRecord.TargetRunId!,
            StrategyId: sourceRun.StrategyId,
            StrategyName: sourceRun.StrategyName,
            RunType: promotionRecord.TargetRunType,
            StartedAt: promotionRecord.PromotedAt,
            EndedAt: null,
            Metrics: null,
            PortfolioId: $"{sourceRun.StrategyId}-{promotionRecord.TargetRunType.ToString().ToLowerInvariant()}-portfolio",
            LedgerReference: $"{sourceRun.StrategyId}-{promotionRecord.TargetRunType.ToString().ToLowerInvariant()}-ledger",
            AuditReference: promotionRecord.AuditReference,
            Engine: promotionRecord.TargetRunType == RunType.Paper ? "BrokerPaper" : "BrokerLive",
            ParameterSet: sourceRun.ParameterSet,
            ParentRunId: sourceRun.RunId,
            FundProfileId: sourceRun.FundProfileId,
            FundDisplayName: sourceRun.FundDisplayName);

    private static string? GetRetainedTargetValidationError(
        StrategyRunEntry sourceRun,
        StrategyPromotionRecord promotionRecord,
        StrategyRunEntry retainedTarget)
    {
        if (!string.Equals(retainedTarget.RunId, promotionRecord.TargetRunId, StringComparison.Ordinal) ||
            !string.Equals(retainedTarget.ParentRunId, sourceRun.RunId, StringComparison.Ordinal) ||
            !string.Equals(retainedTarget.StrategyId, sourceRun.StrategyId, StringComparison.Ordinal) ||
            retainedTarget.RunType != promotionRecord.TargetRunType ||
            !string.Equals(retainedTarget.AuditReference, promotionRecord.AuditReference, StringComparison.Ordinal))
        {
            return "The retained target run conflicts with the durable promotion decision; no launch was attempted.";
        }

        return null;
    }

    private static PromotionDecisionResult CreateDurableApprovalCompletionFailure(
        StrategyPromotionRecord promotionRecord,
        string reason) =>
        new(
            Success: false,
            PromotionId: promotionRecord.PromotionId,
            NewRunId: promotionRecord.TargetRunId,
            Reason: reason,
            AuditReference: promotionRecord.AuditReference,
            ApprovedBy: promotionRecord.ApprovedBy);

    /// <summary>
    /// Hands the newly recorded target run to the live trading engine. Activation failures are
    /// deliberately non-fatal: the promotion decision is already durable, the run entry stays
    /// retained, and the engine's startup resume sweep (or a manual restart) can activate it later.
    /// </summary>
    private async Task ActivatePromotedRunAsync(StrategyRunEntry newRun, CancellationToken ct)
    {
        if (_runLauncher is null)
        {
            _logger.LogWarning(
                "Promoted run {RunId} ({RunType}) was recorded but no run launcher is configured; the run will not execute until an engine activates it.",
                newRun.RunId, newRun.RunType);
            return;
        }

        try
        {
            var launch = await _runLauncher.TryLaunchAsync(newRun, ct).ConfigureAwait(false);
            if (!launch.Launched)
            {
                _logger.LogWarning(
                    "Promoted run {RunId} ({RunType}) was recorded but not activated: {Reason}",
                    newRun.RunId, newRun.RunType, launch.Reason);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex,
                "Promoted run {RunId} ({RunType}) was recorded but its activation failed.",
                newRun.RunId, newRun.RunType);
        }
    }

    private async Task RecordPromotionAuditAsync(
        string action,
        string outcome,
        string? actor,
        string runId,
        string? promotionId,
        string? message,
        string reason,
        string scope,
        IReadOnlyDictionary<string, string> metadata,
        CancellationToken ct)
    {
        if (_auditTrail is null)
        {
            return;
        }

        await _auditTrail.RecordAsync(new ExecutionAuditEntry(
            AuditId: Guid.NewGuid().ToString("N"),
            Category: "Promotion",
            Action: action,
            Outcome: outcome,
            OccurredAt: DateTimeOffset.UtcNow,
            Actor: actor,
            RunId: runId,
            CorrelationId: promotionId,
            Message: message,
            Reason: reason,
            Scope: scope,
            Metadata: metadata), ct).ConfigureAwait(false);
    }

    private static string BuildPromotionAuditScope(StrategyRunEntry run, RunType targetRunType)
        => $"source:{run.RunId}/strategy:{run.StrategyId}/target:{targetRunType}";

    private static Dictionary<string, string> BuildPromotionRecordMetadata(StrategyPromotionRecord promotionRecord)
    {
        var checklist = promotionRecord.ApprovalChecklist ?? [];
        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["decision"] = promotionRecord.Decision,
            ["sourceRunId"] = promotionRecord.SourceRunId,
            ["sourceRunType"] = promotionRecord.SourceRunType.ToString(),
            ["targetRunId"] = promotionRecord.TargetRunId ?? string.Empty,
            ["targetRunType"] = promotionRecord.TargetRunType.ToString(),
            ["manualOverrideId"] = promotionRecord.ManualOverrideId ?? string.Empty,
            ["requiredManualOverrideKind"] = promotionRecord.TargetRunType == RunType.Live
                ? ExecutionManualOverrideKinds.AllowLivePromotion
                : string.Empty,
            ["reviewNotes"] = promotionRecord.ReviewNotes ?? string.Empty,
            ["approvalChecklist"] = string.Join(",", checklist),
            ["approvalChecklistCount"] = checklist.Length.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["evidenceReferences"] = string.Join(",", promotionRecord.EvidenceReferences ?? []),
            ["evidenceReferenceCount"] = (promotionRecord.EvidenceReferences?.Length ?? 0).ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["auditReference"] = promotionRecord.AuditReference ?? string.Empty
        };
    }

    private static Dictionary<string, string> BuildPromotionControlMetadata(
        StrategyRunEntry run,
        RunType targetRunType,
        string? manualOverrideId,
        string[] approvalChecklist,
        string[] evidenceReferences,
        string auditReference,
        string? rejectReason)
        => new(StringComparer.OrdinalIgnoreCase)
        {
            ["decision"] = PromotionDecisionKinds.Rejected,
            ["sourceRunId"] = run.RunId,
            ["sourceRunType"] = run.RunType.ToString(),
            ["targetRunType"] = targetRunType.ToString(),
            ["manualOverrideId"] = manualOverrideId ?? string.Empty,
            ["requiredManualOverrideKind"] = targetRunType == RunType.Live
                ? ExecutionManualOverrideKinds.AllowLivePromotion
                : string.Empty,
            ["approvalChecklist"] = string.Join(",", approvalChecklist),
            ["approvalChecklistCount"] = approvalChecklist.Length.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["evidenceReferences"] = string.Join(",", evidenceReferences),
            ["evidenceReferenceCount"] = evidenceReferences.Length.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["auditReference"] = auditReference,
            ["controlRejectReason"] = rejectReason ?? string.Empty
        };

    private static string[] NormalizeEvidenceReferences(string[]? evidenceReferences)
        => evidenceReferences?
            .Select(static item => item.Trim())
            .Where(static item => item.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray() ?? [];

    private static string[] GetMissingEvidenceRequirements(
        RunType targetRunType,
        string[] evidenceReferences)
    {
        if (targetRunType is not (RunType.Paper or RunType.Live))
        {
            return [];
        }

        var evidenceSet = evidenceReferences
            .Select(GetEvidenceRequirementKey)
            .Where(static item => item.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return PromotionApprovalChecklist
            .CreateRequiredFor(targetRunType)
            .Where(item => !evidenceSet.Contains(item))
            .ToArray();
    }

    private static string[] GetInvalidEvidenceReferences(
        RunType targetRunType,
        string[] evidenceReferences,
        string? manualOverrideId)
    {
        if (targetRunType is not (RunType.Paper or RunType.Live))
        {
            return [];
        }

        var invalid = new List<string>();
        foreach (var requiredItem in PromotionApprovalChecklist.CreateRequiredFor(targetRunType))
        {
            var reference = evidenceReferences.FirstOrDefault(item =>
                string.Equals(GetEvidenceRequirementKey(item), requiredItem, StringComparison.OrdinalIgnoreCase));
            if (string.IsNullOrWhiteSpace(reference))
            {
                continue;
            }

            var value = GetEvidenceReferenceValue(reference);
            if (value.Length == 0)
            {
                invalid.Add($"{requiredItem} must include retained evidence after ':'");
                continue;
            }

            if (string.Equals(requiredItem, PromotionApprovalChecklist.LiveOverrideReviewed, StringComparison.OrdinalIgnoreCase) &&
                string.IsNullOrWhiteSpace(manualOverrideId))
            {
                invalid.Add($"{requiredItem} requires an active manual override id");
                continue;
            }

            if (string.Equals(requiredItem, PromotionApprovalChecklist.LiveOverrideReviewed, StringComparison.OrdinalIgnoreCase) &&
                !ContainsEvidenceReferenceToken(value, manualOverrideId!))
            {
                invalid.Add($"{requiredItem} must reference active manual override {manualOverrideId}");
            }

            // Paper-to-live promotions must record which paper matching and cost model
            // versions produced the cited paper session (e.g. paper-match/1+paper-cost/1),
            // so promotion evidence names the execution realism policy behind it.
            if (string.Equals(requiredItem, PromotionApprovalChecklist.PaperExecutionModelReviewed, StringComparison.OrdinalIgnoreCase) &&
                (!value.Contains("paper-match/", StringComparison.OrdinalIgnoreCase)
                    || !value.Contains("paper-cost/", StringComparison.OrdinalIgnoreCase)))
            {
                invalid.Add($"{requiredItem} must record the paper matching and cost model versions (paper-match/<n>+paper-cost/<n>)");
            }
        }

        return invalid.ToArray();
    }

    private static string GetPromotionPath(RunType targetRunType) =>
        targetRunType == RunType.Live ? "Paper -> Live" : "Backtest -> Paper";

    private static string[] GetInvalidSourceRunEvidenceReferences(
        StrategyRunEntry sourceRun,
        RunType targetRunType,
        string[] evidenceReferences)
    {
        if (targetRunType != RunType.Paper)
        {
            return [];
        }

        var retainedEvidence = sourceRun.RetainedEvidenceReferences
            .Select(static reference => reference.Trim())
            .Where(static reference => reference.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var requiresEvidenceVaultAuthority = IsCoveredCallStrategy(sourceRun.StrategyId) &&
            StrategyRunRepositoryVisibility.TryGetRetainedScope(sourceRun, out _);
        var invalid = new List<string>();

        foreach (var requiredItem in PromotionApprovalChecklist.CreateRequiredFor(targetRunType))
        {
            var reference = evidenceReferences.FirstOrDefault(item =>
                string.Equals(GetEvidenceRequirementKey(item), requiredItem, StringComparison.OrdinalIgnoreCase));
            if (string.IsNullOrWhiteSpace(reference))
            {
                continue;
            }

            var value = GetEvidenceReferenceValue(reference);
            if (value.Length == 0)
            {
                continue;
            }

            if (!retainedEvidence.Contains(value))
            {
                invalid.Add($"{requiredItem} must match evidence retained on source run {sourceRun.RunId}");
                continue;
            }

            if (requiresEvidenceVaultAuthority && !EvidenceVaultReference.TryParseCanonical(value, out _))
            {
                invalid.Add($"{requiredItem} must reference evidence://evidence-vault/{{vaultId}}");
            }
        }

        return invalid.ToArray();
    }

    private static bool IsCoveredCallStrategy(string strategyId) =>
        string.Equals(strategyId, "covered-call-overwrite", StringComparison.Ordinal) ||
        strategyId.StartsWith("covered-call-overwrite:", StringComparison.Ordinal);

    private static string GetEvidenceRequirementKey(string evidenceReference)
    {
        var separatorIndex = evidenceReference.IndexOf(':', StringComparison.Ordinal);
        var key = separatorIndex >= 0 ? evidenceReference[..separatorIndex] : evidenceReference;
        return key.Trim().Replace(' ', '_').Replace('-', '_').ToUpperInvariant();
    }

    private static string GetEvidenceReferenceValue(string evidenceReference)
    {
        var separatorIndex = evidenceReference.IndexOf(':', StringComparison.Ordinal);
        return separatorIndex < 0 || separatorIndex == evidenceReference.Length - 1
            ? string.Empty
            : evidenceReference[(separatorIndex + 1)..].Trim();
    }

    private static bool ContainsEvidenceReferenceToken(string evidenceReferenceValue, string token)
    {
        if (string.Equals(evidenceReferenceValue, token, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var segments = evidenceReferenceValue.Split(
            ['/', '\\', '#', '?', '&', '=', ',', ';', ' ', '\t', '\r', '\n'],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return segments.Any(segment => string.Equals(segment, token, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Rejects a promotion with a recorded reason.
    /// </summary>
    public Task<PromotionDecisionResult> RejectAsync(
        PromotionRejectionRequest request,
        CancellationToken ct = default) =>
        RejectCoreAsync(request, scope: null, ct);

    /// <summary>
    /// Rejects a promotion only when the source run exactly matches the trusted tenant and
    /// company scope.
    /// </summary>
    public Task<PromotionDecisionResult> RejectAsync(
        PromotionRejectionRequest request,
        StrategyRunReadScope scope,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        return RejectCoreAsync(request, scope, ct);
    }

    private async Task<PromotionDecisionResult> RejectCoreAsync(
        PromotionRejectionRequest request,
        StrategyRunReadScope? scope,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.RejectedBy))
        {
            return new PromotionDecisionResult(
                Success: false,
                PromotionId: null,
                NewRunId: null,
                Reason: "Promotion rejection requires an operator.");
        }

        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            return new PromotionDecisionResult(
                Success: false,
                PromotionId: null,
                NewRunId: null,
                Reason: "Promotion rejection requires a rationale.");
        }

        return await RejectSingleDecisionAsync(request, scope, ct).ConfigureAwait(false);
    }

    private async Task<PromotionDecisionResult> RejectSingleDecisionAsync(
        PromotionRejectionRequest request,
        StrategyRunReadScope? scope,
        CancellationToken ct)
    {

        var run = await FindRunAsync(request.RunId, scope, ct).ConfigureAwait(false);
        if (run?.Metrics is null)
        {
            return new PromotionDecisionResult(
                Success: false,
                PromotionId: null,
                NewRunId: null,
                Reason: "Run not found or has no metrics available for rejection trace.");
        }

        var targetRunType = run.RunType == RunType.Backtest ? RunType.Paper : RunType.Live;
        var existingDecision = await FindExistingDecisionAsync(run, targetRunType, ct).ConfigureAwait(false);
        if (existingDecision is not null)
        {
            return await ReserveAndCompleteRejectionAsync(run, existingDecision, ct)
                .ConfigureAwait(false);
        }

        var auditReference = Guid.NewGuid().ToString("N");
        var promotionRecord = _promoter.CreatePromotionRecord(
            run.Metrics,
            run.StrategyId,
            run.StrategyName,
            run.RunType,
            targetRunType,
            run.RunId,
            targetRunId: null,
            decision: PromotionDecisionKinds.Rejected,
            approvedBy: request.RejectedBy,
            approvalReason: request.Reason,
            reviewNotes: request.ReviewNotes,
            manualOverrideId: request.ManualOverrideId,
            auditReference: auditReference);
        if (!TryValidatePromotionRecord(promotionRecord, out var validationError))
        {
            return new PromotionDecisionResult(
                Success: false,
                PromotionId: null,
                NewRunId: null,
                Reason: validationError ?? "Promotion rejection record is invalid.");
        }

        return await ReserveAndCompleteRejectionAsync(run, promotionRecord, ct)
            .ConfigureAwait(false);
    }

    private async Task<PromotionDecisionResult> ReserveAndCompleteRejectionAsync(
        StrategyRunEntry sourceRun,
        StrategyPromotionRecord candidate,
        CancellationToken ct)
    {
        PromotionDecisionReservation reservation;
        try
        {
            reservation = await _promotionRecordStore
                .ReserveFirstDecisionAsync(candidate, ct)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Promotion rejection for source run {RunId} could not be durably reserved.",
                sourceRun.RunId);
            return new PromotionDecisionResult(
                Success: false,
                PromotionId: null,
                NewRunId: null,
                Reason: "Promotion rejection could not be durably recorded.");
        }

        await using (reservation)
        {
            var promotionRecord = reservation.Record;
            if (!string.Equals(
                    promotionRecord.Decision,
                    PromotionDecisionKinds.Rejected,
                    StringComparison.OrdinalIgnoreCase))
            {
                return CreateRejectionRetryResult(promotionRecord);
            }

            try
            {
                await EnsurePromotionRejectionAuditAsync(promotionRecord, CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Promotion rejection {PromotionId} is durable, but its audit could not be retained.",
                    promotionRecord.PromotionId);
                return new PromotionDecisionResult(
                    Success: false,
                    PromotionId: promotionRecord.PromotionId,
                    NewRunId: null,
                    Reason: "Promotion rejection is durable, but its audit could not be retained.",
                    AuditReference: promotionRecord.AuditReference,
                    ApprovedBy: promotionRecord.ApprovedBy);
            }

            _logger.LogInformation(
                "Promotion rejected for run {RunId}: {Reason}",
                promotionRecord.SourceRunId,
                promotionRecord.ApprovalReason);

            return reservation.WasAppended
                ? new PromotionDecisionResult(
                    Success: true,
                    PromotionId: promotionRecord.PromotionId,
                    NewRunId: null,
                    Reason: $"Promotion rejected: {promotionRecord.ApprovalReason}",
                    AuditReference: promotionRecord.AuditReference,
                    ApprovedBy: promotionRecord.ApprovedBy)
                : CreateRejectionRetryResult(promotionRecord);
        }
    }

    private async Task EnsurePromotionRejectionAuditAsync(
        StrategyPromotionRecord promotionRecord,
        CancellationToken ct)
    {
        if (_auditTrail is null)
        {
            return;
        }

        var retainedAudit = await _auditTrail.GetAllAsync(ct).ConfigureAwait(false);
        if (retainedAudit.Any(entry =>
                string.Equals(entry.Category, "Promotion", StringComparison.Ordinal) &&
                string.Equals(entry.Action, "PromotionRejected", StringComparison.Ordinal) &&
                string.Equals(entry.CorrelationId, promotionRecord.PromotionId, StringComparison.Ordinal)))
        {
            return;
        }

        await _auditTrail.RecordAsync(new ExecutionAuditEntry(
            AuditId: Guid.NewGuid().ToString("N"),
            Category: "Promotion",
            Action: "PromotionRejected",
            Outcome: "Rejected",
            OccurredAt: promotionRecord.PromotedAt,
            Actor: promotionRecord.ApprovedBy,
            RunId: promotionRecord.SourceRunId,
            CorrelationId: promotionRecord.PromotionId,
            Message: promotionRecord.ApprovalReason,
            Metadata: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["decision"] = promotionRecord.Decision,
                ["sourceRunId"] = promotionRecord.SourceRunId,
                ["targetRunType"] = promotionRecord.TargetRunType.ToString(),
                ["manualOverrideId"] = promotionRecord.ManualOverrideId ?? string.Empty,
                ["reviewNotes"] = promotionRecord.ReviewNotes ?? string.Empty,
                ["auditReference"] = promotionRecord.AuditReference ?? string.Empty
            }), ct).ConfigureAwait(false);
    }

    private async Task<StrategyPromotionRecord?> FindExistingDecisionAsync(
        StrategyRunEntry sourceRun,
        RunType targetRunType,
        CancellationToken ct)
    {
        var records = await _promotionRecordStore.LoadAllAsync(ct).ConfigureAwait(false);
        return records.FirstOrDefault(record =>
            string.Equals(record.SourceRunId, sourceRun.RunId, StringComparison.Ordinal) &&
            record.SourceRunType == sourceRun.RunType &&
            record.TargetRunType == targetRunType);
    }

    private static PromotionDecisionResult CreateApprovalRetryResult(StrategyPromotionRecord existingDecision)
    {
        if (string.Equals(
                existingDecision.Decision,
                PromotionDecisionKinds.Approved,
                StringComparison.OrdinalIgnoreCase))
        {
            return new PromotionDecisionResult(
                Success: true,
                PromotionId: existingDecision.PromotionId,
                NewRunId: existingDecision.TargetRunId,
                Reason: $"Promotion from {existingDecision.SourceRunType} to {existingDecision.TargetRunType} was already approved.",
                AuditReference: existingDecision.AuditReference,
                ApprovedBy: existingDecision.ApprovedBy);
        }

        return new PromotionDecisionResult(
            Success: false,
            PromotionId: existingDecision.PromotionId,
            NewRunId: null,
            Reason: $"Promotion was already rejected: {existingDecision.ApprovalReason}",
            AuditReference: existingDecision.AuditReference,
            ApprovedBy: existingDecision.ApprovedBy);
    }

    private static PromotionDecisionResult CreateRejectionRetryResult(StrategyPromotionRecord existingDecision)
    {
        if (string.Equals(
                existingDecision.Decision,
                PromotionDecisionKinds.Rejected,
                StringComparison.OrdinalIgnoreCase))
        {
            return new PromotionDecisionResult(
                Success: true,
                PromotionId: existingDecision.PromotionId,
                NewRunId: null,
                Reason: $"Promotion was already rejected: {existingDecision.ApprovalReason}",
                AuditReference: existingDecision.AuditReference,
                ApprovedBy: existingDecision.ApprovedBy);
        }

        return new PromotionDecisionResult(
            Success: false,
            PromotionId: existingDecision.PromotionId,
            NewRunId: existingDecision.TargetRunId,
            Reason: $"Promotion from {existingDecision.SourceRunType} to {existingDecision.TargetRunType} was already approved and cannot be rejected.",
            AuditReference: existingDecision.AuditReference,
            ApprovedBy: existingDecision.ApprovedBy);
    }

    /// <summary>Returns the full promotion audit trail.</summary>
    public Task<IReadOnlyList<StrategyPromotionRecord>> GetPromotionHistoryAsync(CancellationToken ct = default) =>
        GetPromotionHistoryCoreAsync(scope: null, ct);

    /// <summary>
    /// Returns promotion records whose source runs exactly match the trusted tenant and company
    /// scope.
    /// </summary>
    public Task<IReadOnlyList<StrategyPromotionRecord>> GetPromotionHistoryAsync(
        StrategyRunReadScope scope,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        return GetPromotionHistoryCoreAsync(scope, ct);
    }

    private async Task<IReadOnlyList<StrategyPromotionRecord>> GetPromotionHistoryCoreAsync(
        StrategyRunReadScope? scope,
        CancellationToken ct)
    {
        var records = await _promotionRecordStore.LoadAllAsync(ct).ConfigureAwait(false);
        if (records.Count == 0)
        {
            return [];
        }

        var sourceRunIds = records
            .Select(static record => record.SourceRunId)
            .Where(static runId => !string.IsNullOrWhiteSpace(runId))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var sourceRuns = await _repository.GetRunsByIdsAsync(sourceRunIds, ct).ConfigureAwait(false);
        var sourceRunsById = sourceRuns.ToDictionary(static run => run.RunId, StringComparer.Ordinal);

        return records
            .Where(record => IsPromotionRecordVisible(
                record,
                scope,
                sourceRunsById.GetValueOrDefault(record.SourceRunId)))
            .OrderByDescending(static record => record.PromotedAt)
            .ToArray();
    }

    private static bool IsPromotionRecordVisible(
        StrategyPromotionRecord record,
        StrategyRunReadScope? scope,
        StrategyRunEntry? sourceRun)
    {
        if (sourceRun is not null)
        {
            return IsVisibleToPromotionScope(sourceRun, scope);
        }

        // Durable legacy promotion history outlives an in-memory compatibility run store. Keep
        // that unscoped audit trail readable after restart, while scoped reads continue to fail
        // closed without the exact retained source-run scope. Scoped Covered Call identities are
        // never admitted through this compatibility fallback.
        return scope is null && !IsCoveredCallStrategy(record.StrategyId);
    }

    /// <summary>
    /// Records walk-forward/out-of-sample robustness evidence on a completed run so the
    /// promotion policy can gate eligibility on it. Returns the updated run, or <c>null</c>
    /// when the run does not exist.
    /// </summary>
    public Task<StrategyRunEntry?> RecordWalkForwardEvidenceAsync(
        string runId,
        StrategyRunWalkForwardEvidence evidence,
        CancellationToken ct = default) =>
        RecordWalkForwardEvidenceCoreAsync(runId, evidence, scope: null, ct);

    /// <summary>
    /// Records walk-forward evidence only when the source run exactly matches the trusted tenant
    /// and company scope.
    /// </summary>
    public Task<StrategyRunEntry?> RecordWalkForwardEvidenceAsync(
        string runId,
        StrategyRunWalkForwardEvidence evidence,
        StrategyRunReadScope scope,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        return RecordWalkForwardEvidenceCoreAsync(runId, evidence, scope, ct);
    }

    private async Task<StrategyRunEntry?> RecordWalkForwardEvidenceCoreAsync(
        string runId,
        StrategyRunWalkForwardEvidence evidence,
        StrategyRunReadScope? scope,
        CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        ArgumentNullException.ThrowIfNull(evidence);
        if (evidence.Validate() is { } validationError)
            throw new ArgumentException(validationError, nameof(evidence));

        var run = await FindRunAsync(runId, scope, ct).ConfigureAwait(false);
        if (run is null)
        {
            return null;
        }

        var updated = run with { WalkForwardEvidence = evidence };
        await _repository.RecordRunAsync(updated, ct).ConfigureAwait(false);

        // The run id arrives from the API route; strip line endings so a crafted value
        // cannot forge additional log entries.
        _logger.LogInformation(
            "Recorded walk-forward evidence for run {RunId}: oosSharpe={OosSharpe:F3}, degradation={Degradation:F3}, windows={Windows}",
            runId.ReplaceLineEndings(string.Empty),
            evidence.OutOfSampleSharpeRatio,
            evidence.DegradationRatio,
            evidence.WindowCount);

        return updated;
    }

    private async Task<StrategyRunEntry?> FindRunAsync(
        string runId,
        StrategyRunReadScope? scope,
        CancellationToken ct)
    {
        var run = await _repository.GetRunByIdAsync(runId, ct).ConfigureAwait(false);
        return run is not null && IsVisibleToPromotionScope(run, scope) ? run : null;
    }

    private static bool IsVisibleToPromotionScope(StrategyRunEntry run, StrategyRunReadScope? scope)
    {
        if (scope is null)
        {
            return StrategyRunRepositoryVisibility.IsVisible(run, scope: null);
        }

        var repositoryScope = new StrategyRunRepositoryScope(scope.TenantId, scope.CompanyId);
        return StrategyRunRepositoryVisibility.TryGetRetainedScope(run, out var retainedScope) &&
            StrategyRunRepositoryVisibility.TryCreateScopeKey(repositoryScope, out var requestedScope) &&
            retainedScope == requestedScope;
    }

    internal static bool TryValidatePromotionRecord(StrategyPromotionRecord record, out string? validationError)
    {
        ArgumentNullException.ThrowIfNull(record);

        if (string.IsNullOrWhiteSpace(record.ApprovedBy))
        {
            validationError = "Promotion decision record requires operator identity.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(record.Decision))
        {
            validationError = "Promotion decision record requires a decision.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(record.ApprovalReason))
        {
            validationError = "Promotion decision record requires rationale.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(record.AuditReference))
        {
            validationError = "Promotion decision record requires durable audit reference.";
            return false;
        }

        var isApproved = string.Equals(record.Decision, PromotionDecisionKinds.Approved, StringComparison.OrdinalIgnoreCase);
        var isRejected = string.Equals(record.Decision, PromotionDecisionKinds.Rejected, StringComparison.OrdinalIgnoreCase);
        if (!isApproved && !isRejected)
        {
            validationError = $"Promotion decision '{record.Decision}' is unsupported.";
            return false;
        }

        if (isApproved && string.IsNullOrWhiteSpace(record.TargetRunId))
        {
            validationError = "Approved promotion records must include target run lineage.";
            return false;
        }

        if (isApproved && record.TargetRunType is RunType.Paper or RunType.Live)
        {
            var missingEvidence = GetMissingEvidenceRequirements(
                record.TargetRunType,
                NormalizeEvidenceReferences(record.EvidenceReferences));
            if (missingEvidence.Length > 0)
            {
                validationError = $"Approved live promotion records must include evidence references for: {string.Join(", ", missingEvidence)}.";
                return false;
            }

            var invalidEvidence = GetInvalidEvidenceReferences(
                record.TargetRunType,
                NormalizeEvidenceReferences(record.EvidenceReferences),
                record.ManualOverrideId);
            if (invalidEvidence.Length > 0)
            {
                validationError = $"Approved live promotion records must include valid retained evidence references: {string.Join(", ", invalidEvidence)}.";
                return false;
            }
        }

        if (isRejected && !string.IsNullOrWhiteSpace(record.TargetRunId))
        {
            validationError = "Rejected promotion records must not include target run lineage.";
            return false;
        }

        validationError = null;
        return true;
    }
}

// --- Request/response DTOs ---

/// <summary>Result of evaluating a run for promotion eligibility.</summary>
public sealed record PromotionEvaluationResult(
    string RunId,
    string? StrategyId,
    string? StrategyName,
    RunType? SourceMode,
    RunType? TargetMode,
    bool IsEligible,
    double SharpeRatio,
    decimal MaxDrawdownPercent,
    decimal TotalReturn,
    string Reason,
    bool Found = true,
    bool Ready = true,
    bool RequiresHumanApproval = false,
    bool RequiresManualOverride = false,
    string? RequiredManualOverrideKind = null,
    IReadOnlyList<string>? BlockingReasons = null)
{
    public static PromotionEvaluationResult NotFound(string runId) => new(
        RunId: runId, StrategyId: null, StrategyName: null,
        SourceMode: null, TargetMode: null, IsEligible: false,
        SharpeRatio: 0, MaxDrawdownPercent: 0, TotalReturn: 0,
        Reason: "Run not found.", Found: false, Ready: false);

    public static PromotionEvaluationResult NotReady(string runId, string reason) => new(
        RunId: runId, StrategyId: null, StrategyName: null,
        SourceMode: null, TargetMode: null, IsEligible: false,
        SharpeRatio: 0, MaxDrawdownPercent: 0, TotalReturn: 0,
        Reason: reason, Found: true, Ready: false);
}

/// <summary>Request to approve a strategy promotion.</summary>
public sealed record PromotionApprovalRequest(
    string RunId,
    string? ReviewNotes = null,
    string? ApprovedBy = null,
    string? ApprovalReason = null,
    string[]? ApprovalChecklist = null,
    string[]? EvidenceReferences = null,
    string? ManualOverrideId = null);

/// <summary>Request to reject a strategy promotion.</summary>
public sealed record PromotionRejectionRequest(
    string RunId,
    string Reason,
    string? ReviewNotes = null,
    string? RejectedBy = null,
    string? ManualOverrideId = null);

/// <summary>Request to record walk-forward/out-of-sample evidence on a run.</summary>
public sealed record RecordWalkForwardEvidenceRequest(
    double OutOfSampleSharpeRatio,
    decimal OutOfSampleMaxDrawdownPercent,
    double DegradationRatio,
    int WindowCount,
    string? SourceReference = null);

/// <summary>Result of a promotion approval or rejection.</summary>
public sealed record PromotionDecisionResult(
    bool Success,
    string? PromotionId,
    string? NewRunId,
    string Reason,
    string? AuditReference = null,
    string? ApprovedBy = null);
