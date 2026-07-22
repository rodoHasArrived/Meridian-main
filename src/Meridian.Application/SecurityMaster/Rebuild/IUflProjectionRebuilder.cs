namespace Meridian.Application.SecurityMaster.Rebuild;

/// <summary>
/// Rebuilds UFL projections for a requested asset class.
/// </summary>
public interface IUflProjectionRebuilder
{
    /// <summary>
    /// Requests a UFL rebuild for the provided asset class.
    /// In phase 0 this routes through shared Security Master replay and is not yet asset-class scoped.
    /// </summary>
    Task RebuildAsync(string assetClass, CancellationToken ct = default);
}
