using Meridian.Contracts.FxSpot;

namespace Meridian.Instruments.FxSpot;

/// <summary>
/// Queries spot foreign-exchange reference data by security identifier and supported asset-specific
/// filters.
/// </summary>
public interface IFxSpotReferenceService
{
    Task<FxSpotReferenceDto?> GetReferenceAsync(Guid securityId, CancellationToken ct = default);
    Task<FxSpotReferenceDto?> GetByPairCodeAsync(string pairCode, CancellationToken ct = default);
    Task<IReadOnlyList<FxSpotReferenceDto>> GetByCurrencyAsync(string currency, CancellationToken ct = default);
}
