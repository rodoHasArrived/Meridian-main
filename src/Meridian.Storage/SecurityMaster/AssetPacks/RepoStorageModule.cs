using Meridian.Contracts.SecurityMaster;
using static Meridian.Storage.SecurityMaster.AssetPacks.SecurityTermsStorageColumns;

namespace Meridian.Storage.SecurityMaster.AssetPacks;

/// <summary>Class-owned relational projection contribution; retained column and reader semantics are unchanged.</summary>
internal static class RepoStorageModule
{
    internal static SecurityTermsStorageContribution Contribution { get; } =
        SecurityTermsStorageContribution.Declarative(new(
            AssetClass: "Repo",
            TableName: "repo_projection",
            Columns:
            [
                Gate("counterparty", "counterparty", SecurityAssetTermFieldType.String),
                Gate("start_date", "startDate", SecurityAssetTermFieldType.Date),
                Gate("end_date", "endDate", SecurityAssetTermFieldType.Date),
                Optional("repo_rate", "repoRate", SecurityAssetTermFieldType.Decimal),
                Optional("collateral_type", "collateralType", SecurityAssetTermFieldType.String),
                Optional("haircut", "haircut", SecurityAssetTermFieldType.Decimal)
            ],
            ChildTables: []));
}
