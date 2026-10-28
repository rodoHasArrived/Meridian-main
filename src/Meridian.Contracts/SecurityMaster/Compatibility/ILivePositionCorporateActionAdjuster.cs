using CanonicalAdjustment = Meridian.Contracts.SecurityMaster.PositionCorporateActionAdjustment;
using CanonicalAdjuster = Meridian.Contracts.SecurityMaster.ILivePositionCorporateActionAdjuster;

namespace Meridian.Application.SecurityMaster.CorporateActions;

/// <summary>Compatibility result for callers using the former Application-owned contract.</summary>
[Obsolete("Use Meridian.Contracts.SecurityMaster.PositionCorporateActionAdjustment.")]
public sealed record PositionCorporateActionAdjustment(
    string Ticker,
    decimal OriginalQuantity,
    decimal AdjustedQuantity,
    decimal OriginalCostBasis,
    decimal AdjustedCostBasis,
    int ActionCount)
    : CanonicalAdjustment(Ticker, OriginalQuantity, AdjustedQuantity, OriginalCostBasis, AdjustedCostBasis, ActionCount);

/// <summary>
/// Compatibility contract. Existing implementations also satisfy the canonical contract;
/// both overloads preserve the implementation's exact carrying-basis evidence.
/// </summary>
[Obsolete("Use Meridian.Contracts.SecurityMaster.ILivePositionCorporateActionAdjuster.")]
public interface ILivePositionCorporateActionAdjuster : CanonicalAdjuster
{
    new Task<PositionCorporateActionAdjustment> AdjustPositionAsync(
        string ticker,
        decimal quantity,
        decimal costBasis,
        DateTimeOffset positionOpenedAt,
        CancellationToken ct = default);

    new Task<PositionCorporateActionAdjustment> AdjustPositionAsync(
        string ticker,
        decimal quantity,
        decimal costBasis,
        decimal totalCostBasis,
        DateTimeOffset positionOpenedAt,
        CancellationToken ct = default)
        => AdjustPositionAsync(ticker, quantity, costBasis, positionOpenedAt, ct);

    async Task<CanonicalAdjustment> CanonicalAdjuster.AdjustPositionAsync(
        string ticker, decimal quantity, decimal costBasis, DateTimeOffset positionOpenedAt, CancellationToken ct)
        => await AdjustPositionAsync(ticker, quantity, costBasis, positionOpenedAt, ct).ConfigureAwait(false);

    async Task<CanonicalAdjustment> CanonicalAdjuster.AdjustPositionAsync(
        string ticker, decimal quantity, decimal costBasis, decimal totalCostBasis,
        DateTimeOffset positionOpenedAt, CancellationToken ct)
        => await AdjustPositionAsync(ticker, quantity, costBasis, totalCostBasis, positionOpenedAt, ct)
            .ConfigureAwait(false);
}
