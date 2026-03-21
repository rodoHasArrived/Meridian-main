using Meridian.Contracts.Domain.Enums;

namespace Meridian.Contracts.Domain.Models;

/// <summary>
/// Specification for an exchange-traded option contract.
/// Encapsulates the defining characteristics of an options contract:
/// underlying symbol, strike, expiration, right, style, and multiplier.
/// </summary>
public sealed record OptionContractSpec
{
    /// <summary>
    /// Gets the underlying symbol (e.g., "AAPL", "SPX", "QQQ").
    /// </summary>
    public string UnderlyingSymbol { get; }

    /// <summary>
    /// Gets the strike price of the option contract.
    /// </summary>
    public decimal Strike { get; }

    /// <summary>
    /// Gets the expiration date of the option contract.
    /// </summary>
    public DateOnly Expiration { get; }

    /// <summary>
    /// Gets whether this is a call or put option.
    /// </summary>
    public OptionRight Right { get; }

    /// <summary>
    /// Gets the exercise style (American or European).
    /// </summary>
    public OptionStyle Style { get; }

    /// <summary>
    /// Gets the contract multiplier (typically 100 for equity options).
    /// </summary>
    public ushort Multiplier { get; }

    /// <summary>
    /// Gets the exchange where the option is listed (e.g., "CBOE", "SMART", "ISE").
    /// </summary>
    public string Exchange { get; }

    /// <summary>
    /// Gets the settlement currency (e.g., "USD").
    /// </summary>
    public string Currency { get; }

    /// <summary>
    /// Gets the OCC (Options Clearing Corporation) symbol, if available.
    /// Format: underlying (padded to 6) + YYMMDD + C/P + strike*1000 (padded to 8).
    /// Example: "AAPL  260321C00150000" for AAPL Mar 21 2026 150 Call.
    /// </summary>
    public string? OccSymbol { get; }

    /// <summary>
    /// Gets the instrument type classification for this contract.
    /// </summary>
    public InstrumentType InstrumentType { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="OptionContractSpec"/> record.
    /// </summary>
    public OptionContractSpec(
        string UnderlyingSymbol,
        decimal Strike,
        DateOnly Expiration,
        OptionRight Right,
        OptionStyle Style = OptionStyle.American,
        ushort Multiplier = 100,
        string Exchange = "SMART",
        string Currency = "USD",
        string? OccSymbol = null,
        InstrumentType InstrumentType = InstrumentType.EquityOption)
    {
        if (string.IsNullOrWhiteSpace(UnderlyingSymbol))
            throw new ArgumentException("Underlying symbol is required", nameof(UnderlyingSymbol));

        if (Strike <= 0)
            throw new ArgumentOutOfRangeException(nameof(Strike), Strike, "Strike price must be greater than 0");

        if (Multiplier == 0)
            throw new ArgumentOutOfRangeException(nameof(Multiplier), Multiplier, "Multiplier must be greater than 0");

        if (InstrumentType is not (InstrumentType.EquityOption or InstrumentType.IndexOption))
            throw new ArgumentException("InstrumentType must be EquityOption or IndexOption for an option contract", nameof(InstrumentType));

        this.UnderlyingSymbol = UnderlyingSymbol;
        this.Strike = Strike;
        this.Expiration = Expiration;
        this.Right = Right;
        this.Style = Style;
        this.Multiplier = Multiplier;
        this.Exchange = Exchange;
        this.Currency = Currency;
        this.OccSymbol = OccSymbol;
        this.InstrumentType = InstrumentType;
    }

    /// <summary>
    /// Returns a human-readable description of the contract.
    /// Example: "AAPL 2026-03-21 150.00 Call"
    /// </summary>
    public override string ToString()
        => $"{UnderlyingSymbol} {Expiration:yyyy-MM-dd} {Strike:F2} {Right}";

    /// <summary>
    /// Generates the OCC-standard symbol for this contract.
    /// Format: UNDERLYING(6) + YYMMDD + C/P + STRIKE*1000(8)
    /// </summary>
    public string ToOccSymbol()
    {
        var underlying = UnderlyingSymbol.PadRight(6);
        var date = Expiration.ToString("yyMMdd");
        var rightChar = Right == OptionRight.Call ? 'C' : 'P';
        var strikeInt = (long)(Strike * 1000m);
        var strikeStr = strikeInt.ToString("D8");
        return $"{underlying}{date}{rightChar}{strikeStr}";
    }

    /// <summary>
    /// Gets the number of calendar days until expiration from the given date.
    /// </summary>
    public int DaysToExpiration(DateOnly asOf)
        => Expiration.DayNumber - asOf.DayNumber;

    /// <summary>
    /// Returns true if the contract has expired as of the given date.
    /// </summary>
    public bool IsExpired(DateOnly asOf)
        => asOf > Expiration;
}
