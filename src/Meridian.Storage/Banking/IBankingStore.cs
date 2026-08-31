using Meridian.Contracts.Banking;

namespace Meridian.Storage.Banking;

/// <summary>
/// Persistence contract for the Banking domain.
/// </summary>
public interface IBankingStore
{
    // ── Pending payments ─────────────────────────────────────────────────────

    /// <summary>
    /// Inserts a pending-payment intent idempotently. Implementations must never use this method
    /// to alter retained economics or a review decision after the identifier has been claimed.
    /// </summary>
    Task UpsertPendingPaymentAsync(PendingPaymentDto payment, CancellationToken ct = default);
    Task<PendingPaymentDto?> GetPendingPaymentAsync(Guid pendingPaymentId, CancellationToken ct = default);
    Task<IReadOnlyList<PendingPaymentDto>> GetAllPendingPaymentsAsync(CancellationToken ct = default);
    /// <summary>
    /// Repairs currency only when the retained payment is still Pending and its currency is null.
    /// Implementations must retain the supplied operator, reason, and timestamp atomically.
    /// </summary>
    Task<PendingPaymentDto?> TryRemediatePendingPaymentCurrencyAsync(
        Guid pendingPaymentId,
        string currency,
        string remediatedBy,
        string remediationReason,
        DateTimeOffset remediatedAt,
        CancellationToken ct = default);
    Task<PendingPaymentDto?> TryTransitionPendingPaymentAsync(
        Guid pendingPaymentId,
        PaymentApprovalStatus targetStatus,
        string? reviewedBy,
        string? reviewNotes,
        DateTimeOffset reviewedAt,
        CancellationToken ct = default);

    // ── Bank transactions ────────────────────────────────────────────────────

    Task InsertBankTransactionAsync(BankTransactionDto transaction, CancellationToken ct = default);
    Task<PaymentBankEvidenceWriteResult> RecordPaymentBankEvidenceAsync(
        BankTransactionDto transaction,
        CancellationToken ct = default);
    Task<IReadOnlyList<BankTransactionDto>> GetBankTransactionsAsync(Guid? entityId = null, CancellationToken ct = default);

    // ── Utility ──────────────────────────────────────────────────────────────

    Task<bool> IsEmptyAsync(CancellationToken ct = default);
}

/// <summary>Persistence outcome for an atomic payment-evidence verification and write.</summary>
public enum PaymentBankEvidenceWriteStatus
{
    Inserted,
    Replay,
    PaymentNotFound,
    PaymentNotApproved,
    PaymentCurrencyUnresolved,
    PaymentBindingConflict,
    IdempotencyConflict
}

/// <summary>
/// Result of verifying an approved payment and retaining its bank evidence in one transaction.
/// </summary>
public sealed record PaymentBankEvidenceWriteResult(
    PaymentBankEvidenceWriteStatus Status,
    BankTransactionDto? Transaction = null,
    PaymentApprovalStatus? CurrentPaymentStatus = null);
