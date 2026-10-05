namespace Meridian.Documents;

/// <summary>
/// One durable capacity reservation. The owner must dispose it after publication, cancellation,
/// or failure; an operating-system lease makes abandoned owners distinguishable after restart.
/// </summary>
public sealed class EvidenceStorageReservation : IAsyncDisposable
{
    private readonly EvidenceStorageQuotaCoordinator _coordinator;
    internal FileStream? Lease { get; set; }

    internal EvidenceStorageReservation(EvidenceStorageQuotaCoordinator coordinator, string id, FileStream lease)
    {
        _coordinator = coordinator;
        Id = id;
        Lease = lease;
    }

    /// <summary>Private attempt identifier used to associate staging with this reservation.</summary>
    public string Id { get; }

    /// <summary>
    /// Reserves room for the next write, extending an underestimate before any additional bytes
    /// reach storage. Pair each successful write with <see cref="AfterWriteAsync"/>.
    /// </summary>
    public Task BeforeWriteAsync(long bytes, CancellationToken ct = default) =>
        _coordinator.BeforeWriteAsync(this, bytes, ct);

    /// <summary>Reconciles a completed write so disk accounting charges only unwritten capacity.</summary>
    public Task AfterWriteAsync(long bytes, CancellationToken ct = default) =>
        _coordinator.AfterWriteAsync(this, bytes, ct);

    /// <summary>
    /// Reconciles to actual bytes and publishes under the same cross-process gate as admission.
    /// The callback must publish its readable index last and must not call reservation methods.
    /// </summary>
    public Task PublishAsync(Func<CancellationToken, Task> publish, CancellationToken ct = default) =>
        _coordinator.PublishAsync(this, publish, ct);

    /// <summary>Removes only this attempt's unpublished state before releasing its capacity.</summary>
    public ValueTask DisposeAsync() => new(_coordinator.ReleaseAsync(this));
}
