using Meridian.Backtesting.Sdk;
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
    public async Task<PromotionEvaluationResult> EvaluateAsync(
        string runId,
        PromotionCriteria? criteria = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);

        var run = await FindRunAsync(runId, ct).ConfigureAwait(false);
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
            ExecutionManualOverrideKinds.AllowLivePromotion);
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
    public async Task<PromotionDecisionResult> ApproveAsync(
        PromotionApprovalRequest request,
        CancellationToken ct = default)
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

        var run = await FindRunAsync(request.RunId, ct).ConfigureAwait(false);
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
        var approvalChecklist = PromotionApprovalChecklist.Normalize(request.ApprovalChecklist);
        var evidenceReferences = NormalizeEvidenceReferences(request.EvidenceReferences);
        var auditReference = Guid.NewGuid().ToString("N");

        var evaluation = await EvaluateAsync(run.RunId, ct: ct).ConfigureAwait(false);
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

        var missingEvidenceRequirements = GetMissingLiveEvidenceRequirements(targetRunType, evidenceReferences);
        if (missingEvidenceRequirements.Length > 0)
        {
            var reason = $"Paper -> Live promotion evidence is incomplete: {string.Join(", ", missingEvidenceRequirements)}.";
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

        var invalidEvidenceReferences = GetInvalidLiveEvidenceReferences(targetRunType, evidenceReferences, request.ManualOverrideId);
        if (invalidEvidenceReferences.Length > 0)
        {
            var reason = $"Paper -> Live promotion evidence references are invalid: {string.Join(", ", invalidEvidenceReferences)}.";
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

        var newRun = new StrategyRunEntry(
            RunId: newRunId,
            StrategyId: run.StrategyId,
            StrategyName: run.StrategyName,
            RunType: targetRunType,
            StartedAt: DateTimeOffset.UtcNow,
            EndedAt: null,
            Metrics: null,
            PortfolioId: $"{run.StrategyId}-{targetRunType.ToString().ToLowerInvariant()}-portfolio",
            LedgerReference: $"{run.StrategyId}-{targetRunType.ToString().ToLowerInvariant()}-ledger",
            AuditReference: auditReference,
            Engine: targetRunType == RunType.Paper ? "BrokerPaper" : "BrokerLive",
            ParameterSet: run.ParameterSet,
            ParentRunId: run.RunId,
            FundProfileId: run.FundProfileId,
            FundDisplayName: run.FundDisplayName);

        await _repository.RecordRunAsync(newRun, ct).ConfigureAwait(false);
        await _promotionRecordStore.AppendAsync(promotionRecord, ct).ConfigureAwait(false);

        _logger.LogInformation(
            "Promoted strategy {StrategyId} from {Source} to {Target}: promotionId={PromotionId}, newRunId={NewRunId}",
            run.StrategyId, run.RunType, targetRunType, promotionRecord.PromotionId, newRun.RunId);

        if (_auditTrail is not null)
        {
            await RecordPromotionAuditAsync(
                action: "PromotionApproved",
                outcome: "Approved",
                actor: request.ApprovedBy,
                runId: request.RunId,
                promotionId: promotionRecord.PromotionId,
                message: request.ApprovalReason,
                reason: targetRunType == RunType.Live ? "HumanApprovedLivePromotion" : "HumanApprovedPromotion",
                scope: BuildPromotionAuditScope(run, targetRunType),
                metadata: BuildPromotionRecordMetadata(promotionRecord),
                ct).ConfigureAwait(false);
        }

        await ActivatePromotedRunAsync(newRun, ct).ConfigureAwait(false);

        return new PromotionDecisionResult(
            Success: true,
            PromotionId: promotionRecord.PromotionId,
            NewRunId: newRun.RunId,
            Reason: $"Strategy promoted from {run.RunType} to {targetRunType}.",
            AuditReference: auditReference,
            ApprovedBy: request.ApprovedBy);
    }

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

    private static string[] GetMissingLiveEvidenceRequirements(
        RunType targetRunType,
        string[] evidenceReferences)
    {
        if (targetRunType != RunType.Live)
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

    private static string[] GetInvalidLiveEvidenceReferences(
        RunType targetRunType,
        string[] evidenceReferences,
        string? manualOverrideId)
    {
        if (targetRunType != RunType.Live)
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
        }

        return invalid.ToArray();
    }

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
    public async Task<PromotionDecisionResult> RejectAsync(
        PromotionRejectionRequest request,
        CancellationToken ct = default)
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

        var run = await FindRunAsync(request.RunId, ct).ConfigureAwait(false);
        if (run?.Metrics is null)
        {
            return new PromotionDecisionResult(
                Success: false,
                PromotionId: null,
                NewRunId: null,
                Reason: "Run not found or has no metrics available for rejection trace.");
        }

        var targetRunType = run.RunType == RunType.Backtest ? RunType.Paper : RunType.Live;
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

        await _promotionRecordStore.AppendAsync(promotionRecord, ct).ConfigureAwait(false);

        _logger.LogInformation(
            "Promotion rejected for run {RunId}: {Reason}",
            request.RunId, request.Reason);

        if (_auditTrail is not null)
        {
            await _auditTrail.RecordAsync(new ExecutionAuditEntry(
                AuditId: Guid.NewGuid().ToString("N"),
                Category: "Promotion",
                Action: "PromotionRejected",
                Outcome: "Rejected",
                OccurredAt: DateTimeOffset.UtcNow,
                Actor: request.RejectedBy,
                RunId: request.RunId,
                CorrelationId: promotionRecord.PromotionId,
                Message: request.Reason,
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

        return new PromotionDecisionResult(
            Success: true,
            PromotionId: promotionRecord.PromotionId,
            NewRunId: null,
            Reason: $"Promotion rejected: {request.Reason}",
            AuditReference: promotionRecord.AuditReference,
            ApprovedBy: request.RejectedBy);
    }

    /// <summary>Returns the full promotion audit trail.</summary>
    public async Task<IReadOnlyList<StrategyPromotionRecord>> GetPromotionHistoryAsync(CancellationToken ct = default)
    {
        var records = await _promotionRecordStore.LoadAllAsync(ct).ConfigureAwait(false);
        return records
            .OrderByDescending(static record => record.PromotedAt)
            .ToArray();
    }

    private async Task<StrategyRunEntry?> FindRunAsync(string runId, CancellationToken ct)
    {
        await foreach (var run in _repository.GetAllRunsAsync(ct).WithCancellation(ct).ConfigureAwait(false))
        {
            if (string.Equals(run.RunId, runId, StringComparison.Ordinal))
            {
                return run;
            }
        }

        return null;
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

        if (isApproved && record.TargetRunType == RunType.Live)
        {
            var missingEvidence = GetMissingLiveEvidenceRequirements(
                record.TargetRunType,
                NormalizeEvidenceReferences(record.EvidenceReferences));
            if (missingEvidence.Length > 0)
            {
                validationError = $"Approved live promotion records must include evidence references for: {string.Join(", ", missingEvidence)}.";
                return false;
            }

            var invalidEvidence = GetInvalidLiveEvidenceReferences(
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

/// <summary>Result of a promotion approval or rejection.</summary>
public sealed record PromotionDecisionResult(
    bool Success,
    string? PromotionId,
    string? NewRunId,
    string Reason,
    string? AuditReference = null,
    string? ApprovedBy = null);
