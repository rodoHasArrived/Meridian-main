namespace Meridian.Ledger;

/// <summary>
/// Builds balanced fixed-asset depreciation journal lines: a debit to depreciation expense and a
/// credit to the accumulated-depreciation contra-asset, scoped per asset. This is the depreciation
/// analogue of <see cref="FixedIncomeAmortizationProjector"/>.
/// </summary>
public static class FixedAssetDepreciationProjector
{
    /// <summary>Projects balanced journal lines for one fixed-asset depreciation event.</summary>
    public static DepreciationProjection Project(DepreciationInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(input.CostAccount);

        if (input.AssetId == Guid.Empty)
            throw new ArgumentException("Depreciation asset id is required.", nameof(input));

        if (input.DepreciationAmount < 0m)
            throw new ArgumentOutOfRangeException(nameof(input), "Depreciation amount cannot be negative.");

        if (input.DepreciationAmount == 0m)
            throw new ArgumentException("Depreciation amount must be non-zero.", nameof(input));

        if (input.CostAccount.AccountType != LedgerAccountType.Asset)
        {
            throw new ArgumentException(
                "Fixed-asset cost account must be an asset account for capitalized assets.",
                nameof(input));
        }

        var scope = input.AssetId.ToString("D");
        var depreciationExpense = LedgerAccounts.DepreciationExpenseFor(scope);
        var accumulatedDepreciation = LedgerAccounts.AccumulatedDepreciationFor(scope);

        var lines = new List<(LedgerAccount account, decimal debit, decimal credit)>
        {
            (depreciationExpense, input.DepreciationAmount, 0m),
            (accumulatedDepreciation, 0m, input.DepreciationAmount),
        };

        var description = string.IsNullOrWhiteSpace(input.Description)
            ? $"Depreciation for fixed asset {scope}"
            : input.Description.Trim();

        return new DepreciationProjection(input.AssetId, description, lines);
    }
}
