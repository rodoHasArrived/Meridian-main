namespace Meridian.Storage.Ledger;

/// <summary>Fences authoritative ledger mutations while a reference-data review transition commits.</summary>
public interface IImpactDependencyGuard
{
    Task<IAsyncDisposable> AcquireImpactDependencyLeaseAsync(string tenantId, string fundProfileId, CancellationToken ct = default);
}
