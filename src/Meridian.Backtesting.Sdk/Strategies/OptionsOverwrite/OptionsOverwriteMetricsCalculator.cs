namespace Meridian.Backtesting.Sdk.Strategies.OptionsOverwrite;

/// <summary>
/// Computes <see cref="OptionsOverwriteMetrics"/> from the strategy's trade history
/// and daily equity curve.
/// </summary>
public static class OptionsOverwriteMetricsCalculator
{
    private const double TradingDaysPerYear = 252.0;

    // ------------------------------------------------------------------ //
    //  Entry point                                                        //
    // ------------------------------------------------------------------ //

    /// <summary>
    /// Derives all performance metrics from the completed trade history and equity curve.
    /// </summary>
    /// <param name="trades">All completed option trade records.</param>
    /// <param name="equityCurve">
    /// Daily equity data: (date, strategy equity, underlying-only equity).
    /// The strategy equity includes both the underlying and accumulated option P&amp;L.
    /// </param>
    /// <param name="riskFreeRate">Annual risk-free rate (for Sharpe / Sortino).</param>
    public static OptionsOverwriteMetrics Calculate(
        IReadOnlyList<OptionsOverwriteTradeRecord> trades,
        IReadOnlyList<(DateOnly Date, decimal StrategyEquity, decimal UnderlyingEquity)> equityCurve,
        double riskFreeRate)
    {
        var curve = equityCurve;

        // ---- Basic trade stats ----------------------------------------
        int total = trades.Count;
        int wins = trades.Count(t => t.IsWin);
        int assigned = trades.Count(t => t.WasAssigned);
        decimal totalPremium = trades.Sum(t => t.TotalNetPnl > 0 ? t.EntryCredit * t.Contracts * t.Multiplier : 0m)
                             + trades.Sum(t => t.TotalNetPnl <= 0 ? t.EntryCredit * t.Contracts * t.Multiplier : 0m);
        totalPremium = trades.Sum(t => t.EntryCredit * t.Contracts * t.Multiplier);
        decimal totalOptionPnl = trades.Sum(t => t.TotalNetPnl);
        double avgHolding = total > 0 ? trades.Average(t => (double)t.HoldingDays) : 0.0;
        double winRate = total > 0 ? (double)wins / total : 0.0;
        double assignmentRate = total > 0 ? (double)assigned / total : 0.0;

        // ---- Return metrics (require equity curve) ---------------------
        if (curve.Count < 2)
        {
            return new OptionsOverwriteMetrics(
                Cagr: 0, AnnualizedVolatility: 0, SharpeRatio: 0, SortinoRatio: 0,
                CalmarRatio: 0, MaxDrawdownPct: 0,
                WinRate: winRate, AssignmentRate: assignmentRate,
                AverageHoldingDays: avgHolding,
                TotalOptionTrades: total, AssignedTrades: assigned,
                TotalPremiumCollected: totalPremium, TotalOptionPnl: totalOptionPnl,
                UpCapture: 1, DownCapture: 1,
                MonthlyVar1Pct: 0, MonthlyVar5Pct: 0, MonthlyCVar5Pct: 0,
                ReturnSkewness: 0, ReturnKurtosis: 0,
                AnnualizedTurnover: 0,
                EquityCurve: curve);
        }

        decimal startEquity = curve[0].StrategyEquity;
        decimal endEquity = curve[^1].StrategyEquity;

        // CAGR
        double calendarDays = (curve[^1].Date.DayNumber - curve[0].Date.DayNumber);
        double years = Math.Max(calendarDays / 365.0, 1.0 / 365.0);
        double cagr = startEquity > 0
            ? Math.Pow((double)(endEquity / startEquity), 1.0 / years) - 1.0
            : 0.0;

        // Daily returns
        double[] strategyReturns = DailyReturns(curve.Select(p => p.StrategyEquity).ToArray());
        double[] underlyingReturns = DailyReturns(curve.Select(p => p.UnderlyingEquity).ToArray());

        double annVol = AnnualizedVol(strategyReturns);
        double sharpe = annVol > 0
            ? (cagr - riskFreeRate) / annVol
            : 0.0;

        double sortino = SortinoRatio(strategyReturns, riskFreeRate);

        double maxDd = MaxDrawdownPct(curve.Select(p => p.StrategyEquity).ToArray());

        double calmar = maxDd > 0 ? cagr / maxDd : 0.0;

        // Up / down capture
        var (upCapture, downCapture) = UpDownCapture(strategyReturns, underlyingReturns);

        // Monthly tail metrics
        var monthlyReturns = MonthlyReturns(curve);
        double var1 = PercentileReturn(monthlyReturns, 0.01);
        double var5 = PercentileReturn(monthlyReturns, 0.05);
        double cvar5 = CVar(monthlyReturns, 0.05);
        double skew = Skewness(monthlyReturns);
        double kurt = ExcessKurtosis(monthlyReturns);

        // Turnover: number of option contracts × multiplier / average portfolio value
        decimal avgEquity = curve.Count > 0 ? curve.Average(p => p.StrategyEquity) : 1m;
        decimal contractNotional = trades.Sum(t => t.Strike * t.Contracts * t.Multiplier);
        double annualTurnover = avgEquity > 0
            ? (double)(contractNotional / avgEquity) / Math.Max(years, 1.0 / 12.0)
            : 0.0;

        return new OptionsOverwriteMetrics(
            Cagr: cagr,
            AnnualizedVolatility: annVol,
            SharpeRatio: sharpe,
            SortinoRatio: sortino,
            CalmarRatio: calmar,
            MaxDrawdownPct: maxDd,
            WinRate: winRate,
            AssignmentRate: assignmentRate,
            AverageHoldingDays: avgHolding,
            TotalOptionTrades: total,
            AssignedTrades: assigned,
            TotalPremiumCollected: totalPremium,
            TotalOptionPnl: totalOptionPnl,
            UpCapture: upCapture,
            DownCapture: downCapture,
            MonthlyVar1Pct: var1,
            MonthlyVar5Pct: var5,
            MonthlyCVar5Pct: cvar5,
            ReturnSkewness: skew,
            ReturnKurtosis: kurt,
            AnnualizedTurnover: annualTurnover,
            EquityCurve: curve);
    }

