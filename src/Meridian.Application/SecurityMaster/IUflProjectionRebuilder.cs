namespace Meridian.Application.SecurityMaster;

/// <summary>
/// Rebuilds UFL projections for a requested asset class.
/// </summary>
public interface IUflProjectionRebuilder
{
    Task RebuildAsync(string assetClass, CancellationToken ct = default);
}
