using Meridian.Contracts.Domain.Enums;
using static Meridian.Contracts.SecurityMaster.SecurityAssetModuleSchema;
using static Meridian.Contracts.SecurityMaster.SecurityAssetModuleRates;
using static Meridian.Contracts.SecurityMaster.SecurityAssetModuleCodec;
using static Meridian.Contracts.SecurityMaster.SecurityAssetModuleEconomics;

namespace Meridian.Contracts.SecurityMaster;

/// <summary>Compile-time contract contributions for Commodity.</summary>
internal static class CommodityAssetModule
{
    private const string AssetClass = "Commodity";

    internal static SecurityAssetClassModule Create()
        => new(
            Descriptor: new(
                AssetClass: AssetClass,
                AssetPackIds: ["controlled-other-asset"],
                InstrumentTypes: [InstrumentType.Commodity],
                LotUnit: SecurityLotUnitKind.Units,
                CashFlowBehavior: SecurityCashFlowBehavior.None,
                ProjectionKind: SecurityTermsProjectionKind.Declarative,
                LotMultiplierTermKey: "contractSize",
                SupportsBasicCreateWorkflow: false,
                PreferredIdentifierKinds:
                [
                    SecurityIdentifierKind.Ticker,
                    SecurityIdentifierKind.Figi,
                    SecurityIdentifierKind.ProviderSymbol,
                    SecurityIdentifierKind.InternalCode
                ])
            { AccountingOutsideSlice = true },
            Fields: [
                Req("commodityType", SecurityAssetTermFieldType.String),
                Opt("denomination", SecurityAssetTermFieldType.String),
                Opt("contractSize", SecurityAssetTermFieldType.Decimal)
            ],
            SchemaOrder: 21,
            ElementShapes: [])
        {
            CodecBinding = Class(AssetClass,
                B("CommodityContract.CommodityType", "commodityType", SecurityTermRead.RequiredString),
                B("CommodityContract.Denomination", "denomination", SecurityTermRead.OptionalString),
                B("CommodityContract.ContractSize", "contractSize", SecurityTermRead.OptionalDecimal)),
            CodecOrder = 20,
            EconomicFlattenings = [Map("commodityType", ("commodityContract", "commodityType")),
            Map("denomination", ("commodityContract", "denomination")), Map("contractSize", ("commodityContract", "contractSize"))],
            EconomicFlatteningOrder = 5
        };
}
