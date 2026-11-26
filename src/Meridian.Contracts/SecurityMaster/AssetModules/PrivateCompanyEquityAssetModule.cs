using Meridian.Contracts.Domain.Enums;
using static Meridian.Contracts.SecurityMaster.SecurityAssetModuleSchema;
using static Meridian.Contracts.SecurityMaster.SecurityAssetModuleRates;
using static Meridian.Contracts.SecurityMaster.SecurityAssetModuleCodec;
using static Meridian.Contracts.SecurityMaster.SecurityAssetModuleEconomics;

namespace Meridian.Contracts.SecurityMaster;

/// <summary>Compile-time contract contributions for PrivateCompanyEquity.</summary>
internal static class PrivateCompanyEquityAssetModule
{
    private const string AssetClass = SecurityAssetClassNames.PrivateCompanyEquity;

    internal static SecurityAssetClassModule Create()
        => new(
            Descriptor: new(
                AssetClass: AssetClass,
                AssetPackIds: ["private-fund-partnership"],
                LotUnit: SecurityLotUnitKind.Shares,
                CashFlowBehavior: SecurityCashFlowBehavior.None,
                ProjectionKind: SecurityTermsProjectionKind.Declarative,
                SupportsBasicCreateWorkflow: false,
                PreferredIdentifierKinds:
                [
                    SecurityIdentifierKind.InternalCode,
                    SecurityIdentifierKind.Lei,
                    SecurityIdentifierKind.Cik,
                    SecurityIdentifierKind.ProviderSymbol
                ],
                AssetOperationsCapabilities: AssetOperationsCapabilitySet.AlternativeAssetOperations,
                SupportsProfileBackedTerms: true)
            {
                AccountingOutsideSlice = true,
                ClassificationLabels = ["PrivateEquity"],
                ReclassifyingProfileIds = ["private-company-equity"]
            },
            Fields: [
                Req("issuer", SecurityAssetTermFieldType.String),
                Req("shareClass", SecurityAssetTermFieldType.String),
                Req("round", SecurityAssetTermFieldType.String),
                Opt("ownershipPercent", SecurityAssetTermFieldType.Decimal),
                Req("costBasis", SecurityAssetTermFieldType.Decimal),
                Opt("latestValuation", SecurityAssetTermFieldType.Decimal),
                Opt("transferRestrictions", SecurityAssetTermFieldType.String)
            ],
            SchemaOrder: 18,
            ElementShapes: [])
        {
            CodecBinding = ResolvedClass(AssetClass,
                B("Capital.ShareClass", "shareClass", SecurityTermRead.RequiredString, via: "ShareClass"),
                B("PrivateAssetValuation.OwnershipPercent", "ownershipPercent", SecurityTermRead.OptionalDecimal),
                B("PrivateAssetValuation.CostBasis", "costBasis", SecurityTermRead.RequiredDecimal),
                B("PrivateAssetValuation.LatestValuation", "latestValuation", SecurityTermRead.OptionalDecimal),
                B("PrivateAssetValuation.TransferRestrictions", "transferRestrictions", SecurityTermRead.OptionalString),
                B("IssuerTerms.IssuerName", "issuer", SecurityTermRead.RequiredString, via: "Issuer"),
                B("IssuerTerms.IssuerProgram", "round", SecurityTermRead.RequiredString, via: "Round")),
            CodecOrder = 17,
            EconomicFlattenings = [
            Map("issuer", ("issuer", "issuerName")),
            Map("shareClass", ("equityCapital", "shareClass"), ("equityBehavior", "shareClass")),
            Map("round", ("issuer", "issuerProgram")),
            Map("ownershipPercent", ("privateAssetValuation", "ownershipPercent")),
            Map("costBasis", ("privateAssetValuation", "costBasis")),
            Map("latestValuation", ("privateAssetValuation", "latestValuation")),
            Map("transferRestrictions", ("privateAssetValuation", "transferRestrictions"))],
            EconomicFlatteningOrder = 20
        };
}
