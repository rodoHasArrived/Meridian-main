using Meridian.Contracts.Domain.Enums;
using static Meridian.Contracts.SecurityMaster.SecurityAssetModuleSchema;
using static Meridian.Contracts.SecurityMaster.SecurityAssetModuleRates;
using static Meridian.Contracts.SecurityMaster.SecurityAssetModuleCodec;
using static Meridian.Contracts.SecurityMaster.SecurityAssetModuleEconomics;

namespace Meridian.Contracts.SecurityMaster;

/// <summary>Compile-time contract contributions for PrivateFundInterest.</summary>
internal static class PrivateFundInterestAssetModule
{
    private const string AssetClass = SecurityAssetClassNames.PrivateFundInterest;

    internal static SecurityAssetClassModule Create()
        => new(
            Descriptor: new(
                AssetClass: AssetClass,
                AssetPackIds: ["private-fund-partnership"],
                LotUnit: SecurityLotUnitKind.Commitment,
                CashFlowBehavior: SecurityCashFlowBehavior.ProviderSupplied,
                ProjectionKind: SecurityTermsProjectionKind.Declarative,
                SupportsBasicCreateWorkflow: false,
                PreferredIdentifierKinds:
                [
                    SecurityIdentifierKind.InternalCode,
                    SecurityIdentifierKind.Lei,
                    SecurityIdentifierKind.ProviderSymbol
                ],
                AssetOperationsCapabilities: AssetOperationsCapabilitySet.AlternativeAssetOperations,
                SupportsProfileBackedTerms: true,
                // Spellings the private-fund pack planned and Asset Operations used to accept by name;
                // existing subjects carrying them keep resolving to this class.
                Aliases: ["PrivateFund", "PartnershipInterest", "LimitedPartnershipInterest"])
            {
                AccountingOutsideSlice = true,
                SupportsPrivateCapitalRecords = true,
                ClassificationLabels = ["PrivateFunds"],
                ReclassifyingProfileIds = ["private-fund-interest"]
            },
            Fields: [
                Req("gpSponsor", SecurityAssetTermFieldType.String),
                Req("strategy", SecurityAssetTermFieldType.String),
                Req("vintage", SecurityAssetTermFieldType.Integer),
                Req("commitment", SecurityAssetTermFieldType.Decimal),
                Opt("fundedAmount", SecurityAssetTermFieldType.Decimal),
                Opt("unfundedAmount", SecurityAssetTermFieldType.Decimal),
                Req("navDate", SecurityAssetTermFieldType.Date),
                Opt("lockup", SecurityAssetTermFieldType.String)
            ],
            SchemaOrder: 17,
            ElementShapes: [])
        {
            CodecBinding = ResolvedClass(AssetClass,
                B("PrivateAssetValuation.Vintage", "vintage", SecurityTermRead.RequiredInt),
                B("PrivateAssetValuation.NavDate", "navDate", SecurityTermRead.RequiredDate),
                B("PrivateAssetValuation.Lockup", "lockup", SecurityTermRead.OptionalString),
                B("Notional.CommittedAmount", "commitment", SecurityTermRead.RequiredDecimal),
                B("Notional.FundedAmount", "fundedAmount", SecurityTermRead.OptionalDecimal),
                B("Notional.UnfundedAmount", "unfundedAmount", SecurityTermRead.OptionalDecimal),
                B("IssuerTerms.IssuerName", "gpSponsor", SecurityTermRead.RequiredString, via: "GpSponsor"),
                B("IssuerTerms.IssuerProgram", "strategy", SecurityTermRead.RequiredString, via: "Strategy")),
            CodecOrder = 16,
            EconomicFlattenings = [
            Map("gpSponsor", ("issuer", "issuerName"), ("fund", "fundFamily")),
            Map("strategy", ("issuer", "issuerProgram")),
            Map("commitment", ("notional", "committedAmount")),
            Map("fundedAmount", ("notional", "fundedAmount")),
            Map("unfundedAmount", ("notional", "unfundedAmount")),
            Map("vintage", ("privateAssetValuation", "vintage")),
            Map("navDate", ("privateAssetValuation", "navDate")),
            Map("lockup", ("privateAssetValuation", "lockup"))],
            EconomicFlatteningOrder = 19
        };
}
