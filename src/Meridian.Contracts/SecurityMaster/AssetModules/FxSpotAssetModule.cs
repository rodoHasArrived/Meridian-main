using Meridian.Contracts.Domain.Enums;
using static Meridian.Contracts.SecurityMaster.SecurityAssetModuleSchema;
using static Meridian.Contracts.SecurityMaster.SecurityAssetModuleRates;
using static Meridian.Contracts.SecurityMaster.SecurityAssetModuleCodec;
using static Meridian.Contracts.SecurityMaster.SecurityAssetModuleEconomics;

namespace Meridian.Contracts.SecurityMaster;

/// <summary>Compile-time contract contributions for FxSpot.</summary>
internal static class FxSpotAssetModule
{
    private const string AssetClass = SecurityAssetClassNames.FxSpot;

    internal static SecurityAssetClassModule Create()
        => new(
            Descriptor: new(
                AssetClass: AssetClass,
                AssetPackIds: ["derivatives-fx"],
                InstrumentTypes: [InstrumentType.Forex],
                LotUnit: SecurityLotUnitKind.CurrencyAmount,
                CashFlowBehavior: SecurityCashFlowBehavior.None,
                ProjectionKind: SecurityTermsProjectionKind.Declarative,
                SupportsBasicCreateWorkflow: false,
                PreferredIdentifierKinds:
                [
                    SecurityIdentifierKind.Ticker,
                    SecurityIdentifierKind.Ric,
                    SecurityIdentifierKind.ProviderSymbol,
                    SecurityIdentifierKind.InternalCode
                ])
            { AccountingOutsideSlice = true },
            Fields: [
                Req("baseCurrency", SecurityAssetTermFieldType.String),
                Req("quoteCurrency", SecurityAssetTermFieldType.String)
            ],
            SchemaOrder: 4,
            ElementShapes: [])
        {
            CodecBinding = Class(AssetClass,
                B("CurrencyPair.BaseCurrency", "baseCurrency", SecurityTermRead.RequiredString),
                B("CurrencyPair.QuoteCurrency", "quoteCurrency", SecurityTermRead.RequiredString)),
            CodecOrder = 4,
            EconomicFlattenings = [Map("baseCurrency", ("currencyPair", "baseCurrency")), Map("quoteCurrency", ("currencyPair", "quoteCurrency"))],
            EconomicFlatteningOrder = 3
        };
}
