using Meridian.Contracts.Equity;

namespace Meridian.Instruments.Equity;

public interface IEquityReferenceService
{
    Task<EquityReferenceDto?> GetReferenceAsync(Guid securityId, CancellationToken ct = default);
    Task<IReadOnlyList<EquityReferenceDto>> GetByExchangeAsync(string exchangeCode, CancellationToken ct = default);
    Task<IReadOnlyList<EquityReferenceDto>> GetByIssuerAsync(string issuerName, CancellationToken ct = default);
}
