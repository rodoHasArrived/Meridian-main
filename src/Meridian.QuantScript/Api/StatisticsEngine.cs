namespace Meridian.QuantScript.Api;

/// <summary>
/// Pure math utilities for return series statistics. No I/O, no DI.
/// </summary>
internal static class StatisticsEngine
{
    private const int TradingDaysPerYear = 252;

    internal static double Sharpe(IReadOnlyList<double> dailyReturns, double annualRfr)
    {
        if (dailyReturns.Count == 0)
            return 0;
        var dailyRfr = annualRfr / TradingDaysPerYear;
        var excess = dailyReturns.Select(r => r - dailyRfr).ToList();
        var mean = excess.Average();
        var vol = StdDev(excess);
        return vol == 0 ? 0 : mean * Math.Sqrt(TradingDaysPerYear) / vol;
    }

    internal static double Sortino(IReadOnlyList<double> dailyReturns, double annualRfr)
    {
        if (dailyReturns.Count == 0)
            return 0;
        var dailyRfr = annualRfr / TradingDaysPerYear;
        var excess = dailyReturns.Select(r => r - dailyRfr).ToList();
        var mean = excess.Average();
        var downside = excess.Where(r => r < 0).ToList();
        if (downside.Count == 0)
            return double.PositiveInfinity;
        var downsideDev = Math.Sqrt(downside.Select(r => r * r).Average());
        return downsideDev == 0 ? 0 : mean * Math.Sqrt(TradingDaysPerYear) / downsideDev;
    }

    internal static double AnnualizedVolatility(IReadOnlyList<double> dailyReturns)
    {
        if (dailyReturns.Count < 2)
            return 0;
        return StdDev(dailyReturns) * Math.Sqrt(TradingDaysPerYear);
    }

    internal static double MaxDrawdown(IReadOnlyList<double> dailyReturns)
    {
        if (dailyReturns.Count == 0)
            return 0;
        var dd = DrawdownSeries(dailyReturns);
        return dd.Count == 0 ? 0 : dd.Min();
    }

    internal static IReadOnlyList<double> DrawdownSeries(IReadOnlyList<double> dailyReturns)
    {
        var result = new double[dailyReturns.Count];
        double peak = 1.0;
        double cumulative = 1.0;
        for (var i = 0; i < dailyReturns.Count; i++)
        {
            cumulative *= (1.0 + dailyReturns[i]);
            if (cumulative > peak)
                peak = cumulative;
            result[i] = peak == 0 ? 0 : (cumulative - peak) / peak;
        }
        return result;
    }

    internal static double Beta(IReadOnlyList<double> returns, IReadOnlyList<double> benchmarkReturns)
    {
        if (returns.Count < 2)
            return 0;
        var varBenchmark = Variance(benchmarkReturns);
        if (varBenchmark == 0)
            return 0;
        return Covariance(returns, benchmarkReturns) / varBenchmark;
    }

    internal static double Alpha(IReadOnlyList<double> returns, IReadOnlyList<double> benchmarkReturns, double annualRfr)
    {
        if (returns.Count == 0)
            return 0;
        var dailyRfr = annualRfr / TradingDaysPerYear;
        var beta = Beta(returns, benchmarkReturns);
        var meanReturn = returns.Average() - dailyRfr;
        var meanBenchmark = benchmarkReturns.Average() - dailyRfr;
        return (meanReturn - beta * meanBenchmark) * TradingDaysPerYear;
    }

    internal static double Correlation(IReadOnlyList<double> a, IReadOnlyList<double> b)
    {
        if (a.Count < 2)
            return 0;
        var stdA = StdDev(a);
        var stdB = StdDev(b);
        if (stdA == 0 || stdB == 0)
            return 0;
        return Covariance(a, b) / (stdA * stdB);
    }

