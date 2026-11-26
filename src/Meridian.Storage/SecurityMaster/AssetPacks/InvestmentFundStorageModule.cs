using Meridian.Contracts.SecurityMaster;
using static Meridian.Storage.SecurityMaster.AssetPacks.SecurityTermsStorageColumns;

namespace Meridian.Storage.SecurityMaster.AssetPacks;

/// <summary>Class-owned relational projection contribution; retained column and reader semantics are unchanged.</summary>
internal static class InvestmentFundStorageModule
{
    internal static SecurityTermsStorageContribution Contribution { get; } =
        SecurityTermsStorageContribution.Declarative(new(
            AssetClass: "InvestmentFund",
            TableName: "investment_fund_projection",
            Columns:
            [
                // Every InvestmentFund term is optional, so the projection has no gate: a fund row
                // exists for every record of the class and reads null where a term is unknown.
                Optional("fund_type", "fundType", SecurityAssetTermFieldType.String),
                Optional("fund_family", "fundFamily", SecurityAssetTermFieldType.String),
                Optional("nav_currency", "navCurrency", SecurityAssetTermFieldType.String, SecurityTermsProjectionTextNormalization.TrimUpperInvariant),
                Optional("distribution_policy", "distributionPolicy", SecurityAssetTermFieldType.String),
                Optional("is_stable_nav", "isStableNav", SecurityAssetTermFieldType.Boolean),
                Optional("pricing_source", "pricingSource", SecurityAssetTermFieldType.String)
            ],
            ChildTables: []));
}
