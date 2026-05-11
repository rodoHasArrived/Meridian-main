using Meridian.Contracts.Options;

namespace Meridian.Application.Options;

public interface IOptionReferenceService
{
    Task<OptionContractReferenceDto?> GetContractAsync(string contractSymbol, CancellationToken ct = default);
    Task<OptionSeriesDto?> GetSeriesAsync(string optionChainId, DateOnly expiryDate, CancellationToken ct = default);
    Task<OptionChainSnapshotDto?> GetChainSnapshotAsync(string underlyingSymbol, DateOnly expiryDate, CancellationToken ct = default);
    Task<OptionContractReferenceDto?> GetUnderlyingLinkageAsync(string contractSymbol, CancellationToken ct = default);
    Task<IReadOnlyList<DateOnly>> GetExpiryLadderAsync(string underlyingSymbol, CancellationToken ct = default);
}
