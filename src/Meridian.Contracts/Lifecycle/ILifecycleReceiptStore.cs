namespace Meridian.Contracts.Lifecycle;

/// <summary>
/// Durable boundary for host and combined lifecycle receipts.
/// </summary>
public interface ILifecycleReceiptStore
{
    ValueTask WriteHostReceiptAsync(
        LifecycleShutdownReceiptDto receipt,
        CancellationToken ct = default);

    ValueTask<LifecycleShutdownReceiptDto?> ReadLatestHostReceiptAsync(
        CancellationToken ct = default);

    ValueTask WriteSessionReceiptAsync(
        LifecycleSessionReceiptDto receipt,
        CancellationToken ct = default);
}
