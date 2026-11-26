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
                SecurityTermsProjectionColumn.Derived("lifecycle_stat", static _ => "Active")
            ],
            ChildTables: []));
}
