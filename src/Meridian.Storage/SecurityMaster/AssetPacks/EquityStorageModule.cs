using Meridian.Contracts.SecurityMaster;
using static Meridian.Storage.SecurityMaster.AssetPacks.SecurityTermsStorageColumns;

namespace Meridian.Storage.SecurityMaster.AssetPacks;

/// <summary>Class-owned relational projection contribution; retained column and reader semantics are unchanged.</summary>
internal static class EquityStorageModule
{
    internal static SecurityTermsStorageContribution Contribution { get; } =
        SecurityTermsStorageContribution.Declarative(new(
            AssetClass: "Equity",
            TableName: "equity_projection",
            Columns:
            [
                Optional("share_class", "shareClass", SecurityAssetTermFieldType.String, Verbatim),
                Optional("voting_rights_cat", "votingRightsCat", SecurityAssetTermFieldType.String, Verbatim),
                SecurityTermsProjectionColumn.Derived("classification",
                    static record => SecurityTermReader.ReadEquityClassification(record.AssetSpecificTerms)),
                CommonTerm("exchange_code", "exchange"),
                CommonTerm("country_of_risk", "countryOfRisk"),
                CommonTerm("issuer_name", "issuerName")
            ],
            ChildTables: []));
}
