using Meridian.Contracts.SecurityMaster;
using static Meridian.Storage.SecurityMaster.AssetPacks.SecurityTermsStorageColumns;

namespace Meridian.Storage.SecurityMaster.AssetPacks;

/// <summary>Class-owned relational projection contribution; retained column and reader semantics are unchanged.</summary>
internal static class SwapStorageModule
{
    internal static SecurityTermsStorageContribution Contribution { get; } =
        SecurityTermsStorageContribution.Declarative(new(
            AssetClass: "Swap",
            TableName: "swap_projection",
            Columns:
            [
                SecurityTermsProjectionColumn.Derived("swap_type", static record =>
                    PostgresSecurityMasterStore.DeriveSwapType(record.AssetSpecificTerms)),
                Gate("effective_date", "effectiveDate", SecurityAssetTermFieldType.Date),
                Gate("maturity_date", "maturityDate", SecurityAssetTermFieldType.Date),
                SecurityTermsProjectionColumn.Derived("lifecycle_stat", static record => SwapLifecycleState(record))
            ],
            ChildTables: []));

    /// <summary>
    /// Terminated once the record has an effective-to date, matured once its maturity date has
    /// passed, else active. The values match the states <c>PostgresSwapReferenceProjectionStore</c>
    /// already excludes from its maturing-before query; a constant "Active" left matured swaps
    /// looking live in relational reports.
    /// </summary>
    private static string SwapLifecycleState(SecurityProjectionRecord record)
    {
        if (record.EffectiveTo.HasValue)
        {
            return "Terminated";
        }

        return PostgresSecurityMasterStore.GetOptionalDateOnly(record.AssetSpecificTerms, "maturityDate") is { } maturity
            && maturity < DateOnly.FromDateTime(DateTime.UtcNow)
                ? "Matured"
                : "Active";
    }
}
