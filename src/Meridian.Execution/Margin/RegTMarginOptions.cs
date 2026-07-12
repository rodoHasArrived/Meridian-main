namespace Meridian.Execution.Margin;

/// <summary>
/// Configuration-bound Reg T margin rates for U.S. equities.
/// </summary>
public sealed class RegTMarginOptions
{
    public const string SectionKey = "Execution:Margin:RegT";

    public decimal LongInitialRate { get; init; } = 0.50m;

    public decimal LongMaintenanceRate { get; init; } = 0.25m;

    public decimal ShortInitialRate { get; init; } = 1.50m;

    public decimal ShortMaintenanceRate { get; init; } = 1.30m;
}
