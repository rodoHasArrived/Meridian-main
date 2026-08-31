using System.Text.Json;
using System.Text.Json.Serialization;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.Ledger;

namespace Meridian.Contracts.AssetOperations;

/// <summary>
/// Canonical accounting event classes owned by the Asset Accounting Event Spine.
/// These values describe economic purpose; they are not posting statuses.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<AssetAccountingEventKindDto>))]
public enum AssetAccountingEventKindDto
{
    Acquisition = 0,
    Capitalization = 1,
    Valuation = 2,
    Income = 3,
    CorporateAction = 4,
    Impairment = 5,
    DepreciationAmortization = 6,
    Disposal = 7
}

public static class AssetAccountingEventTypeNames
{
    public const string Prefix = "AssetAccounting.";

    public static string For(AssetAccountingEventKindDto eventKind) => $"{Prefix}{eventKind}";

    public static bool TryParse(string? eventType, out AssetAccountingEventKindDto eventKind)
    {
        eventKind = default;
        if (string.IsNullOrWhiteSpace(eventType))
        {
            return false;
        }

        var normalized = eventType.Trim();
        if (!normalized.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return false;
        }

        return Enum.TryParse(normalized[Prefix.Length..], ignoreCase: false, out eventKind) &&
               Enum.IsDefined(eventKind);
    }
}

/// <summary>
/// Independent accounting lifecycle attestations. A later stage never rewrites an earlier one.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<AssetAccountingLifecycleStageDto>))]
public enum AssetAccountingLifecycleStageDto
{
    Expected = 0,
    Projected = 1,
    Drafted = 2,
    Approved = 3,
    Posted = 4,
    Reconciled = 5,
    Reported = 6
}

/// <summary>
/// The authoritative book and position boundary against which an asset event is resolved.
/// </summary>
public sealed record AssetAccountingEventScopeDto(
    Guid SecurityId,
    long ExpectedSecurityVersion,
    Guid BookPositionId,
    long ExpectedBookPositionVersion,
    Guid LedgerBookId,
    Guid PeriodId,
    AccountingBasisKindDto AccountingBasis,
    string FundProfileId,
    string? TenantId = null,
    string? CompanyId = null,
    LedgerDimensionSetDto? Dimensions = null);

/// <summary>
/// Evidence-backed attestation of one lifecycle stage. Stage evidence is append-only.
/// </summary>
public sealed record AssetAccountingStageEvidenceDto(
    AssetAccountingLifecycleStageDto Stage,
    DateTimeOffset AttestedAtUtc,
    string AttestedBy,
    IReadOnlyList<RetainedEvidenceIdentityDto>? Evidence = null,
    string? ReferenceId = null,
    string? Notes = null)
{
    public IReadOnlyList<RetainedEvidenceIdentityDto> Evidence { get; init; } = Evidence ?? [];
}

/// <summary>
/// A calculated line that has not been durably posted. It intentionally has no journal identity.
/// </summary>
public sealed record ProjectedAccountingEffectLineDto(
    string AccountId,
    decimal Debit,
    decimal Credit,
    string Currency,
    string? Description = null,
    LedgerDimensionSetDto? Dimensions = null);

/// <summary>
/// Expected or projected accounting effect. This must never be presented as journal impact.
/// </summary>
public sealed record ProjectedAccountingEffectDto(
    Guid ProjectionRunId,
    string ModelKey,
    string ModelVersion,
    DateOnly ProjectionAsOfDate,
    decimal TotalDebits,
    decimal TotalCredits,
    string Currency,
    IReadOnlyList<ProjectedAccountingEffectLineDto>? Lines = null)
{
    public IReadOnlyList<ProjectedAccountingEffectLineDto> Lines { get; init; } = Lines ?? [];

    public bool IsBalanced => TotalDebits == TotalCredits;
}

/// <summary>
/// A line read from an immutable posted journal.
/// </summary>
public sealed record PostedJournalImpactLineDto(
    Guid LineId,
    string AccountId,
    decimal Debit,
    decimal Credit,
    string Currency,
    string? Description = null,
    LedgerDimensionSetDto? Dimensions = null);

[JsonConverter(typeof(JsonStringEnumConverter<JournalPostingStatusDto>))]
public enum JournalPostingStatusDto
{
    Unknown = 0,
    Posted = 1
}

/// <summary>
/// Durable posted journal impact. Every identity and amount is required so clients cannot promote
/// a projection, draft identifier, or provenance reference into posted accounting truth.
/// </summary>
public sealed record PostedJournalImpactDto(
    Guid JournalEntryId,
    Guid LedgerBookId,
    Guid PeriodId,
    AccountingBasisKindDto AccountingBasis,
    DateTimeOffset PostedAtUtc,
    JournalPostingStatusDto PostingStatus,
    string Currency,
    decimal TotalDebits,
    decimal TotalCredits,
    IReadOnlyList<PostedJournalImpactLineDto>? Lines = null)
{
    public IReadOnlyList<PostedJournalImpactLineDto> Lines { get; init; } = Lines ?? [];

    public bool IsBalanced => TotalDebits == TotalCredits;
}

