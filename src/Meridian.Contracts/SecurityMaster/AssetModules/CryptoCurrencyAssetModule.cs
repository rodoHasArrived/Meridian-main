using Meridian.Contracts.Domain.Enums;
using static Meridian.Contracts.SecurityMaster.SecurityAssetModuleSchema;
using static Meridian.Contracts.SecurityMaster.SecurityAssetModuleRates;
using static Meridian.Contracts.SecurityMaster.SecurityAssetModuleCodec;
using static Meridian.Contracts.SecurityMaster.SecurityAssetModuleEconomics;

namespace Meridian.Contracts.SecurityMaster;

/// <summary>Compile-time contract contributions for CryptoCurrency.</summary>
internal static class CryptoCurrencyAssetModule
{
    private const string AssetClass = SecurityAssetClassNames.CryptoCurrency;

    internal static SecurityAssetClassModule Create()
        => new(
            Descriptor: new(
                AssetClass: AssetClass,
                AssetPackIds: ["controlled-other-asset"],
                InstrumentTypes: [InstrumentType.Crypto],
                LotUnit: SecurityLotUnitKind.Units,
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
                Req("quoteCurrency", SecurityAssetTermFieldType.String),
                Opt("network", SecurityAssetTermFieldType.String)
            ],
            SchemaOrder: 22,
            ElementShapes: [])
        {
            CodecBinding = Class(AssetClass,
                B("CurrencyPair.BaseCurrency", "baseCurrency", SecurityTermRead.RequiredString),
                B("CurrencyPair.QuoteCurrency", "quoteCurrency", SecurityTermRead.RequiredString),
                B("DigitalAsset.Network", "network", SecurityTermRead.OptionalString)),
            CodecOrder = 21,
            EconomicFlattenings = [Map("baseCurrency", ("currencyPair", "baseCurrency")), Map("quoteCurrency", ("currencyPair", "quoteCurrency")),
            Map("network", ("digitalAsset", "network"))],
            EconomicFlatteningOrder = 4
        };
}
