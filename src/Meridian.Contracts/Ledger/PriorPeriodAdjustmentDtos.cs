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

    /// <summary>
    /// Server-retained immediate predecessor of a correction to an already posted adjustment.
    /// The original closed-period source above remains the root of the accounting lineage.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public PriorPeriodAdjustmentCorrectionDto? Correction { get; init; }
}

public sealed record PriorPeriodAdjustmentCorrectionDto(
    Guid JournalEntryId,
    bool ReverseSides,
    string Reason);
