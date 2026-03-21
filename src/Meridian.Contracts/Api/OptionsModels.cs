using Meridian.Contracts.Domain.Enums;

namespace Meridian.Contracts.Api;

/// <summary>
/// Response DTO for option chain expirations.
/// </summary>
public sealed record OptionsExpirationsResponse(
    string UnderlyingSymbol,
    IReadOnlyList<DateOnly> Expirations,
    int Count,
    DateTimeOffset Timestamp
);

/// <summary>
/// Response DTO for option chain strikes.
/// </summary>
public sealed record OptionsStrikesResponse(
    string UnderlyingSymbol,
    DateOnly Expiration,
    IReadOnlyList<decimal> Strikes,
    int Count,
    DateTimeOffset Timestamp
);

/// <summary>
/// Response DTO for an option chain snapshot.
/// </summary>
public sealed record OptionsChainResponse(
    string UnderlyingSymbol,
    decimal UnderlyingPrice,
    DateOnly Expiration,
    int DaysToExpiration,
    string InstrumentType,
    decimal? AtTheMoneyStrike,
    decimal? PutCallVolumeRatio,
    decimal? PutCallOpenInterestRatio,
    IReadOnlyList<OptionQuoteDto> Calls,
    IReadOnlyList<OptionQuoteDto> Puts,
    int TotalContracts,
    DateTimeOffset Timestamp
);

/// <summary>
/// Response DTO for an option quote.
/// </summary>
public sealed record OptionQuoteDto(
    string Symbol,
    string UnderlyingSymbol,
    decimal Strike,
    string Right,
    DateOnly Expiration,
    string Style,
    decimal BidPrice,
    long BidSize,
    decimal AskPrice,
    long AskSize,
    decimal? LastPrice,
    decimal? MidPrice,
    decimal Spread,
    decimal UnderlyingPrice,
    decimal? ImpliedVolatility,
    decimal? Delta,
    decimal? Gamma,
    decimal? Theta,
    decimal? Vega,
    long? OpenInterest,
    long? Volume,
    bool IsInTheMoney,
    decimal Moneyness,
    decimal? NotionalValue,
    long SequenceNumber,
    string Source,
    DateTimeOffset Timestamp
);

/// <summary>
/// Response DTO for an option trade.
/// </summary>
public sealed record OptionTradeDto(
    string Symbol,
    string UnderlyingSymbol,
    decimal Strike,
    string Right,
    DateOnly Expiration,
    decimal Price,
    long Size,
    string Aggressor,
    decimal UnderlyingPrice,
    decimal? ImpliedVolatility,
    string? TradeExchange,
    decimal NotionalValue,
    bool IsInTheMoney,
    long SequenceNumber,
    string Source,
    DateTimeOffset Timestamp
);

/// <summary>
/// Response DTO for a greeks snapshot.
/// </summary>
public sealed record GreeksSnapshotDto(
    string Symbol,
    string UnderlyingSymbol,
    decimal Strike,
    string Right,
    DateOnly Expiration,
    decimal Delta,
    decimal Gamma,
    decimal Theta,
    decimal Vega,
    decimal Rho,
    decimal ImpliedVolatility,
    string ImpliedVolatilityPercent,
    decimal UnderlyingPrice,
    decimal? TheoreticalPrice,
    decimal IntrinsicValue,
    bool IsInTheMoney,
    long SequenceNumber,
    string Source,
    DateTimeOffset Timestamp
);

/// <summary>
/// Response DTO for an open interest update.
/// </summary>
public sealed record OpenInterestDto(
    string Symbol,
    string UnderlyingSymbol,
    decimal Strike,
    string Right,
    DateOnly Expiration,
    long OpenInterest,
    long? PreviousOpenInterest,
    long? OpenInterestChange,
    long Volume,
    decimal VolumeToOpenInterestRatio,
    long SequenceNumber,
    string Source,
    DateTimeOffset Timestamp
);

/// <summary>
/// Response DTO for the options data summary.
/// </summary>
public sealed record OptionsSummaryResponse(
    int TrackedContracts,
    int TrackedChains,
    int TrackedUnderlyings,
    int ContractsWithGreeks,
    int ContractsWithOpenInterest,
    bool ProviderAvailable,
    DateTimeOffset Timestamp
);

/// <summary>
/// Request DTO for fetching a specific option quote.
/// </summary>
public sealed record OptionQuoteRequest(
    string UnderlyingSymbol,
    decimal Strike,
    string Right,
    string Expiration,
    string? Style = null,
    string? Exchange = null
);

/// <summary>
/// Request DTO for refreshing option chain data.
/// </summary>
public sealed record OptionsRefreshRequest(
    string? UnderlyingSymbol = null,
    string? Expiration = null,
    int? StrikeRange = null
);
