namespace Meridian.Execution.Margin;

/// <summary>
/// The margin requirement calculated for a single position or the whole portfolio.
/// </summary>
/// <param name="Symbol">Instrument symbol the requirement applies to, or <c>null</c> for portfolio-level.</param>
/// <param name="NotionalValue">Market value of the position used for the margin calculation.</param>
/// <param name="InitialMargin">
///     Amount of capital required to open (or maintain during the session opening) the position.
/// </param>
/// <param name="MaintenanceMargin">
///     Minimum equity required to hold the position without triggering a margin call.
/// </param>
/// <param name="ExcessLiquidity">
///     Portfolio equity minus the maintenance margin requirement. Negative values indicate
///     a margin-call situation.
/// </param>
public sealed record MarginRequirement(
    string? Symbol,
    decimal NotionalValue,
    decimal InitialMargin,
    decimal MaintenanceMargin,
    decimal ExcessLiquidity)
{
    /// <summary><c>true</c> when the account is in margin-call territory.</summary>
    public bool IsMarginCall => ExcessLiquidity < 0m;

    /// <summary>Initial margin as a percentage of notional value.</summary>
    public decimal InitialMarginRate =>
        NotionalValue == 0m ? 0m : InitialMargin / Math.Abs(NotionalValue);

    /// <summary>Maintenance margin as a percentage of notional value.</summary>
    public decimal MaintenanceMarginRate =>
        NotionalValue == 0m ? 0m : MaintenanceMargin / Math.Abs(NotionalValue);
}
