using System;
using System.Collections.Generic;

namespace Meridian.Contracts.Domain.Reconciliation;

public enum ReconciliationSourceType
{
    Prime,
    Custodian,
    Administrator,
    InternalLedger
}

public enum ReconciliationOutcome
{
    Matched,
    MatchedWithinTolerance,
    PotentialBreak,
    TrueBreak
}

public sealed record ReconciliationRun(
    Guid RunId,
    DateTimeOffset RunTimestamp,
    DateOnly AccountingPeriod,
    string SnapshotVersion,
    bool IsRerun,
    string Trigger,
    IReadOnlyList<DataSourceSnapshot> SourceSnapshots);

public sealed record DataSourceSnapshot(
    string SourceId,
    ReconciliationSourceType SourceType,
    string AdapterId,
    DateTimeOffset CapturedAt,
    string Version,
    IReadOnlyList<NormalizedPosition> Positions,
    IReadOnlyList<NormalizedCashEntry> CashEntries);

public sealed record NormalizedPosition(
    string PositionId,
    string InstrumentKey,
    string? Cusip,
    string? Isin,
    string? Ticker,
    string? InternalSecurityId,
    decimal Quantity,
    decimal Price,
    decimal MarketValue,
    string LocalCurrency,
    string BaseCurrency,
    decimal FxRateToBase,
    DateTimeOffset AsOfTimestamp);

public sealed record NormalizedCashEntry(
    string CashEntryId,
    string AccountId,
    string? CounterpartyReference,
    string? SettlementId,
    string? Comments,
    decimal Amount,
    string LocalCurrency,
    string BaseCurrency,
    decimal FxRateToBase,
    decimal BaseAmount,
    DateTimeOffset BookingTimestamp,
    DateOnly AccountingPeriod);

public sealed record MatchGroup(
    Guid MatchGroupId,
    ReconciliationOutcome Outcome,
    string Stage,
    IReadOnlyList<string> RuleIds,
    IReadOnlyDictionary<string, string> Evidence,
    IReadOnlyList<NormalizedPosition> Positions,
    IReadOnlyList<NormalizedCashEntry> CashEntries,
    DateTimeOffset EvaluatedAt);

public sealed record BreakRecord(
    Guid BreakId,
    Guid MatchGroupId,
    ReconciliationOutcome Outcome,
    string BreakCode,
    string Description,
    IReadOnlyList<string> RuleIds,
    IReadOnlyDictionary<string, string> Evidence,
    DateTimeOffset CreatedAt);
