using Meridian.Contracts.Domain.Enums;

namespace Meridian.Contracts.Configuration;

/// <summary>
/// Configuration for a subscribed symbol and how to build its IB contract.
///
/// Notes for preferred shares on IB:
/// - Preferreds are usually represented as SecType=STK with a LocalSymbol like "PCG PRA" or "PCG PR A".
/// - To avoid ambiguity, set LocalSymbol explicitly when possible.
///
/// Notes for options:
/// - Set SecurityType to "OPT" and provide Strike, Right, and LastTradeDateOrContractMonth.
/// - For index options (SPX, NDX, RUT), set OptionStyle to European and InstrumentType to IndexOption.
/// - For equity options, OptionStyle defaults to American.
/// </summary>
public sealed record SymbolConfig(
    string Symbol,

    // Data collection toggles
    bool SubscribeTrades = true,
    bool SubscribeDepth = true,
    int DepthLevels = 10,

    // Contract fields (IB)
    string SecurityType = "STK",        // STK, OPT, IND_OPT, FUT, FOP, SSF, CASH, CMDTY, CRYPTO, MARGIN, etc.
    string Exchange = "SMART",          // SMART is usually best; set direct venue if needed
    string Currency = "USD",
    string? PrimaryExchange = null,     // e.g. NYSE, NASDAQ
    string? LocalSymbol = null,         // strongly recommended for preferreds (e.g. "PCG PRA")
    string? TradingClass = null,
    int? ConId = null,                 // if you know the exact contract id, this wins

    // Instrument classification
    InstrumentType InstrumentType = InstrumentType.Equity,

    // Liquidity classification - controls monitoring thresholds for gap detection,
    // completeness scoring, SLA freshness, and anomaly detection.
    // When null, defaults to High for large-cap equities / ETFs.
    LiquidityProfile? LiquidityProfile = null,

    // Validation configuration - when true, uses relaxed (historical-preset) thresholds
    // for the F# validation pipeline stage. Useful for illiquid symbols, preferreds, or
    // any instrument where the default real-time constraints are too strict.
    // When null or false, the standard default validation config is used.
    bool? UseRelaxedValidation = null,

    // Options contract fields (required when SecurityType = "OPT")
    decimal? Strike = null,                        // Strike price
    OptionRight? Right = null,                     // Call or Put
    string? LastTradeDateOrContractMonth = null,   // Expiration: "20260321" or "202603"
    OptionStyle? OptionStyle = null,               // American (default for equity) or European (index)
    int? Multiplier = null,                        // Contract multiplier (default 100 for options)
    string? UnderlyingSymbol = null                // Underlying for options (null = same as Symbol)
);
