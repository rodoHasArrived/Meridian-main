using CanonicalAdjustment = Meridian.Contracts.SecurityMaster.PositionCorporateActionAdjustment;
using CanonicalAdjuster = Meridian.Contracts.SecurityMaster.ILivePositionCorporateActionAdjuster;

namespace Meridian.Application.SecurityMaster.CorporateActions;

/// <summary>Exposes a canonical adjuster through the former Application contract.</summary>
#pragma warning disable CS0618 // This adapter intentionally supports the compatibility contract and result.
public sealed class LivePositionCorporateActionAdjusterAdapter : ILivePositionCorporateActionAdjuster
{
    private readonly CanonicalAdjuster _adjuster;

    public LivePositionCorporateActionAdjusterAdapter(CanonicalAdjuster adjuster)
    {
        _adjuster = adjuster ?? throw new ArgumentNullException(nameof(adjuster));
    }

    public async Task<PositionCorporateActionAdjustment> AdjustPositionAsync(
        string ticker, decimal quantity, decimal costBasis, DateTimeOffset positionOpenedAt,
        CancellationToken ct = default)
        => Convert(await _adjuster.AdjustPositionAsync(ticker, quantity, costBasis, positionOpenedAt, ct)
            .ConfigureAwait(false));

    public async Task<PositionCorporateActionAdjustment> AdjustPositionAsync(
        string ticker, decimal quantity, decimal costBasis, decimal totalCostBasis,
        DateTimeOffset positionOpenedAt, CancellationToken ct = default)
        => Convert(await _adjuster.AdjustPositionAsync(ticker, quantity, costBasis, totalCostBasis, positionOpenedAt, ct)
            .ConfigureAwait(false));

    private static PositionCorporateActionAdjustment Convert(CanonicalAdjustment result)
        => new(result.Ticker, result.OriginalQuantity, result.AdjustedQuantity,
            result.OriginalCostBasis, result.AdjustedCostBasis, result.ActionCount)
        {
            AdjustedTotalCostBasis = result.AdjustedTotalCostBasis
        };
}
#pragma warning restore CS0618
