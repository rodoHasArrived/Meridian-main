namespace Meridian.Contracts.Banking;

// ---------------------------------------------------------------------------
// Payment initiation & approval workflow
// ---------------------------------------------------------------------------

/// <summary>Lifecycle status of a payment that is routed through the approval workflow.</summary>
public enum PaymentApprovalStatus : byte
{
    Pending = 0,
    Approved = 1,
    Rejected = 2,
    Cancelled = 3
}

/// <summary>Request body to submit a payment for approval before it is applied.</summary>
public sealed record InitiatePaymentRequest(
    decimal Amount,
    DateOnly EffectiveDate,
    string? ExternalRef,
    string? Notes);

/// <summary>A payment that is awaiting an approval decision.</summary>
public sealed record PendingPaymentDto(
    Guid PendingPaymentId,
    /// <summary>Opaque entity identifier — e.g. a loan id, account id, or counterparty id.</summary>
    Guid EntityId,
    decimal Amount,
    DateOnly EffectiveDate,
    string? ExternalRef,
    string? Notes,
    PaymentApprovalStatus Status,
    string? ReviewedBy,
    string? ReviewNotes,
    DateTimeOffset InitiatedAt,
    DateTimeOffset? ReviewedAt);

/// <summary>Approve a pending payment request.</summary>
public sealed record ApprovePaymentRequest(
    string? ReviewNotes,
    string? ReviewedBy);

/// <summary>Reject a pending payment request.</summary>
public sealed record RejectPaymentRequest(
    string Reason,
    string? ReviewedBy);

// ---------------------------------------------------------------------------
// Bank transaction records
// ---------------------------------------------------------------------------

/// <summary>
/// A single bank-side transaction record.  This is distinct from a loan-level
/// <c>CashTransactionDto</c> in the DirectLending module; the two may be
/// reconciled but are not the same object.
/// </summary>
public sealed record BankTransactionDto(
    Guid BankTransactionId,
    /// <summary>Opaque entity identifier — e.g. a loan id, account id, or counterparty id.</summary>
    Guid EntityId,
    string TransactionType,
    DateOnly EffectiveDate,
    DateOnly TransactionDate,
    DateOnly SettlementDate,
    decimal Amount,
    string Currency,
    string? ExternalRef,
    DateTimeOffset RecordedAt,
    bool IsVoided);

// ---------------------------------------------------------------------------
// Bank transaction seeding (development / demo use)
// ---------------------------------------------------------------------------

/// <summary>
/// Request to seed representative bank transactions for one or more entities.
/// When <see cref="EntityIds"/> is null or empty, all known entities are seeded.
/// </summary>
public sealed record BankTransactionSeedRequest(
    IReadOnlyList<Guid>? EntityIds,
    int CountPerEntity,
    DateOnly? FromDate,
    DateOnly? ToDate);

/// <summary>Result returned after seeding bank transactions.</summary>
public sealed record BankTransactionSeedResultDto(
    int EntitiesProcessed,
    int TransactionsSeeded,
    IReadOnlyList<Guid> ProcessedEntityIds);

// ---------------------------------------------------------------------------
// Cross-domain read abstraction
// ---------------------------------------------------------------------------

/// <summary>
/// Read-only banking data source for cross-domain integrations such as
/// reconciliation.  Services outside the Banking module can depend on this
/// narrow interface instead of the full <c>IBankingService</c>.
/// </summary>
public interface IBankTransactionSource
{
    /// <summary>
    /// Return bank transactions.  When <paramref name="entityId"/> is provided
    /// the result is scoped to that entity; otherwise all transactions are returned.
    /// </summary>
    Task<IReadOnlyList<BankTransactionDto>> GetBankTransactionsAsync(
        Guid? entityId = null,
        CancellationToken ct = default);
}
