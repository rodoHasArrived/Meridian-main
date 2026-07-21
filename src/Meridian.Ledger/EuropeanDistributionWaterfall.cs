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
        decimal priorGpCatchUp = 0m)
    {
        if (contributedCapital < 0m)
            throw new ArgumentOutOfRangeException(nameof(contributedCapital), contributedCapital, "Contributed capital cannot be negative.");
        if (preferredReturnAccrued < 0m)
            throw new ArgumentOutOfRangeException(nameof(preferredReturnAccrued), preferredReturnAccrued, "Preferred return cannot be negative.");
        if (amountToDistribute < 0m)
            throw new ArgumentOutOfRangeException(nameof(amountToDistribute), amountToDistribute, "Amount to distribute cannot be negative.");
        if (carryRate < 0m || carryRate >= 1m)
            throw new ArgumentOutOfRangeException(nameof(carryRate), carryRate, "Carry rate must be in [0, 1).");
        if (catchUpRate <= 0m || catchUpRate > 1m)
            throw new ArgumentOutOfRangeException(nameof(catchUpRate), catchUpRate, "Catch-up rate must be in (0, 1].");
        if (priorReturnOfCapital < 0m || priorPreferredPaid < 0m || priorGpCatchUp < 0m)
            throw new ArgumentOutOfRangeException(nameof(priorReturnOfCapital), "Prior cumulative amounts cannot be negative.");

        ContributedCapital = contributedCapital;
        PreferredReturnAccrued = preferredReturnAccrued;
        AmountToDistribute = amountToDistribute;
        CarryRate = carryRate;
        CatchUpRate = catchUpRate;
        PriorReturnOfCapital = priorReturnOfCapital;
        PriorPreferredPaid = priorPreferredPaid;
        PriorGpCatchUp = priorGpCatchUp;
    }

    public decimal ContributedCapital { get; }

    public decimal PreferredReturnAccrued { get; }

    public decimal AmountToDistribute { get; }

    public decimal CarryRate { get; }

    public decimal CatchUpRate { get; }

    public decimal PriorReturnOfCapital { get; }

    public decimal PriorPreferredPaid { get; }

    public decimal PriorGpCatchUp { get; }
}

/// <summary>One tier's split of a distribution between LP and GP.</summary>
public sealed record EuropeanWaterfallTierAllocation(string Tier, decimal LpAmount, decimal GpAmount);

/// <summary>Result of running one distribution through the European waterfall.</summary>
public sealed record EuropeanWaterfallResult(
    decimal ReturnOfCapital,
    decimal PreferredReturn,
    decimal GpCatchUp,
    decimal LpCarry,
    decimal GpCarry,
    IReadOnlyList<EuropeanWaterfallTierAllocation> Tiers)
{
    /// <summary>Total paid to limited partners this distribution.</summary>
    public decimal LpTotal => ReturnOfCapital + PreferredReturn + LpCarry;

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

        // Tier 3 — GP catch-up. Target so the GP holds CarryRate of profit distributed above the
        // return of capital (preferred + catch-up): catchUpTarget = carry/(1-carry) x preferredPaid.
        var totalPreferredPaid = input.PriorPreferredPaid + preferredReturn;
        var catchUpTarget = input.CarryRate <= 0m
            ? 0m
            : RoundCurrency(input.CarryRate / (1m - input.CarryRate) * totalPreferredPaid);
        var gpCatchUp = 0m;
        var catchUpRemainingTarget = Math.Max(0m, catchUpTarget - input.PriorGpCatchUp);
        if (catchUpRemainingTarget > 0m && remaining > 0m)
        {
            // During catch-up the GP takes CatchUpRate of each dollar; any remainder goes to LPs.
            var catchUpPoolNeeded = input.CatchUpRate <= 0m
                ? 0m
                : RoundCurrency(catchUpRemainingTarget / input.CatchUpRate);
            var catchUpPool = Math.Min(remaining, catchUpPoolNeeded);
            gpCatchUp = RoundCurrency(catchUpPool * input.CatchUpRate);
            var catchUpToLp = catchUpPool - gpCatchUp;
            remaining -= catchUpPool;
            if (gpCatchUp != 0m || catchUpToLp != 0m)
                tiers.Add(new EuropeanWaterfallTierAllocation("GpCatchUp", catchUpToLp, gpCatchUp));
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

        return new EuropeanWaterfallResult(returnOfCapital, preferredReturn, gpCatchUp, lpCarry, gpCarry, tiers);
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
