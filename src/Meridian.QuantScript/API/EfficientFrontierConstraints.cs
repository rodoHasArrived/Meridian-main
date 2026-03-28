namespace Meridian.QuantScript.API;

/// <summary>
/// Stub constraints record for future efficient-frontier optimisation.
/// </summary>
public sealed record EfficientFrontierConstraints(
    double? MinWeight = null,
    double? MaxWeight = null,
    double? TargetReturn = null,
    double? TargetVolatility = null);
