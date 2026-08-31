using Meridian.Core.Exceptions;
using Meridian.ReferenceData.SecurityMaster;
using Microsoft.Extensions.Logging;

namespace Meridian.Application.SecurityMaster.Rebuild;

/// <summary>
/// Routes UFL projection rebuild requests through the shared Security Master rebuild pipeline.
/// </summary>
public sealed class UflProjectionRebuilder : IUflProjectionRebuilder
{
    private readonly SecurityMasterRebuildOrchestrator _orchestrator;
    private readonly ILogger<UflProjectionRebuilder> _logger;

    public UflProjectionRebuilder(
        SecurityMasterRebuildOrchestrator orchestrator,
        ILogger<UflProjectionRebuilder> logger)
    {
        _orchestrator = orchestrator;
        _logger = logger;
    }

    public async Task RebuildAsync(string assetClass, CancellationToken ct = default)
    {
        if (!SecurityKindMapping.TryNormalizeAssetClass(assetClass, out var normalizedAssetClass))
        {
            throw new UnsupportedAssetClassException(assetClass);
        }

        _logger.LogInformation(
            "Rebuilding UFL projections scoped to asset class {AssetClass}.",
            normalizedAssetClass);

        // The signature's promise is now delivered: only the requested class's securities are
        // re-folded, so the rebuild cost stays bounded by that class's population instead of a
        // full shared replay (the Phase-0 behavior this replaced).
        await _orchestrator.RebuildAssetClassAsync(normalizedAssetClass, ct).ConfigureAwait(false);
    }
}

/// <summary>
/// No-op projection rebuilder used when Security Master backing storage is not configured.
/// </summary>
public sealed class NullUflProjectionRebuilder : IUflProjectionRebuilder
{
    private readonly ILogger<NullUflProjectionRebuilder> _logger;

    public NullUflProjectionRebuilder(ILogger<NullUflProjectionRebuilder> logger)
    {
        _logger = logger;
    }

    public Task RebuildAsync(string assetClass, CancellationToken ct = default)
    {
        _logger.LogWarning(
            "Ignoring UFL projection rebuild request for asset class {AssetClass} because Security Master storage is not configured.",
            assetClass);
        return Task.CompletedTask;
    }
}
