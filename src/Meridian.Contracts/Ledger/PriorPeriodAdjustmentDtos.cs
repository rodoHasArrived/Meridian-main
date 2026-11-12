namespace Meridian.Contracts.Ledger;

/// <summary>Server-retained authority for a new review draft; it does not reopen its source period.</summary>
public sealed record PriorPeriodAdjustmentSourceDto(
    Guid SourceJournalEntryId,
    Guid SourcePeriodId,
    DateOnly OriginalEffectiveDate,
    Guid TargetPeriodId,
    DateOnly AccountingDate,
    string IntentId,
    string Reason,
    bool ReversePeriodEndMark,
    IReadOnlyList<ManualJournalEntryLineDto>? ExpectedLines = null)
{
    public IReadOnlyList<ManualJournalEntryLineDto> ExpectedLines { get; init; } = ExpectedLines ?? [];
}