/// <summary>Typed immutable lineage to the posted event outcome being corrected.</summary>
public sealed record AssetAccountingCorrectionReferenceDto(
    Guid EventId,
    long EventVersion,
    Guid PostedJournalEntryId,
    Guid LedgerBookId,
    Guid PeriodId,
    AccountingBasisKindDto AccountingBasis,
    Guid? TaxLotMutationBatchId = null);

/// <summary>
/// One authoritative economic event with independently attested accounting lifecycle stages.
/// </summary>
public sealed record AssetAccountingEventSpineDto(
    Guid EventId,
    AssetAccountingEventKindDto EventKind,
    long EventVersion,
    long SpineVersion,
    DateOnly EffectiveDate,
    decimal EventAmount,
    string Currency,
    AssetAccountingEventScopeDto Scope,
    EconomicEventReferenceDto EconomicEvent,
    ProjectionLineageDto ProjectionLineage,
    IReadOnlyList<RetainedEvidenceIdentityDto>? RetainedEvidence = null,
    IReadOnlyList<AssetAccountingStageEvidenceDto>? Stages = null,
    ProjectedAccountingEffectDto? ProjectedEffect = null,
    PostedJournalImpactDto? PostedJournalImpact = null,
    Guid? TaxLotMutationBatchId = null,
    AssetAccountingCorrectionReferenceDto? Correction = null,
    PostingRuleJournalCandidateRequestDto? DraftedCandidate = null,
    PostingRuleJournalCandidateResultDto? DraftedCandidateResult = null,
    string? DraftedCandidateFingerprint = null,
    string? DraftedCandidateResultFingerprint = null,
    AssetLotMutationInstructionDto? DraftedLotMutation = null,
    string? DraftedLotMutationFingerprint = null)
{
    public IReadOnlyList<RetainedEvidenceIdentityDto> RetainedEvidence { get; init; } = RetainedEvidence ?? [];

    public IReadOnlyList<AssetAccountingStageEvidenceDto> Stages { get; init; } = Stages ?? [];
}

