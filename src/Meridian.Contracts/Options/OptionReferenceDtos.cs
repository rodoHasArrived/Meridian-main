namespace Meridian.Contracts.Options;

public sealed record OptionContractReferenceDto(
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
    long Version);

public sealed record OptionSeriesDto(
    string OptionChainId,
    string UnderlyingSymbol,
    DateOnly ExpiryDate,
    IReadOnlyList<OptionContractReferenceDto> Contracts);

public sealed record OptionChainSnapshotDto(
    string OptionChainId,
    string UnderlyingSymbol,
    DateOnly ExpiryDate,
    DateTimeOffset LastUpdatedUtc,
    int ContractCount,
    IReadOnlyList<OptionSeriesDto> Series);
