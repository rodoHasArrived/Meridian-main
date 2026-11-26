using Meridian.Contracts.Domain.Enums;
using static Meridian.Contracts.SecurityMaster.SecurityAssetModuleSchema;
using static Meridian.Contracts.SecurityMaster.SecurityAssetModuleRates;
using static Meridian.Contracts.SecurityMaster.SecurityAssetModuleCodec;
using static Meridian.Contracts.SecurityMaster.SecurityAssetModuleEconomics;

namespace Meridian.Contracts.SecurityMaster;

/// <summary>Compile-time contract contributions for Option.</summary>
internal static class OptionAssetModule
{
    private const string AssetClass = SecurityAssetClassNames.Option;

    internal static SecurityAssetClassModule Create()
        => new(
            Descriptor: new(
                AssetClass: AssetClass,
                UsesOccOptionSymbols: true,
                AssetPackIds: ["derivatives-fx"],
                InstrumentTypes: [InstrumentType.EquityOption, InstrumentType.IndexOption, InstrumentType.FuturesOption],
                LotUnit: SecurityLotUnitKind.Contracts,
                CashFlowBehavior: SecurityCashFlowBehavior.None,
                ProjectionKind: SecurityTermsProjectionKind.Specialized,
                LotMultiplierTermKey: "multiplier",
                SupportsBasicCreateWorkflow: false,
                PreferredIdentifierKinds:
                [
                    SecurityIdentifierKind.OccOptionSymbol,
                    SecurityIdentifierKind.Ticker,
                    SecurityIdentifierKind.Figi,
                    SecurityIdentifierKind.ProviderSymbol,
                    SecurityIdentifierKind.InternalCode
                ])
            { AccountingOutsideSlice = true },
            Fields: [
                Req("underlyingId", SecurityAssetTermFieldType.Guid).LinksUnderlying(SecurityUnderlyingRole.Underlying),
                // The F# option command rejects anything but Put/Call; declaring the vocabulary moves
                // the same rejection onto the edit surface and the write-mode codec. OptionTerms
                // holds it as a plain string, so an odd value already in a row re-serializes intact
                // — the vocabulary constrains new writes, it does not make old rows unwritable.
                ReqOneOf("putCall", "Put", "Call").WithVerbatimUndeclaredValue(),
                Req("strike", SecurityAssetTermFieldType.Decimal),
                Req("expiry", SecurityAssetTermFieldType.Date),
                Req("multiplier", SecurityAssetTermFieldType.Decimal),
                Opt("optChainId", SecurityAssetTermFieldType.String),
                // ExerciseStyle has no "other" case: an unrecognized style decodes to None, so the
                // value would be silently dropped rather than preserved.
                OptOneOf("exerciseStyle", "American", "European", "Bermudan"),
                Opt("settlementType", SecurityAssetTermFieldType.String).WithConvention(SecurityTermConventionKind.SettlementMethod),
                Req("isAdjusted", SecurityAssetTermFieldType.Boolean).ReadsAbsentAs("false"),
                Opt("lastTradingDt", SecurityAssetTermFieldType.Date)
            ],
            SchemaOrder: 1,
            ElementShapes: [])
        {
            CodecBinding = Class(AssetClass,
                B("Dates.MaturityDate", "expiry", SecurityTermRead.RequiredDate, via: "Expiry"),
                B("ListingDetail.OptChainId", "optChainId", SecurityTermRead.OptionalString),
                B("ListingDetail.SettlementType", "settlementType", SecurityTermRead.OptionalString),
                B("ListingDetail.IsAdjusted", "isAdjusted", SecurityTermRead.BooleanDefaultFalse),
                B("ListingDetail.LastTradingDt", "lastTradingDt", SecurityTermRead.OptionalDate),
                B("Exercise.OptionRight", "putCall", SecurityTermRead.RequiredString),
                B("Exercise.StrikePrice", "strike", SecurityTermRead.RequiredDecimal),
                new("Exercise.ExerciseStyle", "exerciseStyle", null,
                    DecodeExpression: "CanonicalExerciseStyle(ParseExerciseStyle(bound.OptionalString(\"exerciseStyle\")))"),
                B("Exercise.UnderlyingSecurityId", "underlyingId", SecurityTermRead.RequiredSecurityId),
                B("TradingParameters.ContractMultiplier", "multiplier", SecurityTermRead.RequiredDecimal)),
            CodecOrder = 1,
            EconomicFlattenings = [
            Map("underlyingId", ("exercise", "underlyingSecurityId")),
            Map("putCall", ("exercise", "optionRight")),
            Map("strike", ("exercise", "strikePrice")),
            Map("expiry", MaturityDate),
            Map("multiplier", ("tradingParameters", "contractMultiplier")),
            Map("exerciseStyle", ("exercise", "exerciseStyle")),
            Map("optChainId", ("listingDetail", "optChainId")),
            Map("settlementType", ("listingDetail", "settlementType")),
            Map("isAdjusted", ("listingDetail", "isAdjusted")),
            Map("lastTradingDt", ("listingDetail", "lastTradingDt"))],
            EconomicFlatteningOrder = 1
        };
}
