using System.Globalization;
using System.Text.Json;
using Meridian.Contracts.AssetOperations;
using Meridian.Storage.AssetOperations;

namespace Meridian.Ui.Shared.Services;

public sealed partial class BookReceiptSource
{
    /// <summary>Stable schedule identity independent of expectation revision and amended payment date.</summary>
    public static string ObligationKey(Guid positionId, string flowType, DateOnly dueDate,
        DateOnly? accrualStartDate = null, DateOnly? accrualEndDate = null,
        string? sourceObligationId = null, bool isFinalPrincipal = false)
    {
        var prefix = $"receipt:{positionId:D}:{flowType}";
        if (!string.IsNullOrWhiteSpace(sourceObligationId))
            return $"{prefix}:source:{Uri.EscapeDataString(sourceObligationId)}";
        if (flowType is "Coupon" or "Interest" && accrualStartDate.HasValue && accrualEndDate.HasValue)
            return $"{prefix}:period:{DateKey(accrualStartDate)}:{DateKey(accrualEndDate)}";
        if (flowType == "Principal" && isFinalPrincipal)
            return $"{prefix}:maturity";
        return $"{prefix}:due:{DateKey(dueDate)}";
    }

    private static string DateKey(DateOnly? date) => date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "-";

    private static bool ReceivesCash(InstrumentRoleDto role, BookPositionDto position)
        => role.SecurityId == position.SecurityId && role.OwnerScopeId == position.BookContext.FundProfileId
            && role.OwnerScopeKind == position.BookContext.FundStructureNodeKind.ToString()
            && role.EconomicSide is InstrumentEconomicSides.Asset or InstrumentEconomicSides.Inflow
            && role.RoleKind is InstrumentRoleKinds.Holder or InstrumentRoleKinds.Lender or InstrumentRoleKinds.Receiver
            && position.PositionSide is BookPositionSides.Long or BookPositionSides.Asset
            && position.Status is "Active" or "Open" or "Closed" or "Matured" or "Terminated" or "Inactive";

    private static bool HasHoldingProvenance(BookPositionDto position, PositionEconomicStateDto state)
        => (position.OriginEvent is not null || position.ProjectionLineage is not null)
            && (state.SourceEvent is not null || state.ProjectionLineage is not null)
            && position.EvidenceLinks.Count > 0 && state.EvidenceLinks.Count > 0;

    private static bool AcceptedProjectionEvidence(RetainedEvidenceIdentityDto evidence,
        AssetCashFlowProjectionRunDto run, AssetProjectedCashFlowDto flow, DateOnly asOf)
        => RetainedEvidenceIdentityValidator.IsComplete(evidence)
            && KnownBy(evidence.ReviewedAtUtc, asOf) && KnownBy(evidence.RetainedAtUtc, asOf)
            && evidence.EffectiveDate <= asOf && Guid.TryParse(evidence.SubjectId, out var subjectId)
            && (subjectId == run.ProjectionRunId && evidence.SubjectType is "AssetCashFlowProjectionRun" or "CashFlowProjectionRun"
                || subjectId == flow.ProjectedCashFlowId && evidence.SubjectType is "AssetProjectedCashFlow" or "ProjectedCashFlow");

    private static AssetTermsVersionDto? ResolveTerms(AssetOperationsDetailDto detail,
        InstrumentPositionProjectionSnapshot snapshot, AssetCashFlowProjectionRunDto run,
        AssetProjectedCashFlowDto flow, Guid positionId, DateOnly asOf)
    {
        var candidates = detail.TermsHistory.Where(row => row.SecurityId == detail.Subject.SecurityId
            && row.EffectiveDate <= run.ProjectionAsOf && KnownBy(row.RecordedAt, asOf) && row.RecordedAt <= run.GeneratedAt);
        var termsId = ReadText(flow.ExtensionPayload, "termsVersionId") ?? ReadText(run.ExtensionPayload, "termsVersionId");
        var lineageHashes = snapshot.ProjectionLineages.Concat(detail.ProjectionLineages)
            .Where(row => KnownPositionLineage(row, run, positionId, asOf))
            .Select(static row => row.TermsHash).Where(static hash => !string.IsNullOrWhiteSpace(hash))
            .Distinct(StringComparer.Ordinal).ToArray();
        if (lineageHashes.Length > 1)
            return null;
        var termsHash = ReadText(flow.ExtensionPayload, "termsHash") ?? ReadText(run.ExtensionPayload, "termsHash");
        if (termsHash is not null && lineageHashes.Length == 1 && termsHash != lineageHashes[0])
            return null;
        termsHash ??= lineageHashes.SingleOrDefault();
        if (termsId is not null)
            candidates = Guid.TryParse(termsId, out var id) ? candidates.Where(row => row.TermsVersionId == id) : [];
        if (termsHash is not null)
            candidates = candidates.Where(row => string.Equals(row.TermsHash, termsHash, StringComparison.Ordinal));
        var matches = candidates.Where(static row => !string.IsNullOrWhiteSpace(row.TermsHash)).ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }

