using Meridian.Contracts.Treasury;

namespace Meridian.Application.Treasury;

/// <summary>
/// Liquidity-focused query surface for money market funds.
/// Provides WAM-based liquidity state, fund-family projections,
/// and portfolio-wide liquid-fund views for treasury and governance consumers.
/// </summary>
public interface IMmfLiquidityService
{
    /// <summary>
    /// Returns the current liquidity state for the given MMF,
    /// or <c>null</c> if the security does not exist.
    /// </summary>
    Task<MmfLiquidityDto?> GetLiquidityStateAsync(Guid securityId, CancellationToken ct = default);

    /// <summary>
    /// Returns the fund-family projection for the given normalised family name,
    /// or <c>null</c> if no MMFs are registered under that family.
    /// </summary>
    Task<MmfFundFamilyDto?> GetFamilyProjectionAsync(string normalizedFamilyName, CancellationToken ct = default);

    /// <summary>
    /// Returns all MMFs belonging to the given fund family.
    /// Returns an empty list when no members are found.
    /// </summary>
    Task<IReadOnlyList<MmfDetailDto>> GetByFamilyAsync(string normalizedFamilyName, CancellationToken ct = default);

    /// <summary>
    /// Returns all active MMFs currently in the <see cref="MmfLiquidityState.Liquid"/> state.
    /// Used by treasury and cash-management dashboards for portfolio-level liquidity views.
    /// </summary>
    Task<IReadOnlyList<MmfLiquidityDto>> GetAllLiquidFundsAsync(CancellationToken ct = default);
}
