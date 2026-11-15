using Meridian.Contracts.Domain.Enums;
using static Meridian.Contracts.SecurityMaster.SecurityAssetModuleSchema;
using static Meridian.Contracts.SecurityMaster.SecurityAssetModuleRates;
using static Meridian.Contracts.SecurityMaster.SecurityAssetModuleCodec;
using static Meridian.Contracts.SecurityMaster.SecurityAssetModuleEconomics;

namespace Meridian.Contracts.SecurityMaster;

/// <summary>Compile-time contract contributions for FxForward.</summary>
internal static class FxForwardAssetModule
{
    private const string AssetClass = "FxForward";

    internal static SecurityAssetClassModule Create()
        => new(
            Descriptor: new(
                AssetClass: AssetClass,
                TradesAsBilateralContract: true,
                AssetPackIds: ["derivatives-fx"],
                LotUnit: SecurityLotUnitKind.CurrencyAmount,
                CashFlowBehavior: SecurityCashFlowBehavior.None,
                ProjectionKind: SecurityTermsProjectionKind.Declarative,
                SupportsBasicCreateWorkflow: false,
                PreferredIdentifierKinds:
                [
                    SecurityIdentifierKind.InternalCode,
                    SecurityIdentifierKind.ProviderSymbol,
                    SecurityIdentifierKind.Ric,
                    SecurityIdentifierKind.Uti,
                    SecurityIdentifierKind.Upi
                ])
            { AccountingOutsideSlice = true },
            Fields: [
                Req("baseCurrency", SecurityAssetTermFieldType.String),
                Req("quoteCurrency", SecurityAssetTermFieldType.String),
                Opt("notional", SecurityAssetTermFieldType.Decimal, "notionalAmount"),
                Opt("forwardRate", SecurityAssetTermFieldType.Decimal, "rate"),
                Opt("tradeDate", SecurityAssetTermFieldType.Date),
                Req("valueDate", SecurityAssetTermFieldType.Date, "settlementDate", "maturityDate"),
                Opt("settlementType", SecurityAssetTermFieldType.String).WithConvention(SecurityTermConventionKind.SettlementMethod)
            ],
            SchemaOrder: 25,
            ElementShapes: [])
        {
            CodecBinding = Class(AssetClass,
                B("Dates.EffectiveDate", "tradeDate", SecurityTermRead.StrictDate),
                B("Dates.MaturityDate", "valueDate", SecurityTermRead.RequiredDate, via: "ValueDate"),
                B("CurrencyPair.BaseCurrency", "baseCurrency", SecurityTermRead.RequiredString),
                B("CurrencyPair.QuoteCurrency", "quoteCurrency", SecurityTermRead.RequiredString),
                B("CurrencyPair.ForwardRate", "forwardRate", SecurityTermRead.StrictDecimal),
                B("Notional.Notional", "notional", SecurityTermRead.StrictDecimal),
                B("Settlement.SettlementType", "settlementType", SecurityTermRead.ConventionString)),
            CodecOrder = 24,
            EconomicFlattenings = [
                Map("baseCurrency", ("currencyPair", "baseCurrency")),
                Map("quoteCurrency", ("currencyPair", "quoteCurrency")),
                Map("forwardRate", ("currencyPair", "forwardRate")),
                Map("notional", ("notional", "notional")),
                Map("tradeDate", ("maturity", "effectiveDate")),
                Map("valueDate", MaturityDate),
                Map("settlementType", ("settlement", "settlementType"))
            ],
            EconomicFlatteningOrder = 24
        };
}
