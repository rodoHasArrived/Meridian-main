using Meridian.Contracts.Ledger;

namespace Meridian.Storage.Ledger;

public interface IConsolidationTransitionStore
{
    Task<IReadOnlyList<ConsolidationTransitionReceiptDto>> ListAsync(Guid organizationId, Guid ownershipRootId, CancellationToken ct = default);
    Task<ConsolidationTransitionReceiptDto?> GetAsync(Guid transitionId, CancellationToken ct = default);
    Task<ConsolidationTransitionPreviewDto> RetainPreviewAsync(ConsolidationTransitionPreviewDto preview, CancellationToken ct = default);
    Task<ConsolidationTransitionPreviewDto?> GetPreviewAsync(Guid previewId, CancellationToken ct = default);
    Task<ConsolidationTransitionReceiptDto> AppendAsync(ConsolidationTransitionReceiptDto receipt, CancellationToken ct = default);
    /// <summary>Runs read-only final source revalidation under the exclusive route/append fence.</summary>
    /// <remarks>The caller retains domain leases; the callback must not publish another preview or approval.</remarks>
    Task<ConsolidationTransitionReceiptDto> AppendValidatedAsync(ConsolidationTransitionReceiptDto receipt,
        Func<CancellationToken, Task> revalidate, CancellationToken ct = default);
    /// <summary>Acquires book mapping authority before domain leases, then exclusive routing and append authority.</summary>
    /// <remarks>The store retains the returned domain lease through its durable commit.</remarks>
    Task<ConsolidationTransitionReceiptDto> AppendValidatedWithAuthorityAsync(ConsolidationTransitionReceiptDto receipt,
        Func<CancellationToken, Task<IAsyncDisposable>> acquireAuthority,
        Func<CancellationToken, Task> revalidate, CancellationToken ct = default);
    /// <summary>Hold through the consumer's ledger commit; route writers retain the exclusive side.</summary>
    Task<IAsyncDisposable> AcquireAuthorityLeaseAsync(CancellationToken ct = default);
}
