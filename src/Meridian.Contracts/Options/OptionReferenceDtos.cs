using Meridian.Contracts.SecurityMaster;

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
    long Version)
{
    /// <summary>
    /// <see cref="PutCall"/> as a typed right, or null when the stored text names neither. The raw
    /// text stays on <see cref="PutCall"/>.
    /// </summary>
    public SecurityOptionRight? Right
        => SecurityTermConventions.TryParseOptionRight(PutCall, out var right) ? right : null;
}

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
