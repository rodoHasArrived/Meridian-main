using Meridian.Contracts.Domain.Enums;
using static Meridian.Contracts.SecurityMaster.SecurityAssetModuleSchema;
using static Meridian.Contracts.SecurityMaster.SecurityAssetModuleRates;
using static Meridian.Contracts.SecurityMaster.SecurityAssetModuleCodec;
using static Meridian.Contracts.SecurityMaster.SecurityAssetModuleEconomics;

namespace Meridian.Contracts.SecurityMaster;

/// <summary>Compile-time contract contributions for NonDeliverableForward.</summary>
internal static class NonDeliverableForwardAssetModule
{
    private const string AssetClass = SecurityAssetClassNames.NonDeliverableForward;

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
                Req("settlementCurrency", SecurityAssetTermFieldType.String),
                Opt("notional", SecurityAssetTermFieldType.Decimal, "notionalAmount"),
                Opt("forwardRate", SecurityAssetTermFieldType.Decimal, "rate"),
                Req("fixingDate", SecurityAssetTermFieldType.Date),
                Req("valueDate", SecurityAssetTermFieldType.Date, "settlementDate", "maturityDate"),
                Opt("fixingSource", SecurityAssetTermFieldType.String).AsOperational()
            ],
            SchemaOrder: 26,
            ElementShapes: [])
        {
            CodecBinding = Class(AssetClass,
                B("Dates.EffectiveDate", "fixingDate", SecurityTermRead.RequiredDate, via: "FixingDate"),
                B("Dates.MaturityDate", "valueDate", SecurityTermRead.RequiredDate, via: "ValueDate"),
                B("CurrencyPair.BaseCurrency", "baseCurrency", SecurityTermRead.RequiredString),
                B("CurrencyPair.QuoteCurrency", "quoteCurrency", SecurityTermRead.RequiredString),
                B("CurrencyPair.SettlementCurrency", "settlementCurrency", SecurityTermRead.RequiredString),
                B("CurrencyPair.ForwardRate", "forwardRate", SecurityTermRead.StrictDecimal),
                B("Notional.Notional", "notional", SecurityTermRead.StrictDecimal),
                B("ReferenceDataSource.FixingSource", "fixingSource", SecurityTermRead.ConventionString)),
            CodecOrder = 25,
            EconomicFlattenings = [
                Map("baseCurrency", ("currencyPair", "baseCurrency")),
                Map("quoteCurrency", ("currencyPair", "quoteCurrency")),
                Map("settlementCurrency", ("currencyPair", "settlementCurrency")),
                Map("forwardRate", ("currencyPair", "forwardRate")),
                Map("notional", ("notional", "notional")),
                Map("fixingDate", ("maturity", "effectiveDate")),
                Map("valueDate", MaturityDate),
                Map("fixingSource", ("referenceDataSource", "fixingSource"))
            ],
            EconomicFlatteningOrder = 25
        };
}
