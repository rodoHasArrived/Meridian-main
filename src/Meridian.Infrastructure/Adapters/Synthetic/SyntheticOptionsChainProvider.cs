using System.Runtime.CompilerServices;
using Meridian.Application.Config;
using Meridian.Contracts.Domain.Enums;
using Meridian.Contracts.Domain.Models;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Infrastructure.Contracts;
using Meridian.Infrastructure.DataSources;
using InfraDataSourceType = Meridian.Infrastructure.DataSources.DataSourceType;

namespace Meridian.Infrastructure.Adapters.Synthetic;

/// <summary>
/// Deterministic synthetic options chain provider for offline development and testing.
/// Generates realistic option chains around the current underlying price using a
/// closed-form Black-Scholes model. All data is deterministic and reproducible.
/// </summary>
[DataSource("synthetic-options", "Synthetic Options Chain", InfraDataSourceType.Realtime, DataSourceCategory.Free,
    Priority = 200, Description = "Deterministic synthetic option chains for offline development, testing, and backtesting.")]
[ImplementsAdr("ADR-001", "Options chain provider implementation following provider abstraction contract")]
[ImplementsAdr("ADR-004", "All async methods support CancellationToken")]
[ImplementsAdr("ADR-005", "Attribute-based provider discovery")]
public sealed class SyntheticOptionsChainProvider : IOptionsChainProvider
{
    private const double BaseImpliedVolatility = 0.25; // 25 % annualised IV
    private const double RiskFreeRate = 0.04;           // 4 %
    private const double SpreadFactor = 0.015;          // 1.5 % bid-ask half-spread at ATM
    private const int WeeklyCount = 4;
    private const int MonthlyCount = 3;
    private const int QuarterlyCount = 2;

    private readonly SyntheticMarketDataConfig _config;

    public SyntheticOptionsChainProvider(SyntheticMarketDataConfig? config = null)
    {
        _config = config ?? new SyntheticMarketDataConfig(Enabled: true);
    }

    // --------------------------------------------------------------------- //
    //  IProviderMetadata                                                      //
    // --------------------------------------------------------------------- //

    public string ProviderId => "synthetic-options";
    public string ProviderDisplayName => "Synthetic Options Chain";
    public string ProviderDescription => "Deterministic Black-Scholes option chains for offline development and testing.";
    public int ProviderPriority => 200;
    public ProviderCapabilities ProviderCapabilities => ProviderCapabilities.OptionsChain();

    // --------------------------------------------------------------------- //
    //  IOptionsChainProvider                                                  //
    // --------------------------------------------------------------------- //

    /// <inheritdoc/>
    public OptionsChainCapabilities Capabilities { get; } = new()
    {
        SupportsGreeks = true,
        SupportsOpenInterest = true,
        SupportsImpliedVolatility = true,
        SupportsIndexOptions = true,
        SupportsHistorical = false,
        SupportsStreaming = false,
        SupportedInstrumentTypes = new[]
        {
            InstrumentType.EquityOption,
            InstrumentType.IndexOption
        }
    };

