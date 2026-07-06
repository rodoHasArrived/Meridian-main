namespace Meridian.Ledger;

/// <summary>One period row in a fixed-asset depreciation schedule.</summary>
/// <param name="PeriodIndex">1-based period ordinal.</param>
/// <param name="PeriodEnd">Last calendar day covered by the period.</param>
/// <param name="OpeningNetBookValue">Net book value at the start of the period.</param>
/// <param name="DepreciationAmount">Depreciation charged in the period.</param>
/// <param name="ClosingNetBookValue">Net book value at the end of the period.</param>
public sealed record DepreciationPeriod(
    int PeriodIndex,
    DateOnly PeriodEnd,
    decimal OpeningNetBookValue,
    decimal DepreciationAmount,
    decimal ClosingNetBookValue);
