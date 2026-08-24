namespace Meridian.Application.SecurityMaster.Rebuild;

/// <summary>
/// Rebuilds UFL projections for a requested asset class.
/// </summary>
public interface IUflProjectionRebuilder
{
    /// <summary>
    /// Requests a UFL rebuild scoped to the provided asset class: only that class's securities are
    /// re-folded from the event stream and upserted into the projection store and cache, so the
    /// rebuild cost stays bounded by the class's population as the class count grows.
    /// </summary>
    Task RebuildAsync(string assetClass, CancellationToken ct = default);
}
