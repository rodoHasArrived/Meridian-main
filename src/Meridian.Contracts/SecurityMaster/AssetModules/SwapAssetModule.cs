using Meridian.Contracts.Domain.Enums;
using static Meridian.Contracts.SecurityMaster.SecurityAssetModuleSchema;
using static Meridian.Contracts.SecurityMaster.SecurityAssetModuleRates;
using static Meridian.Contracts.SecurityMaster.SecurityAssetModuleCodec;
using static Meridian.Contracts.SecurityMaster.SecurityAssetModuleEconomics;

namespace Meridian.Contracts.SecurityMaster;

/// <summary>Compile-time contract contributions for Swap.</summary>
internal static class SwapAssetModule
{
    private const string AssetClass = SecurityAssetClassNames.Swap;

    internal static SecurityAssetClassModule Create()
        => new(
            Descriptor: new(
                AssetClass: AssetClass,
                TradesAsBilateralContract: true,
                AssetPackIds: ["derivatives-fx"],
                InstrumentTypes: [InstrumentType.Swap],
                LotUnit: SecurityLotUnitKind.Notional,
                CashFlowBehavior: SecurityCashFlowBehavior.SwapLegs,
                ProjectionKind: SecurityTermsProjectionKind.Declarative,
                DefaultValuationModel: SecurityValuationModelKind.DiscountedCashFlow,
                SupportsBasicCreateWorkflow: false,
                PreferredIdentifierKinds:
                [
                    SecurityIdentifierKind.InternalCode,
                    SecurityIdentifierKind.PermId,
                    SecurityIdentifierKind.Lei,
                    SecurityIdentifierKind.ProviderSymbol,
                    SecurityIdentifierKind.Uti,
                    SecurityIdentifierKind.Upi
                ],
                RequiresMaturity: true,
                // Version 2 adds the optional scheduleConventions block to each leg; read ahead of write
                // (ADR-024).
                TermsVersion: 4,
                TermsUpgrades:
                [
                    SecurityAssetClassTermsUpgrade.Identity(
                        fromVersion: 1,
                        rationale: "Version 2 adds the optional scheduleConventions block to each leg and changes no version-1 term, so a version-1 payload reads it as unset on every leg (ADR-024, 2026-10-25)."),
                    SecurityAssetClassTermsUpgrade.Identity(fromVersion: 2, rationale: "Version 3 adds the optional calculation-only cashFlowContract companion without changing earlier terms (ADR-024/025, 2026-10-29)."),
                    SecurityAssetClassTermsUpgrade.Identity(fromVersion: 3, rationale: "Version 4 adds the optional governed scalar rateUnit declaration without changing earlier terms.")
                ])
            {
                CashFlowTermRoles =
                [
                    new(SecurityCashFlowTermRole.Maturity, "maturityDate"),
                    new(SecurityCashFlowTermRole.Issue, "effectiveDate")
                ],
                StampFloor = 1,
                AccountingOutsideSlice = true
            },
            Fields: [
                OptOneOf("rateUnit", "Fraction", "Percent", "BasisPoints").WithVerbatimUndeclaredValue().Since(4),
                Req("effectiveDate", SecurityAssetTermFieldType.Date),
                Req("maturityDate", SecurityAssetTermFieldType.Date),
                Req("legs", SecurityAssetTermFieldType.Array, "swapLegs", "cashFlowLegs"),
                Opt("cashFlowContract", SecurityAssetTermFieldType.Object).Since(3).ReadOnly()
            ],
            SchemaOrder: 14,
            ElementShapes:
            [
                new("legs", 0, [
                    Opt("legId", SecurityAssetTermFieldType.String, "id", "name"),
                    Req("legType", SecurityAssetTermFieldType.String, "rateType", "type"),
                    Req("currency", SecurityAssetTermFieldType.String),
                    Opt("direction", SecurityAssetTermFieldType.String, "payReceive", "payOrReceive", "side"),
                    Opt("index", SecurityAssetTermFieldType.String, "indexName", "referenceIndex").LinksUnderlying(SecurityUnderlyingRole.Index),
                    Opt("fixedRate", SecurityAssetTermFieldType.Decimal, "rate", "couponRate"),
                    Opt("spreadBps", SecurityAssetTermFieldType.Decimal),
                    Opt("currentIndexRate", SecurityAssetTermFieldType.Decimal, "lastFixing", "currentRate", "indexRate"),
                    Opt("notional", SecurityAssetTermFieldType.Decimal, "notionalAmount", "faceAmount", "principal"),
                    Opt("paymentFrequency", SecurityAssetTermFieldType.String, "frequency").WithConvention(SecurityTermConventionKind.CouponFrequency),
                    Opt("dayCount", SecurityAssetTermFieldType.String, "dayCountConvention", "dayCountBasis").WithConvention(SecurityTermConventionKind.DayCount),
                    Req("exchangesPrincipal", SecurityAssetTermFieldType.Boolean, "principalExchange", "notionalExchange"),
                    // Dated {effectiveDate, notional} steps of an amortizing or accreting leg.
                    Opt("notionalSchedule", SecurityAssetTermFieldType.Array, "amortizationSchedule"),
                    // Dated {resetDate, fixingRate} resets of a floating leg, with the fixing once observed.
                    Opt("resetSchedule", SecurityAssetTermFieldType.Array, "fixings"),
                    // The leg's own schedule conventions (asset-class terms version 2, readable before
                    // writable: ADR-024).
                    Opt(SecurityVersionedTerms.ScheduleConventionsKey, SecurityAssetTermFieldType.Object).Since(2)
                ]),
                new("legs.notionalSchedule", 1, [
                    Req("effectiveDate", SecurityAssetTermFieldType.Date, "date"),
                    Req("notional", SecurityAssetTermFieldType.Decimal, "amount")
                ]),
                new("legs.resetSchedule", 2, [
                    Req("resetDate", SecurityAssetTermFieldType.Date, "effectiveDate", "date"),
                    Opt("fixingRate", SecurityAssetTermFieldType.Decimal, "indexRate", "rate")
                ]),
                new("legs.scheduleConventions", 9, ScheduleConventionFields)
            ])
        {
            RateFields =
            [
                // Swap legs: the leg's own couponRateUnit governs fixed rates and fixings.
                SignedRate(AssetClass, "legs.fixedRate", "Fixed-leg annual rate", consumedBy: LegacyCashFlow + ", " + Preview),
                SignedRate(AssetClass, "legs.currentIndexRate", "Observed floating index fixing", consumedBy: LegacyCashFlow),
                SignedRate(AssetClass, "legs.resetSchedule.fixingRate", "Observed floating index fixing on a reset date", consumedBy: LegacyCashFlow + ", " + Preview),
                Bps(AssetClass, "legs.spreadBps", "Floating-leg spread over the index", allowsNegative: true, LegacyCashFlow + ", " + Preview)
            ],
            CodecBinding = new(AssetClass, false,
            [
                B("Dates.EffectiveDate", "effectiveDate", SecurityTermRead.RequiredDate, via: "EffectiveDate"),
                Also("Dates.IssueDate", "effectiveDate", SecurityTermRead.RequiredDate),
                B("Dates.MaturityDate", "maturityDate", SecurityTermRead.RequiredDate, via: "MaturityDate"),
                // Each leg carries its own schedule-conventions block from the version that declares it.
                Computed("SwapLegs.Legs", "legs", "ToSwapLegs(json, assetClass, versionedTerms, mode)",
                    "terms.Legs |> List.map (FlatWriterParts.swapLeg writesScheduleConventions)"),
            ], Inputs: SecurityCodecInputs.VersionedTerms, WritesConventionsFlag: true),
            CodecOrder = 13,
            EconomicFlattenings = [Map("effectiveDate", ("maturity", "effectiveDate")), Map("maturityDate", MaturityDate)],
            EconomicFlatteningOrder = 16
        };
}
