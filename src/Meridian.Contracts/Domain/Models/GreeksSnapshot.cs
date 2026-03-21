using Meridian.Contracts.Domain.Events;

namespace Meridian.Contracts.Domain.Models;

/// <summary>
/// Point-in-time snapshot of option greeks and implied volatility.
/// Greeks measure the sensitivity of an option's price to various factors.
/// </summary>
public sealed record GreeksSnapshot : MarketEventPayload
{
    /// <summary>
    /// Gets the timestamp of this greeks observation.
    /// </summary>
    public DateTimeOffset Timestamp { get; }

    /// <summary>
    /// Gets the option contract symbol (OCC or provider-specific).
    /// </summary>
    public string Symbol { get; }

    /// <summary>
    /// Gets the option contract specification.
    /// </summary>
    public OptionContractSpec Contract { get; }

    /// <summary>
    /// Gets the delta — rate of change of option price with respect to underlying price.
    /// Range: [-1, 1]. Calls: [0, 1], Puts: [-1, 0].
    /// </summary>
    public decimal Delta { get; }

    /// <summary>
    /// Gets the gamma — rate of change of delta with respect to underlying price.
    /// Always non-negative. Highest for at-the-money options near expiration.
    /// </summary>
    public decimal Gamma { get; }

    /// <summary>
    /// Gets the theta — rate of time decay per calendar day.
    /// Typically negative (options lose value over time).
    /// </summary>
    public decimal Theta { get; }

    /// <summary>
    /// Gets the vega — sensitivity to a 1% change in implied volatility.
    /// Always non-negative.
    /// </summary>
    public decimal Vega { get; }

    /// <summary>
    /// Gets the rho — sensitivity to a 1% change in interest rates.
    /// </summary>
    public decimal Rho { get; }

    /// <summary>
    /// Gets the implied volatility as a decimal (e.g., 0.25 = 25%).
    /// </summary>
    public decimal ImpliedVolatility { get; }

    /// <summary>
    /// Gets the underlying price at the time of this snapshot.
    /// </summary>
    public decimal UnderlyingPrice { get; }

    /// <summary>
    /// Gets the option's theoretical price based on the model.
    /// </summary>
    public decimal? TheoreticalPrice { get; }

    /// <summary>
    /// Gets the time to expiration in years (used for greeks computation).
    /// </summary>
    public decimal? TimeToExpiry { get; }

    /// <summary>
    /// Gets the sequence number for ordering events.
    /// </summary>
    public long SequenceNumber { get; }

    /// <summary>
    /// Gets the data source identifier.
    /// </summary>
    public string Source { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="GreeksSnapshot"/> record.
    /// </summary>
    public GreeksSnapshot(
        DateTimeOffset Timestamp,
        string Symbol,
        OptionContractSpec Contract,
        decimal Delta,
        decimal Gamma,
        decimal Theta,
        decimal Vega,
        decimal Rho,
        decimal ImpliedVolatility,
        decimal UnderlyingPrice,
        decimal? TheoreticalPrice = null,
        decimal? TimeToExpiry = null,
        long SequenceNumber = 0,
        string Source = "IB")
    {
        if (string.IsNullOrWhiteSpace(Symbol))
            throw new ArgumentException("Symbol is required", nameof(Symbol));

        if (ImpliedVolatility < 0)
            throw new ArgumentOutOfRangeException(nameof(ImpliedVolatility), ImpliedVolatility, "Implied volatility cannot be negative");

        if (UnderlyingPrice <= 0)
            throw new ArgumentOutOfRangeException(nameof(UnderlyingPrice), UnderlyingPrice, "Underlying price must be greater than 0");

        if (Gamma < 0)
            throw new ArgumentOutOfRangeException(nameof(Gamma), Gamma, "Gamma cannot be negative");

        if (Vega < 0)
            throw new ArgumentOutOfRangeException(nameof(Vega), Vega, "Vega cannot be negative");

        this.Timestamp = Timestamp;
        this.Symbol = Symbol;
        this.Contract = Contract ?? throw new ArgumentNullException(nameof(Contract));
        this.Delta = Delta;
        this.Gamma = Gamma;
        this.Theta = Theta;
        this.Vega = Vega;
        this.Rho = Rho;
        this.ImpliedVolatility = ImpliedVolatility;
        this.UnderlyingPrice = UnderlyingPrice;
        this.TheoreticalPrice = TheoreticalPrice;
        this.TimeToExpiry = TimeToExpiry;
        this.SequenceNumber = SequenceNumber;
        this.Source = Source;
    }

    /// <summary>
    /// Gets the implied volatility as a percentage string (e.g., "25.00%").
    /// </summary>
    public string ImpliedVolatilityPercent => $"{ImpliedVolatility * 100m:F2}%";

    /// <summary>
    /// Returns true if the option is in-the-money based on the underlying price.
    /// </summary>
    public bool IsInTheMoney => Contract.Right switch
    {
        Enums.OptionRight.Call => UnderlyingPrice > Contract.Strike,
        Enums.OptionRight.Put => UnderlyingPrice < Contract.Strike,
        _ => false
    };

    /// <summary>
    /// Gets the intrinsic value of the option.
    /// </summary>
    public decimal IntrinsicValue => Contract.Right switch
    {
        Enums.OptionRight.Call => Math.Max(0, UnderlyingPrice - Contract.Strike),
        Enums.OptionRight.Put => Math.Max(0, Contract.Strike - UnderlyingPrice),
        _ => 0m
    };
}
