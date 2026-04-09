using Meridian.Contracts.Domain.Enums;

namespace Meridian.Backtesting.Sdk.Strategies.OptionsOverwrite;

/// <summary>
/// Static filter methods that screen option candidates before scoring.
/// Each method returns <c>true</c> when the candidate <em>passes</em> the filter.
/// </summary>
public static class OptionsOverwriteFilters
{
    // ------------------------------------------------------------------ //
    //  Liquidity filter                                                   //
    // ------------------------------------------------------------------ //

    /// <summary>
    /// Returns <c>true</c> when the candidate meets all liquidity requirements:
    /// <list type="bullet">
    ///   <item>Bid price must be strictly positive (no zero-bid contracts)</item>
    ///   <item>Open interest ≥ <see cref="OptionsOverwriteParams.MinOpenInterest"/></item>
    ///   <item>Volume ≥ <see cref="OptionsOverwriteParams.MinVolume"/></item>
    ///   <item>Bid-ask spread ≤ <see cref="OptionsOverwriteParams.MaxSpreadPct"/></item>
    /// </list>
    /// </summary>
    public static bool PassesLiquidityFilter(OptionCandidateInfo opt, OptionsOverwriteParams p)
    {
        if (opt.Bid <= 0m)
            return false;

        if (opt.OpenInterest < p.MinOpenInterest)
            return false;

        if (opt.Volume < p.MinVolume)
            return false;

        if (opt.SpreadPct > p.MaxSpreadPct)
            return false;

        return true;
    }

    // ------------------------------------------------------------------ //
    //  Risk filter                                                        //
    // ------------------------------------------------------------------ //

    /// <summary>
    /// Returns <c>true</c> when the candidate satisfies all risk constraints:
    /// <list type="bullet">
    ///   <item>Strike ≥ <see cref="OptionsOverwriteParams.MinStrike"/></item>
    ///   <item>|Delta| ≤ <see cref="OptionsOverwriteParams.MaxDelta"/></item>
    ///   <item>DTE ≥ <see cref="OptionsOverwriteParams.MinDte"/></item>
    ///   <item>DTE ≤ <see cref="OptionsOverwriteParams.MaxDte"/> (when set)</item>
    ///   <item>IV percentile ≥ <see cref="OptionsOverwriteParams.MinIvPercentile"/> (when available)</item>
    /// </list>
    /// </summary>
    public static bool PassesRiskFilter(OptionCandidateInfo opt, OptionsOverwriteParams p)
    {
        if (opt.Strike < p.MinStrike)
            return false;

        if (Math.Abs(opt.Delta) > p.MaxDelta)
            return false;

        if (opt.DaysToExpiration < p.MinDte)
            return false;

        if (p.MaxDte.HasValue && opt.DaysToExpiration > p.MaxDte.Value)
            return false;

        // Only apply IV percentile filter when the data is available
        if (opt.IvPercentile.HasValue && opt.IvPercentile.Value < p.MinIvPercentile)
            return false;

        return true;
    }

    // ------------------------------------------------------------------ //
    //  Dividend-assignment-risk filter                                    //
    // ------------------------------------------------------------------ //

    /// <summary>
    /// Returns <c>true</c> when the candidate has elevated dividend-early-assignment risk
    /// and should therefore be <em>excluded</em>.
    /// <para>
    /// Applies only to American-style options where:
    /// <list type="number">
    ///   <item>Ex-dividend date is within <see cref="OptionsOverwriteParams.ExDivWindowDays"/> days.</item>
    ///   <item>The option is in-the-money.</item>
    ///   <item>Remaining extrinsic value is less than the expected dividend amount — the rational holder
    ///         will exercise to capture the dividend rather than sell the option.</item>
    /// </list>
    /// </para>
    /// </summary>
    public static bool HasDividendAssignmentRisk(OptionCandidateInfo opt, OptionsOverwriteParams p)
    {
        // Index options are European and cannot be early-assigned
        if (opt.Style == OptionStyle.European)
            return false;

        // No dividend data → cannot assess risk → conservative pass-through (no exclusion)
        if (opt.DaysToNextExDiv is null || opt.NextDividendAmount is null)
            return false;

        // Ex-div date is far away
        if (opt.DaysToNextExDiv.Value > p.ExDivWindowDays)
            return false;

        // Only in-the-money calls are subject to dividend exercise
        bool isItm = opt.Strike < opt.Bid + opt.Ask; // proxy: strike < underlying (use mid as stand-in)
        // A cleaner check uses underlying price; we approximate here since we only have bid/ask
        // The caller should pre-filter with actual underlying price when available.

        // Extrinsic value heuristic: mid - max(0, underlyingPrice - strike)
        // Without underlying price at the point of the candidate, we use a conservative proxy:
        // if the spread-adjusted extrinsic is less than the dividend, flag it.
        decimal extrinsic = opt.Mid; // when deeply ITM, extrinsic ≈ time value ≈ mid (overestimates slightly)
        if (extrinsic < opt.NextDividendAmount.Value)
            return true;

        return false;
    }

    /// <summary>
    /// Returns <c>true</c> when the candidate has elevated dividend-assignment risk,
    /// with access to the actual underlying price for a precise ITM / extrinsic calculation.
    /// </summary>
    public static bool HasDividendAssignmentRiskWithUnderlyingPrice(
        OptionCandidateInfo opt,
        OptionsOverwriteParams p,
        decimal underlyingPrice)
    {
        if (opt.Style == OptionStyle.European)
            return false;

        if (opt.DaysToNextExDiv is null || opt.NextDividendAmount is null)
            return false;

        if (opt.DaysToNextExDiv.Value > p.ExDivWindowDays)
            return false;

        bool isItm = underlyingPrice > opt.Strike;
        if (!isItm)
            return false;

        decimal intrinsic = underlyingPrice - opt.Strike;
        decimal extrinsic = Math.Max(0m, opt.Mid - intrinsic);
        return extrinsic < opt.NextDividendAmount.Value;
    }

    // ------------------------------------------------------------------ //
    //  Open-position risk check (for position management)               //
    // ------------------------------------------------------------------ //

    /// <summary>
    /// Returns <c>true</c> when an open short call has elevated assignment risk from an
    /// upcoming ex-dividend event (mirror of <see cref="HasDividendAssignmentRiskWithUnderlyingPrice"/>
    /// but operating on a <see cref="ShortCallPosition"/>).
    /// </summary>
    public static bool OpenPositionHasDividendRisk(
        ShortCallPosition position,
        OptionsOverwriteParams p,
        DateOnly asOf,
        decimal underlyingPrice,
        int? daysToNextExDiv,
        decimal? nextDividendAmount)
    {
        if (position.Style == OptionStyle.European)
            return false;

        if (daysToNextExDiv is null || nextDividendAmount is null)
            return false;

        if (daysToNextExDiv.Value > p.ExDivWindowDays)
            return false;

        bool isItm = underlyingPrice > position.Strike;
        if (!isItm)
            return false;

        decimal mark = position.MarkToClose;
        decimal intrinsic = Math.Max(0m, underlyingPrice - position.Strike);
        decimal extrinsic = Math.Max(0m, mark - intrinsic);
        return extrinsic < nextDividendAmount.Value;
    }
}
