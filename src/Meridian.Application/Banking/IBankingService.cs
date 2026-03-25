using Meridian.Contracts.Banking;

namespace Meridian.Application.Banking;

/// <summary>
/// Standalone banking service responsible for payment approval workflows and
/// bank-side transaction records.  This service has no dependency on the
/// DirectLending module; the two domains may be integrated at the application
/// composition layer or reconciled asynchronously.
/// </summary>
public interface IBankingService
{
    // -----------------------------------------------------------------------
    // Payment initiation & approval workflow
    // -----------------------------------------------------------------------

    /// <summary>Submit a payment for review. Returns the pending payment record.</summary>
    Task<PendingPaymentDto> InitiatePaymentAsync(Guid entityId, InitiatePaymentRequest request, CancellationToken ct = default);

    /// <summary>Approve a pending payment. Records a bank transaction.</summary>
    Task<PendingPaymentDto?> ApprovePaymentAsync(Guid pendingPaymentId, ApprovePaymentRequest request, CancellationToken ct = default);

    /// <summary>Reject a pending payment without recording a bank transaction.</summary>
    Task<PendingPaymentDto?> RejectPaymentAsync(Guid pendingPaymentId, RejectPaymentRequest request, CancellationToken ct = default);

    /// <summary>
    /// Get all pending payments. When <paramref name="entityId"/> is provided, results are
    /// scoped to that entity; otherwise all pending payments are returned.
    /// </summary>
    Task<IReadOnlyList<PendingPaymentDto>> GetPendingPaymentsAsync(Guid? entityId = null, CancellationToken ct = default);

    // -----------------------------------------------------------------------
    // Bank transaction records
    // -----------------------------------------------------------------------

    /// <summary>
    /// Get bank transactions. When <paramref name="entityId"/> is provided, results are
    /// scoped to that entity; otherwise all bank transactions are returned.
    /// </summary>
    Task<IReadOnlyList<BankTransactionDto>> GetBankTransactionsAsync(Guid? entityId = null, CancellationToken ct = default);

    // -----------------------------------------------------------------------
    // Bank transaction seeding (development / demo use)
    // -----------------------------------------------------------------------

    /// <summary>
    /// Seed representative bank transactions for one or more entities.
    /// Useful for development, demos, and integration test setup.
    /// </summary>
    Task<BankTransactionSeedResultDto> SeedBankTransactionsAsync(BankTransactionSeedRequest request, CancellationToken ct = default);
}
