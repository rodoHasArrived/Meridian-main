using Meridian.Contracts.Domain.Models;

namespace Meridian.Storage.SecurityMaster;

public interface IOptionReferenceProjectionStore
{
    Task<OptionContractProjectionRow?> GetContractAsync(string contractSymbol, CancellationToken ct = default);
    Task<IReadOnlyList<OptionContractProjectionRow>> GetSeriesContractsAsync(string optionChainId, DateOnly expiryDate, CancellationToken ct = default);
    Task<IReadOnlyList<OptionContractProjectionRow>> GetUnderlyingContractsAsync(string underlyingSymbol, DateOnly expiryDate, CancellationToken ct = default);
    Task<IReadOnlyList<DateOnly>> GetExpiryLadderAsync(string underlyingSymbol, CancellationToken ct = default);
    Task UpsertChainSnapshotAsync(OptionChainSnapshot snapshot, CancellationToken ct = default);
}

public sealed record OptionContractProjectionRow(
    string ContractSymbol,
    Guid? SecurityId,
    string OptionChainId,
    string UnderlyingSymbol,
    Guid? UnderlyingSecurityId,
    string PutCall,
    decimal Strike,
    DateOnly ExpiryDate,
    decimal Multiplier,
    bool IsAdjusted,
    DateOnly? LastTradingDate,
    string LifecycleStat,
    DateTimeOffset LastUpdatedUtc,
    long Version);
