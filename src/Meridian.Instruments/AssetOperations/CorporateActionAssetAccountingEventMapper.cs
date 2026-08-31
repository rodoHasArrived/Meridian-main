using System.Globalization;
using System.Text;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.Ledger;

namespace Meridian.Instruments.AssetOperations;

public interface ICorporateActionAssetAccountingEventMapper
{
    CorporateActionAssetAccountingEventMapResult Map(CorporateActionAssetAccountingEventMapRequest request);
}

public sealed record CorporateActionAssetAccountingEventMapRequest(
    CorporateActionAccountingProjectionDto Projection,
    AssetAccountingEventScopeDto Scope,
    CorporateActionMappedAccountingEffectDto MappedEffect,
    long ExpectedPeriodVersion,
    string Actor,
    DateTimeOffset ProjectedAtUtc,
    IReadOnlyList<RetainedEvidenceIdentityDto>? RetainedEvidence = null,
    string? Notes = null,
    AssetAccountingCorrectionReferenceDto? Correction = null)
{
    public IReadOnlyList<RetainedEvidenceIdentityDto> RetainedEvidence { get; init; } =
        RetainedEvidence ?? [];
}

public sealed record CorporateActionAssetAccountingEventMapResult(
    CorporateActionAssetAccountingEventProjectionDto? Projection,
    IReadOnlyList<CorporateActionProjectionBlockerDto>? Blockers = null)
{
    public IReadOnlyList<CorporateActionProjectionBlockerDto> Blockers { get; init; } = Blockers ?? [];

    public bool IsMapped => Projection is not null && Blockers.Count == 0;
}

/// <summary>
/// Adapts reviewed economic intent to the canonical Asset Accounting Event Spine handoff. The
/// caller must supply a balanced effect already mapped by the promoted accounting rule pack; this
/// mapper never invents general-ledger accounts or appends a journal.
/// </summary>
public sealed class CorporateActionAssetAccountingEventMapper : ICorporateActionAssetAccountingEventMapper
{
    public CorporateActionAssetAccountingEventMapResult Map(CorporateActionAssetAccountingEventMapRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Projection);
        ArgumentNullException.ThrowIfNull(request.Scope);
        ArgumentNullException.ThrowIfNull(request.MappedEffect);
        ArgumentNullException.ThrowIfNull(request.MappedEffect.Effect);
        ArgumentNullException.ThrowIfNull(request.MappedEffect.AccountingRulePack);

        var projection = request.Projection;
        var mappedEffect = request.MappedEffect;
        var effect = mappedEffect.Effect;
        var blockers = new List<CorporateActionProjectionBlockerDto>();
        if (projection.Status != CorporateActionProjectionStatusDto.Projected || projection.Blockers.Count > 0)
        {
            blockers.Add(new CorporateActionProjectionBlockerDto(
                "corporate-action.event-map-projection-blocked",
                "A blocked corporate-action projection cannot enter the Asset Accounting Event Spine."));
        }

        if (projection.EconomicEvent is not { } economicEvent ||
            projection.ProjectionLineage is not { } lineage ||
            projection.LotMutations is not { } lotMutations ||
            projection.PostingSet is not { } postingSet)
        {
            blockers.Add(new CorporateActionProjectionBlockerDto(
                "corporate-action.event-map-incomplete-projection",
                "Economic event, lineage, lot intent, and posting intent are required for event-spine mapping."));
            return new CorporateActionAssetAccountingEventMapResult(null, blockers);
        }

        if (postingSet.RequiresJournalCandidate)
        {
            foreach (var blocker in CorporateActionLotMutationPlanValidator.Validate(lotMutations.Mutations))
            {
                blockers.Add(blocker);
            }
        }
        else if (projection.Status == CorporateActionProjectionStatusDto.Projected)
        {
            blockers.Add(new CorporateActionProjectionBlockerDto(
                "corporate-action.event-map-no-journal-intent",
                "The corporate action has no journal-candidate intent; retain it as an operational event instead."));
        }

        if (projection.CaseId == Guid.Empty ||
            projection.CaseVersion <= 0 ||
            projection.PolicyDecisionVersion <= 0 ||
            projection.PositionSnapshotId == Guid.Empty ||
            projection.LotSnapshotId == Guid.Empty ||
            projection.LotSnapshotVersion <= 0 ||
            projection.PolicyDecisionId == Guid.Empty ||
            projection.ElectionVersion.HasValue != projection.ElectionId.HasValue ||
            !Sha256Digest.IsCanonical(projection.ProjectionInputHash))
        {
            blockers.Add(new CorporateActionProjectionBlockerDto(
                "corporate-action.event-map-dependency-identity-incomplete",
                "Case, policy, position/lot snapshots, election, and canonical projection-input identities are required."));
        }

