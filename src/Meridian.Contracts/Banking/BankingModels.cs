using Meridian.Contracts.Workstation;

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
    string? Notes,
    /// <summary>
    /// Recognized three-letter currency code. This trailing nullable member preserves transport
    /// compatibility with older clients; banking services require it for every new intent.
    /// </summary>
    string? Currency = null);

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
    DateTimeOffset? ReviewedAt,
    /// <summary>
    /// Normalized recognized three-letter currency code. Null identifies a legacy intent that must
    /// be remediated before bank evidence or transfer authorization can proceed.
    /// </summary>
    string? Currency = null,
    /// <summary>Human operator who repaired a legacy missing-currency intent.</summary>
    string? CurrencyRemediatedBy = null,
    /// <summary>Retained explanation for the legacy currency repair.</summary>
    string? CurrencyRemediationReason = null,
    /// <summary>When the legacy currency repair was retained.</summary>
    DateTimeOffset? CurrencyRemediatedAt = null);

/// <summary>
/// Governed repair request for a legacy Pending payment whose currency was never retained.
/// The command is compare-and-set: it cannot alter economics, replace an existing currency, or
/// repair a payment after a review decision.
/// </summary>
public sealed record RemediatePaymentCurrencyRequest(
    string Currency,
    string Reason,
    string? RemediatedBy,
    OperationsActionOriginDto ActionOrigin = OperationsActionOriginDto.HumanOperator);

/// <summary>Approve a pending payment request.</summary>
public sealed record ApprovePaymentRequest(
    string? ReviewNotes,
    string? ReviewedBy,
    OperationsActionOriginDto ActionOrigin = OperationsActionOriginDto.HumanOperator);

/// <summary>Reject a pending payment request.</summary>
public sealed record RejectPaymentRequest(
    string Reason,
    string? ReviewedBy,
    OperationsActionOriginDto ActionOrigin = OperationsActionOriginDto.HumanOperator);

/// <summary>Record retained bank-side evidence for an approved Meridian payment intent.</summary>
public sealed record RecordPaymentBankEvidenceRequest(
    string EvidenceType,
    DateOnly? TransactionDate = null,
    DateOnly? SettlementDate = null,
    decimal? Amount = null,
    string? Currency = null,
    string? ExternalRef = null,
    string? RecordedBy = null,
    OperationsActionOriginDto ActionOrigin = OperationsActionOriginDto.HumanOperator,
    /// <summary>
    /// Caller-stable idempotency key for this bank evidence item. The key is unique within the
    /// pending payment and must be reused only with identical evidence input.
    /// </summary>
    string? EvidenceId = null);

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
    bool IsVoided,
    string? RecordedBy = null,
    /// <summary>Payment intent this evidence proves; null for generic bank transactions.</summary>
    Guid? PendingPaymentId = null,
    /// <summary>Caller-stable evidence id, scoped to <see cref="PendingPaymentId"/>.</summary>
    string? EvidenceId = null,
    /// <summary>SHA-256 hash of the canonical payment-evidence input used for replay checks.</summary>
    string? CanonicalInputHash = null);

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
