using System.Text.Json.Serialization;
using Meridian.Ledger;
using Meridian.Storage.Ledger;
using Meridian.Contracts.Ledger;

namespace Meridian.Storage.Export;

/// <summary>ADR-014 metadata for lossless journal export audit fields.</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, UseStringEnumConverter = true)]
[JsonSerializable(typeof(JournalEntryMetadata))]
[JsonSerializable(typeof(LedgerLineDimensionSet))]
[JsonSerializable(typeof(LedgerEntryCurrency))]
[JsonSerializable(typeof(LedgerJournalPostingExportMetadata))]
internal partial class LedgerJournalExportJsonContext : JsonSerializerContext;

// The journal and line envelopes have dedicated columns. Avoid repeating an entire multi-line
// journal inside every line's posting metadata (which otherwise grows quadratically).
internal sealed record LedgerJournalPostingExportMetadata(
    Guid AggregateId, Guid PeriodId, Guid? CommandId, Guid? CorrelationId, long GlobalSequence,
    DateTimeOffset CreatedAt, AccountingBasisKindDto AccountingBasis, string AccountingPolicyId,
    string AccountingPolicyVersion, string? RuleId, string? RuleVersion, Guid? SourceEventId,
    Guid? SourceJournalEntryId, LedgerPostingKindDto PostingKind,
    LedgerAdjustmentApprovalMetadataDto? AdjustmentApproval)
{
    public Guid? LedgerBookId { get; init; }

    public static LedgerJournalPostingExportMetadata FromRecord(LedgerJournalEntryRecord record, Guid? ledgerBookId) => new(
        record.AggregateId, record.PeriodId, record.CommandId, record.CorrelationId, record.GlobalSequence,
        record.CreatedAt, record.AccountingBasis, record.AccountingPolicyId, record.AccountingPolicyVersion,
        record.RuleId, record.RuleVersion, record.SourceEventId, record.SourceJournalEntryId,
        record.PostingKind, record.AdjustmentApproval)
    { LedgerBookId = ledgerBookId };
}
