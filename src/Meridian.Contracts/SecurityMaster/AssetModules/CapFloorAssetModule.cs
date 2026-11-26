using Meridian.Contracts.Domain.Enums;
using static Meridian.Contracts.SecurityMaster.SecurityAssetModuleSchema;
using static Meridian.Contracts.SecurityMaster.SecurityAssetModuleRates;
using static Meridian.Contracts.SecurityMaster.SecurityAssetModuleCodec;
using static Meridian.Contracts.SecurityMaster.SecurityAssetModuleEconomics;

namespace Meridian.Contracts.SecurityMaster;

/// <summary>Compile-time contract contributions for CapFloor.</summary>
internal static class CapFloorAssetModule
{
    private const string AssetClass = SecurityAssetClassNames.CapFloor;

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
                RequiresMaturity: true,
                TermsVersion: 2,
                TermsUpgrades: [SecurityAssetClassTermsUpgrade.Identity(fromVersion: 1, rationale: "Version 2 adds the optional governed scalar rateUnit declaration; existing values and units are unchanged.")])
            { StampFloor = 1, AccountingOutsideSlice = true },
            Fields: [
                OptOneOf("rateUnit", "Fraction", "Percent", "BasisPoints").WithVerbatimUndeclaredValue().Since(2),
                ReqOneOf("capFloorType", "Cap", "Floor", "Collar").WithVerbatimUndeclaredValue(),
                Req("referenceIndex", SecurityAssetTermFieldType.String, "index", "floatingIndex").LinksUnderlying(SecurityUnderlyingRole.Index),
                Opt("capStrike", SecurityAssetTermFieldType.Decimal, "capRate"),
                Opt("floorStrike", SecurityAssetTermFieldType.Decimal, "floorRate"),
                Req("notional", SecurityAssetTermFieldType.Decimal, "notionalAmount"),
                Req("effectiveDate", SecurityAssetTermFieldType.Date),
                Req("maturityDate", SecurityAssetTermFieldType.Date),
                Opt("resetFrequency", SecurityAssetTermFieldType.String, "frequency").WithConvention(SecurityTermConventionKind.PaymentFrequency),
                Opt("dayCount", SecurityAssetTermFieldType.String, "dayCountConvention").WithConvention(SecurityTermConventionKind.DayCount),
                Opt("premiumAmount", SecurityAssetTermFieldType.Decimal, "premium")
            ],
            SchemaOrder: 29,
            ElementShapes: [])
        {
            RateFields =
            [
                SignedRate(AssetClass, "capStrike", "Cap strike rate"),
                SignedRate(AssetClass, "floorStrike", "Floor strike rate")
            ],
            CodecBinding = Class(AssetClass,
                B("Dates.EffectiveDate", "effectiveDate", SecurityTermRead.RequiredDate, via: "EffectiveDate"),
                B("Dates.MaturityDate", "maturityDate", SecurityTermRead.RequiredDate, via: "MaturityDate"),
                B("Exercise.OptionRight", "capFloorType", SecurityTermRead.RequiredString),
                B("Exercise.PremiumAmount", "premiumAmount", SecurityTermRead.StrictDecimal),
                B("FloatingRate.ReferenceIndex", "referenceIndex", SecurityTermRead.RequiredString),
                B("FloatingRate.ResetFrequency", "resetFrequency", SecurityTermRead.ConventionString),
                B("FloatingRate.FloorRate", "floorStrike", SecurityTermRead.StrictDecimal),
                B("FloatingRate.CapRate", "capStrike", SecurityTermRead.StrictDecimal),
                B("Notional.Notional", "notional", SecurityTermRead.RequiredDecimal),
                B("ContractualRate.DayCount", "dayCount", SecurityTermRead.DayCount)),
            CodecOrder = 28,
            EconomicFlattenings = [
                Map("effectiveDate", ("maturity", "effectiveDate")),
                Map("maturityDate", MaturityDate),
                // ADR-027 stage 1 increment 6: the floatingRate module (serialized since increment 5)
                // recovers the flat keys termsFromKind feeds it from.
                Map("referenceIndex", ("floatingRate", "referenceIndex")),
                Map("capStrike", ("floatingRate", "capRate")),
                Map("floorStrike", ("floatingRate", "floorRate")),
                Map("resetFrequency", ("floatingRate", "resetFrequency")),
                Map("capFloorType", ("exercise", "optionRight")),
                Map("premiumAmount", ("exercise", "premiumAmount")),
                Map("notional", ("notional", "notional"))
            ],
            EconomicFlatteningOrder = 28
        };
}
