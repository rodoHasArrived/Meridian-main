namespace Meridian.QuantScript.API;

/// <summary>
/// Statistical analysis functions for return series and price data.
/// All methods accept plain double arrays to remain independent of domain types.
/// </summary>
internal static class StatisticsEngine
{
    public static double Mean(double[] values)
    {
        if (values.Length == 0) return double.NaN;
        return values.Sum() / values.Length;
    }

    public static double Variance(double[] values)
    {
        if (values.Length < 2) return double.NaN;
        var mean = Mean(values);
        return values.Sum(v => (v - mean) * (v - mean)) / (values.Length - 1);
    }

    public static double StdDev(double[] values) => Math.Sqrt(Variance(values));

    public static double Skewness(double[] values)
    {
        if (values.Length < 3) return double.NaN;
        var mean = Mean(values);
        var std = StdDev(values);
        if (std == 0) return double.NaN;
        var n = values.Length;
        var sum = values.Sum(v => Math.Pow((v - mean) / std, 3));
        return sum * n / ((n - 1.0) * (n - 2.0));
    }

    public static double Kurtosis(double[] values)
    {
        if (values.Length < 4) return double.NaN;
        var mean = Mean(values);
        var std = StdDev(values);
        if (std == 0) return double.NaN;
        var n = values.Length;
        var sum = values.Sum(v => Math.Pow((v - mean) / std, 4));
        return sum * n * (n + 1) / ((n - 1.0) * (n - 2.0) * (n - 3.0))
               - 3.0 * (n - 1) * (n - 1) / ((n - 2.0) * (n - 3.0));
    }

    public static double Covariance(double[] x, double[] y)
    {
        if (x.Length != y.Length || x.Length < 2) return double.NaN;
        var mx = Mean(x);
        var my = Mean(y);
        return x.Zip(y).Sum(p => (p.First - mx) * (p.Second - my)) / (x.Length - 1);
    }

    public static double Correlation(double[] x, double[] y)
    {
        var cov = Covariance(x, y);
        var sx = StdDev(x);
        var sy = StdDev(y);
        if (sx == 0 || sy == 0) return double.NaN;
        return cov / (sx * sy);
    }

    /// <summary>
    /// Sharpe ratio: (mean - riskFreeRate) / stdDev, annualised assuming daily returns.
    /// </summary>
    public static double SharpeRatio(double[] dailyReturns, double annualRiskFreeRate = 0.04)
    {
        if (dailyReturns.Length < 2) return double.NaN;
        var dailyRfr = annualRiskFreeRate / 252.0;
        var excess = dailyReturns.Select(r => r - dailyRfr).ToArray();
        var std = StdDev(excess);
        if (std == 0) return double.NaN;
        return Mean(excess) / std * Math.Sqrt(252.0);
    }

    /// <summary>
    /// Sortino ratio: (mean - riskFreeRate) / downside deviation, annualised.
    /// </summary>
    public static double SortinoRatio(double[] dailyReturns, double annualRiskFreeRate = 0.04)
    {
        if (dailyReturns.Length < 2) return double.NaN;
        var dailyRfr = annualRiskFreeRate / 252.0;
        var excess = dailyReturns.Select(r => r - dailyRfr).ToArray();
        var negativeReturns = excess.Where(r => r < 0).ToArray();
        if (negativeReturns.Length == 0) return double.PositiveInfinity;
        var downsideDev = Math.Sqrt(negativeReturns.Sum(r => r * r) / negativeReturns.Length) * Math.Sqrt(252.0);
        if (downsideDev == 0) return double.NaN;
        return Mean(excess) * 252.0 / downsideDev;
    }

    /// <summary>Maximum drawdown from peak to trough as a positive fraction.</summary>
    public static double MaxDrawdown(double[] dailyReturns)
    {
        if (dailyReturns.Length == 0) return 0.0;
        var peak = 1.0;
        var equity = 1.0;
        var maxDd = 0.0;
        foreach (var r in dailyReturns)
        {
            equity *= (1.0 + r);
            if (equity > peak) peak = equity;
            var dd = (peak - equity) / peak;
            if (dd > maxDd) maxDd = dd;
        }
        return maxDd;
    }

    /// <summary>Compound Annual Growth Rate.</summary>
    public static double CAGR(double[] dailyReturns, double years)
    {
        if (dailyReturns.Length == 0 || years <= 0) return double.NaN;
        var totalReturn = dailyReturns.Aggregate(1.0, (acc, r) => acc * (1.0 + r));
        return Math.Pow(totalReturn, 1.0 / years) - 1.0;
    }

    /// <summary>Beta of asset relative to benchmark (both daily return arrays).</summary>
    public static double Beta(double[] assetReturns, double[] benchmarkReturns)
    {
        var variance = Variance(benchmarkReturns);
        if (variance == 0 || double.IsNaN(variance)) return double.NaN;
        return Covariance(assetReturns, benchmarkReturns) / variance;
    }

    /// <summary>Jensen's alpha (annualised) relative to benchmark.</summary>
    public static double Alpha(double[] assetReturns, double[] benchmarkReturns, double annualRfr = 0.04)
    {
        var beta = Beta(assetReturns, benchmarkReturns);
        if (double.IsNaN(beta)) return double.NaN;
        var dailyRfr = annualRfr / 252.0;
        return (Mean(assetReturns) - dailyRfr - beta * (Mean(benchmarkReturns) - dailyRfr)) * 252.0;
    }

    /// <summary>Information ratio: active return / tracking error.</summary>
    public static double InformationRatio(double[] assetReturns, double[] benchmarkReturns)
    {
        if (assetReturns.Length != benchmarkReturns.Length) return double.NaN;
        var active = assetReturns.Zip(benchmarkReturns, (a, b) => a - b).ToArray();
        var te = StdDev(active);
        if (te == 0) return double.NaN;
        return Mean(active) / te * Math.Sqrt(252.0);
    }
}
