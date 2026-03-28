namespace Meridian.QuantScript.API;

/// <summary>
/// A single return observation associated with a date.
/// </summary>
public sealed record ReturnPoint(DateTime Date, double Value);
