using static Meridian.Contracts.Ledger.LedgerCurrencyRounding;

namespace Meridian.Ledger;

/// <summary>
/// Inputs for one distribution through a European (whole-fund) waterfall. Cumulative "prior"
/// figures thread multi-distribution state so return of capital, preferred return, and GP catch-up
/// are only paid to their remaining targets across the fund's life.
/// </summary>
public sealed record EuropeanWaterfallInput
{
    public EuropeanWaterfallInput(
        decimal contributedCapital,
        decimal preferredReturnAccrued,
        decimal amountToDistribute,
        decimal carryRate,
        decimal catchUpRate = 1m,
        decimal priorReturnOfCapital = 0m,
        decimal priorPreferredPaid = 0m,
        decimal priorGpCatchUp = 0m,
        decimal priorCatchUpPool = 0m)
    {
        if (contributedCapital < 0m)
            throw new ArgumentOutOfRangeException(nameof(contributedCapital), contributedCapital, "Contributed capital cannot be negative.");
        if (preferredReturnAccrued < 0m)
            throw new ArgumentOutOfRangeException(nameof(preferredReturnAccrued), preferredReturnAccrued, "Preferred return cannot be negative.");
        if (amountToDistribute < 0m)
            throw new ArgumentOutOfRangeException(nameof(amountToDistribute), amountToDistribute, "Amount to distribute cannot be negative.");
        if (carryRate < 0m || carryRate >= 1m)
            throw new ArgumentOutOfRangeException(nameof(carryRate), carryRate, "Carry rate must be in [0, 1).");
        // A catch-up rate below the carry rate can never bring the GP to its carry share, so the
        // tier would silently be skipped and the residual carry rate would overpay the GP.
        // Equality is the boundary that stays valid: it is how fund terms with no special GP
        // catch-up are expressed, and the calculation treats it as the no-catch-up case.
        if (catchUpRate < carryRate || catchUpRate > 1m)
            throw new ArgumentOutOfRangeException(nameof(catchUpRate), catchUpRate, "Catch-up rate must be at least the carry rate and no more than 1.");
        if (priorReturnOfCapital < 0m || priorPreferredPaid < 0m || priorGpCatchUp < 0m)
            throw new ArgumentOutOfRangeException(nameof(priorReturnOfCapital), "Prior cumulative amounts cannot be negative.");
        if (priorCatchUpPool < 0m)
            throw new ArgumentOutOfRangeException(nameof(priorCatchUpPool), priorCatchUpPool, "Prior catch-up pool cannot be negative.");

        ContributedCapital = contributedCapital;
        PreferredReturnAccrued = preferredReturnAccrued;
        AmountToDistribute = amountToDistribute;
        CarryRate = carryRate;
        CatchUpRate = catchUpRate;
        PriorReturnOfCapital = priorReturnOfCapital;
        PriorPreferredPaid = priorPreferredPaid;
        PriorGpCatchUp = priorGpCatchUp;
        PriorCatchUpPool = priorCatchUpPool;
    }

    public decimal ContributedCapital { get; }

    public decimal PreferredReturnAccrued { get; }

    public decimal AmountToDistribute { get; }

    public decimal CarryRate { get; }

    public decimal CatchUpRate { get; }

    public decimal PriorReturnOfCapital { get; }

    public decimal PriorPreferredPaid { get; }

    public decimal PriorGpCatchUp { get; }

    /// <summary>
    /// Cumulative catch-up pool (LP + GP) already distributed. When threaded across multiple
    /// distributions this preserves the exact prior pool; if left zero it is estimated from
    /// <see cref="PriorGpCatchUp"/>, which can drift by a cent under a sub-100% catch-up rate.
    /// </summary>
    public decimal PriorCatchUpPool { get; }
}

/// <summary>One tier's split of a distribution between LP and GP.</summary>
public sealed record EuropeanWaterfallTierAllocation(string Tier, decimal LpAmount, decimal GpAmount);

/// <summary>Result of running one distribution through the European waterfall.</summary>
public sealed record EuropeanWaterfallResult(
    decimal ReturnOfCapital,
    decimal PreferredReturn,
    decimal GpCatchUp,
    decimal LpCatchUp,
    decimal LpCarry,
    decimal GpCarry,
    IReadOnlyList<EuropeanWaterfallTierAllocation> Tiers)
{
    /// <summary>Total paid to limited partners this distribution.</summary>
    public decimal LpTotal => ReturnOfCapital + PreferredReturn + LpCatchUp + LpCarry;

    /// <summary>Total paid to the general partner this distribution (catch-up plus carry).</summary>
    public decimal GpTotal => GpCatchUp + GpCarry;

    /// <summary>Total distributed across LP and GP.</summary>
    public decimal Distributed => LpTotal + GpTotal;
}

