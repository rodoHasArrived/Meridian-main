using System.Text.Json.Serialization;

namespace Meridian.Storage.Ledger;

internal sealed record LedgerBalanceCheckpointRow(
    string AccountName,
    string AccountType,
    string? Symbol,
    string? FinancialAccountId,
    string? DimensionsJson,
    decimal Debit,
    decimal Credit,
    long EntryCount);

internal sealed record LedgerBalanceCheckpointContent(
    int SchemaVersion,
    Guid CheckpointId,
    Guid CloseEventId,
    Guid LedgerBookId,
    Guid PeriodId,
    long PeriodVersion,
    string? TenantId,
    DateTimeOffset AsOfUtc,
    long SourceSequence,
    long? FutureAuditSequence,
    long JournalCount,
    long LineCount,
    DateTimeOffset CreatedAtUtc,
    IReadOnlyList<LedgerBalanceCheckpointRow> Rows);

[JsonSerializable(typeof(LedgerBalanceCheckpointContent))]
internal sealed partial class LedgerBalanceCheckpointJsonContext : JsonSerializerContext;
