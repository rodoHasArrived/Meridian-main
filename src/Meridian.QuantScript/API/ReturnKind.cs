namespace Meridian.QuantScript.API;

/// <summary>
/// Discriminates the type of return calculation.
/// </summary>
public enum ReturnKind
{
    /// <summary>Simple (arithmetic) return: (P_t - P_{t-1}) / P_{t-1}.</summary>
    Simple,

    /// <summary>Logarithmic return: ln(P_t / P_{t-1}).</summary>
    Log
}
