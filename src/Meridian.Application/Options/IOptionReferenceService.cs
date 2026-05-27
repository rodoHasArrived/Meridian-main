using Meridian.Contracts.Options;

namespace Meridian.Application.Options;

public interface IOptionReferenceService
{
    /// <summary>
    /// Looks up one option contract by its normalized OCC or provider contract symbol.
    /// Implementations trim and uppercase input before querying exact-match projection stores.
    /// </summary>
    Task<OptionContractReferenceDto?> GetContractAsync(string contractSymbol, CancellationToken ct = default);

    /// <summary>
    /// Gets the contracts for a normalized option chain and expiry date.
    /// Implementations trim and uppercase the chain identifier before querying storage.
    /// </summary>
    Task<OptionSeriesDto?> GetSeriesAsync(string optionChainId, DateOnly expiryDate, CancellationToken ct = default);

    /// <summary>
    /// Gets a point-in-time chain snapshot for a normalized underlying symbol and expiry date.
    /// </summary>
    Task<OptionChainSnapshotDto?> GetChainSnapshotAsync(string underlyingSymbol, DateOnly expiryDate, CancellationToken ct = default);

    /// <summary>
    /// Gets an option contract together with the stored underlying linkage, returning null when linkage is absent.
    /// </summary>
    Task<OptionContractReferenceDto?> GetUnderlyingLinkageAsync(string contractSymbol, CancellationToken ct = default);

    /// <summary>
    /// Gets the available expiry dates for a normalized underlying symbol.
    /// </summary>
    Task<IReadOnlyList<DateOnly>> GetExpiryLadderAsync(string underlyingSymbol, CancellationToken ct = default);
}
