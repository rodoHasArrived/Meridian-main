using Meridian.Contracts.SecurityMaster;
using static Meridian.Storage.SecurityMaster.AssetPacks.SecurityTermsStorageColumns;

namespace Meridian.Storage.SecurityMaster.AssetPacks;

/// <summary>Class-owned relational projection contribution; retained column and reader semantics are unchanged.</summary>
internal static class DepositStorageModule
{
    internal static SecurityTermsStorageContribution Contribution { get; } =
        SecurityTermsStorageContribution.Declarative(new(
            AssetClass: "Deposit",
            TableName: "deposit_projection",
            Columns:
            [
                Gate("deposit_type", "depositType", SecurityAssetTermFieldType.String),
                Gate("institution_name", "institutionName", SecurityAssetTermFieldType.String),
                Optional("maturity_date", "maturity", SecurityAssetTermFieldType.Date),
                Optional("interest_rate", "interestRate", SecurityAssetTermFieldType.Decimal),
                Optional("day_count", "dayCount", SecurityAssetTermFieldType.String, Verbatim),
                Flag("is_callable", "isCallable", defaultWhenAbsent: false)
            ],
            ChildTables: []));
}
