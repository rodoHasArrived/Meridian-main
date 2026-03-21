namespace Meridian.Contracts.Domain.Enums;

/// <summary>
/// Provider-agnostic canonical trade condition codes.
/// Maps raw provider-specific condition codes (CTA plan, SEC numeric, IB text)
/// to a unified set of canonical conditions for cross-provider comparison.
/// </summary>
public enum CanonicalTradeCondition : byte
{
    /// <summary>
    /// Regular trade (normal market conditions).
    /// </summary>
    Regular = 0,

    /// <summary>
    /// Form T / extended hours trade (pre-market or after-hours).
    /// </summary>
    FormT_ExtendedHours = 1,

    /// <summary>
    /// Odd lot trade (fewer than 100 shares for equities).
    /// </summary>
    OddLot = 2,

    /// <summary>
    /// Average price trade.
    /// </summary>
    AveragePrice = 3,

    /// <summary>
    /// Intermarket sweep order (ISO).
    /// </summary>
    Intermarket_Sweep = 4,

    /// <summary>
    /// Opening print.
    /// </summary>
    OpeningPrint = 5,

    /// <summary>
    /// Closing print.
    /// </summary>
    ClosingPrint = 6,

    /// <summary>
    /// Derivatively priced trade.
    /// </summary>
    DerivativelyPriced = 7,

    /// <summary>
    /// Cross trade.
    /// </summary>
    CrossTrade = 8,

    /// <summary>
    /// Stock option trade.
    /// </summary>
    StockOption = 9,

    /// <summary>
    /// Trading halt indicator.
    /// </summary>
    Halted = 10,

    /// <summary>
    /// Corrected consolidated trade.
    /// </summary>
    CorrectedConsolidated = 11,

    /// <summary>
    /// Sold out of sequence (late report).
    /// </summary>
    SoldOutOfSequence = 12,

    /// <summary>
    /// Contingent trade.
    /// </summary>
    Contingent = 13,

    /// <summary>
    /// Acquisition trade.
    /// </summary>
    Acquisition = 14,

    /// <summary>
    /// Bunched trade.
    /// </summary>
    Bunched = 15,

    /// <summary>
    /// Cash settlement.
    /// </summary>
    Cash = 16,

    /// <summary>
    /// Next day settlement.
    /// </summary>
    NextDay = 17,

    /// <summary>
    /// Seller-initiated trade (definitive aggressor inference).
    /// </summary>
    SellerInitiated = 18,

    /// <summary>
    /// Prior reference price.
    /// </summary>
    PriorReferencePrice = 19,

    /// <summary>
    /// Market-wide circuit breaker halt (MWCB Level 1: 7% drop in S&amp;P 500).
    /// </summary>
    CircuitBreakerLevel1 = 20,

    /// <summary>
    /// Market-wide circuit breaker halt (MWCB Level 2: 13% drop in S&amp;P 500).
    /// </summary>
    CircuitBreakerLevel2 = 21,

    /// <summary>
    /// Market-wide circuit breaker halt (MWCB Level 3: 20% drop in S&amp;P 500, trading halted for remainder of day).
    /// </summary>
    CircuitBreakerLevel3 = 22,

    /// <summary>
    /// LULD (Limit Up/Limit Down) trading pause. Individual stock halted due to
    /// price moving outside its LULD band for more than 15 seconds.
    /// </summary>
    LuldPause = 23,

    /// <summary>
    /// Regulatory halt (SEC or exchange-imposed trading halt, e.g., news pending).
    /// </summary>
    RegulatoryHalt = 24,

    /// <summary>
    /// Trading resumed after halt.
    /// </summary>
    TradingResumed = 25,

    /// <summary>
    /// IPO halt (pre-opening, price discovery).
    /// </summary>
    IpoHalt = 26,

    /// <summary>
    /// Unknown / unmapped condition code.
    /// </summary>
    Unknown = 255
}
