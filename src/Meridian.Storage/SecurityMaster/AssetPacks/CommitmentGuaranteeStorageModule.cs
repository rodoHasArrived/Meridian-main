using Meridian.Contracts.SecurityMaster;
using static Meridian.Storage.SecurityMaster.AssetPacks.SecurityTermsStorageColumns;

namespace Meridian.Storage.SecurityMaster.AssetPacks;

/// <summary>Class-owned relational projection contribution; retained column and reader semantics are unchanged.</summary>
internal static class CommitmentGuaranteeStorageModule
{
    internal static SecurityTermsStorageContribution Contribution { get; } =
        SecurityTermsStorageContribution.Declarative(new(
            AssetClass: "CommitmentGuarantee",
            TableName: "commitment_guarantee_projection",
            Columns:
            [
                Gate("counterparty", "counterparty", SecurityAssetTermFieldType.String),
                Optional("beneficiary", "beneficiary", SecurityAssetTermFieldType.String),
                Gate("committed_amount", "committedAmount", SecurityAssetTermFieldType.Decimal),
                Optional("unfunded_amount", "unfundedAmount", SecurityAssetTermFieldType.Decimal),
                Gate("effective_date", "effectiveDate", SecurityAssetTermFieldType.Date),
                Optional("expiry_date", "expiryDate", SecurityAssetTermFieldType.Date),
                Optional("fee_rate", "feeRate", SecurityAssetTermFieldType.Decimal),
                Optional("collateral", "collateral", SecurityAssetTermFieldType.String)
            ],
            ChildTables:
            [
                // Same element shape as the direct-loan covenant list: a typed covenant with a prose
                // threshold ("$5m", "4.5x") that must never be coerced to a number.
                new(
                    TableName: "commitment_guarantee_covenant_projection",
                    TermKey: "covenants",
                    Columns:
                    [
                        new("covenant_type", "covenantType", SecurityAssetTermFieldType.String, Required: true),
                        new("threshold", "threshold", SecurityAssetTermFieldType.String, Required: true),
                        new("notes", "notes", SecurityAssetTermFieldType.String)
                    ])
            ]));
}
