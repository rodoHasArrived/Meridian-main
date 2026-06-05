using Meridian.Contracts.Futures;

namespace Meridian.Instruments.Futures;

public interface IFutureReferenceService
{
    Task<FutureReferenceDto?> GetReferenceAsync(Guid securityId, CancellationToken ct = default);
    Task<IReadOnlyList<FutureReferenceDto>> GetByRootSymbolAsync(string rootSymbol, CancellationToken ct = default);
    Task<IReadOnlyList<FutureReferenceDto>> GetExpiryLadderAsync(string rootSymbol, CancellationToken ct = default);
    Task<FutureReferenceDto?> GetFrontMonthAsync(string rootSymbol, CancellationToken ct = default);
}
