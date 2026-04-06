namespace Meridian.Backtesting.Sdk.Strategies.OptionsOverwrite;

/// <summary>
/// Static scoring methods that rank filtered call candidates by premium quality.
/// </summary>
public static class OptionsOverwriteScoring
{
    // ------------------------------------------------------------------ //
    //  Basic score                                                        //
    // ------------------------------------------------------------------ //

    /// <summary>
    /// Scores by the executable (bid) premium dollar value for the full contract
    /// (<c>bid × multiplier</c>).  Conservative: we receive the bid in practice.
    /// </summary>
    public static double ScoreBasic(OptionCandidateInfo opt)
        => (double)(opt.Bid * opt.Multiplier);

    // ------------------------------------------------------------------ //
    //  Relative score                                                     //
    // ------------------------------------------------------------------ //

    /// <summary>
    /// Scores by relative IV richness:
    /// <list type="number">
    ///   <item>Compute IV residual = opt.IV - surfaceIvRef (model IV at that delta/DTE)</item>
    ///   <item>Multiply by vega-dollar to get expected dollar richness</item>
    ///   <item>Penalise by spread (wide spreads reduce execution quality)</item>
    ///   <item>Add a log-depth bonus to prefer liquid names</item>
    /// </list>
    /// Falls back to <see cref="ScoreBasic"/> when IV or vega data is missing.
    /// </summary>
    /// <param name="opt">Candidate option.</param>
    /// <param name="p">Strategy parameters.</param>
    /// <param name="ivSurfaceRef">
    /// Model IV at the same delta and DTE from a fitted IV surface.
    /// Pass <c>null</c> to fall back to basic scoring.
    /// </param>
    public static double ScoreRelative(
        OptionCandidateInfo opt,
        OptionsOverwriteParams p,
        double? ivSurfaceRef)
    {
        if (ivSurfaceRef is null || opt.ImpliedVolatility is null || opt.Vega is null)
            return ScoreBasic(opt);

        double ivResidual = opt.ImpliedVolatility.Value - ivSurfaceRef.Value;
        double vegaDollar = opt.Vega.Value * ivResidual * opt.Multiplier;

        // Liquidity penalty: 1 when spread is at max; 0 when spread is at zero
        double liqPenalty = p.MaxSpreadPct > 0
            ? Math.Max(0.0, 1.0 - Math.Min(1.0, opt.SpreadPct / p.MaxSpreadPct))
            : 1.0;

        // Depth bonus: log(1 + OI) + log(1 + volume)
        double depthBonus = Math.Log(1.0 + opt.OpenInterest) + Math.Log(1.0 + opt.Volume);

        return vegaDollar * liqPenalty + p.DepthBonusWeight * depthBonus;
    }

    // ------------------------------------------------------------------ //
    //  Best call selection                                                //
    // ------------------------------------------------------------------ //

    /// <summary>
    /// Runs all filters and scores all eligible candidates, returning the best one.
    /// Returns <c>null</c> when no candidate survives the filter stack.
    /// </summary>
    /// <param name="candidates">All call candidates for the underlying.</param>
    /// <param name="p">Strategy parameters.</param>
    /// <param name="underlyingPrice">Current underlying price (for precise ITM / extrinsic checks).</param>
    /// <param name="daysToNextExDiv">Days to the next ex-dividend date, if known.</param>
    /// <param name="nextDividendAmount">Expected dividend amount, if known.</param>
    /// <param name="ivSurfaceRef">
    /// Optional function that returns the model IV at a given (delta, dte) pair.
    /// Used only in <see cref="OverwriteScoringMode.Relative"/> mode.
    /// </param>
    public static OptionCandidateInfo? ChooseBestCall(
        IReadOnlyList<OptionCandidateInfo> candidates,
        OptionsOverwriteParams p,
        decimal underlyingPrice,
        int? daysToNextExDiv = null,
        decimal? nextDividendAmount = null,
        Func<double, int, double?>? ivSurfaceRef = null)
    {
        // 1) Apply all filters
        var eligible = new List<OptionCandidateInfo>(candidates.Count);
        foreach (var opt in candidates)
        {
            // Dividend-assignment-risk filter (precise, with underlying price)
            if (OptionsOverwriteFilters.HasDividendAssignmentRiskWithUnderlyingPrice(
                    opt with
                    {
                        DaysToNextExDiv = daysToNextExDiv,
                        NextDividendAmount = nextDividendAmount
                    },
                    p,
                    underlyingPrice))
                continue;

            if (!OptionsOverwriteFilters.PassesLiquidityFilter(opt, p))
                continue;

            if (!OptionsOverwriteFilters.PassesRiskFilter(opt, p))
                continue;

            eligible.Add(opt);
        }

        if (eligible.Count == 0)
            return null;

        // 2) Score and pick best
        OptionCandidateInfo? best = null;
        double bestScore = double.NegativeInfinity;

        foreach (var opt in eligible)
        {
            double score = p.ScoringMode == OverwriteScoringMode.Relative
                ? ScoreRelative(opt, p, ivSurfaceRef?.Invoke(opt.Delta, opt.DaysToExpiration))
                : ScoreBasic(opt);

            if (score > bestScore)
            {
                bestScore = score;
                best = opt;
            }
        }

        return best;
    }

    // ------------------------------------------------------------------ //
    //  Position sizing                                                    //
    // ------------------------------------------------------------------ //

    /// <summary>
    /// Computes the number of call contracts to write.
    /// One equity option contract covers 100 shares.
    /// Returns 0 when the overwrite would be less than one contract.
    /// </summary>
    /// <param name="underlyingShares">Number of underlying shares held (long).</param>
    /// <param name="p">Strategy parameters (uses <see cref="OptionsOverwriteParams.OverwriteRatio"/>).</param>
    public static int PositionSize(long underlyingShares, OptionsOverwriteParams p)
    {
        if (underlyingShares <= 0 || p.OverwriteRatio <= 0)
            return 0;

        double eligibleShares = underlyingShares * p.OverwriteRatio;
        return (int)Math.Floor(eligibleShares / 100.0);
    }
}