    /// <inheritdoc/>
    public Task<IReadOnlyList<DateOnly>> GetExpirationsAsync(
        string underlyingSymbol,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(underlyingSymbol);
        return Task.FromResult<IReadOnlyList<DateOnly>>(GenerateExpirations(DateOnly.FromDateTime(DateTime.UtcNow)));
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<decimal>> GetStrikesAsync(
        string underlyingSymbol,
        DateOnly expiration,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(underlyingSymbol);
        var profile = SyntheticReferenceDataCatalog.GetProfileOrDefault(underlyingSymbol);
        var strikes = GenerateStrikes(profile.BasePrice, strikeRange: 12);
        return Task.FromResult<IReadOnlyList<decimal>>(strikes);
    }

    /// <inheritdoc/>
    public Task<OptionChainSnapshot?> GetChainSnapshotAsync(
        string underlyingSymbol,
        DateOnly expiration,
        int? strikeRange = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(underlyingSymbol);
        var profile = SyntheticReferenceDataCatalog.GetProfileOrDefault(underlyingSymbol);
        var asOf = DateOnly.FromDateTime(DateTime.UtcNow);

        if (expiration <= asOf)
            return Task.FromResult<OptionChainSnapshot?>(null);

        var strikes = GenerateStrikes(profile.BasePrice, strikeRange ?? 10);
        var now = DateTimeOffset.UtcNow;

        var calls = strikes
            .Select(k => BuildQuote(underlyingSymbol, k, expiration, OptionRight.Call, profile.BasePrice, now))
            .ToArray();
        var puts = strikes
            .Select(k => BuildQuote(underlyingSymbol, k, expiration, OptionRight.Put, profile.BasePrice, now))
            .ToArray();

        var snapshot = new OptionChainSnapshot(
            Timestamp: now,
            UnderlyingSymbol: underlyingSymbol,
            UnderlyingPrice: profile.BasePrice,
            Expiration: expiration,
            InstrumentType: InstrumentType.EquityOption,
            Strikes: strikes,
            Calls: calls,
            Puts: puts,
            SequenceNumber: 0,
            Source: "synthetic");

        return Task.FromResult<OptionChainSnapshot?>(snapshot);
    }

    /// <inheritdoc/>
    public Task<OptionQuote?> GetOptionQuoteAsync(
        OptionContractSpec contract,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(contract);
        var asOf = DateOnly.FromDateTime(DateTime.UtcNow);
        if (contract.IsExpired(asOf))
            return Task.FromResult<OptionQuote?>(null);

        var profile = SyntheticReferenceDataCatalog.GetProfileOrDefault(contract.UnderlyingSymbol);
        var quote = BuildQuote(
            contract.UnderlyingSymbol,
            contract.Strike,
            contract.Expiration,
            contract.Right,
            profile.BasePrice,
            DateTimeOffset.UtcNow);

        return Task.FromResult<OptionQuote?>(quote);
    }

    // --------------------------------------------------------------------- //
    //  Chain generation helpers                                               //
    // --------------------------------------------------------------------- //

    /// <summary>
    /// Produces a sorted list of expiration dates:
    /// <see cref="WeeklyCount"/> Fridays + <see cref="MonthlyCount"/> third-Fridays-of-month
    /// + <see cref="QuarterlyCount"/> quarterly third-Fridays.
    /// </summary>
    internal static IReadOnlyList<DateOnly> GenerateExpirations(DateOnly asOf)
    {
        var expirations = new SortedSet<DateOnly>();

        // Weekly: next N Fridays
        var current = asOf.AddDays(1);
        int weekliesFound = 0;
        while (weekliesFound < WeeklyCount)
        {
            if (current.DayOfWeek == DayOfWeek.Friday)
            {
                expirations.Add(current);
                weekliesFound++;
            }
            current = current.AddDays(1);
        }

        // Monthly: 3rd Friday of next N months
        int monthliesFound = 0;
        var monthStart = new DateOnly(asOf.Year, asOf.Month, 1).AddMonths(1);
        while (monthliesFound < MonthlyCount)
        {
            var thirdFriday = ThirdFriday(monthStart.Year, monthStart.Month);
            if (thirdFriday > asOf)
            {
                expirations.Add(thirdFriday);
                monthliesFound++;
            }
            monthStart = monthStart.AddMonths(1);
        }

        // Quarterly: 3rd Friday of next N quarterly months (Mar, Jun, Sep, Dec cycle)
        var quarterlyMonths = new[] { 3, 6, 9, 12 };
        int quarterliesFound = 0;
        int yearScan = asOf.Year;
        while (quarterliesFound < QuarterlyCount)
        {
            foreach (var month in quarterlyMonths)
            {
                var thirdFriday = ThirdFriday(yearScan, month);
                if (thirdFriday > asOf.AddDays(60))  // only add if > 60 days out
                {
                    expirations.Add(thirdFriday);
                    quarterliesFound++;
                    if (quarterliesFound >= QuarterlyCount)
                        break;
                }
            }
            yearScan++;
        }

        return expirations.ToArray();
    }

    /// <summary>
    /// Returns the third Friday of the given year/month.
    /// </summary>
    private static DateOnly ThirdFriday(int year, int month)
    {
        var first = new DateOnly(year, month, 1);
        // Find the first Friday
        int daysUntilFriday = ((int)DayOfWeek.Friday - (int)first.DayOfWeek + 7) % 7;
        var firstFriday = first.AddDays(daysUntilFriday);
        return firstFriday.AddDays(14); // third Friday = first + 2 weeks
    }

    /// <summary>
    /// Generates evenly-spaced strike prices centred around ATM.
    /// Strike increment: $5 for underlying ≥ $100, $2.50 for $20-$100, $1 below $20.
    /// </summary>
    internal static IReadOnlyList<decimal> GenerateStrikes(decimal underlyingPrice, int strikeRange)
    {
        var increment = underlyingPrice switch
        {
            >= 200m => 5m,
            >= 100m => 2.5m,
            >= 20m => 1m,
            _ => 0.5m
        };

        // Round underlying to nearest increment to get ATM strike
        decimal atm = Math.Round(underlyingPrice / increment, MidpointRounding.AwayFromZero) * increment;

        var strikes = new List<decimal>(strikeRange * 2 + 1);
        for (int i = -strikeRange; i <= strikeRange; i++)
        {
            var strike = atm + i * increment;
            if (strike > 0m)
                strikes.Add(strike);
        }
        return strikes;
    }

    /// <summary>
    /// Builds a synthetic <see cref="OptionQuote"/> using BSM pricing.
    /// </summary>
    private static OptionQuote BuildQuote(
        string underlyingSymbol,
        decimal strike,
        DateOnly expiration,
        OptionRight right,
        decimal underlyingPrice,
        DateTimeOffset now)
    {
        var asOf = DateOnly.FromDateTime(now.UtcDateTime);
        int dte = expiration.DayNumber - asOf.DayNumber;
        double tYears = Math.Max(0.0, dte / 365.0);

        double s = (double)underlyingPrice;
        double k = (double)strike;
        double r = RiskFreeRate;
        double sigma = BaseImpliedVolatility;

        // BSM pricing
        double mid;
        double delta, gamma, theta, vega;

        if (right == OptionRight.Call)
        {
            mid = BsmCallPrice(s, k, r, sigma, tYears);
            delta = BsmCallDelta(s, k, r, sigma, tYears);
            delta = Math.Clamp(delta, 0.0, 1.0);
        }
        else
        {
            mid = BsmPutPrice(s, k, r, sigma, tYears);
            delta = BsmCallDelta(s, k, r, sigma, tYears) - 1.0; // put delta = call delta - 1
            delta = Math.Clamp(delta, -1.0, 0.0);
        }

        gamma = BsmGamma(s, k, r, sigma, tYears);
        theta = (right == OptionRight.Call)
            ? BsmCallTheta(s, k, r, sigma, tYears)
            : BsmPutTheta(s, k, r, sigma, tYears);
        vega = BsmVega(s, k, r, sigma, tYears);

        // Bid/ask spread: wider for deep OTM/ITM and near-expiry
        double moneyness = s / k;
        double spreadMultiplier = 1.0 + 2.0 * Math.Abs(moneyness - 1.0) + (dte <= 7 ? 0.5 : 0.0);
        double halfSpread = Math.Max(0.01, mid * SpreadFactor * spreadMultiplier);
        var bid = Math.Max(0m, (decimal)(mid - halfSpread));
        var ask = (decimal)(mid + halfSpread);

        // Synthetic open interest: peaks near ATM, decays exponentially
        double otmRatio = Math.Abs(moneyness - 1.0);
        long openInterest = (long)(10_000 * Math.Exp(-8.0 * otmRatio));
        long volume = openInterest / 10;

        var contract = new OptionContractSpec(
            UnderlyingSymbol: underlyingSymbol,
            Strike: strike,
            Expiration: expiration,
            Right: right,
            Style: OptionStyle.American,
            Multiplier: 100,
            Exchange: "SYNTHETIC",
            Currency: "USD",
            InstrumentType: InstrumentType.EquityOption);

        var occSymbol = contract.ToOccSymbol();

        return new OptionQuote(
            Timestamp: now,
            Symbol: occSymbol,
            Contract: contract,
            BidPrice: bid,
            BidSize: volume,
            AskPrice: ask,
            AskSize: volume,
            LastPrice: (decimal)mid,
            UnderlyingPrice: underlyingPrice,
            ImpliedVolatility: (decimal)sigma,
            Delta: (decimal)delta,
            Gamma: (decimal)gamma,
            Theta: (decimal)theta,
            Vega: (decimal)vega,
            OpenInterest: openInterest,
            Volume: volume,
            SequenceNumber: 0,
            Source: "synthetic");
    }

    // --------------------------------------------------------------------- //
    //  Inline BSM — kept here to avoid referencing Meridian.Backtesting.Sdk  //
    // --------------------------------------------------------------------- //

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static double Ncdf(double x)
    {
        const double a1 = 0.319381530, a2 = -0.356563782, a3 = 1.781477937;
        const double a4 = -1.821255978, a5 = 1.330274429;
        double t = 1.0 / (1.0 + 0.2316419 * Math.Abs(x));
        double poly = t * (a1 + t * (a2 + t * (a3 + t * (a4 + t * a5))));
        double pdf = Math.Exp(-0.5 * x * x) / Math.Sqrt(2.0 * Math.PI);
        double result = 1.0 - pdf * poly;
        return x >= 0 ? result : 1.0 - result;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static double Npdf(double x)
        => Math.Exp(-0.5 * x * x) / Math.Sqrt(2.0 * Math.PI);

    private static (double d1, double d2) D1D2(double s, double k, double r, double sigma, double t)
    {
        if (sigma <= 0 || t <= 0 || s <= 0 || k <= 0) return (double.NaN, double.NaN);
        double sqrtT = Math.Sqrt(t);
        double d1 = (Math.Log(s / k) + (r + 0.5 * sigma * sigma) * t) / (sigma * sqrtT);
        return (d1, d1 - sigma * sqrtT);
    }

    private static double BsmCallPrice(double s, double k, double r, double sigma, double t)
    {
        if (t <= 0) return Math.Max(0.0, s - k);
        var (d1, d2) = D1D2(s, k, r, sigma, t);
        if (double.IsNaN(d1)) return Math.Max(0.0, s - k);
        return s * Ncdf(d1) - k * Math.Exp(-r * t) * Ncdf(d2);
    }

    private static double BsmPutPrice(double s, double k, double r, double sigma, double t)
    {
        if (t <= 0) return Math.Max(0.0, k - s);
        var (d1, d2) = D1D2(s, k, r, sigma, t);
        if (double.IsNaN(d1)) return Math.Max(0.0, k - s);
        return k * Math.Exp(-r * t) * Ncdf(-d2) - s * Ncdf(-d1);
    }

    private static double BsmCallDelta(double s, double k, double r, double sigma, double t)
    {
        if (t <= 0) return s > k ? 1.0 : 0.0;
        var (d1, _) = D1D2(s, k, r, sigma, t);
        return double.IsNaN(d1) ? 0.0 : Ncdf(d1);
    }

    private static double BsmGamma(double s, double k, double r, double sigma, double t)
    {
        if (t <= 0 || s <= 0 || sigma <= 0) return 0.0;
        var (d1, _) = D1D2(s, k, r, sigma, t);
        return double.IsNaN(d1) ? 0.0 : Npdf(d1) / (s * sigma * Math.Sqrt(t));
    }

    private static double BsmCallTheta(double s, double k, double r, double sigma, double t)
    {
        if (t <= 0) return 0.0;
        var (d1, d2) = D1D2(s, k, r, sigma, t);
        if (double.IsNaN(d1)) return 0.0;
        double term1 = -s * Npdf(d1) * sigma / (2.0 * Math.Sqrt(t));
        double term2 = r * k * Math.Exp(-r * t) * Ncdf(d2);
        return (term1 - term2) / 365.0; // per calendar day
    }

    private static double BsmPutTheta(double s, double k, double r, double sigma, double t)
    {
        if (t <= 0) return 0.0;
        var (d1, d2) = D1D2(s, k, r, sigma, t);
        if (double.IsNaN(d1)) return 0.0;
        double term1 = -s * Npdf(d1) * sigma / (2.0 * Math.Sqrt(t));
        double term2 = r * k * Math.Exp(-r * t) * Ncdf(-d2);
        return (term1 + term2) / 365.0; // per calendar day
    }

    private static double BsmVega(double s, double k, double r, double sigma, double t)
    {
        if (t <= 0) return 0.0;
        var (d1, _) = D1D2(s, k, r, sigma, t);
        return double.IsNaN(d1) ? 0.0 : s * Npdf(d1) * Math.Sqrt(t) / 100.0; // per 1% IV move
    }
}