        var accountingRulePack = mappedEffect.AccountingRulePack;
        if (accountingRulePack is null ||
            string.IsNullOrWhiteSpace(accountingRulePack.RulePackId) ||
            string.IsNullOrWhiteSpace(accountingRulePack.RulePackVersion) ||
            string.IsNullOrWhiteSpace(accountingRulePack.SelectedRuleId) ||
            string.IsNullOrWhiteSpace(accountingRulePack.SelectedRuleVersion))
        {
            blockers.Add(new CorporateActionProjectionBlockerDto(
                "corporate-action.event-map-accounting-rule-required",
                "A promoted accounting rule pack and exact selected rule/version are required."));
        }

        if (economicEvent.EventType != AssetAccountingEventTypeNames.For(AssetAccountingEventKindDto.CorporateAction))
        {
            blockers.Add(new CorporateActionProjectionBlockerDto(
                "corporate-action.event-map-kind-mismatch",
                "The economic event is not typed as AssetAccounting.CorporateAction."));
        }

        if (request.Scope.SecurityId != economicEvent.SecurityId ||
            request.Scope.BookPositionId != economicEvent.BookPositionId ||
            request.Scope.BookPositionId != lotMutations.PositionId ||
            request.Scope.ExpectedBookPositionVersion != lotMutations.ExpectedPositionVersion ||
            request.Scope.AccountingBasis != projection.Treatment.AccountingBasis ||
            request.Scope.AccountingBasis != postingSet.AccountingBasis)
        {
            blockers.Add(new CorporateActionProjectionBlockerDto(
                "corporate-action.event-map-scope-mismatch",
                "Security, position version, and accounting basis must match the projected corporate action."));
        }

        if (projection.AccountingScope is not { } projectedScope ||
            !string.Equals(request.Scope.TenantId, projectedScope.TenantId, StringComparison.Ordinal) ||
            !string.Equals(request.Scope.CompanyId, projectedScope.CompanyId, StringComparison.Ordinal) ||
            !string.Equals(request.Scope.FundProfileId, projectedScope.FundProfileId, StringComparison.Ordinal) ||
            request.Scope.LedgerBookId != projectedScope.LedgerBookId ||
            request.Scope.PeriodId != projectedScope.PeriodId ||
            request.ExpectedPeriodVersion != projectedScope.ExpectedPeriodVersion)
        {
            blockers.Add(new CorporateActionProjectionBlockerDto(
                "corporate-action.event-map-accounting-scope-mismatch",
                "Tenant, company, fund, ledger book, period, and period version must match the projection scope."));
        }

        if (request.ExpectedPeriodVersion <= 0)
        {
            blockers.Add(new CorporateActionProjectionBlockerDto(
                "corporate-action.event-map-period-version-required",
                "A positive expected accounting-period version is required."));
        }

        if (string.IsNullOrWhiteSpace(request.Actor) ||
            request.ProjectedAtUtc == default ||
            request.ProjectedAtUtc.Offset != TimeSpan.Zero ||
            request.ProjectedAtUtc < lineage.GeneratedAtUtc)
        {
            blockers.Add(new CorporateActionProjectionBlockerDto(
                "corporate-action.event-map-attestation-invalid",
                "Projection mapping requires an actor and a UTC attestation at or after lineage generation."));
        }

        ValidateProjectedEffect(effect, projection, lineage, blockers);
        ValidateMappedEffectAttestation(mappedEffect, projection, request.Scope, blockers);
        ValidateEvidence(request.RetainedEvidence, projection, economicEvent, blockers);
        if (blockers.Count > 0)
        {
            return new CorporateActionAssetAccountingEventMapResult(null, blockers);
        }

        var eventRequest = new ProjectAssetAccountingEventRequestDto(
            AssetAccountingEventKindDto.CorporateAction,
            request.Scope,
            economicEvent,
            lineage,
            effect,
            projection.EventAmount,
            postingSet.Currency,
            request.ExpectedPeriodVersion,
            request.Actor.Trim(),
            request.ProjectedAtUtc,
            request.RetainedEvidence,
            request.Notes,
            request.Correction);

        var postingIdempotencyKey = BuildPostingIdempotencyKey(
            projection,
            request.Scope,
            mappedEffect,
            request.ExpectedPeriodVersion,
            request.RetainedEvidence);

