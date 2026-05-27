using Meridian.Contracts.Banking;

namespace Meridian.Storage.Banking;

/// <summary>
/// Persistence contract for the Banking domain.
/// </summary>
public interface IBankingStore
{
    // ── Pending payments ─────────────────────────────────────────────────────

    Task UpsertPendingPaymentAsync(PendingPaymentDto payment, CancellationToken ct = default);
    Task<PendingPaymentDto?> GetPendingPaymentAsync(Guid pendingPaymentId, CancellationToken ct = default);
    Task<IReadOnlyList<PendingPaymentDto>> GetAllPendingPaymentsAsync(CancellationToken ct = default);

    // ── Bank transactions ────────────────────────────────────────────────────

    Task InsertBankTransactionAsync(BankTransactionDto transaction, CancellationToken ct = default);
    Task<IReadOnlyList<BankTransactionDto>> GetBankTransactionsAsync(Guid? entityId = null, CancellationToken ct = default);

    // ── Utility ──────────────────────────────────────────────────────────────

    Task<bool> IsEmptyAsync(CancellationToken ct = default);
}
