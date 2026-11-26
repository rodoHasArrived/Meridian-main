using Meridian.Contracts.Domain.Enums;
using static Meridian.Contracts.SecurityMaster.SecurityAssetModuleSchema;
using static Meridian.Contracts.SecurityMaster.SecurityAssetModuleRates;
using static Meridian.Contracts.SecurityMaster.SecurityAssetModuleCodec;
using static Meridian.Contracts.SecurityMaster.SecurityAssetModuleEconomics;

namespace Meridian.Contracts.SecurityMaster;

/// <summary>Compile-time contract contributions for Swaption.</summary>
internal static class SwaptionAssetModule
{
    private const string AssetClass = SecurityAssetClassNames.Swaption;

    internal static SecurityAssetClassModule Create()
        => new(
            Descriptor: new(
                AssetClass: AssetClass,
                TradesAsBilateralContract: true,
                AssetPackIds: ["derivatives-fx"],
                LotUnit: SecurityLotUnitKind.Notional,
                CashFlowBehavior: SecurityCashFlowBehavior.None,
                ProjectionKind: SecurityTermsProjectionKind.Declarative,
                SupportsBasicCreateWorkflow: false,
                PreferredIdentifierKinds:
                [
                    SecurityIdentifierKind.InternalCode,
                    SecurityIdentifierKind.PermId,
                    SecurityIdentifierKind.ProviderSymbol,
                    SecurityIdentifierKind.Uti,
                    SecurityIdentifierKind.Upi
                ],
                TermsVersion: 2,
                TermsUpgrades: [SecurityAssetClassTermsUpgrade.Identity(fromVersion: 1, rationale: "Version 2 adds the optional governed scalar rateUnit declaration; existing values and units are unchanged.")])
            { StampFloor = 1, AccountingOutsideSlice = true },
            Fields: [
                OptOneOf("rateUnit", "Fraction", "Percent", "BasisPoints").WithVerbatimUndeclaredValue().Since(2),
                ReqOneOf("payerReceiver", "Payer", "Receiver").WithVerbatimUndeclaredValue(),
                OptOneOf("exerciseStyle", "European", "American", "Bermudan").WithVerbatimUndeclaredValue(),
                Req("strikeRate", SecurityAssetTermFieldType.Decimal, "strike", "fixedRate"),
                Req("expiry", SecurityAssetTermFieldType.Date, "expiryDate", "expirationDate"),
                Opt("underlyingEffectiveDate", SecurityAssetTermFieldType.Date),
                Req("underlyingMaturityDate", SecurityAssetTermFieldType.Date),
                Req("notional", SecurityAssetTermFieldType.Decimal, "notionalAmount"),
                Opt("floatingIndex", SecurityAssetTermFieldType.String, "index", "referenceIndex").LinksUnderlying(SecurityUnderlyingRole.Index),
                Opt("fixedLegPaymentFrequency", SecurityAssetTermFieldType.String).WithConvention(SecurityTermConventionKind.CouponFrequency),
                Opt("fixedLegDayCount", SecurityAssetTermFieldType.String).WithConvention(SecurityTermConventionKind.DayCount),
                Opt("settlementType", SecurityAssetTermFieldType.String).WithConvention(SecurityTermConventionKind.SettlementMethod)
            ],
            SchemaOrder: 28,
            ElementShapes: [])
        {
            RateFields =
            [
                SignedRate(AssetClass, "strikeRate", "Underlying swap fixed-rate strike")
            ],
            CodecBinding = Class(AssetClass,
                B("Dates.EffectiveDate", "underlyingEffectiveDate", SecurityTermRead.StrictDate),
                B("Dates.MaturityDate", "expiry", SecurityTermRead.RequiredDate, via: "Expiry"),
                B("Exercise.OptionRight", "payerReceiver", SecurityTermRead.RequiredString),
                B("Exercise.StrikeRate", "strikeRate", SecurityTermRead.RequiredDecimal),
                B("Exercise.ExerciseStyle", "exerciseStyle", SecurityTermRead.ConventionString),
                B("Notional.Notional", "notional", SecurityTermRead.RequiredDecimal),
                B("UnderlyingSwap.MaturityDate", "underlyingMaturityDate", SecurityTermRead.RequiredDate),
                B("UnderlyingSwap.FloatingIndex", "floatingIndex", SecurityTermRead.ConventionString),
                B("UnderlyingSwap.FixedLegPaymentFrequency", "fixedLegPaymentFrequency", SecurityTermRead.ConventionString),
                B("UnderlyingSwap.FixedLegDayCount", "fixedLegDayCount", SecurityTermRead.DayCount),
                B("Settlement.SettlementType", "settlementType", SecurityTermRead.ConventionString)),
            CodecOrder = 27,
            EconomicFlattenings = [
                Map("payerReceiver", ("exercise", "optionRight")),
                Map("exerciseStyle", ("exercise", "exerciseStyle")),
                Map("strikeRate", ("exercise", "strikeRate")),
                Map("expiry", MaturityDate),
                Map("underlyingEffectiveDate", ("maturity", "effectiveDate")),
                Map("notional", ("notional", "notional")),
                Map("settlementType", ("settlement", "settlementType"))
            ],
            EconomicFlatteningOrder = 27
        };
}
