using Meridian.Contracts.Derivatives;

namespace Meridian.Application.Derivatives;

public interface ISwapReferenceService
{
    Task<SwapReferenceDto?> GetReferenceAsync(Guid securityId, CancellationToken ct = default);
    Task<IReadOnlyList<SwapReferenceDto>> GetBySwapTypeAsync(string swapType, CancellationToken ct = default);
    Task<IReadOnlyList<SwapReferenceDto>> GetMaturingBeforeAsync(DateOnly beforeDate, CancellationToken ct = default);
}