/// <summary>
/// Authoritative handoff from an Asset Operations projector into the persisted accounting-event
/// spine. The service resolves the position, ledger book, and period again; request scope is an
/// expected-version assertion and not client-owned accounting truth.
/// </summary>
public sealed record ProjectAssetAccountingEventRequestDto(
    AssetAccountingEventKindDto EventKind,
    AssetAccountingEventScopeDto Scope,
    EconomicEventReferenceDto EconomicEvent,
    ProjectionLineageDto ProjectionLineage,
    ProjectedAccountingEffectDto ProjectedEffect,
    decimal EventAmount,
    string Currency,
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

/// <summary>
/// Evidence-only append for post-journal lifecycle stages. Posted impact cannot be supplied or
/// changed through this command.
/// </summary>
public sealed record AppendAssetAccountingLifecycleStageRequestDto(
    Guid EventId,
    long EventVersion,
    AssetAccountingLifecycleStageDto Stage,
    long ExpectedSpineVersion,
    long ExpectedBookPositionVersion,
    string FundProfileId,
    string Actor,
    DateTimeOffset AttestedAtUtc,
    string ReferenceId,
    IReadOnlyList<RetainedEvidenceIdentityDto>? Evidence = null,
    string? Notes = null,
    string? TenantId = null,
    string? CompanyId = null)
{
    public IReadOnlyList<RetainedEvidenceIdentityDto> Evidence { get; init; } = Evidence ?? [];
}

public sealed record AssetAccountingEventSpineAppendResultDto(
    AssetAccountingEventSpineDto Spine,
    bool WasReplay);

[JsonConverter(typeof(JsonStringEnumConverter<AssetLotMutationIntentDto>))]
public enum AssetLotMutationIntentDto
{
    None = 0,
    Acquire = 1,
    Dispose = 2
}

public sealed record AssetAcquisitionLotDto(
    Guid TaxLotRecordId,
    string LotId,
    DateOnly AcquiredDate,
    decimal Quantity,
    decimal UnitCost,
    string AccountId);

public sealed record AssetDisposalLotSelectionDto(
    Guid TaxLotRecordId,
    string LotId,
    long ExpectedVersion,
    decimal ExpectedOpenQuantity,
    decimal Quantity,
    int SelectionOrdinal,
    string SelectionEvidenceId,
    decimal ExpectedUnitCost = 0m,
    decimal ExpectedCostBasis = 0m);

/// <summary>
/// Lot intent retained with the accounting event. Persistence still re-resolves and locks the
/// authoritative lots; client-provided selections are expected-version assertions, not truth.
/// </summary>
public sealed record AssetLotMutationInstructionDto(
    AssetLotMutationIntentDto Intent,
    AssetAcquisitionLotDto? Acquisition = null,
    IReadOnlyList<AssetDisposalLotSelectionDto>? DisposalSelections = null,
    string? ReliefMethod = null,
    string? PolicyRevision = null,
    Guid? CorrectsMutationBatchId = null,
    Guid? CorrectsJournalEntryId = null,
    LedgerAdjustmentApprovalMetadataDto? CorrectionApproval = null,
    string? AssetAccountId = null)
{
    public IReadOnlyList<AssetDisposalLotSelectionDto> DisposalSelections { get; init; } =
        DisposalSelections ?? [];
}

public sealed record AssetAccountingPostingCandidateRequestDto(
    AssetAccountingEventKindDto EventKind,
    AssetAccountingEventScopeDto Scope,
    EconomicEventReferenceDto EconomicEvent,
    ProjectionLineageDto ProjectionLineage,
    decimal EventAmount,
    string Currency,
    string Actor,
    DateTimeOffset AccountingTimestamp,
    string Description,
    long ExpectedSpineVersion,
    long ExpectedPeriodVersion,
    IReadOnlyList<RetainedEvidenceIdentityDto>? RetainedEvidence = null,
    AssetLotMutationInstructionDto? LotMutation = null,
    Guid? CorrelationId = null,
    string? CounterpartyId = null,
    string? InstrumentSymbol = null,
    LedgerAdjustmentApprovalMetadataDto? CorrectionApproval = null)
{
    public IReadOnlyList<RetainedEvidenceIdentityDto> RetainedEvidence { get; init; } = RetainedEvidence ?? [];
}

public sealed record AssetAccountingPostingCandidateDto(
    AssetAccountingEventSpineDto Spine,
    PostingRuleJournalCandidateResultDto Candidate);

public static class PostedJournalImpactValidator
{
    public static IReadOnlyList<string> Validate(PostedJournalImpactDto? impact)
    {
        if (impact is null)
        {
            return ["Posted journal impact is required."];
        }

        var issues = new List<string>();
        if (impact.JournalEntryId == Guid.Empty)
            issues.Add("Posted journal impact requires a journal entry id.");
        if (impact.LedgerBookId == Guid.Empty)
            issues.Add("Posted journal impact requires a ledger book id.");
        if (impact.PeriodId == Guid.Empty)
            issues.Add("Posted journal impact requires a period id.");
        if (!Enum.IsDefined(impact.AccountingBasis))
            issues.Add("Posted journal impact requires a defined accounting basis.");
        if (!Enum.IsDefined(impact.PostingStatus) || impact.PostingStatus != JournalPostingStatusDto.Posted)
            issues.Add("Posted journal impact requires explicit Posted status.");
        if (impact.PostedAtUtc == default || impact.PostedAtUtc.Offset != TimeSpan.Zero)
            issues.Add("Posted journal impact requires a non-default UTC posting timestamp.");
        if (string.IsNullOrWhiteSpace(impact.Currency))
            issues.Add("Posted journal impact requires a currency.");
        if (impact.TotalDebits < 0m || impact.TotalCredits < 0m)
            issues.Add("Posted journal impact totals cannot be negative.");
        if (!impact.IsBalanced)
            issues.Add("Posted journal impact must be balanced.");
        if (impact.Lines.Count == 0)
            issues.Add("Posted journal impact requires immutable journal lines.");
        if (impact.Lines.Any(static line => line.LineId == Guid.Empty || string.IsNullOrWhiteSpace(line.AccountId)))
            issues.Add("Posted journal impact lines require line and account identities.");
        if (impact.Lines.Any(static line =>
                line.Debit < 0m || line.Credit < 0m ||
                (line.Debit == 0m) == (line.Credit == 0m)))
            issues.Add("Posted journal impact lines require exactly one positive debit or credit amount.");
        if (impact.Lines.Sum(static line => line.Debit) != impact.TotalDebits ||
            impact.Lines.Sum(static line => line.Credit) != impact.TotalCredits)
            issues.Add("Posted journal impact totals must equal immutable journal-line amounts.");
        if (impact.Lines.Any(line => !string.Equals(line.Currency, impact.Currency, StringComparison.OrdinalIgnoreCase)))
            issues.Add("Posted journal impact line currency must match the journal currency.");

        return issues;
    }

    public static bool IsValid(PostedJournalImpactDto? impact) => Validate(impact).Count == 0;
}

public static class AssetAccountingEventSpineValidator
{
    private static readonly AssetAccountingLifecycleStageDto[] LifecycleOrder =
    [
        AssetAccountingLifecycleStageDto.Expected,
        AssetAccountingLifecycleStageDto.Projected,
        AssetAccountingLifecycleStageDto.Drafted,
        AssetAccountingLifecycleStageDto.Approved,
        AssetAccountingLifecycleStageDto.Posted,
        AssetAccountingLifecycleStageDto.Reconciled,
        AssetAccountingLifecycleStageDto.Reported
    ];

    public static IReadOnlyList<string> Validate(AssetAccountingEventSpineDto? spine)
    {
        if (spine is null)
        {
            return ["Asset accounting event spine is required."];
        }

        var issues = new List<string>();
        if (spine.EventId == Guid.Empty)
            issues.Add("Asset accounting event id is required.");
        if (spine.EventVersion <= 0)
            issues.Add("Asset accounting event version must be positive.");
        if (spine.SpineVersion <= 0)
            issues.Add("Asset accounting spine version must be positive.");
        if (spine.Scope.SecurityId == Guid.Empty || spine.Scope.BookPositionId == Guid.Empty)
            issues.Add("Asset accounting event requires Security Master and book-position identity.");
        if (spine.Scope.ExpectedSecurityVersion <= 0)
            issues.Add("Asset accounting event requires a positive expected Security Master version.");
        if (spine.Scope.ExpectedBookPositionVersion <= 0)
            issues.Add("Asset accounting event requires a positive expected book-position version.");
        if (spine.Scope.LedgerBookId == Guid.Empty || spine.Scope.PeriodId == Guid.Empty)
            issues.Add("Asset accounting event requires ledger-book and period identity.");
        if (string.IsNullOrWhiteSpace(spine.Scope.FundProfileId))
            issues.Add("Asset accounting event requires fund profile scope.");
        if (string.IsNullOrWhiteSpace(spine.Currency))
            issues.Add("Asset accounting event requires currency.");
        if (spine.Correction is { } correction &&
            (correction.EventId == Guid.Empty ||
             correction.EventId == spine.EventId ||
             correction.EventVersion <= 0 ||
             correction.PostedJournalEntryId == Guid.Empty ||
             correction.LedgerBookId == Guid.Empty ||
             correction.PeriodId == Guid.Empty ||
             !Enum.IsDefined(correction.AccountingBasis)))
        {
            issues.Add("Correction lineage requires a different event id and its exact original posted journal, book, period, and accounting basis.");
        }
        if (spine.EconomicEvent.EventId != spine.EventId ||
            spine.EconomicEvent.EventVersion != spine.EventVersion ||
            !string.Equals(
                spine.EconomicEvent.EventType,
                AssetAccountingEventTypeNames.For(spine.EventKind),
                StringComparison.Ordinal) ||
            spine.EconomicEvent.SecurityId != spine.Scope.SecurityId ||
            spine.EconomicEvent.BookPositionId != spine.Scope.BookPositionId ||
            spine.EconomicEvent.EffectiveDate != spine.EffectiveDate)
        {
            issues.Add("Economic-event identity, type, version, date, security, and position must match the spine.");
        }
        if (spine.ProjectionLineage.TriggerEvent.EventId != spine.EventId ||
            spine.ProjectionLineage.TriggerEvent.EventVersion != spine.EventVersion ||
            spine.ProjectionLineage.BookPositionId != spine.Scope.BookPositionId)
        {
            issues.Add("Projection lineage must match the spine event version and book position.");
        }
        if (!EconomicEventEquals(spine.ProjectionLineage.TriggerEvent, spine.EconomicEvent))
            issues.Add("Projection lineage trigger event must exactly match the canonical economic event.");

        if (spine.RetainedEvidence.Count == 0)
        {
            issues.Add("Asset accounting event requires verified retained evidence.");
        }
        else
        {
            AddDuplicateEvidenceIssues(spine.RetainedEvidence, "Retained event evidence", issues);
            foreach (var evidence in spine.RetainedEvidence)
            {
                issues.AddRange(RetainedEvidenceIdentityValidator.Validate(evidence));
                if (evidence.EffectiveDate != spine.EffectiveDate)
                    issues.Add("Retained evidence effective date must match the asset accounting event.");
            }


            if (!spine.RetainedEvidence.Any(evidence =>
                    string.Equals(evidence.SubjectType, AssetAccountingEvidenceSubjects.Event, StringComparison.Ordinal) &&
                    string.Equals(evidence.SubjectId, spine.EventId.ToString("D"), StringComparison.Ordinal)))
            {
                issues.Add("Asset accounting event requires retained evidence with an exact typed event subject binding.");
            }

            if (string.IsNullOrWhiteSpace(spine.EconomicEvent.SourceContentHash) ||
                !IsSha256(spine.EconomicEvent.SourceContentHash) ||
                !spine.RetainedEvidence.Any(evidence =>
                    string.Equals(evidence.SubjectType, AssetAccountingEvidenceSubjects.Event, StringComparison.Ordinal) &&
                    string.Equals(evidence.SubjectId, spine.EventId.ToString("D"), StringComparison.Ordinal) &&
                    string.Equals(evidence.ContentHashSha256, spine.EconomicEvent.SourceContentHash, StringComparison.OrdinalIgnoreCase) &&
                    (string.IsNullOrWhiteSpace(spine.EconomicEvent.SourceEntityId) ||
                     string.Equals(evidence.SourceReference, spine.EconomicEvent.SourceEntityId, StringComparison.Ordinal))))
            {
                issues.Add("Retained event evidence must exactly bind the economic-event source hash and source entity.");
            }
        }

        var duplicateStages = spine.Stages
            .GroupBy(static stage => stage.Stage)
            .Where(static group => group.Count() > 1)
            .Select(static group => group.Key)
            .ToArray();
        if (duplicateStages.Length > 0)
            issues.Add($"Lifecycle stages are independently attested once; duplicates found: {string.Join(", ", duplicateStages)}.");

        if (spine.Stages.Count == 0 || spine.Stages.Count > LifecycleOrder.Length)
        {
            issues.Add("Asset accounting lifecycle requires at least the Expected attestation and cannot exceed the canonical stage sequence.");
        }
        else
        {
            for (var index = 0; index < spine.Stages.Count; index++)
            {
                if (spine.Stages[index].Stage != LifecycleOrder[index])
                {
                    issues.Add("Asset accounting lifecycle stages must be the ordered contiguous Expected-to-Reported prefix.");
                    break;
                }

                if (index > 0 && spine.Stages[index].AttestedAtUtc < spine.Stages[index - 1].AttestedAtUtc)
                {
                    issues.Add("Asset accounting lifecycle attestation timestamps must be nondecreasing.");
                    break;
                }
            }
        }

        var approvedStage = spine.Stages.FirstOrDefault(static stage =>
            stage.Stage == AssetAccountingLifecycleStageDto.Approved);
        foreach (var stage in spine.Stages)
        {
            if (string.IsNullOrWhiteSpace(stage.AttestedBy) ||
                stage.AttestedAtUtc == default ||
                stage.AttestedAtUtc.Offset != TimeSpan.Zero)
                issues.Add($"{stage.Stage} stage requires an attestation actor and non-default UTC timestamp.");
            if (string.IsNullOrWhiteSpace(stage.ReferenceId))
                issues.Add($"{stage.Stage} stage requires a retained reference id.");
            AddDuplicateEvidenceIssues(stage.Evidence, $"{stage.Stage} stage evidence", issues);
            if (stage.Evidence.Count == 0 || stage.Evidence.Any(evidence => !RetainedEvidenceIdentityValidator.IsComplete(evidence)))
                issues.Add($"{stage.Stage} stage requires complete retained evidence.");
            if (stage.Evidence.Any(evidence =>
                    evidence.ReviewedAtUtc > stage.AttestedAtUtc ||
                    evidence.RetainedAtUtc > stage.AttestedAtUtc))
                issues.Add($"{stage.Stage} stage cannot attest evidence before it was reviewed and retained.");
            if (stage.Stage is AssetAccountingLifecycleStageDto.Expected or
                AssetAccountingLifecycleStageDto.Projected or
                AssetAccountingLifecycleStageDto.Drafted &&
                stage.Evidence.Any(evidence => !spine.RetainedEvidence.Contains(evidence)))
            {
                issues.Add($"{stage.Stage} stage evidence must exactly match an immutable retained event evidence identity.");
            }

            var expectedSubjectType = stage.Stage switch
            {
                AssetAccountingLifecycleStageDto.Expected or
                AssetAccountingLifecycleStageDto.Projected or
                AssetAccountingLifecycleStageDto.Drafted => AssetAccountingEvidenceSubjects.Event,
                AssetAccountingLifecycleStageDto.Approved or
                AssetAccountingLifecycleStageDto.Posted => AssetAccountingEvidenceSubjects.PostingApproval,
                AssetAccountingLifecycleStageDto.Reconciled => AssetAccountingEvidenceSubjects.Reconciliation,
                AssetAccountingLifecycleStageDto.Reported => AssetAccountingEvidenceSubjects.Report,
                _ => string.Empty
            };
            var expectedSubjectId = stage.Stage switch
            {
                AssetAccountingLifecycleStageDto.Expected or
                AssetAccountingLifecycleStageDto.Projected or
                AssetAccountingLifecycleStageDto.Drafted => spine.EventId.ToString("D"),
                AssetAccountingLifecycleStageDto.Approved or
                AssetAccountingLifecycleStageDto.Posted => BuildExpectedPostingApprovalSubjectId(
                    spine,
                    approvedStage),
                AssetAccountingLifecycleStageDto.Reconciled or
                AssetAccountingLifecycleStageDto.Reported => AssetAccountingEvidenceSubjects.LifecycleSubjectId(
                    spine.EventId,
                    spine.Scope.FundProfileId,
                    spine.Scope.LedgerBookId,
                    spine.Scope.TenantId,
                    spine.Scope.CompanyId,
                    stage.ReferenceId ?? string.Empty),
                _ => string.Empty
            };
            var exactSubjectEvidence = stage.Evidence.Where(evidence =>
                    string.Equals(evidence.SubjectType, expectedSubjectType, StringComparison.Ordinal))
                .ToArray();
            if (!exactSubjectEvidence.Any(evidence =>
                    string.Equals(evidence.SubjectId, expectedSubjectId, StringComparison.Ordinal)) ||
                (stage.Stage == AssetAccountingLifecycleStageDto.Approved &&
                 (exactSubjectEvidence.Length != stage.Evidence.Count ||
                  exactSubjectEvidence.Any(evidence =>
                      !string.Equals(evidence.SubjectId, expectedSubjectId, StringComparison.Ordinal)))) ||
                (stage.Stage == AssetAccountingLifecycleStageDto.Posted &&
                 exactSubjectEvidence.Any(evidence =>
                     !string.Equals(evidence.SubjectId, expectedSubjectId, StringComparison.Ordinal))))
                issues.Add($"{stage.Stage} stage requires exact typed evidence bound to its event, owner scope, book, and lifecycle reference.");
        }

        var hasProjectedStage = spine.Stages.Any(static stage => stage.Stage == AssetAccountingLifecycleStageDto.Projected);
        if (hasProjectedStage != (spine.ProjectedEffect is not null))
            issues.Add("Projected stage and projected accounting effect must either both be present or both be absent.");
        if (spine.ProjectedEffect is { } projected)
        {
            if (projected.ProjectionRunId != spine.ProjectionLineage.ProjectionRunId ||
                !string.Equals(projected.ModelKey, spine.ProjectionLineage.ModelKey, StringComparison.Ordinal) ||
                !string.Equals(projected.ModelVersion, spine.ProjectionLineage.ModelVersion, StringComparison.Ordinal) ||
                projected.ProjectionAsOfDate != spine.ProjectionLineage.ProjectionAsOfDate)
            {
                issues.Add("Projected accounting effect must match the authoritative projection lineage.");
            }
            if (!projected.IsBalanced || projected.Lines.Count == 0)
                issues.Add("Projected accounting effect requires balanced projected lines.");
            if (projected.Lines.Any(static line =>
                    string.IsNullOrWhiteSpace(line.AccountId) ||
                    line.Debit < 0m ||
                    line.Credit < 0m ||
                    (line.Debit == 0m) == (line.Credit == 0m) ||
                    string.IsNullOrWhiteSpace(line.Currency)))
                issues.Add("Projected accounting lines require an account, currency, and exactly one positive debit or credit amount.");
            if (projected.Lines.Sum(static line => line.Debit) != projected.TotalDebits ||
                projected.Lines.Sum(static line => line.Credit) != projected.TotalCredits)
                issues.Add("Projected accounting effect totals must equal projected line amounts.");
            if (!string.Equals(projected.Currency, spine.Currency, StringComparison.OrdinalIgnoreCase))
                issues.Add("Projected accounting effect currency must match the spine.");
            if (projected.Lines.Any(line =>
                    !string.Equals(line.Currency, projected.Currency, StringComparison.OrdinalIgnoreCase)))
                issues.Add("Projected accounting line currency must match the projected effect currency.");
        }

        var hasDraftedStage = spine.Stages.Any(static stage => stage.Stage == AssetAccountingLifecycleStageDto.Drafted);
        if (hasDraftedStage != (spine.DraftedCandidate is not null && spine.DraftedCandidateResult is not null))
            issues.Add("Drafted stage and exact retained posting candidate request/result must be present together.");
        if (hasDraftedStage)
        {
            if (spine.DraftedCandidate is { } draftedCandidate)
            {
                if (draftedCandidate.SourceEventId != spine.EventId ||
                    draftedCandidate.AggregateId != spine.Scope.LedgerBookId ||
                    draftedCandidate.LedgerBookId != spine.Scope.LedgerBookId ||
                    draftedCandidate.PeriodId != spine.Scope.PeriodId ||
                    (draftedCandidate.AssetLotMutation is not null) != (spine.DraftedLotMutation is not null) ||
                    !PayloadEquals(draftedCandidate.AssetLotMutation, spine.DraftedLotMutation))
                {
                    issues.Add("Retained Drafted candidate scope and lot mutation must match the immutable spine.");
                }
            }

            if (spine.DraftedCandidate is not null &&
                !string.Equals(
                    CanonicalPayloadFingerprint(spine.DraftedCandidate),
                    spine.DraftedCandidateFingerprint,
                    StringComparison.Ordinal))
                issues.Add("Retained Drafted candidate fingerprint does not match its full canonical posting intent.");
            if (spine.DraftedCandidateResult is not null &&
                !string.Equals(
                    CanonicalPayloadFingerprint(spine.DraftedCandidateResult),
                    spine.DraftedCandidateResultFingerprint,
                    StringComparison.Ordinal))
                issues.Add("Retained Drafted candidate result fingerprint does not match its canonical payload.");

            var expectedLotFingerprint = spine.DraftedLotMutation is null
                ? null
                : AssetLotMutationInstructionValidator.Fingerprint(spine.DraftedLotMutation);
            if (!string.Equals(expectedLotFingerprint, spine.DraftedLotMutationFingerprint, StringComparison.Ordinal))
                issues.Add("Retained Drafted lot mutation fingerprint does not match its canonical payload.");
            issues.AddRange(AssetLotMutationInstructionValidator.Validate(
                spine.EventKind,
                spine.DraftedLotMutation,
                spine.EventAmount,
                spine.EffectiveDate,
                spine.RetainedEvidence));
            if (spine.Correction is { } correctedReference && spine.DraftedLotMutation is { } lotMutation &&
                (lotMutation.CorrectsJournalEntryId != correctedReference.PostedJournalEntryId ||
                 lotMutation.CorrectsMutationBatchId != correctedReference.TaxLotMutationBatchId))
                issues.Add("Drafted lot correction must exactly match the typed corrected journal and mutation batch lineage.");
        }
        else if (spine.DraftedCandidate is not null ||
                 spine.DraftedCandidateResult is not null ||
                 spine.DraftedCandidateFingerprint is not null ||
                 spine.DraftedCandidateResultFingerprint is not null ||
                 spine.DraftedLotMutation is not null ||
                 spine.DraftedLotMutationFingerprint is not null)
        {
            issues.Add("Lot mutation authority may only be retained with the Drafted stage.");
        }

        var hasPostedStage = spine.Stages.Any(static stage => stage.Stage == AssetAccountingLifecycleStageDto.Posted);
        if (hasPostedStage != (spine.PostedJournalImpact is not null))
            issues.Add("Posted stage and durable posted journal impact must either both be present or both be absent.");
        if (spine.PostedJournalImpact is { } posted)
        {
            issues.AddRange(PostedJournalImpactValidator.Validate(posted));
            if (posted.LedgerBookId != spine.Scope.LedgerBookId ||
                posted.PeriodId != spine.Scope.PeriodId ||
                posted.AccountingBasis != spine.Scope.AccountingBasis ||
                !string.Equals(posted.Currency, spine.Currency, StringComparison.OrdinalIgnoreCase))
            {
                issues.Add("Posted journal impact must match the spine book, period, accounting basis, and currency.");
            }
            if (posted.Lines.Any(line => !PostedDimensionsMatchScope(line.Dimensions, spine.Scope)))
                issues.Add("Posted journal impact lines must retain exact fund, book, Security Master, book-position, and canonical dimension scope.");
        }
        if (spine.TaxLotMutationBatchId.HasValue && !hasPostedStage)
            issues.Add("Tax-lot mutation batch identity may only be retained with Posted journal impact.");

        return issues.Distinct(StringComparer.Ordinal).ToArray();
    }

    public static bool IsValid(AssetAccountingEventSpineDto? spine) => Validate(spine).Count == 0;

    private static void AddDuplicateEvidenceIssues(
        IReadOnlyList<RetainedEvidenceIdentityDto> evidence,
        string label,
        ICollection<string> issues)
    {
        if (evidence.GroupBy(static item => item.EvidenceId?.Trim(), StringComparer.OrdinalIgnoreCase)
            .Any(static group => string.IsNullOrWhiteSpace(group.Key) || group.Count() > 1))
        {
            issues.Add($"{label} cannot contain duplicate evidence ids.");
        }
    }

    private static bool EconomicEventEquals(EconomicEventReferenceDto left, EconomicEventReferenceDto right)
        => PayloadEquals(left, right);

    private static bool PayloadEquals<T>(T left, T right)
        => JsonElement.DeepEquals(
            JsonSerializer.SerializeToElement(left),
            JsonSerializer.SerializeToElement(right));

    // An all-zero digest is a placeholder sentinel rather than a real fingerprint, so it is
    // rejected on top of the shared digest contract.
    private static bool IsSha256(string? value)
        => Sha256Digest.IsWellFormed(value)
           && value!.Any(static character => character != '0');

    private static string? BuildExpectedPostingApprovalSubjectId(
        AssetAccountingEventSpineDto spine,
        AssetAccountingStageEvidenceDto? approvedStage)
    {
        if (spine.EventId == Guid.Empty ||
            spine.EventVersion <= 0 ||
            spine.Scope.LedgerBookId == Guid.Empty ||
            spine.Scope.PeriodId == Guid.Empty ||
            !Enum.IsDefined(spine.Scope.AccountingBasis) ||
            string.IsNullOrWhiteSpace(spine.Scope.FundProfileId) ||
            string.IsNullOrWhiteSpace(approvedStage?.ReferenceId) ||
            !IsSha256(spine.DraftedCandidateFingerprint))
        {
            return null;
        }

        return AssetAccountingEvidenceSubjects.PostingApprovalSubjectId(
            spine.EventId,
            spine.EventVersion,
            spine.Scope.FundProfileId,
            spine.Scope.LedgerBookId,
            spine.Scope.PeriodId,
            spine.Scope.AccountingBasis,
            approvedStage.ReferenceId,
            spine.DraftedCandidateFingerprint!,
            spine.Scope.TenantId,
            spine.Scope.CompanyId);
    }

    private static bool PostedDimensionsMatchScope(
        LedgerDimensionSetDto? dimensions,
        AssetAccountingEventScopeDto scope)
    {
        if (dimensions is null ||
            !string.Equals(dimensions.FundId, scope.FundProfileId, StringComparison.Ordinal) ||
            !string.Equals(dimensions.BookId, scope.LedgerBookId.ToString("D"), StringComparison.OrdinalIgnoreCase) ||
            dimensions.InstrumentId != scope.SecurityId ||
            dimensions.PositionId != scope.BookPositionId)
            return false;

        var expected = scope.Dimensions;
        return expected is null ||
               (MatchesOptional(expected.EntityId, dimensions.EntityId) &&
                MatchesOptional(expected.SleeveId, dimensions.SleeveId) &&
                MatchesOptional(expected.StrategyId, dimensions.StrategyId) &&
                MatchesOptional(expected.InvestorId, dimensions.InvestorId) &&
                MatchesOptional(expected.CapitalAccountId, dimensions.CapitalAccountId) &&
                MatchesOptional(expected.TaxLotId, dimensions.TaxLotId) &&
                MatchesOptional(expected.CostCenterId, dimensions.CostCenterId) &&
                MatchesOptional(expected.CounterpartyId, dimensions.CounterpartyId) &&
                MatchesOptional(expected.OrganizationId, dimensions.OrganizationId) &&
                MatchesOptional(expected.PortfolioId, dimensions.PortfolioId) &&
                MatchesOptional(expected.AccountId, dimensions.AccountId) &&
                MatchesOptional(expected.CustomerId, dimensions.CustomerId) &&
                MatchesOptional(expected.VendorId, dimensions.VendorId) &&
                MatchesOptional(expected.ProjectId, dimensions.ProjectId) &&
                expected.ExternalGlDimensions.All(pair =>
                    dimensions.ExternalGlDimensions.TryGetValue(pair.Key, out var actual) &&
                    string.Equals(actual, pair.Value, StringComparison.Ordinal)));
    }

    private static bool MatchesOptional(string? expected, string? actual)
        => string.IsNullOrWhiteSpace(expected) || string.Equals(expected, actual, StringComparison.Ordinal);

    public static string CanonicalPayloadFingerprint<T>(T value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var payload = JsonSerializer.SerializeToUtf8Bytes(value);
        return Sha256Digest.Compute(payload);
    }
}

public static class AssetLotMutationInstructionValidator
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static IReadOnlyList<string> Validate(
        AssetAccountingEventKindDto eventKind,
        AssetLotMutationInstructionDto? instruction,
        decimal eventAmount,
        DateOnly effectiveDate,
        IReadOnlyList<RetainedEvidenceIdentityDto> retainedEvidence)
    {
        var issues = new List<string>();
        if (eventKind == AssetAccountingEventKindDto.Acquisition)
        {
            if (instruction is not { Intent: AssetLotMutationIntentDto.Acquire, Acquisition: { } acquisition } ||
                instruction.DisposalSelections.Count != 0)
            {
                return ["Acquisition events require one complete Acquire lot instruction and no disposal selections."];
            }

            if (acquisition.TaxLotRecordId == Guid.Empty ||
                string.IsNullOrWhiteSpace(acquisition.LotId) ||
                acquisition.Quantity <= 0m || acquisition.UnitCost < 0m ||
                string.IsNullOrWhiteSpace(acquisition.AccountId))
                issues.Add("Acquisition lot identity, quantity, unit cost, and exact asset account are required.");
            if (acquisition.AcquiredDate != effectiveDate)
                issues.Add("Acquisition lot date must equal the accounting event effective date.");
            if (acquisition.Quantity * acquisition.UnitCost != eventAmount)
                issues.Add("Acquisition lot quantity times unit cost must equal the accounting event amount.");
            if (!string.IsNullOrWhiteSpace(instruction.AssetAccountId) &&
                !string.Equals(instruction.AssetAccountId, acquisition.AccountId, StringComparison.Ordinal))
                issues.Add("Acquisition instruction asset account must exactly match the acquisition lot account.");
        }
        else if (eventKind == AssetAccountingEventKindDto.Disposal)
        {
            if (instruction is not { Intent: AssetLotMutationIntentDto.Dispose } ||
                instruction.Acquisition is not null || instruction.DisposalSelections.Count == 0 ||
                string.IsNullOrWhiteSpace(instruction.ReliefMethod) ||
                string.IsNullOrWhiteSpace(instruction.PolicyRevision) ||
                string.IsNullOrWhiteSpace(instruction.AssetAccountId))
            {
                return ["Disposal events require a complete Dispose lot instruction with relief policy and versioned selections."];
            }

            var ordinals = new HashSet<int>();
            var lotIds = new HashSet<Guid>();
            var evidenceIds = retainedEvidence.Select(static item => item.EvidenceId)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var selection in instruction.DisposalSelections)
            {
                if (selection.TaxLotRecordId == Guid.Empty || !lotIds.Add(selection.TaxLotRecordId) ||
                    string.IsNullOrWhiteSpace(selection.LotId) || selection.ExpectedVersion <= 0 ||
                    selection.ExpectedOpenQuantity <= 0m || selection.Quantity <= 0m ||
                    selection.Quantity > selection.ExpectedOpenQuantity ||
                    selection.SelectionOrdinal < 0 || !ordinals.Add(selection.SelectionOrdinal) ||
                    string.IsNullOrWhiteSpace(selection.SelectionEvidenceId) ||
                    !evidenceIds.Contains(selection.SelectionEvidenceId) ||
                    selection.ExpectedUnitCost <= 0m ||
                    selection.ExpectedCostBasis <= 0m ||
                    selection.ExpectedCostBasis != selection.Quantity * selection.ExpectedUnitCost)
                {
                    issues.Add("Disposal selections require unique authoritative lot identity/version/order, retained evidence, and exact expected cost basis.");
                    break;
                }
            }
        }
        else if (instruction is not null)
        {
            issues.Add($"{eventKind} events cannot carry acquisition or disposal lot mutations.");
        }

        var correctionFields = new object?[]
        {
            instruction?.CorrectsMutationBatchId,
            instruction?.CorrectsJournalEntryId,
            instruction?.CorrectionApproval
        };
        var populatedCorrectionFields = correctionFields.Count(static value => value is not null);
        if (populatedCorrectionFields is > 0 and < 3)
            issues.Add("Lot corrections require the exact corrected mutation batch, corrected journal, and approved correction metadata together.");
        if (instruction?.CorrectionApproval is { } approval &&
            (approval.Status != LedgerAdjustmentApprovalStatusDto.Approved ||
             string.IsNullOrWhiteSpace(approval.ApprovalId) ||
             string.IsNullOrWhiteSpace(approval.ApprovedBy) ||
             approval.ApprovedAt == default || approval.ApprovedAt.Offset != TimeSpan.Zero ||
             string.IsNullOrWhiteSpace(approval.ReasonCode)))
        {
            issues.Add("Lot correction approval metadata must be complete, approved, and UTC.");
        }

        return issues.Distinct(StringComparer.Ordinal).ToArray();
    }

    public static string Fingerprint(AssetLotMutationInstructionDto instruction)
    {
        ArgumentNullException.ThrowIfNull(instruction);
        var payload = JsonSerializer.SerializeToUtf8Bytes(instruction, JsonOptions);
        return Sha256Digest.Compute(payload);
    }
}
