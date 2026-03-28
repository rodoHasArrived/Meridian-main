namespace Meridian.QuantScript.API;

/// <summary>
/// Represents the weights and expected statistics for a constructed portfolio.
/// </summary>
public sealed record PortfolioResult(
    IReadOnlyDictionary<string, double> Weights,
    double ExpectedReturn,
    double ExpectedVolatility,
    double SharpeRatio);