    internal static double Skewness(IReadOnlyList<double> values)
    {
        if (values.Count < 3)
            return 0;
        var mean = values.Average();
        var std = StdDev(values);
        if (std == 0)
            return 0;
        var n = values.Count;
        var sum = values.Sum(v => Math.Pow((v - mean) / std, 3));
        return (double)n / ((n - 1) * (n - 2)) * sum;
    }

    internal static double Kurtosis(IReadOnlyList<double> values)
    {
        if (values.Count < 4)
            return 0;
        var mean = values.Average();
        var std = StdDev(values);
        if (std == 0)
            return 0;
        var n = values.Count;
        var sum = values.Sum(v => Math.Pow((v - mean) / std, 4));
        var kurtosis = (double)n * (n + 1) / ((n - 1) * (n - 2) * (n - 3)) * sum;
        var correction = 3.0 * (n - 1) * (n - 1) / ((n - 2) * (n - 3));
        return kurtosis - correction;
    }

    internal static IReadOnlyList<double> RollingMean(IReadOnlyList<double> values, int window)
    {
        var result = new double[values.Count];
        for (var i = 0; i < values.Count; i++)
        {
            if (i < window - 1)
            { result[i] = double.NaN; continue; }
            result[i] = values.Skip(i - window + 1).Take(window).Average();
        }
        return result;
    }

    internal static IReadOnlyList<double> RollingSd(IReadOnlyList<double> values, int window)
    {
        var result = new double[values.Count];
        for (var i = 0; i < values.Count; i++)
        {
            if (i < window - 1)
            { result[i] = double.NaN; continue; }
            result[i] = StdDev(values.Skip(i - window + 1).Take(window).ToList());
        }
        return result;
    }

    internal static double[,] CorrelationMatrix(IReadOnlyList<IReadOnlyList<double>> returnStreams)
    {
        var n = returnStreams.Count;
        var matrix = new double[n, n];
        for (var i = 0; i < n; i++)
            for (var j = 0; j < n; j++)
                matrix[i, j] = i == j ? 1.0 : Correlation(returnStreams[i], returnStreams[j]);
        return matrix;
    }

    internal static double[,] CovarianceMatrix(IReadOnlyList<IReadOnlyList<double>> returnStreams)
    {
        var n = returnStreams.Count;
        var matrix = new double[n, n];
        for (var i = 0; i < n; i++)
            for (var j = 0; j < n; j++)
                matrix[i, j] = Covariance(returnStreams[i], returnStreams[j]);
        return matrix;
    }

    /// <summary>Aligns two series by DateOnly, returns matched pairs.</summary>
    internal static (IReadOnlyList<double> a, IReadOnlyList<double> b) AlignByDate(ReturnSeries a, ReturnSeries b)
    {
        var dictA = a.Points.ToDictionary(p => p.Date, p => p.Value);
        var dictB = b.Points.ToDictionary(p => p.Date, p => p.Value);
        var dates = dictA.Keys.Intersect(dictB.Keys).OrderBy(d => d).ToList();
        return ([.. dates.Select(d => dictA[d])], [.. dates.Select(d => dictB[d])]);
    }

    private static double StdDev(IEnumerable<double> values)
    {
        var list = values.ToList();
        if (list.Count < 2)
            return 0;
        var mean = list.Average();
        return Math.Sqrt(list.Sum(v => (v - mean) * (v - mean)) / (list.Count - 1));
    }

    private static double Variance(IReadOnlyList<double> values)
    {
        if (values.Count < 2)
            return 0;
        var mean = values.Average();
        return values.Sum(v => (v - mean) * (v - mean)) / (values.Count - 1);
    }

    private static double Covariance(IReadOnlyList<double> a, IReadOnlyList<double> b)
    {
        if (a.Count < 2 || b.Count < 2)
            return 0;
        var n = Math.Min(a.Count, b.Count);
        var meanA = a.Take(n).Average();
        var meanB = b.Take(n).Average();
        return Enumerable.Range(0, n).Sum(i => (a[i] - meanA) * (b[i] - meanB)) / (n - 1);
    }
}
