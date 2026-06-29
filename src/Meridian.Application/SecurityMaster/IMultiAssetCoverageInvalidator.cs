using Meridian.Contracts.Workstation;

namespace Meridian.Application.SecurityMaster;

/// <summary>
/// Evicts any cached multi-asset coverage / operational-readiness view affected by a published
/// Security Master revision, so the next read recomputes against the refreshed projection.
///
/// <para>The coverage read path (<c>MultiAssetCoverageReadService</c>) is currently recomputed per
/// request rather than cached, so the default <see cref="NullMultiAssetCoverageInvalidator"/> is a
/// no-op; a real eviction lands when a coverage cache is introduced. The seam exists now so the
/// ordered published-revision fan-out has a stable place to evict from.</para>
/// </summary>
public interface IMultiAssetCoverageInvalidator
{
    /// <summary>Invalidates cached coverage for the security/fund implicated by <paramref name="evt"/>.</summary>
    Task InvalidateAsync(SecurityMasterRevisionPublishedEvent evt, CancellationToken ct = default);
}

/// <summary>
/// No-op <see cref="IMultiAssetCoverageInvalidator"/> default for the currently-uncached coverage read
/// path. Replace with a cache-backed implementation when multi-asset coverage gains a cache.
/// </summary>
public sealed class NullMultiAssetCoverageInvalidator : IMultiAssetCoverageInvalidator
{
    public Task InvalidateAsync(SecurityMasterRevisionPublishedEvent evt, CancellationToken ct = default)
        => Task.CompletedTask;
}
