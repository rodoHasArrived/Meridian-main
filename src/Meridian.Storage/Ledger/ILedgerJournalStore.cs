using Meridian.Ledger;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Npgsql;

namespace Meridian.Storage.Ledger;

public interface ILedgerJournalStore
{
    Task AppendAsync(LedgerJournalEntryWrite entry, CancellationToken ct = default);

    Task<IReadOnlyList<LedgerJournalEntryRecord>> GetByPeriodAsync(Guid periodId, CancellationToken ct = default);

    Task<IReadOnlyList<LedgerJournalEntryRecord>> GetByAggregateAsync(Guid aggregateId, CancellationToken ct = default);

    Task<LedgerAccountingPeriod?> GetPeriodAsync(Guid periodId, CancellationToken ct = default);

    Task<IReadOnlyList<LedgerAccountingPeriod>> ListPeriodsAsync(
        Guid? ledgerBookId = null,
        string? status = null,
        string? fundProfileId = null,
        Guid? fundStructureNodeId = null,
        CancellationToken ct = default);

    Task<LedgerAccountingPeriod> SavePeriodAsync(
        LedgerAccountingPeriod period,
        long expectedVersion,
        PeriodCloseEventRecord? closeEvent = null,
        CancellationToken ct = default);

    Task<LedgerBookRecord?> GetLedgerBookAsync(Guid ledgerBookId, CancellationToken ct = default);

    Task<IReadOnlyList<LedgerBookRecord>> ListLedgerBooksAsync(
        string? fundProfileId = null,
        Guid? fundStructureNodeId = null,
        FundStructureNodeKindDto? fundStructureNodeKind = null,
        CancellationToken ct = default);

    Task<LedgerBookRecord> SaveLedgerBookAsync(LedgerBookRecord book, CancellationToken ct = default);
}

public interface ITransactionalLedgerJournalStore : ILedgerJournalStore
{
    Task AppendAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        LedgerJournalEntryWrite entry,
        CancellationToken ct = default);
}

public sealed record LedgerJournalEntryWrite(
    JournalEntry Entry,
    Guid AggregateId,
    Guid PeriodId,
    Guid? CommandId = null,
    Guid? CorrelationId = null,
    AccountingBasisKindDto AccountingBasis = AccountingBasisKindDto.Primary,
    string AccountingPolicyId = "legacy-v1",
    string AccountingPolicyVersion = "legacy-v1",
    string? RuleId = null,
    string? RuleVersion = null,
    Guid? SourceEventId = null,
    Guid? SourceJournalEntryId = null,
    LedgerPostingKindDto PostingKind = LedgerPostingKindDto.Originating);

public sealed record LedgerJournalEntryRecord(
    JournalEntry Entry,
    Guid AggregateId,
    Guid PeriodId,
    Guid? CommandId,
    Guid? CorrelationId,
    long GlobalSequence,
    DateTimeOffset CreatedAt,
    AccountingBasisKindDto AccountingBasis = AccountingBasisKindDto.Primary,
    string AccountingPolicyId = "legacy-v1",
    string AccountingPolicyVersion = "legacy-v1",
    string? RuleId = null,
    string? RuleVersion = null,
    Guid? SourceEventId = null,
    Guid? SourceJournalEntryId = null,
    LedgerPostingKindDto PostingKind = LedgerPostingKindDto.Originating);

public sealed record LedgerAccountingPeriod(
    Guid PeriodId,
    Guid? LedgerBookId,
    int FiscalYear,
    int PeriodNo,
    string Label,
    DateOnly StartDate,
    DateOnly EndDate,
    string Status,
    DateTimeOffset OpenedAt,
    DateTimeOffset? ClosedAt,
    long Version);

public sealed record LedgerBookRecord(
    Guid LedgerBookId,
    string FundProfileId,
    Guid FundStructureNodeId,
    FundStructureNodeKindDto FundStructureNodeKind,
    string DisplayName,
    string BaseCurrency,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string? Description = null,
    AccountingBasisKindDto AccountingBasis = AccountingBasisKindDto.Primary,
    string AccountingPolicyId = "legacy-v1",
    string AccountingPolicyVersion = "legacy-v1");

public sealed record PeriodCloseEventRecord(
    Guid EventId,
    Guid PeriodId,
    string PriorStatus,
    string NewStatus,
    string ClosedBy,
    string Notes,
    DateTimeOffset RecordedAt);
