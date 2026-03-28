namespace Meridian.QuantScript.Api;

/// <summary>Portfolio optimisation target constraints for <see cref="PortfolioBuilder.EfficientFrontier"/>.</summary>
public sealed class EfficientFrontierConstraints
{
    public double TargetReturn { get; init; }
    public double? MinWeight { get; init; } = 0.0;
    public double? MaxWeight { get; init; } = 1.0;
}
