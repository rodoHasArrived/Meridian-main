using Meridian.Contracts.Treasury;

namespace Meridian.Application.Treasury;

/// <summary>
/// Canonical reference service for money market funds.
/// Provides read access to MMF identity, liquidity projections, sweep profiles,
/// fund-family groupings, and rebuild orchestration.
/// </summary>
public interface IMoneyMarketFundService
{
    /// <summary>
    /// Returns the full MMF reference record for the given security ID,
    /// or <c>null</c> if the security does not exist or is not an MMF.
    /// </summary>
    Task<MmfDetailDto?> GetByIdAsync(Guid securityId, CancellationToken ct = default);

    /// <summary>
    /// Searches for MMFs matching the given criteria.
    /// Returns an empty list when no matches are found.
    /// </summary>
    Task<IReadOnlyList<MmfDetailDto>> SearchAsync(MmfSearchQuery query, CancellationToken ct = default);

    /// <summary>
    /// Returns the current liquidity projection (state and WAM) for the given MMF,
    /// or <c>null</c> if the security does not exist.
    /// </summary>
    Task<MmfLiquidityDto?> GetLiquidityAsync(Guid securityId, CancellationToken ct = default);

    /// <summary>
    /// Returns the sweep-eligibility and fee profile for the given MMF,
    /// or <c>null</c> if the security does not exist.
    /// </summary>
    Task<MmfSweepProfileDto?> GetSweepProfileAsync(Guid securityId, CancellationToken ct = default);

    /// <summary>
    /// Returns the fund-family grouping for the given normalised family name,
    /// or <c>null</c> if no MMFs are registered under that family.
    /// </summary>
    Task<MmfFundFamilyDto?> GetFundFamilyAsync(string normalizedFamilyName, CancellationToken ct = default);

    /// <summary>
    /// Returns all projection rebuild checkpoints across registered MMFs.
    /// </summary>
    Task<IReadOnlyList<MmfRebuildCheckpointDto>> GetRebuildCheckpointsAsync(CancellationToken ct = default);

    /// <summary>
    /// Triggers a deterministic rebuild of all projections for the given MMF
    /// and records a rebuild checkpoint.
    /// </summary>
    Task RebuildProjectionsAsync(Guid securityId, CancellationToken ct = default);
}
