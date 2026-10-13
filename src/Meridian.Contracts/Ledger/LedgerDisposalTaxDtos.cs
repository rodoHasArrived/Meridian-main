namespace Meridian.Contracts.Ledger;

/// <summary>A read of retained disposal evidence. EvaluatedAt is read time, not tax finalization.</summary>
public sealed record LedgerJournalTaxResultsDto(
    Guid LedgerBookId,
    Guid PeriodId,
    Guid JournalEntryId,
    string FunctionalCurrency,
    DateTimeOffset EvaluatedAt,
    string EvidenceState,
    string Message,
    IReadOnlyList<LedgerDisposalTaxResultDto> Disposals);

/// <summary>
/// Server-derived tax results; absent evidence is null rather than a settled zero.
/// Monetary values use invariant plain decimal text so clients retain every significant digit.
/// </summary>
public sealed record LedgerDisposalTaxResultDto(
    Guid MutationBatchId,
    Guid JournalEntryId,
    DateOnly SaleDate,
    string AccountName,
    string? Symbol,
    string ReliefMethod,
    string? PolicyRevision,
    DateTimeOffset? RecordedAt,
    string State,
    string StateReason,
    bool CanChange,
    bool ReEvaluationRequired,
    DateOnly? ReplacementWindowEnd,
    string? Character,
    string? EconomicGainOrLoss,
    string? RecognizedGainOrLoss,
    string? DeferredLoss,
    IReadOnlyList<LedgerDisposalTaxParcelDto> Parcels);

/// <summary>Retained parcel evidence with exact invariant decimal text for quantity and amounts.</summary>
public sealed record LedgerDisposalTaxParcelDto(
    string LotId,
    DateOnly AcquiredDate,
    DateOnly HoldingPeriodStart,
    int HoldingPeriodDays,
    bool HoldingPeriodCarried,
    string Character,
    string Quantity,
    string Proceeds,
    string CostBasis,
    string EconomicGainOrLoss,
    string? RecognizedGainOrLoss,
    string? DeferredLoss);
