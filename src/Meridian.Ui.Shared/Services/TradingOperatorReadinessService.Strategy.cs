using Meridian.Contracts.Workstation;
using Meridian.Strategies.Models;
using Meridian.Strategies.Promotions;
using Meridian.Ui.Shared.Services.CoveredCall;

namespace Meridian.Ui.Shared.Services;

public sealed partial class TradingOperatorReadinessService
{
    private const string ScopedCoveredCallStrategyIdPrefix = CoveredCallBacktestService.StrategyId + ":";

    internal static StrategyRunSummary? FindScopedCoveredCallPaperTarget(
        IEnumerable<StrategyRunSummary> runs)
    {
        ArgumentNullException.ThrowIfNull(runs);
        return runs.FirstOrDefault(static candidate =>
            candidate.Mode == StrategyRunMode.Paper
            && candidate.StrategyId.StartsWith(
                ScopedCoveredCallStrategyIdPrefix,
                StringComparison.Ordinal));
    }

    private static TradingPromotionReadinessDto? BuildPromotion(
        StrategyRunDetail? latestRun,
        IReadOnlyList<StrategyPromotionRecord> promotionRecords)
    {
        var record = latestRun is null
            ? promotionRecords.FirstOrDefault()
            : promotionRecords.FirstOrDefault(candidate =>
                IsPromotionRecordLinkedToRun(candidate, latestRun.Summary.RunId));

        if (record is not null)
        {
            return new TradingPromotionReadinessDto(
                State: record.Decision,
                Reason: record.ApprovalReason ?? record.ReviewNotes ?? "Promotion decision recorded.",
                RequiresReview: !IsPromotionRecordTraceComplete(record),
                SourceRunId: record.SourceRunId,
                TargetRunId: record.TargetRunId,
                SuggestedNextMode: record.TargetRunType.ToString(),
                AuditReference: record.AuditReference,
                ApprovalStatus: record.Decision,
                ManualOverrideId: record.ManualOverrideId,
                ApprovedBy: record.ApprovedBy,
                ApprovalChecklist: record.ApprovalChecklist ?? [],
                EvidenceReferences: record.EvidenceReferences ?? []);
        }

        var promotion = latestRun?.Promotion ?? latestRun?.Summary.Promotion;
        return promotion is null
            ? null
            : new TradingPromotionReadinessDto(
                State: promotion.State.ToString(),
                Reason: promotion.Reason,
                RequiresReview: promotion.RequiresReview,
                SourceRunId: promotion.SourceRunId ?? latestRun?.Summary.RunId,
                TargetRunId: promotion.TargetRunId,
                SuggestedNextMode: promotion.SuggestedNextMode?.ToString(),
                AuditReference: promotion.AuditReference,
                ApprovalStatus: promotion.ApprovalStatus,
                ManualOverrideId: promotion.ManualOverrideId,
                ApprovedBy: promotion.ApprovedBy,
                ApprovalChecklist: promotion.ApprovalChecklist ?? [],
                EvidenceReferences: promotion.EvidenceReferences ?? []);
    }

    private static bool IsPromotionRecordLinkedToRun(StrategyPromotionRecord record, string runId) =>
        string.Equals(record.SourceRunId, runId, StringComparison.Ordinal) ||
        string.Equals(record.TargetRunId, runId, StringComparison.Ordinal);

    private static bool IsPromotionRecordTraceComplete(StrategyPromotionRecord record) =>
        !string.IsNullOrWhiteSpace(record.Decision) &&
        !string.IsNullOrWhiteSpace(record.ApprovedBy) &&
        !string.IsNullOrWhiteSpace(record.ApprovalReason) &&
        HasApprovalChecklist(record.ApprovalChecklist) &&
        (record.TargetRunType != RunType.Live ||
         (GetMissingLivePromotionChecklistItems(record.ApprovalChecklist).Count == 0 &&
          GetMissingLivePromotionEvidenceReferences(record.EvidenceReferences).Count == 0 &&
          GetInvalidLivePromotionEvidenceReferenceFields(record.EvidenceReferences, record.ManualOverrideId).Count == 0)) &&
        !string.IsNullOrWhiteSpace(record.SourceRunId) &&
        !string.IsNullOrWhiteSpace(record.AuditReference);

    private static bool IsPromotionTraceComplete(TradingPromotionReadinessDto? promotion) =>
        GetMissingPromotionTraceFields(promotion).Count == 0;

    private static IReadOnlyList<string> GetMissingPromotionTraceFields(TradingPromotionReadinessDto? promotion)
    {
        if (promotion is null)
        {
            return ["promotion"];
        }

        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(promotion.ApprovalStatus))
        {
            missing.Add("decision");
        }

        if (string.IsNullOrWhiteSpace(promotion.ApprovedBy))
        {
            missing.Add("operator");
        }

        if (string.IsNullOrWhiteSpace(promotion.Reason))
        {
            missing.Add("rationale");
        }

