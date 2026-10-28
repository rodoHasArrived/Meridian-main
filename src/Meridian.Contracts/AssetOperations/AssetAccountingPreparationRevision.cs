using System.Text.Json;
using Meridian.Contracts.Integrity;

namespace Meridian.Contracts.AssetOperations;

/// <summary>An append-only replacement of an undrafted factor preparation, bound to its exact predecessor.</summary>
public sealed record AssetAccountingPreparationRevisionDto(
    long PreviousSpineVersion,
    string PreviousCanonicalFingerprint,
    string PreparedBy,
    DateTimeOffset PreparedAtUtc,
    string Reason);

/// <summary>The bounded recovery transition; ordinary source and lifecycle immutability remain unchanged.</summary>
public static class AssetAccountingPreparationRevisionValidator
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>The existing append-store serialization, including historical null-field omission.</summary>
    public static string ProjectionFingerprint(AssetAccountingEventSpineDto projection)
        => Sha256Digest.Compute(JsonSerializer.SerializeToUtf8Bytes(projection, JsonOptions));

    public static void ValidateTransition(AssetAccountingEventSpineDto previous, AssetAccountingEventSpineDto next)
    {
        var revision = next.PreparationRevision
            ?? throw new InvalidOperationException("A preparation revision requires its exact retained predecessor.");
        Require(revision.PreviousSpineVersion == previous.SpineVersion &&
            Sha256Digest.FixedEquals(revision.PreviousCanonicalFingerprint, ProjectionFingerprint(previous)),
            "A preparation revision must reference the exact immediately preceding immutable snapshot.");
        Require(IsUndraftedFactorPreparation(previous) && IsUndraftedFactorPreparation(next) &&
            next.Stages.Count == 2 && next.Stages[1].Stage == AssetAccountingLifecycleStageDto.Projected &&
            next.ProjectedEffect is not null && next.SpineVersion == previous.SpineVersion + 1,
            "Only an Expected or Projected factor preparation can be replaced by one freshly projected snapshot.");
        Require(revision.PreparedAtUtc != default && revision.PreparedAtUtc.Offset == TimeSpan.Zero &&
            revision.PreparedAtUtc <= DateTimeOffset.UtcNow &&
            revision.PreparedAtUtc >= previous.Stages[^1].AttestedAtUtc && !string.IsNullOrWhiteSpace(revision.Reason) &&
            string.Equals(previous.Stages[0].AttestedBy, revision.PreparedBy, StringComparison.Ordinal) &&
            next.Stages.All(stage => stage.AttestedBy == revision.PreparedBy && stage.AttestedAtUtc == revision.PreparedAtUtc),
            "A preparation revision requires the same preparing actor and fresh authenticated attestations.");
        Require(previous.EventId == next.EventId && previous.EventVersion == next.EventVersion &&
            previous.EventKind == next.EventKind && previous.EffectiveDate == next.EffectiveDate &&
            previous.Currency == next.Currency && Equal(previous.EconomicEvent, next.EconomicEvent) &&
            Equal(previous.ProjectionLineage with { GeneratedAtUtc = default }, next.ProjectionLineage with { GeneratedAtUtc = default }) &&
            Equal(previous.RetainedEvidence, next.RetainedEvidence) &&
            previous.Scope.SecurityId == next.Scope.SecurityId && previous.Scope.BookPositionId == next.Scope.BookPositionId &&
            previous.Scope.LedgerBookId == next.Scope.LedgerBookId && previous.Scope.PeriodId == next.Scope.PeriodId &&
            previous.Scope.AccountingBasis == next.Scope.AccountingBasis && previous.Scope.FundProfileId == next.Scope.FundProfileId &&
            previous.Scope.TenantId == next.Scope.TenantId && previous.Scope.CompanyId == next.Scope.CompanyId &&
            previous.FactorPaydown!.PriorFactor == next.FactorPaydown!.PriorFactor &&
            previous.FactorPaydown.NewFactor == next.FactorPaydown.NewFactor,
            "A preparation revision cannot change its economic source, factors, evidence or ownership identity.");
    }

    private static bool IsUndraftedFactorPreparation(AssetAccountingEventSpineDto projection)
        => projection.EventKind == AssetAccountingEventKindDto.CorporateAction && projection.FactorPaydown is not null &&
            projection.CorporateAction is null && projection.CapitalReturn is null && projection.Correction is null &&
            projection.Stages.Count is 1 or 2 && projection.Stages[0].Stage == AssetAccountingLifecycleStageDto.Expected &&
            (projection.Stages.Count == 1 || projection.Stages[1].Stage == AssetAccountingLifecycleStageDto.Projected) &&
            projection.DraftedCandidate is null && projection.DraftedCandidateResult is null && projection.DraftedLotMutation is null &&
            projection.DraftedCandidateFingerprint is null && projection.DraftedCandidateResultFingerprint is null &&
            projection.DraftedLotMutationFingerprint is null && projection.PostedJournalImpact is null && projection.TaxLotMutationBatchId is null;

    private static bool Equal<T>(T left, T right)
        => JsonElement.DeepEquals(JsonSerializer.SerializeToElement(left, JsonOptions), JsonSerializer.SerializeToElement(right, JsonOptions));

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
