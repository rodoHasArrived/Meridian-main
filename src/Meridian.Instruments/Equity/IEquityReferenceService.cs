using Meridian.Contracts.Equity;

namespace Meridian.Instruments.Equity;

/// <summary>
/// Queries equity reference data by security identifier and supported asset-specific filters.
/// </summary>
public interface IEquityReferenceService
{
    Task<EquityReferenceDto?> GetReferenceAsync(Guid securityId, CancellationToken ct = default);
    Task<IReadOnlyList<EquityReferenceDto>> GetByExchangeAsync(string exchangeCode, CancellationToken ct = default);
    Task<IReadOnlyList<EquityReferenceDto>> GetByIssuerAsync(string issuerName, CancellationToken ct = default);
}
