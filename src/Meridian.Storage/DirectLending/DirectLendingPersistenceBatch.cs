using Meridian.Storage.Ledger;

namespace Meridian.Storage.DirectLending;

public sealed record DirectLendingCashTransactionWrite(
    Guid CashTransactionId,
    string TransactionType,
    DateOnly EffectiveDate,
    DateOnly TransactionDate,
    DateOnly SettlementDate,
    decimal Amount,
    string Currency,
    string? Counterparty,
    string? ExternalRef);

public sealed record DirectLendingPaymentAllocationWrite(
    Guid CashTransactionId,
    int AllocationSequenceNumber,
    string TargetType,
    Guid TargetId,
    decimal AllocatedAmount,
    string AllocationRule);

public sealed record DirectLendingFeeBalanceWrite(
    Guid FeeBalanceId,
    string FeeType,
    DateOnly EffectiveDate,
    decimal OriginalAmount,
    decimal UnpaidAmount,
    string? Note);

public sealed record DirectLendingOutboxMessageWrite(
    string Topic,
    string MessageKey,
    string PayloadJson,
    string? HeadersJson,
    DateTimeOffset OccurredAt,
    DateTimeOffset? VisibleAfter);

/// <summary>An assessment caused by the primary command event, committed in the same transaction.</summary>
public sealed record DirectLendingAdditionalEventWrite(
    Guid EventId,
    string EventType,
    int EventSchemaVersion,
    DateOnly? EffectiveDate,
    string PayloadJson,
    DirectLendingEventWriteMetadata Metadata,
    IReadOnlyList<LedgerJournalEntryWrite>? LedgerJournalEntries,
    IReadOnlyList<DirectLendingOutboxMessageWrite>? OutboxMessages);

public sealed record DirectLendingPersistenceBatch(
    IReadOnlyList<DirectLendingCashTransactionWrite>? CashTransactions,
    IReadOnlyList<DirectLendingPaymentAllocationWrite>? PaymentAllocations,
    IReadOnlyList<DirectLendingFeeBalanceWrite>? FeeBalances,
    IReadOnlyList<DirectLendingOutboxMessageWrite>? OutboxMessages)
{
    public IReadOnlyList<DirectLendingAdditionalEventWrite> AdditionalEvents { get; init; } = [];

    public static readonly DirectLendingPersistenceBatch Empty =
        new([], [], [], []);
}
