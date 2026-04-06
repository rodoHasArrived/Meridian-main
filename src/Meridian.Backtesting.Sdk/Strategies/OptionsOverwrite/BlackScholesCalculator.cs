using System.Runtime.CompilerServices;

namespace Meridian.Backtesting.Sdk.Strategies.OptionsOverwrite;

/// <summary>
/// Black-Scholes-Merton option pricing model for European calls.
/// Used to mark-to-market short call positions and compute greeks.
/// American equity options are approximated as European for simplicity;
/// a binomial model should be used for precise early-exercise valuation.
/// </summary>
public static class BlackScholesCalculator
{
    // ------------------------------------------------------------------ //
    //  Standard Normal Distribution helpers                               //
    // ------------------------------------------------------------------ //

    /// <summary>
    /// Cumulative standard normal distribution N(x) via rational approximation
    /// (Abramowitz and Stegun 26.2.17, maximum error ≤ 7.5e-8).
    /// </summary>
    public static double Ncdf(double x)
    {
        const double a1 = 0.319381530;
        const double a2 = -0.356563782;
        const double a3 = 1.781477937;
        const double a4 = -1.821255978;
        const double a5 = 1.330274429;

        double t = 1.0 / (1.0 + 0.2316419 * Math.Abs(x));
        double poly = t * (a1 + t * (a2 + t * (a3 + t * (a4 + t * a5))));
        double result = 1.0 - Npdf(x) * poly;
        return x >= 0 ? result : 1.0 - result;
    }

    /// <summary>Standard normal probability density function N'(x).</summary>
    public static double Npdf(double x)
        => Math.Exp(-0.5 * x * x) / Math.Sqrt(2.0 * Math.PI);

    // ------------------------------------------------------------------ //
    //  BSM core                                                           //
    // ------------------------------------------------------------------ //

