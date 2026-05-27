namespace Meridian.Domain.Reconciliation;

public enum ReconciliationSourceType
{
    Prime,
    Custodian,
    Administrator,
    InternalLedger
}

public enum BreakClassification
{
    Matched,
    MatchedWithinTolerance,
    PotentialBreak,
    TrueBreak
}

public sealed record ReconciliationRun(
    Guid RunId,
    DateOnly BusinessDate,
    DateTimeOffset RunTimestampUtc,
    bool IsRerun,
    int SnapshotVersion,
    string IdempotencyKey,
    IReadOnlyList<DataSourceSnapshot> SourceSnapshots);

public sealed record DataSourceSnapshot(
    string SnapshotId,
    ReconciliationSourceType SourceType,
    DateTimeOffset CapturedAtUtc,
    string Version,
    IReadOnlyList<NormalizedPosition> Positions,
    IReadOnlyList<NormalizedCashEntry> CashEntries);

public sealed record NormalizedPosition(
    string PositionId,
    string InstrumentCanonicalId,
    string? Cusip,
    string? Isin,
    string? Ticker,
    string? InternalSecurityId,
    decimal Quantity,
    decimal Price,
    decimal MarketValue,
    string Currency,
    DateTimeOffset AsOfUtc,
    string SourceReference);

public sealed record NormalizedCashEntry(
    string CashEntryId,
    string AccountId,
    decimal Amount,
    string Currency,
    decimal AmountBase,
    string BaseCurrency,
    DateTimeOffset PostedAtUtc,
    DateOnly AccountingPeriod,
    string SourceReference,
    string? CounterpartyReference = null,
    string? SettlementId = null,
    string? Comment = null);

public sealed record MatchGroup(
    string MatchGroupId,
    BreakClassification Classification,
    string RuleId,
    IReadOnlyList<string> PositionIds,
    IReadOnlyList<string> CashEntryIds,
    IReadOnlyList<string> EvidenceIds);

public sealed record BreakRecord(
    string BreakId,
    BreakClassification Classification,
    string RuleId,
    string Reason,
    IReadOnlyList<string> SnapshotIds,
    IReadOnlyList<string> EvidenceIds);

public sealed record MatchEvidence(
    string EvidenceId,
    string RuleId,
    string Stage,
    string Narrative,
    decimal? NumericDelta,
    IReadOnlyDictionary<string, string> Attributes);
