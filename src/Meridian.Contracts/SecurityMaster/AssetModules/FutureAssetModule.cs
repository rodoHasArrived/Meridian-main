using Meridian.Contracts.Domain.Enums;
using static Meridian.Contracts.SecurityMaster.SecurityAssetModuleSchema;
using static Meridian.Contracts.SecurityMaster.SecurityAssetModuleRates;
using static Meridian.Contracts.SecurityMaster.SecurityAssetModuleCodec;
using static Meridian.Contracts.SecurityMaster.SecurityAssetModuleEconomics;

namespace Meridian.Contracts.SecurityMaster;

/// <summary>Compile-time contract contributions for Future.</summary>
internal static class FutureAssetModule
{
    private const string AssetClass = "Future";

    internal static SecurityAssetClassModule Create()
        => new(
            Descriptor: new(
                AssetClass: AssetClass,
                AssetPackIds: ["derivatives-fx"],
                InstrumentTypes: [InstrumentType.Future, InstrumentType.SingleStockFuture],
                LotUnit: SecurityLotUnitKind.Contracts,
                CashFlowBehavior: SecurityCashFlowBehavior.None,
                ProjectionKind: SecurityTermsProjectionKind.Declarative,
                LotMultiplierTermKey: "multiplier",
                SupportsBasicCreateWorkflow: false,
                PreferredIdentifierKinds:
                [
                    SecurityIdentifierKind.Ticker,
                    SecurityIdentifierKind.Figi,
                    SecurityIdentifierKind.Ric,
                    SecurityIdentifierKind.ProviderSymbol,
                    SecurityIdentifierKind.InternalCode
                ])
            { AccountingOutsideSlice = true, SettlesDailyVariationMargin = true },
            Fields: [
                Req("rootSymbol", SecurityAssetTermFieldType.String),
                Req("contractMonth", SecurityAssetTermFieldType.String),
                Req("expiry", SecurityAssetTermFieldType.Date),
                Req("multiplier", SecurityAssetTermFieldType.Decimal),
                Opt("lastTradingDt", SecurityAssetTermFieldType.Date),
                Opt("firstNoticeDt", SecurityAssetTermFieldType.Date),
                Opt("deliveryMonthDt", SecurityAssetTermFieldType.Date),
                Opt("settlementType", SecurityAssetTermFieldType.String).WithConvention(SecurityTermConventionKind.SettlementMethod),
                Opt("deliveryLocationCode", SecurityAssetTermFieldType.String),
                Req("isRollTarget", SecurityAssetTermFieldType.Boolean).ReadsAbsentAs("false"),
                Opt("rollWindowDays", SecurityAssetTermFieldType.Integer)
            ],
            SchemaOrder: 2,
            ElementShapes: [])
        {
            CodecBinding = Class(AssetClass,
                B("Dates.MaturityDate", "expiry", SecurityTermRead.RequiredDate, via: "Expiry"),
                B("ListingDetail.SettlementType", "settlementType", SecurityTermRead.OptionalString),
                B("ListingDetail.LastTradingDt", "lastTradingDt", SecurityTermRead.OptionalDate),
                B("ListingDetail.RootSymbol", "rootSymbol", SecurityTermRead.RequiredString),
                B("ListingDetail.ContractMonth", "contractMonth", SecurityTermRead.RequiredString),
                B("ListingDetail.FirstNoticeDt", "firstNoticeDt", SecurityTermRead.OptionalDate),
                B("ListingDetail.DeliveryMonthDt", "deliveryMonthDt", SecurityTermRead.OptionalDate),
                B("ListingDetail.DeliveryLocationCode", "deliveryLocationCode", SecurityTermRead.OptionalString),
                B("ListingDetail.IsRollTarget", "isRollTarget", SecurityTermRead.BooleanDefaultFalse),
                B("ListingDetail.RollWindowDays", "rollWindowDays", SecurityTermRead.OptionalInt),
                B("TradingParameters.ContractMultiplier", "multiplier", SecurityTermRead.RequiredDecimal)),
            CodecOrder = 2,
            EconomicFlattenings = [Map("expiry", MaturityDate), Map("multiplier", ("tradingParameters", "contractMultiplier")),
            Map("rootSymbol", ("listingDetail", "rootSymbol")),
            Map("contractMonth", ("listingDetail", "contractMonth")),
            Map("lastTradingDt", ("listingDetail", "lastTradingDt")),
            Map("firstNoticeDt", ("listingDetail", "firstNoticeDt")),
            Map("deliveryMonthDt", ("listingDetail", "deliveryMonthDt")),
            Map("settlementType", ("listingDetail", "settlementType")),
            Map("deliveryLocationCode", ("listingDetail", "deliveryLocationCode")),
            Map("isRollTarget", ("listingDetail", "isRollTarget")),
            Map("rollWindowDays", ("listingDetail", "rollWindowDays"))],
            EconomicFlatteningOrder = 2
        };
}
