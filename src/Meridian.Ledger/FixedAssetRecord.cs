namespace Meridian.Ledger;

/// <summary>
/// A capitalized fixed asset whose gross cost is depreciated to salvage value over its useful life.
/// </summary>
/// <param name="AssetId">Stable identity used to scope this asset's ledger accounts.</param>
/// <param name="DisplayName">Human-readable asset name for evidence and descriptions.</param>
/// <param name="Currency">ISO 4217 currency the cost and salvage amounts are expressed in.</param>
/// <param name="Cost">Gross capitalized cost of the asset.</param>
/// <param name="SalvageValue">Estimated residual value at the end of the useful life.</param>
/// <param name="InServiceDate">Date the asset was placed in service; anchors the schedule.</param>
/// <param name="UsefulLifeMonths">Depreciable life in whole months.</param>
/// <param name="Method">Depreciation method to apply.</param>
/// <param name="DecliningBalanceFactor">
/// Multiplier applied to the straight-line rate for <see cref="DepreciationMethod.DecliningBalance"/>
/// (e.g. 2.0 = double-declining). Ignored by other methods.
/// </param>
/// <param name="TotalUnits">
/// Total expected lifetime units for <see cref="DepreciationMethod.UnitsOfProduction"/>. Ignored by
/// other methods.
/// </param>
/// <param name="FinancialAccountId">Optional fund/brokerage scope carried into posting metadata.</param>
public sealed record FixedAssetRecord(
    Guid AssetId,
    string DisplayName,
    string Currency,
    decimal Cost,
    decimal SalvageValue,
    DateOnly InServiceDate,
    int UsefulLifeMonths,
    DepreciationMethod Method,
    decimal DecliningBalanceFactor = 2.0m,
    long? TotalUnits = null,
    string? FinancialAccountId = null);