        if (!HasApprovalChecklist(promotion.ApprovalChecklist))
        {
            missing.Add("checklist");
        }
        else if (IsLivePromotionTrace(promotion))
        {
            missing.AddRange(GetMissingLivePromotionChecklistItems(promotion.ApprovalChecklist)
                .Select(static item => $"approvalChecklist:{item}"));
        }

        if (IsLivePromotionTrace(promotion) &&
            !HasEvidenceReferences(promotion.EvidenceReferences))
        {
            missing.Add("evidenceReferences");
        }
        else if (IsLivePromotionTrace(promotion))
        {
            missing.AddRange(GetMissingLivePromotionEvidenceReferences(promotion.EvidenceReferences)
                .Select(static item => $"evidenceReferences:{item}"));
            missing.AddRange(GetInvalidLivePromotionEvidenceReferenceFields(
                promotion.EvidenceReferences,
                promotion.ManualOverrideId));
        }

        if (string.IsNullOrWhiteSpace(promotion.SourceRunId))
        {
            missing.Add("sourceRunId");
        }

        if (string.Equals(promotion.ApprovalStatus, PromotionDecisionKinds.Approved, StringComparison.OrdinalIgnoreCase) &&
            string.IsNullOrWhiteSpace(promotion.TargetRunId))
        {
            missing.Add("targetRunId");
        }

        if (string.Equals(promotion.ApprovalStatus, PromotionDecisionKinds.Rejected, StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(promotion.TargetRunId))
        {
            missing.Add("targetRunId must be empty for rejected decisions");
        }

        if (string.IsNullOrWhiteSpace(promotion.AuditReference))
        {
            missing.Add("auditReference");
        }

        return missing;
    }

    private static bool HasApprovalChecklist(IReadOnlyList<string>? approvalChecklist)
        => approvalChecklist is { Count: > 0 } &&
           approvalChecklist.All(static item => !string.IsNullOrWhiteSpace(item));

    private static bool HasEvidenceReferences(IReadOnlyList<string>? evidenceReferences)
        => evidenceReferences is { Count: > 0 } &&
           evidenceReferences.All(static item => !string.IsNullOrWhiteSpace(item));

    private static bool IsLivePromotionTrace(TradingPromotionReadinessDto promotion)
        => string.Equals(promotion.SuggestedNextMode, RunType.Live.ToString(), StringComparison.OrdinalIgnoreCase);

    private static IReadOnlyList<string> GetMissingLivePromotionChecklistItems(IReadOnlyList<string>? approvalChecklist)
    {
        var provided = PromotionApprovalChecklist.Normalize(approvalChecklist).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return RequiredLivePromotionEvidenceTokens
            .Where(required => !provided.Contains(required))
            .ToArray();
    }

    private static IReadOnlyList<string> GetMissingLivePromotionEvidenceReferences(IReadOnlyList<string>? evidenceReferences)
    {
        var provided = evidenceReferences?
            .Select(GetPromotionEvidenceReferenceKey)
            .Where(static item => item.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase)
            ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        return RequiredLivePromotionEvidenceTokens
            .Where(required => !provided.Contains(required))
            .ToArray();
    }

    private static IReadOnlyList<string> GetInvalidLivePromotionEvidenceReferenceFields(
        IReadOnlyList<string>? evidenceReferences,
        string? manualOverrideId)
        => RequiredLivePromotionEvidenceTokens
            .Select(token => EvaluatePromotionEvidenceReference(evidenceReferences, token, manualOverrideId))
            .Where(static status => status.HasReference && !status.IsSatisfied)
            .Select(static status => status.MissingOrInvalidField!)
            .ToArray();

    private static string GetPromotionEvidenceReferenceKey(string? evidenceReference)
    {
        if (string.IsNullOrWhiteSpace(evidenceReference))
        {
            return string.Empty;
        }

        var separatorIndex = evidenceReference.IndexOf(':', StringComparison.Ordinal);
        var key = separatorIndex >= 0 ? evidenceReference[..separatorIndex] : evidenceReference;
        return key.Trim().Replace(' ', '_').Replace('-', '_').ToUpperInvariant();
    }

    private static string GetPromotionEvidenceReferenceValue(string? evidenceReference)
    {
        if (string.IsNullOrWhiteSpace(evidenceReference))
        {
            return string.Empty;
        }

        var separatorIndex = evidenceReference.IndexOf(':', StringComparison.Ordinal);
        return separatorIndex < 0 || separatorIndex == evidenceReference.Length - 1
            ? string.Empty
            : evidenceReference[(separatorIndex + 1)..].Trim();
    }

    private static bool ContainsPromotionEvidenceReferenceToken(string referenceValue, string token)
    {
        if (string.Equals(referenceValue, token, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var segments = referenceValue.Split(
            ['/', '\\', '#', '?', '&', '=', ',', ';', ' ', '\t', '\r', '\n'],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return segments.Any(segment => string.Equals(segment, token, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsLiveOverrideChecklistItem(string checklistItem) =>
        string.Equals(checklistItem, PromotionApprovalChecklist.LiveOverrideReviewed, StringComparison.OrdinalIgnoreCase);
}