    // ------------------------------------------------------------------ //
    //  Return series helpers                                              //
    // ------------------------------------------------------------------ //

    private static double[] DailyReturns(decimal[] equityArray)
    {
        if (equityArray.Length < 2)
            return Array.Empty<double>();

        var returns = new double[equityArray.Length - 1];
        for (int i = 1; i < equityArray.Length; i++)
        {
            returns[i - 1] = equityArray[i - 1] > 0
                ? (double)((equityArray[i] - equityArray[i - 1]) / equityArray[i - 1])
                : 0.0;
        }
        return returns;
    }

    private static double AnnualizedVol(double[] dailyReturns)
    {
        if (dailyReturns.Length < 2)
            return 0.0;

        double mean = dailyReturns.Average();
        double variance = dailyReturns.Sum(r => (r - mean) * (r - mean)) / (dailyReturns.Length - 1);
        return Math.Sqrt(variance * TradingDaysPerYear);
    }

    private static double SortinoRatio(double[] dailyReturns, double riskFreeRate)
    {
        if (dailyReturns.Length < 2)
            return 0.0;

        double dailyRfr = riskFreeRate / TradingDaysPerYear;
        double meanExcess = dailyReturns.Average() - dailyRfr;

        var downside = dailyReturns.Where(r => r < dailyRfr).ToArray();
        if (downside.Length == 0)
            return double.PositiveInfinity;

        double downsideVariance = downside.Sum(r => (r - dailyRfr) * (r - dailyRfr)) / downside.Length;
        double downsideVol = Math.Sqrt(downsideVariance * TradingDaysPerYear);

        return downsideVol > 0 ? (meanExcess * TradingDaysPerYear) / downsideVol : 0.0;
    }

    private static double MaxDrawdownPct(decimal[] equityArray)
    {
        if (equityArray.Length == 0)
            return 0.0;

        decimal peak = equityArray[0];
        double maxDd = 0.0;

        foreach (decimal e in equityArray)
        {
            if (e > peak) peak = e;
            double dd = peak > 0 ? (double)((peak - e) / peak) : 0.0;
            if (dd > maxDd) maxDd = dd;
        }
        return maxDd;
    }

