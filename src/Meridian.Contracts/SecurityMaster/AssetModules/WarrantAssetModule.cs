using Meridian.Contracts.Domain.Enums;
using static Meridian.Contracts.SecurityMaster.SecurityAssetModuleSchema;
using static Meridian.Contracts.SecurityMaster.SecurityAssetModuleRates;
using static Meridian.Contracts.SecurityMaster.SecurityAssetModuleCodec;
using static Meridian.Contracts.SecurityMaster.SecurityAssetModuleEconomics;

namespace Meridian.Contracts.SecurityMaster;

/// <summary>Compile-time contract contributions for Warrant.</summary>
internal static class WarrantAssetModule
{
    private const string AssetClass = "Warrant";

    internal static SecurityAssetClassModule Create()
        => new(
            Descriptor: new(
                AssetClass: AssetClass,
                UsesOccOptionSymbols: true,
                AssetPackIds: ["derivatives-fx"],
                InstrumentTypes: [InstrumentType.Warrant],
                LotUnit: SecurityLotUnitKind.Contracts,
                CashFlowBehavior: SecurityCashFlowBehavior.None,
                ProjectionKind: SecurityTermsProjectionKind.Declarative,
                LotMultiplierTermKey: "multiplier",
                SupportsBasicCreateWorkflow: false,
                PreferredIdentifierKinds:
                [
                    SecurityIdentifierKind.Ticker,
                    SecurityIdentifierKind.Isin,
                    SecurityIdentifierKind.Cusip,
                    SecurityIdentifierKind.Figi,
                    SecurityIdentifierKind.ProviderSymbol,
                    SecurityIdentifierKind.InternalCode
                ])
            { AccountingOutsideSlice = true },
            Fields: [
                Req("underlyingId", SecurityAssetTermFieldType.Guid).LinksUnderlying(SecurityUnderlyingRole.Underlying),
                Req("warrantType", SecurityAssetTermFieldType.String),
                Opt("strike", SecurityAssetTermFieldType.Decimal),
                Opt("expiry", SecurityAssetTermFieldType.Date),
                Opt("multiplier", SecurityAssetTermFieldType.Decimal)
            ],
            SchemaOrder: 24,
            ElementShapes: [])
        {
            CodecBinding = Class(AssetClass,
                B("Dates.MaturityDate", "expiry", SecurityTermRead.OptionalDate),
                B("Exercise.OptionRight", "warrantType", SecurityTermRead.RequiredString),
                B("Exercise.StrikePrice", "strike", SecurityTermRead.OptionalDecimal),
                B("Exercise.UnderlyingSecurityId", "underlyingId", SecurityTermRead.RequiredSecurityId),
                B("TradingParameters.ContractMultiplier", "multiplier", SecurityTermRead.OptionalDecimal)),
            CodecOrder = 23,
            EconomicFlattenings = [
                Map("underlyingId", ("exercise", "underlyingSecurityId")),
                Map("warrantType", ("exercise", "optionRight")),
                Map("strike", ("exercise", "strikePrice")),
                Map("expiry", MaturityDate),
                Map("multiplier", ("tradingParameters", "contractMultiplier"))
            ],
            EconomicFlatteningOrder = 23
        };
}