        return new CorporateActionAssetAccountingEventMapResult(
            new CorporateActionAssetAccountingEventProjectionDto(
                eventRequest,
                projection.Treatment,
                lotMutations,
                postingSet,
                accountingRulePack!,
                postingIdempotencyKey));
    }

    private static void ValidateProjectedEffect(
        ProjectedAccountingEffectDto effect,
        CorporateActionAccountingProjectionDto projection,
        ProjectionLineageDto lineage,
        ICollection<CorporateActionProjectionBlockerDto> blockers)
    {
        if (effect.ProjectionRunId != lineage.ProjectionRunId ||
            effect.ModelKey != lineage.ModelKey ||
            effect.ModelVersion != lineage.ModelVersion ||
            effect.ProjectionAsOfDate != lineage.ProjectionAsOfDate)
        {
            blockers.Add(new CorporateActionProjectionBlockerDto(
                "corporate-action.event-map-effect-lineage-mismatch",
                "The mapped accounting effect must retain the exact corporate-action projection lineage."));
        }

        if (!effect.IsBalanced || effect.Lines.Count == 0 ||
            effect.Lines.Sum(static line => line.Debit) != effect.TotalDebits ||
            effect.Lines.Sum(static line => line.Credit) != effect.TotalCredits)
        {
            blockers.Add(new CorporateActionProjectionBlockerDto(
                "corporate-action.event-map-effect-unbalanced",
                "The promoted rule-pack effect must contain balanced lines and matching totals."));
        }

        if (effect.Lines.Any(static line =>
                string.IsNullOrWhiteSpace(line.AccountId) ||
                line.Debit < 0m ||
                line.Credit < 0m ||
                (line.Debit == 0m) == (line.Credit == 0m)))
        {
            blockers.Add(new CorporateActionProjectionBlockerDto(
                "corporate-action.event-map-effect-line-invalid",
                "Each mapped effect line requires an account and exactly one positive debit or credit amount."));
        }

        if (projection.PostingSet is { } postingSet &&
            (!string.Equals(effect.Currency, postingSet.Currency, StringComparison.OrdinalIgnoreCase) ||
             effect.Lines.Any(line =>
                 !string.Equals(line.Currency, postingSet.Currency, StringComparison.OrdinalIgnoreCase))))
        {
            blockers.Add(new CorporateActionProjectionBlockerDto(
                "corporate-action.event-map-currency-mismatch",
                "Mapped effect and line currencies must match the corporate-action posting set."));
        }

    }

    private static void ValidateMappedEffectAttestation(
        CorporateActionMappedAccountingEffectDto mappedEffect,
        CorporateActionAccountingProjectionDto projection,
        AssetAccountingEventScopeDto scope,
        ICollection<CorporateActionProjectionBlockerDto> blockers)
    {
        if (!Sha256Digest.IsCanonical(projection.PostingIntentHash) ||
            !Sha256Digest.FixedEquals(mappedEffect.PostingIntentHash, projection.PostingIntentHash))
        {
            blockers.Add(new CorporateActionProjectionBlockerDto(
                "corporate-action.event-map-posting-intent-stale",
                "The promoted accounting effect does not bind the current posting-intent hash."));
        }

        if (!CorporateActionMappedAccountingEffectAttestor.HasCompleteMapping(
                projection.PostingSet?.Components ?? [],
                mappedEffect.Effect.Lines,
                mappedEffect.ComponentLineMappings))
        {
            blockers.Add(new CorporateActionProjectionBlockerDto(
                "corporate-action.event-map-component-reconciliation-incomplete",
                "Every posting component and generated effect line requires an exact component-to-line reconciliation mapping."));
        }

        var expectedMappingHash = CorporateActionMappedAccountingEffectAttestor.ComputeMappingHash(
            projection.PostingIntentHash ?? string.Empty,
            scope,
            mappedEffect.Effect,
            mappedEffect.AccountingRulePack,
            mappedEffect.ComponentLineMappings);
        if (!Sha256Digest.IsCanonical(mappedEffect.MappingHash) ||
            !Sha256Digest.FixedEquals(mappedEffect.MappingHash, expectedMappingHash))
        {
            blockers.Add(new CorporateActionProjectionBlockerDto(
                "corporate-action.event-map-mapping-attestation-invalid",
                "The mapped effect, rule pack, scope, and reconciliation manifest do not match their attestation hash."));
        }
    }

    private static void ValidateEvidence(
        IReadOnlyList<RetainedEvidenceIdentityDto> evidence,
        CorporateActionAccountingProjectionDto projection,
        EconomicEventReferenceDto economicEvent,
        ICollection<CorporateActionProjectionBlockerDto> blockers)
    {
        if (evidence.Count == 0)
        {
            blockers.Add(new CorporateActionProjectionBlockerDto(
                "corporate-action.event-map-evidence-required",
                "Complete retained corporate-action evidence is required."));
            return;
        }

        if (evidence.Any(item => RetainedEvidenceIdentityValidator.Validate(item).Count > 0))
        {
            blockers.Add(new CorporateActionProjectionBlockerDto(
                "corporate-action.event-map-evidence-incomplete",
                "Every retained corporate-action evidence identity must be complete and accepted."));
        }

        if (projection.EvidenceManifest.Any(dependency => !evidence.Any(item =>
                string.Equals(item.EvidenceId, dependency.EvidenceId, StringComparison.Ordinal) &&
                string.Equals(item.EvidenceUri, dependency.EvidenceUri, StringComparison.Ordinal) &&
                item.EvidenceVersion == dependency.EvidenceVersion &&
                string.Equals(item.SubjectType, dependency.SubjectType, StringComparison.Ordinal) &&
                string.Equals(item.SubjectId, dependency.SubjectId, StringComparison.Ordinal) &&
                Sha256Digest.FixedEquals(item.ContentHashSha256, dependency.ContentHashSha256))))
        {
            blockers.Add(new CorporateActionProjectionBlockerDto(
                "corporate-action.event-map-evidence-manifest-mismatch",
                "Retained evidence must satisfy every typed source, position, lot, election, and policy dependency in the projection."));
        }

        if (!evidence.Any(item =>
                item.SubjectType == AssetAccountingEvidenceSubjects.Event &&
                item.SubjectId == economicEvent.EventId.ToString("D") &&
                item.EffectiveDate == economicEvent.EffectiveDate &&
                item.SourceReference == economicEvent.SourceEntityId &&
                string.Equals(
                    item.ContentHashSha256,
                    economicEvent.SourceContentHash,
                    StringComparison.OrdinalIgnoreCase)))
        {
            blockers.Add(new CorporateActionProjectionBlockerDto(
                "corporate-action.event-map-evidence-binding-mismatch",
                "Retained evidence must exactly bind the event id, date, source reference, and source hash."));
        }
    }

    private static string BuildPostingIdempotencyKey(
        CorporateActionAccountingProjectionDto projection,
        AssetAccountingEventScopeDto scope,
        CorporateActionMappedAccountingEffectDto mappedEffect,
        long expectedPeriodVersion,
        IReadOnlyList<RetainedEvidenceIdentityDto> evidence)
    {
        var builder = new StringBuilder(1024);
        AppendToken(builder, "corporate-action-posting/v1");
        AppendToken(builder, projection.CaseId.ToString("N"));
        AppendToken(builder, projection.CaseVersion.ToString(CultureInfo.InvariantCulture));
        AppendToken(builder, projection.ElectionVersion?.ToString(CultureInfo.InvariantCulture));
        AppendToken(builder, projection.PolicyDecisionVersion.ToString(CultureInfo.InvariantCulture));
        AppendToken(builder, projection.PositionSnapshotId.ToString("N"));
        AppendToken(builder, projection.ProjectionInputHash);
        AppendToken(builder, scope.LedgerBookId.ToString("N"));
        AppendToken(builder, scope.PeriodId.ToString("N"));
        AppendToken(builder, scope.AccountingBasis.ToString());
        AppendToken(builder, scope.ExpectedBookPositionVersion.ToString(CultureInfo.InvariantCulture));
        AppendToken(builder, expectedPeriodVersion.ToString(CultureInfo.InvariantCulture));
        AppendToken(builder, mappedEffect.PostingIntentHash);
        AppendToken(builder, mappedEffect.MappingHash);
        var accountingRulePack = mappedEffect.AccountingRulePack;
        AppendToken(builder, accountingRulePack.RulePackId.Trim());
        AppendToken(builder, accountingRulePack.RulePackVersion.Trim());
        AppendToken(builder, accountingRulePack.SelectedRuleId?.Trim());
        AppendToken(builder, accountingRulePack.SelectedRuleVersion?.Trim());
        foreach (var item in evidence
                     .OrderBy(static item => item.EvidenceId, StringComparer.Ordinal)
                     .ThenBy(static item => item.EvidenceVersion))
        {
            AppendToken(builder, item.EvidenceId);
            AppendToken(builder, item.EvidenceUri);
            AppendToken(builder, item.ContentHashSha256);
            AppendToken(builder, item.EvidenceVersion.ToString(CultureInfo.InvariantCulture));
            AppendToken(builder, item.SubjectType);
            AppendToken(builder, item.SubjectId);
        }

        return $"corporate-action-posting/v1:{Sha256Digest.ComputeUtf8(builder.ToString())}";
    }

    private static void AppendToken(StringBuilder builder, string? value)
    {
        var token = value ?? "<null>";
        builder.Append(token.Length.ToString(CultureInfo.InvariantCulture));
        builder.Append(':');
        builder.Append(token);
        builder.Append(';');
    }
}
