using Meridian.Contracts.Domain.Enums;
using static Meridian.Contracts.SecurityMaster.SecurityAssetModuleSchema;
using static Meridian.Contracts.SecurityMaster.SecurityAssetModuleRates;
using static Meridian.Contracts.SecurityMaster.SecurityAssetModuleCodec;
using static Meridian.Contracts.SecurityMaster.SecurityAssetModuleEconomics;

namespace Meridian.Contracts.SecurityMaster;

/// <summary>Compile-time contract contributions for Cfd.</summary>
internal static class CfdAssetModule
{
    private const string AssetClass = "Cfd";

    internal static SecurityAssetClassModule Create()
        => new(
            Descriptor: new(
                AssetClass: AssetClass,
                TradesAsBilateralContract: true,
                AssetPackIds: ["derivatives-fx"],
                InstrumentTypes: [InstrumentType.CFD],
                LotUnit: SecurityLotUnitKind.Units,
                CashFlowBehavior: SecurityCashFlowBehavior.None,
                ProjectionKind: SecurityTermsProjectionKind.Declarative,
                SupportsBasicCreateWorkflow: false,
                PreferredIdentifierKinds:
                [
                    SecurityIdentifierKind.InternalCode,
                    SecurityIdentifierKind.ProviderSymbol,
                    SecurityIdentifierKind.Ticker,
                    SecurityIdentifierKind.Uti,
                    SecurityIdentifierKind.Upi
                ])
            { AccountingOutsideSlice = true },
            Fields: [
                Req("underlyingAssetClass", SecurityAssetTermFieldType.String),
                Opt("underlyingDescription", SecurityAssetTermFieldType.String).LinksUnderlying(SecurityUnderlyingRole.Underlying),
                Opt("leverage", SecurityAssetTermFieldType.Decimal)
            ],
            SchemaOrder: 23,
            ElementShapes: [])
        {
            CodecBinding = Class(AssetClass,
                B("UnderlyingExposure.UnderlyingAssetClass", "underlyingAssetClass", SecurityTermRead.RequiredString),
                B("UnderlyingExposure.UnderlyingDescription", "underlyingDescription", SecurityTermRead.OptionalString),
                B("UnderlyingExposure.Leverage", "leverage", SecurityTermRead.OptionalDecimal)),
            CodecOrder = 22,
            EconomicFlattenings = [Map("underlyingAssetClass", ("underlyingExposure", "underlyingAssetClass")),
            Map("underlyingDescription", ("underlyingExposure", "underlyingDescription")), Map("leverage", ("underlyingExposure", "leverage"))],
            EconomicFlatteningOrder = 6
        };
}
