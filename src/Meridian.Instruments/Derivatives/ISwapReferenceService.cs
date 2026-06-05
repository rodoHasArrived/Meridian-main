using Meridian.Contracts.Derivatives;

namespace Meridian.Instruments.Derivatives;

public interface ISwapReferenceService
{
    /// <summary>
    /// Gets one swap reference and hydrates leg terms from the canonical security projection.
    /// </summary>
    Task<SwapReferenceDto?> GetReferenceAsync(Guid securityId, CancellationToken ct = default);

    /// <summary>
    /// Gets active swap references for a swap type, including available leg terms for each row.
    /// </summary>
    Task<IReadOnlyList<SwapReferenceDto>> GetBySwapTypeAsync(string swapType, CancellationToken ct = default);

    /// <summary>
    /// Gets swap references maturing before the supplied date, including available leg terms for each row.
    /// </summary>
    Task<IReadOnlyList<SwapReferenceDto>> GetMaturingBeforeAsync(DateOnly beforeDate, CancellationToken ct = default);
}
