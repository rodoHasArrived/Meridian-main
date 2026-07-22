using static Meridian.Contracts.Ledger.LedgerCurrencyRounding;

namespace Meridian.Ledger;

/// <summary>Result of a carried-interest clawback test at (or toward) fund wind-down.</summary>
public sealed record ClawbackResult(
    decimal CumulativeProfit,
    decimal GpCarryReceived,
    decimal GpCarryEntitled,
    decimal ClawbackDue)
{
    /// <summary>True when the GP has received more carry than its final entitlement.</summary>
    public bool HasClawback => ClawbackDue > 0m;
}

/// <summary>
/// Computes the general partner's end-of-life carried-interest clawback: the excess of carry the GP
/// actually received over the carry it is entitled to on cumulative realized profit. Guards against
/// early-deal winners over-distributing carry that later losses would have clawed back. Pure and
/// deterministic.
/// </summary>
public static class CarriedInterestClawbackCalculator
{
    /// <summary>
    /// Computes the clawback from cumulative distributions and contributions.
    /// </summary>
    /// <param name="cumulativeContributedCapital">Total LP capital contributed over the fund's life.</param>
    /// <param name="cumulativeDistributions">Total distributed to LPs and GP over the fund's life.</param>
    /// <param name="gpCarryReceived">Total carry (catch-up plus carried interest) the GP received.</param>
    /// <param name="carryRate">The agreed GP carry rate.</param>
    public static ClawbackResult Compute(
        decimal cumulativeContributedCapital,
        decimal cumulativeDistributions,
        decimal gpCarryReceived,
        decimal carryRate)
    {
        if (cumulativeContributedCapital < 0m)
            throw new ArgumentOutOfRangeException(nameof(cumulativeContributedCapital), cumulativeContributedCapital, "Contributed capital cannot be negative.");
        if (cumulativeDistributions < 0m)
            throw new ArgumentOutOfRangeException(nameof(cumulativeDistributions), cumulativeDistributions, "Distributions cannot be negative.");
        if (gpCarryReceived < 0m)
            throw new ArgumentOutOfRangeException(nameof(gpCarryReceived), gpCarryReceived, "GP carry received cannot be negative.");
        if (carryRate < 0m || carryRate >= 1m)
            throw new ArgumentOutOfRangeException(nameof(carryRate), carryRate, "Carry rate must be in [0, 1).");

        // Profit is distributions above the return of contributed capital, floored at zero.
        var cumulativeProfit = Math.Max(0m, cumulativeDistributions - cumulativeContributedCapital);
        var entitled = RoundCurrency(cumulativeProfit * carryRate);
        var clawbackDue = Math.Max(0m, RoundCurrency(gpCarryReceived - entitled));

        return new ClawbackResult(cumulativeProfit, gpCarryReceived, entitled, clawbackDue);
    }
}
