using Meridian.Contracts.Domain.Enums;
using static Meridian.Contracts.SecurityMaster.SecurityAssetModuleSchema;
using static Meridian.Contracts.SecurityMaster.SecurityAssetModuleRates;
using static Meridian.Contracts.SecurityMaster.SecurityAssetModuleCodec;
using static Meridian.Contracts.SecurityMaster.SecurityAssetModuleEconomics;

namespace Meridian.Contracts.SecurityMaster;

/// <summary>Compile-time contract contributions for RealEstateHolding.</summary>
internal static class RealEstateHoldingAssetModule
{
    private const string AssetClass = SecurityAssetClassNames.RealEstateHolding;

    internal static SecurityAssetClassModule Create()
        => new(
            Descriptor: new(
                AssetClass: AssetClass,
                AssetPackIds: ["real-estate"],
                LotUnit: SecurityLotUnitKind.Units,
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
                SupportsProfileBackedTerms: true)
            {
                AccountingOutsideSlice = true,
                ClassificationLabels = ["RealEstate", "RealEstateInterest"],
                ReclassifyingProfileIds = ["real-estate-holding"]
            },
            Fields: [
                Req("propertyType", SecurityAssetTermFieldType.String),
                Req("addressOrMarket", SecurityAssetTermFieldType.String),
                Req("ownershipPercent", SecurityAssetTermFieldType.Decimal),
                Req("appraisalValue", SecurityAssetTermFieldType.Decimal),
                Req("valuationDate", SecurityAssetTermFieldType.Date),
                Opt("debtStack", SecurityAssetTermFieldType.String),
                Opt("sponsor", SecurityAssetTermFieldType.String)
            ],
            SchemaOrder: 19,
            ElementShapes: [])
        {
            CodecBinding = ResolvedClass(AssetClass,
                B("PrivateAssetValuation.OwnershipPercent", "ownershipPercent", SecurityTermRead.RequiredDecimal, via: "PrivateAssetValuation.OwnershipPercent"),
                B("PrivateAssetValuation.AddressOrMarket", "addressOrMarket", SecurityTermRead.RequiredString),
                B("PrivateAssetValuation.AppraisalValue", "appraisalValue", SecurityTermRead.RequiredDecimal),
                B("PrivateAssetValuation.ValuationDate", "valuationDate", SecurityTermRead.RequiredDate),
                B("PrivateAssetValuation.DebtStack", "debtStack", SecurityTermRead.OptionalString),
                B("IssuerTerms.IssuerName", "sponsor", SecurityTermRead.OptionalString),
                B("IssuerTerms.IssuerProgram", "propertyType", SecurityTermRead.RequiredString, via: "PropertyType"),
                Fixed("IssuerTerms.IssuerSector", "ToOption(\"RealEstate\")")),
            CodecOrder = 18,
            EconomicFlattenings = [Map("propertyType", ("issuer", "issuerProgram")), Map("sponsor", ("issuer", "issuerName")),
            Map("ownershipPercent", ("privateAssetValuation", "ownershipPercent")),
            Map("addressOrMarket", ("privateAssetValuation", "addressOrMarket")),
            Map("appraisalValue", ("privateAssetValuation", "appraisalValue")),
            Map("valuationDate", ("privateAssetValuation", "valuationDate")),
            Map("debtStack", ("privateAssetValuation", "debtStack"))],
            EconomicFlatteningOrder = 21
        };
}
