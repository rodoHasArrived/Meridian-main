using Meridian.Contracts.Workstation;
using Microsoft.Extensions.Logging;

namespace Meridian.Application.SecurityMaster;

/// <summary>
/// Published-revision side effect (<c>Order = 20</c>, runs after the projection rebuild): evicts any
/// cached multi-asset coverage / operational-readiness view affected by the edit, via
/// <see cref="IMultiAssetCoverageInvalidator"/>, so the next coverage read recomputes against the
/// freshly-rebuilt projection.
///
/// <para>Idempotent: invalidation is safe to repeat on a retried publish. The default invalidator is a
/// no-op while the coverage read path is recomputed per request (uncached).</para>
/// </summary>
public sealed class CoverageInvalidationHandler : ISecurityMasterRevisionPublishedHandler
{
    private readonly IMultiAssetCoverageInvalidator _invalidator;
    private readonly ILogger<CoverageInvalidationHandler> _logger;

    public CoverageInvalidationHandler(
        IMultiAssetCoverageInvalidator invalidator,
        ILogger<CoverageInvalidationHandler> logger)
    {
        _invalidator = invalidator ?? throw new ArgumentNullException(nameof(invalidator));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public int Order => 20;

    public async Task HandleAsync(SecurityMasterRevisionPublishedEvent evt, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(evt);

        await _invalidator.InvalidateAsync(evt, ct).ConfigureAwait(false);

        _logger.LogInformation(
            "Invalidated multi-asset coverage cache for published security {SecurityId} (revision {RevisionId}).",
            evt.SecurityId, evt.RevisionId);
    }
}
