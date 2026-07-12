namespace Meridian.Ledger;

/// <summary>
/// Inputs for a single fixed-asset depreciation journal projection. Amounts are expressed in the
/// ledger book currency for the asset's accounts.
/// </summary>
/// <param name="AssetId">Identity used to scope the depreciation expense and accumulated-depreciation accounts.</param>
/// <param name="CostAccount">The asset's gross-cost account; validated to be an asset account.</param>
/// <param name="DepreciationAmount">Depreciation charged for the period (must be positive).</param>
/// <param name="FinancialAccountId">Optional fund/brokerage scope carried into posting metadata.</param>
/// <param name="Description">Optional description override for the projected journal.</param>
public sealed record DepreciationInput(
    Guid AssetId,
    LedgerAccount CostAccount,
    decimal DepreciationAmount,
    string? FinancialAccountId = null,
    string? Description = null);