    private static (bool Applies, bool PositionSized, string? Issue) ResolveProjectionScope(
        AssetOperationsDetailDto detail, InstrumentPositionProjectionSnapshot snapshot,
        AssetCashFlowProjectionRunDto run, AssetProjectedCashFlowDto flow, Guid positionId, DateOnly asOf)
    {
        var lineages = detail.ProjectionLineages.Concat(snapshot.ProjectionLineages)
            .Where(row => row.ProjectionRunId == run.ProjectionRunId).ToArray();
        var explicitIds = new[] { ReadText(run.ExtensionPayload, "bookPositionId"), ReadText(run.ExtensionPayload, "positionId"),
            ReadText(flow.ExtensionPayload, "bookPositionId"), ReadText(flow.ExtensionPayload, "positionId") }
            .Where(static value => value is not null)
            .Select(static value => Guid.TryParse(value, out var id) ? id : Guid.Empty).ToArray();
        var boundIds = lineages.SelectMany(static row => new[] { row.BookPositionId, row.TriggerEvent.BookPositionId })
            .Where(static id => id.HasValue).Select(static id => id!.Value).Concat(explicitIds).Distinct().ToArray();
        if (boundIds.Length == 0)
            return (true, false, null);
        if (boundIds.Length != 1 || boundIds[0] == Guid.Empty)
            return (false, false, $"{run.ProjectionRunId:D}: conflicting position bindings prevent receipt sizing.");
        if (boundIds[0] != positionId)
            return (false, false, null);
        if (explicitIds.Length > 0 || lineages.Any(row => KnownPositionLineage(row, run, positionId, asOf)))
            return (true, true, null);
        return (false, false, $"{run.ProjectionRunId:D}: position-bound projection lineage is unavailable at the observation date.");
    }

    private static bool KnownPositionLineage(ProjectionLineageDto lineage, AssetCashFlowProjectionRunDto run,
        Guid positionId, DateOnly asOf)
        => lineage.ProjectionRunId == run.ProjectionRunId && lineage.ProjectionAsOfDate <= asOf
            && KnownBy(lineage.GeneratedAtUtc, asOf) && KnownBy(lineage.TriggerEvent.OccurredAtUtc, asOf)
            && lineage.TriggerEvent.EffectiveDate <= asOf
            && (lineage.BookPositionId == positionId || lineage.TriggerEvent.BookPositionId == positionId)
            && (lineage.BookPositionId is null || lineage.BookPositionId == positionId)
            && (lineage.TriggerEvent.BookPositionId is null || lineage.TriggerEvent.BookPositionId == positionId)
            && (lineage.TriggerEvent.SecurityId is null || lineage.TriggerEvent.SecurityId == run.SecurityId);

    private static string? ReadText(JsonElement? payload, string name)
        => payload is { ValueKind: JsonValueKind.Object } value && value.TryGetProperty(name, out var property)
            && property.ValueKind == JsonValueKind.String ? property.GetString() : null;

    private static bool KnownBy(DateTimeOffset timestamp, DateOnly asOf)
        => timestamp != default && DateOnly.FromDateTime(timestamp.UtcDateTime) <= asOf;

    private static bool AcceptedRunStatus(string status) => status is "Accepted" or "Approved" or "Completed" or "Ready";
    private static bool AcceptedFlowStatus(string status) => status is "Expected" or "Projected" or "Accepted" or "Approved" or "Due" or "Overdue";

    private static string? NormalizeFlowType(string value)
        => value.Trim().ToUpperInvariant() switch
        {
            "COUPON" or "COUPONPAYMENT" => "Coupon",
            "INTEREST" or "INTERESTPAYMENT" => "Interest",
            "PRINCIPAL" or "PRINCIPALREPAYMENT" or "MATURITY" or "REDEMPTION" or "PRINCIPALPAYMENT" => "Principal",
            _ => null
        };

    private static string? JoinIssues(IEnumerable<string> issues)
    {
        var text = string.Join(" ", issues.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal));
        return text.Length == 0 ? null : text;
    }
}