    /// <summary>
    /// Computes BSM d1 and d2 parameters.
    /// Returns (double.NaN, double.NaN) when inputs are degenerate.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static (double d1, double d2) D1D2(
        double s, double k, double r, double sigma, double t)
    {
        if (sigma <= 0 || t <= 0 || s <= 0 || k <= 0)
            return (double.NaN, double.NaN);

        double sqrtT = Math.Sqrt(t);
        double d1 = (Math.Log(s / k) + (r + 0.5 * sigma * sigma) * t) / (sigma * sqrtT);
        double d2 = d1 - sigma * sqrtT;
        return (d1, d2);
    }

    /// <summary>
    /// Black-Scholes price for a European call option.
    /// </summary>
    /// <param name="underlyingPrice">Current underlying price S.</param>
    /// <param name="strike">Strike price K.</param>
    /// <param name="riskFreeRate">Continuously compounded risk-free rate r.</param>
    /// <param name="impliedVolatility">Annualised IV σ.</param>
    /// <param name="timeToExpiryYears">Time to expiration in years T.</param>
    /// <returns>BSM call price, or intrinsic value when inputs are degenerate.</returns>
    public static double CallPrice(
        double underlyingPrice,
        double strike,
        double riskFreeRate,
        double impliedVolatility,
        double timeToExpiryYears)
    {
        if (timeToExpiryYears <= 0)
            return Math.Max(0.0, underlyingPrice - strike);

        var (d1, d2) = D1D2(underlyingPrice, strike, riskFreeRate, impliedVolatility, timeToExpiryYears);
        if (double.IsNaN(d1))
            return Math.Max(0.0, underlyingPrice - strike);

        return underlyingPrice * Ncdf(d1)
             - strike * Math.Exp(-riskFreeRate * timeToExpiryYears) * Ncdf(d2);
    }

    /// <summary>
    /// Black-Scholes delta of a European call option (∂C/∂S = N(d1)).
    /// </summary>
    public static double CallDelta(
        double underlyingPrice,
        double strike,
        double riskFreeRate,
        double impliedVolatility,
        double timeToExpiryYears)
    {
        if (timeToExpiryYears <= 0)
            return underlyingPrice > strike ? 1.0 : 0.0;

        var (d1, _) = D1D2(underlyingPrice, strike, riskFreeRate, impliedVolatility, timeToExpiryYears);
        return double.IsNaN(d1) ? 0.0 : Ncdf(d1);
    }

    /// <summary>
    /// Black-Scholes vega of a European call option (∂C/∂σ = S·N'(d1)·√T).
    /// Returns the price sensitivity to a 1-unit (100 %) change in σ.
    /// </summary>
    public static double CallVega(
        double underlyingPrice,
        double strike,
        double riskFreeRate,
        double impliedVolatility,
        double timeToExpiryYears)
    {
        if (timeToExpiryYears <= 0)
            return 0.0;

        var (d1, _) = D1D2(underlyingPrice, strike, riskFreeRate, impliedVolatility, timeToExpiryYears);
        return double.IsNaN(d1) ? 0.0 : underlyingPrice * Npdf(d1) * Math.Sqrt(timeToExpiryYears);
    }

    // ------------------------------------------------------------------ //
    //  Implied Volatility (Newton-Raphson)                               //
    // ------------------------------------------------------------------ //

    /// <summary>
    /// Solves for the implied volatility of a European call given a market price.
    /// Uses Newton-Raphson iteration seeded with the Brenner-Subrahmanyam approximation.
    /// </summary>
    /// <param name="marketPrice">Observed market price (use mid for fair value).</param>
    /// <param name="underlyingPrice">Current underlying price S.</param>
    /// <param name="strike">Strike price K.</param>
    /// <param name="riskFreeRate">Continuously compounded risk-free rate r.</param>
    /// <param name="timeToExpiryYears">Time to expiration in years T.</param>
    /// <param name="tolerance">Convergence tolerance (default 1e-6).</param>
    /// <param name="maxIterations">Maximum Newton-Raphson iterations (default 100).</param>
    /// <returns>Implied volatility, or <c>null</c> when no convergence is achieved.</returns>
    public static double? ImpliedVolatility(
        double marketPrice,
        double underlyingPrice,
        double strike,
        double riskFreeRate,
        double timeToExpiryYears,
        double tolerance = 1e-6,
        int maxIterations = 100)
    {
        if (marketPrice <= 0 || underlyingPrice <= 0 || strike <= 0 || timeToExpiryYears <= 0)
            return null;

        // Clamp market price to arbitrage bounds
        double intrinsic = Math.Max(0.0, underlyingPrice - strike * Math.Exp(-riskFreeRate * timeToExpiryYears));
        if (marketPrice <= intrinsic)
            return null;

        // Brenner-Subrahmanyam approximation as seed
        double sigma = Math.Sqrt(2.0 * Math.PI / timeToExpiryYears) * marketPrice / underlyingPrice;
        sigma = Math.Clamp(sigma, 0.001, 10.0);

        for (int i = 0; i < maxIterations; i++)
        {
            double price = CallPrice(underlyingPrice, strike, riskFreeRate, sigma, timeToExpiryYears);
            double diff = price - marketPrice;

            if (Math.Abs(diff) < tolerance)
                return sigma;

            double vega = CallVega(underlyingPrice, strike, riskFreeRate, sigma, timeToExpiryYears);
            if (Math.Abs(vega) < 1e-12)
                break;

            sigma -= diff / vega;
            sigma = Math.Clamp(sigma, 0.001, 10.0);
        }

        // Return last estimate if reasonably close
        double finalPrice = CallPrice(underlyingPrice, strike, riskFreeRate, sigma, timeToExpiryYears);
        return Math.Abs(finalPrice - marketPrice) < 0.01 ? sigma : null;
    }

    // ------------------------------------------------------------------ //
    //  Mark-to-close helper                                               //
    // ------------------------------------------------------------------ //

    /// <summary>
    /// Computes the current BSM mark-to-close value of a short call position.
    /// Falls back to intrinsic value when IV is unavailable.
    /// </summary>
    public static decimal MarkToClose(
        decimal underlyingPrice,
        decimal strike,
        DateOnly asOf,
        DateOnly expiration,
        double impliedVolatility,
        double riskFreeRate)
    {
        int calendarDays = expiration.DayNumber - asOf.DayNumber;
        if (calendarDays <= 0)
            return Math.Max(0m, underlyingPrice - strike);

        double t = calendarDays / 365.0;
        double callValue = CallPrice(
            (double)underlyingPrice,
            (double)strike,
            riskFreeRate,
            impliedVolatility,
            t);

        return Math.Max(0m, (decimal)callValue);
    }

    /// <summary>
    /// Re-derives the implied volatility from the option's current mid price and
    /// updates the BSM mark accordingly.
    /// Returns the BSM price at the provided or estimated IV.
    /// </summary>
    public static (decimal price, double iv) MarkToCloseWithIv(
        decimal underlyingPrice,
        decimal strike,
        DateOnly asOf,
        DateOnly expiration,
        decimal optionMidPrice,
        double riskFreeRate,
        double? fallbackIv = null)
    {
        int calendarDays = expiration.DayNumber - asOf.DayNumber;
        if (calendarDays <= 0)
        {
            decimal intrinsic = Math.Max(0m, underlyingPrice - strike);
            return (intrinsic, 0.0);
        }

        double t = calendarDays / 365.0;
        double? iv = ImpliedVolatility(
            (double)optionMidPrice,
            (double)underlyingPrice,
            (double)strike,
            riskFreeRate,
            t);

        double usedIv = iv ?? fallbackIv ?? 0.20; // 20 % fallback
        double price = CallPrice((double)underlyingPrice, (double)strike, riskFreeRate, usedIv, t);
        return (Math.Max(0m, (decimal)price), usedIv);
    }
}