/// <summary>
/// Runs a European (whole-fund) distribution waterfall with automatic preferred-return and GP
/// catch-up solving: return of capital, then preferred return, then a GP catch-up computed to bring
/// the GP to its carry share of profit, then the carried-interest split. Pure and deterministic —
/// callers no longer pre-compute the pref or catch-up amounts.
/// </summary>
public static class EuropeanDistributionWaterfall
{
    public static EuropeanWaterfallResult Distribute(EuropeanWaterfallInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var remaining = input.AmountToDistribute;
        var tiers = new List<EuropeanWaterfallTierAllocation>();

        // Tier 1 — return of capital to LPs.
        var returnOfCapital = TakeToLp(
            ref remaining,
            Math.Max(0m, input.ContributedCapital - input.PriorReturnOfCapital),
            "ReturnOfCapital",
            tiers);

        // Tier 2 — preferred return to LPs.
        var preferredReturn = TakeToLp(
            ref remaining,
            Math.Max(0m, input.PreferredReturnAccrued - input.PriorPreferredPaid),
            "PreferredReturn",
            tiers);

        // Tier 3 — GP catch-up. Solve for the catch-up POOL (LP + GP) that brings the GP to CarryRate
        // of the profit distributed above return of capital (preferred + catch-up), accounting for the
        // LP leakage of (1 - CatchUpRate) per catch-up dollar when catch-up is not 100% to the GP:
        //   CatchUpRate * pool = CarryRate * (preferred + pool)
        //   => pool = CarryRate * preferred / (CatchUpRate - CarryRate)
        // With CatchUpRate == 1 this reduces to carry/(1-carry) * preferred. When CatchUpRate does not
        // exceed CarryRate the GP can never reach its share, so no catch-up is attempted.
        var totalPreferredPaid = input.PriorPreferredPaid + preferredReturn;
        var gpCatchUp = 0m;
        var lpCatchUp = 0m;
        if (input.CarryRate > 0m && input.CatchUpRate > input.CarryRate && remaining > 0m)
        {
            var targetPoolTotal = RoundCurrency(
                input.CarryRate * totalPreferredPaid / (input.CatchUpRate - input.CarryRate));
            // Prefer the exact prior pool when the caller threads it; otherwise estimate it from the
            // rounded prior GP catch-up, which can drift by a cent at sub-100% catch-up rates.
            var priorPool = input.PriorCatchUpPool > 0m
                ? input.PriorCatchUpPool
                : RoundCurrency(input.PriorGpCatchUp / input.CatchUpRate);
            var remainingPoolTarget = Math.Max(0m, targetPoolTotal - priorPool);
            var catchUpPool = Math.Min(remaining, remainingPoolTarget);
            if (catchUpPool > 0m)
            {
                gpCatchUp = RoundCurrency(catchUpPool * input.CatchUpRate);
                lpCatchUp = catchUpPool - gpCatchUp;
                remaining -= catchUpPool;
                tiers.Add(new EuropeanWaterfallTierAllocation("GpCatchUp", lpCatchUp, gpCatchUp));
            }
        }

        // Tier 4 — residual carried-interest split.
        var lpCarry = 0m;
        var gpCarry = 0m;
        if (remaining > 0m)
        {
            gpCarry = RoundCurrency(remaining * input.CarryRate);
            lpCarry = remaining - gpCarry;
            remaining = 0m;
            tiers.Add(new EuropeanWaterfallTierAllocation("CarriedInterest", lpCarry, gpCarry));
        }

        return new EuropeanWaterfallResult(returnOfCapital, preferredReturn, gpCatchUp, lpCatchUp, lpCarry, gpCarry, tiers);
    }

    private static decimal TakeToLp(
        ref decimal remaining,
        decimal target,
        string tier,
        List<EuropeanWaterfallTierAllocation> tiers)
    {
        if (target <= 0m || remaining <= 0m)
            return 0m;

        var taken = Math.Min(remaining, target);
        remaining -= taken;
        tiers.Add(new EuropeanWaterfallTierAllocation(tier, taken, 0m));
        return taken;
    }
}
