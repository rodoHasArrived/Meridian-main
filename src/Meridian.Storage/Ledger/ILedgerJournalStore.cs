using Meridian.Ledger;

namespace Meridian.Storage.Ledger;

public interface ILedgerJournalStore
{
    Task AppendAsync(LedgerJournalEntryWrite entry, CancellationToken ct = default);

    Task<IReadOnlyList<LedgerJournalEntryRecord>> GetByPeriodAsync(Guid periodId, CancellationToken ct = default);

    Task<IReadOnlyList<LedgerJournalEntryRecord>> GetByAggregateAsync(Guid aggregateId, CancellationToken ct = default);

    Task<LedgerAccountingPeriod?> GetPeriodAsync(Guid periodId, CancellationToken ct = default);

    Task<LedgerAccountingPeriod> SavePeriodAsync(
        LedgerAccountingPeriod period,
        long expectedVersion,
        PeriodCloseEventRecord? closeEvent = null,
        CancellationToken ct = default);
}

public sealed record LedgerJournalEntryWrite(
    JournalEntry Entry,
    Guid AggregateId,
    Guid PeriodId,
    Guid? CommandId = null,
    Guid? CorrelationId = null);

public sealed record LedgerJournalEntryRecord(
    JournalEntry Entry,
    Guid AggregateId,
    Guid PeriodId,
    Guid? CommandId,
    Guid? CorrelationId,
    long GlobalSequence,
    DateTimeOffset CreatedAt);

public sealed record LedgerAccountingPeriod(
    Guid PeriodId,
    int FiscalYear,
    int PeriodNo,
    string Label,
    DateOnly StartDate,
    DateOnly EndDate,
    string Status,
    DateTimeOffset OpenedAt,
    DateTimeOffset? ClosedAt,
    long Version);

public sealed record PeriodCloseEventRecord(
    Guid EventId,
    Guid PeriodId,
    string PriorStatus,
    string NewStatus,
    string ClosedBy,
    string Notes,
    DateTimeOffset RecordedAt);