    private static (double up, double down) UpDownCapture(double[] stratReturns, double[] benchReturns)
    {
        if (stratReturns.Length == 0 || benchReturns.Length != stratReturns.Length)
            return (1.0, 1.0);

        int n = stratReturns.Length;

        var upS = new List<double>();
        var upB = new List<double>();
        var dnS = new List<double>();
        var dnB = new List<double>();

        for (int i = 0; i < n; i++)
        {
            if (benchReturns[i] > 0) { upS.Add(stratReturns[i]); upB.Add(benchReturns[i]); }
            else if (benchReturns[i] < 0) { dnS.Add(stratReturns[i]); dnB.Add(benchReturns[i]); }
        }

        double upCapture = upB.Count > 0 && upB.Average() != 0
            ? upS.Average() / upB.Average()
            : 1.0;

        double downCapture = dnB.Count > 0 && dnB.Average() != 0
            ? dnS.Average() / dnB.Average()
            : 1.0;

        return (upCapture, downCapture);
    }

    // ------------------------------------------------------------------ //
    //  Monthly tail metrics                                               //
    // ------------------------------------------------------------------ //

    private static double[] MonthlyReturns(
        IReadOnlyList<(DateOnly Date, decimal StrategyEquity, decimal UnderlyingEquity)> curve)
    {
        if (curve.Count < 2)
            return Array.Empty<double>();

        var monthlyReturns = new List<double>();
        var groups = curve
            .GroupBy(p => new YearMonth(p.Date.Year, p.Date.Month))
            .OrderBy(g => g.Key)
            .ToList();

        foreach (var g in groups)
        {
            var sorted = g.OrderBy(p => p.Date).ToList();
            if (sorted.Count < 2)
                continue;

            decimal start = sorted[0].StrategyEquity;
            decimal end = sorted[^1].StrategyEquity;
            if (start > 0)
                monthlyReturns.Add((double)((end - start) / start));
        }

        return monthlyReturns.ToArray();
    }

    private static double PercentileReturn(double[] returns, double p)
    {
        if (returns.Length == 0)
            return 0.0;

        var sorted = returns.OrderBy(r => r).ToArray();
        int idx = (int)Math.Floor(p * sorted.Length);
        return sorted[Math.Clamp(idx, 0, sorted.Length - 1)];
    }

    private static double CVar(double[] returns, double confidenceLevel)
    {
        if (returns.Length == 0)
            return 0.0;

        var sorted = returns.OrderBy(r => r).ToArray();
        int cutoff = Math.Max(1, (int)Math.Floor(confidenceLevel * sorted.Length));
        return sorted.Take(cutoff).Average();
    }

    private static double Skewness(double[] returns)
    {
        int n = returns.Length;
        if (n < 3) return 0.0;

        double mean = returns.Average();
        double std = Math.Sqrt(returns.Sum(r => (r - mean) * (r - mean)) / (n - 1));
        if (std < 1e-12) return 0.0;

        double m3 = returns.Sum(r => Math.Pow(r - mean, 3)) / n;
        return m3 / Math.Pow(std, 3);
    }

    private static double ExcessKurtosis(double[] returns)
    {
        int n = returns.Length;
        if (n < 4) return 0.0;

        double mean = returns.Average();
        double std = Math.Sqrt(returns.Sum(r => (r - mean) * (r - mean)) / (n - 1));
        if (std < 1e-12) return 0.0;

        double m4 = returns.Sum(r => Math.Pow(r - mean, 4)) / n;
        return m4 / Math.Pow(std, 4) - 3.0;
    }

    // ------------------------------------------------------------------ //
    //  Helper types                                                       //
    // ------------------------------------------------------------------ //

    private readonly record struct YearMonth(int Year, int Month) : IComparable<YearMonth>
    {
        public int CompareTo(YearMonth other)
        {
            int c = Year.CompareTo(other.Year);
            return c != 0 ? c : Month.CompareTo(other.Month);
        }
    }
}
