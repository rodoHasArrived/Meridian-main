namespace Meridian.QuantScript.API;

/// <summary>
/// Fluent builder for constructing equal-weight or custom-weight portfolios from price series.
/// </summary>
public sealed class PortfolioBuilder
{
    private readonly Dictionary<string, PriceSeries> _assets = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Adds an asset price series to the portfolio universe.</summary>
    public PortfolioBuilder AddAsset(string symbol, PriceSeries prices)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol);
        _assets[symbol] = prices ?? throw new ArgumentNullException(nameof(prices));
        return this;
    }

    /// <summary>
    /// Builds an equal-weight portfolio and computes basic expected statistics.
    /// </summary>
    public PortfolioResult Build()
    {
        if (_assets.Count == 0)
            return new PortfolioResult(
                new Dictionary<string, double>(),
                0.0, 0.0, double.NaN);

        var equalWeight = 1.0 / _assets.Count;
        var weights = _assets.Keys
            .ToDictionary(k => k, _ => equalWeight, StringComparer.OrdinalIgnoreCase);

        var allReturns = _assets.Values
            .Select(ps => ps.DailyReturns().ToList().Select(r => r.Value).ToArray())
            .ToArray();

        var portfolioReturns = BuildPortfolioReturns(allReturns, equalWeight);
        var expectedReturn = StatisticsEngine.Mean(portfolioReturns) * 252.0;
        var expectedVolatility = StatisticsEngine.StdDev(portfolioReturns) * Math.Sqrt(252.0);
        var sharpe = StatisticsEngine.SharpeRatio(portfolioReturns);

        return new PortfolioResult(weights, expectedReturn, expectedVolatility, sharpe);
    }

    private static double[] BuildPortfolioReturns(double[][] assetReturns, double weight)
    {
        var minLen = assetReturns.Min(r => r.Length);
        if (minLen == 0) return Array.Empty<double>();

        var result = new double[minLen];
        for (var t = 0; t < minLen; t++)
            result[t] = assetReturns.Sum(r => r[r.Length - minLen + t] * weight);
        return result;
    }
}
