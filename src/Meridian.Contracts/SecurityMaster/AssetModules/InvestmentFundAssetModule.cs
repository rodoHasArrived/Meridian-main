using Meridian.Contracts.Domain.Enums;
using static Meridian.Contracts.SecurityMaster.SecurityAssetModuleSchema;
using static Meridian.Contracts.SecurityMaster.SecurityAssetModuleRates;
using static Meridian.Contracts.SecurityMaster.SecurityAssetModuleCodec;
using static Meridian.Contracts.SecurityMaster.SecurityAssetModuleEconomics;

namespace Meridian.Contracts.SecurityMaster;

/// <summary>Compile-time contract contributions for InvestmentFund.</summary>
internal static class InvestmentFundAssetModule
{
    private const string AssetClass = SecurityAssetClassNames.InvestmentFund;

    internal static SecurityAssetClassModule Create()
        => new(
            Descriptor: new(
                AssetClass: AssetClass,
                AssetPackIds: ["public-equity-etf"],
                LotUnit: SecurityLotUnitKind.Units,
                CashFlowBehavior: SecurityCashFlowBehavior.FundDistributions,
                ProjectionKind: SecurityTermsProjectionKind.Declarative,
                SupportsBasicCreateWorkflow: false,
                PreferredIdentifierKinds:
                [
                    SecurityIdentifierKind.Ticker,
                    SecurityIdentifierKind.Isin,
                    SecurityIdentifierKind.Cusip,
                    SecurityIdentifierKind.Figi,
                    SecurityIdentifierKind.ProviderSymbol,
                    SecurityIdentifierKind.InternalCode
                ],
                // Every InvestmentFund asset-specific term is optional; see Equity above.
                SupportsIdentifierOnlyImport: true)
            { AccountingOutsideSlice = true },
            Fields: [
                Opt("fundType", SecurityAssetTermFieldType.String),
                Opt("fundFamily", SecurityAssetTermFieldType.String),
                Opt("navCurrency", SecurityAssetTermFieldType.String),
                Opt("distributionPolicy", SecurityAssetTermFieldType.String),
                Opt("isStableNav", SecurityAssetTermFieldType.Boolean),
                Opt("pricingSource", SecurityAssetTermFieldType.String).AsOperational()
            ],
            SchemaOrder: 31,
            ElementShapes: [])
        {
            CodecBinding = Class(AssetClass,
                B("FundShare.FundType", "fundType", SecurityTermRead.OptionalString),
                B("FundShare.NavCurrency", "navCurrency", SecurityTermRead.OptionalString),
                Computed("FundShare.DistributionPolicy", "distributionPolicy",
                    "ToDistributionPolicyOption(bound.OptionalString(\"distributionPolicy\"))",
                    "terms.DistributionPolicy |> Option.map DistributionPolicy.label"),
                B("FundShare.IsStableNav", "isStableNav", SecurityTermRead.OptionalBoolean),
                B("FundTerms.FundFamily", "fundFamily", SecurityTermRead.OptionalString),
                B("ReferenceDataSource.PricingSource", "pricingSource", SecurityTermRead.OptionalString)),
            CodecOrder = 30,
            EconomicFlattenings = [Map("fundFamily", ("fund", "fundFamily")),
            Map("fundType", ("fundShare", "fundType")), Map("navCurrency", ("fundShare", "navCurrency")),
            Map("distributionPolicy", ("fundShare", "distributionPolicy")), Map("isStableNav", ("fundShare", "isStableNav")),
            Map("pricingSource", ("referenceDataSource", "pricingSource"))],
            EconomicFlatteningOrder = 30
        };
}
