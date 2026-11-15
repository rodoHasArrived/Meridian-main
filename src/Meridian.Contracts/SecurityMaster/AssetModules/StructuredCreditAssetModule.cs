using Meridian.Contracts.Domain.Enums;
using static Meridian.Contracts.SecurityMaster.SecurityAssetModuleSchema;
using static Meridian.Contracts.SecurityMaster.SecurityAssetModuleRates;
using static Meridian.Contracts.SecurityMaster.SecurityAssetModuleCodec;
using static Meridian.Contracts.SecurityMaster.SecurityAssetModuleEconomics;

namespace Meridian.Contracts.SecurityMaster;

/// <summary>Compile-time contract contributions for StructuredCredit.</summary>
internal static class StructuredCreditAssetModule
{
    private const string AssetClass = "StructuredCredit";

    internal static SecurityAssetClassModule Create()
        => new(
            Descriptor: new(
                AssetClass: AssetClass,
                AssetPackIds: ["fixed-income"],
                LotUnit: SecurityLotUnitKind.FaceValue,
                CashFlowBehavior: SecurityCashFlowBehavior.FactorAmortizing,
                ProjectionKind: SecurityTermsProjectionKind.Declarative,
                SupportsBasicCreateWorkflow: false,
                PreferredIdentifierKinds:
                [
                    SecurityIdentifierKind.InternalCode,
                    SecurityIdentifierKind.Clip,
                    SecurityIdentifierKind.Cusip,
                    SecurityIdentifierKind.Isin,
                    SecurityIdentifierKind.Figi,
                    SecurityIdentifierKind.ProviderSymbol
                ],
                AssetOperationsCapabilities: AssetOperationsCapabilitySet.AlternativeAssetOperations,
                AmortizesTowardPar: true,
                // Version 2 added the typed tranche terms (structuredCreditKind, seniority, attachment and
                // detachment points, WAC/WAM/WALA, CLO period dates); every addition is optional, so a
                // version-1 payload reads unchanged and needs no field transformation (ADR-024). The
                // identity step below records it, and SecurityAssetClassTermsHistoryTests checks it
                // against the committed manifests. Version 3 adds the optional scheduleConventions block and
                // version 4 the optional callSchedule and putSchedule arrays. The contract-step writer admits
                // through version 4, while S2 retains floor 2 for documents that declare no schedules.
                TermsVersion: 6,
                TermsUpgrades:
                [
                    SecurityAssetClassTermsUpgrade.Identity(
                        fromVersion: 1,
                        rationale: "Version 2 added nine optional typed tranche terms and changed no version-1 term, so a version-1 payload reads them as unset (ADR-024, 2026-10-18)."),
                    SecurityAssetClassTermsUpgrade.Identity(
                        fromVersion: 2,
                        rationale: "Version 3 adds the optional scheduleConventions block and changes no version-2 term, so a version-2 payload reads it as unset (ADR-024, 2026-10-25)."),
                    SecurityAssetClassTermsUpgrade.Identity(
                        fromVersion: 3,
                        rationale: "Version 4 adds the optional callSchedule and putSchedule arrays and changes no version-3 term, so a version-3 payload reads them as unset (ADR-024, 2026-10-25)."),
                    SecurityAssetClassTermsUpgrade.Identity(fromVersion: 4, rationale: "Version 5 adds the optional calculation-only cashFlowContract companion without changing earlier terms (ADR-024/025, 2026-10-29)."),
                    SecurityAssetClassTermsUpgrade.Identity(fromVersion: 5, rationale: "Version 6 adds the optional governed scalar rateUnit declaration without changing earlier terms.")
                ],
                SupportsProfileBackedTerms: true,
                Aliases: [.. SecurityCanonicalAssetHomes.SecuritizedLabels.Where(static label => label != AssetClass)],
                AccountingInstrumentClass: SecurityAccountingInstrumentClasses.AssetBackedSecurity)
            {
                CashFlowTermRoles =
                [
                    new(SecurityCashFlowTermRole.Maturity, "maturity"),
                    new(SecurityCashFlowTermRole.PrincipalFace, "originalFace"),
                    new(SecurityCashFlowTermRole.CurrentFactor, "currentFactor"),
                    new(SecurityCashFlowTermRole.CouponRate, "couponOrIndex", ReadAfterSharedSpellings: true)
                ],
                StampFloor = 2,
                RecurringAccrualPolicy = SecurityRecurringAccrualPolicies.ExistingFixedIncome,
                ReclassifyingProfileIds = ["structured-credit-io-po"]
            },
            Fields: [
                OptOneOf("rateUnit", "Fraction", "Percent", "BasisPoints").WithVerbatimUndeclaredValue().Since(6),
                Req("tranche", SecurityAssetTermFieldType.String),
                Opt("poolId", SecurityAssetTermFieldType.String),
                Req("collateralType", SecurityAssetTermFieldType.String),
                Req("originalFace", SecurityAssetTermFieldType.Decimal),
                Opt("currentFactor", SecurityAssetTermFieldType.Decimal),
                // A fixed coupon or a floating index name in one free-text field; the graph records
                // it as an index reference until the field is split.
                Req("couponOrIndex", SecurityAssetTermFieldType.String).LinksUnderlying(SecurityUnderlyingRole.Index),
                // Free-text trustee or servicer report pointer: the evidence lineage the paydown
                // projector requires for typed factor rows. Operational, not economic: no cash-flow
                // or accounting math reads it. The dated schedule is factorScheduleEntries.
                Opt("factorSchedule", SecurityAssetTermFieldType.String).AsOperational(),
                // Typed, dated factor points ({asOfDate, factor}) consumed by the structured
                // cash-flow resolver's FactorAsOf lookup.
                Opt("factorScheduleEntries", SecurityAssetTermFieldType.Array),
                // Legal final maturity of the tranche: the anchor date for calculated cash-flow
                // projection — without it the factor schedule has no production effect.
                Opt("maturity", SecurityAssetTermFieldType.Date),
                // Typed tranche terms (asset-class terms version 2): the securitisation family,
                // capital-structure position, loss attachment/detachment, collateral-pool statistics
                // and the CLO period dates. All optional, so a version-1 payload reads unchanged;
                // tagged .Since(2) so the committed version manifests place them in version 2.
                Opt("structuredCreditKind", SecurityAssetTermFieldType.String).Since(2),
                Opt("seniority", SecurityAssetTermFieldType.String).Since(2),
                Opt("attachmentPoint", SecurityAssetTermFieldType.Decimal).Since(2),
                Opt("detachmentPoint", SecurityAssetTermFieldType.Decimal).Since(2),
                Opt("weightedAverageCoupon", SecurityAssetTermFieldType.Decimal).Since(2),
                Opt("weightedAverageMaturityMonths", SecurityAssetTermFieldType.Integer).Since(2),
                Opt("weightedAverageLoanAgeMonths", SecurityAssetTermFieldType.Integer).Since(2),
                Opt("reinvestmentPeriodEnd", SecurityAssetTermFieldType.Date).Since(2),
                Opt("nonCallPeriodEnd", SecurityAssetTermFieldType.Date).Since(2),
                // Asset-class terms version 3, readable before writable (ADR-024).
                Opt("scheduleConventions", SecurityAssetTermFieldType.Object).Since(3),
                // Declared call and put schedules (asset-class terms version 4, readable before
                // writable: ADR-024). Entry shapes: CallScheduleEntryFields, PutScheduleEntryFields.
                Opt("callSchedule", SecurityAssetTermFieldType.Array).Since(4),
                Opt("putSchedule", SecurityAssetTermFieldType.Array).Since(4),
                Opt("cashFlowContract", SecurityAssetTermFieldType.Object).Since(5).ReadOnly()
            ],
            SchemaOrder: 16,
            ElementShapes:
            [
                new("factorScheduleEntries", 8, [
                    Req("asOfDate", SecurityAssetTermFieldType.Date, "factorDate", "effectiveDate", "date"),
                    Req("factor", SecurityAssetTermFieldType.Decimal, "currentFactor")
                ]),
                new("scheduleConventions", 13, ScheduleConventionFields),
                new("callSchedule", 18, CallScheduleEntryFields),
                new("putSchedule", 19, PutScheduleEntryFields)
            ])
        {
            RateFields =
            [
                Coupon(AssetClass, "couponOrIndex", "Numeric tranche coupon (an index name is not a rate)"),
                Coupon(AssetClass, "weightedAverageCoupon", "Collateral weighted-average coupon", AccountingReaders),
                Ratio(AssetClass, "attachmentPoint", "Loss attachment point", 1m),
                Ratio(AssetClass, "detachmentPoint", "Loss detachment point", 1m)
            ],
            CouponRateFieldKey = "couponOrIndex",
            CodecBinding = // Scalar keys from the profile-resolved document; the versioned containers from the root,
            // since a profile envelope decodes none.
            new(AssetClass, true,
            [
                Converted("CouponTerms.CouponType", "couponOrIndex",
                    "FSharpOption<CouponKind>.Some(CouponKind.NewOtherCoupon(bound.RequiredString(\"couponOrIndex\")))", "CouponOrIndex"),
                Computed("TrancheContract.Kind", "structuredCreditKind",
                    "ToStructuredCreditKindOption(bound.OptionalConventionString(\"structuredCreditKind\"))",
                    "terms.Kind |> Option.map StructuredCreditKind.asString"),
                B("TrancheContract.Seniority", "seniority", SecurityTermRead.ConventionString),
                B("TrancheContract.AttachmentPoint", "attachmentPoint", SecurityTermRead.StrictDecimal),
                B("TrancheContract.DetachmentPoint", "detachmentPoint", SecurityTermRead.StrictDecimal),
                B("TrancheContract.ReinvestmentPeriodEnd", "reinvestmentPeriodEnd", SecurityTermRead.StrictDate),
                B("TrancheContract.NonCallPeriodEnd", "nonCallPeriodEnd", SecurityTermRead.StrictDate),
                B("Pool.Factor", "currentFactor", SecurityTermRead.OptionalDecimal),
                B("Pool.WeightedAvgCoupon", "weightedAverageCoupon", SecurityTermRead.StrictDecimal),
                B("Pool.WeightedAvgMaturityMonths", "weightedAverageMaturityMonths", SecurityTermRead.StrictInt),
                B("Pool.WeightedAvgLoanAgeMos", "weightedAverageLoanAgeMonths", SecurityTermRead.StrictInt),
                B("Pool.CollateralType", "collateralType", SecurityTermRead.RequiredString, via: "CollateralType"),
                B("Pool.PoolIdentifier", "poolId", SecurityTermRead.OptionalString),
                B("Pool.TrancheClass", "tranche", SecurityTermRead.RequiredString, via: "Tranche"),
                Fixed("Pool.IsInterestOnly", "false"),
                Fixed("Pool.IsPrincipalOnly", "false"),
                B("Pool.NotionalBalance", "originalFace", SecurityTermRead.RequiredDecimal, via: "OriginalFace"),
                B("Pool.FactorScheduleReference", "factorSchedule", SecurityTermRead.OptionalString),
                Listed("Pool.FactorScheduleEntries", "factorScheduleEntries", "FactorScheduleEntries(economicSchedules)",
                    "FactorScheduleEntries", ("asOfDate", "AsOfDate"), ("factor", "Factor")),
                B("Dates.MaturityDate", "maturity", SecurityTermRead.OptionalDate),
                Module("DeclaredSchedule",
                    "new DeclaredScheduleTerms(ReadScheduleConventions(json, assetClass, versionedTerms),\n                ReadCallPutSchedules(json, assetClass, versionedTerms))"),
            ], VersionedSchedules: true, Inputs: SecurityCodecInputs.EconomicSchedules),
            CodecOrder = 15,
            EconomicFlattenings = [
                Map("tranche", ("structuredProduct", "trancheClass")),
                Map("poolId", ("structuredProduct", "poolIdentifier"), ("issuer", "issuerName")),
                Map("collateralType", ("structuredProduct", "collateralType"), ("issuer", "issuerProgram")),
                Map("originalFace", ("structuredProduct", "notionalBalance")),
                Map("currentFactor", ("structuredProduct", "factor")),
                Map("couponOrIndex", ("coupon", "couponType")),
                // The trustee-report pointer is evidence lineage on the structured-product module, not a
                // coupon frequency.
                Map("factorSchedule", ("structuredProduct", "factorScheduleReference")),
                Map("maturity", MaturityDate),
                Map("weightedAverageCoupon", ("structuredProduct", "weightedAvgCoupon")),
                Map("weightedAverageMaturityMonths", ("structuredProduct", "weightedAvgMaturityMonths")),
                Map("weightedAverageLoanAgeMonths", ("structuredProduct", "weightedAvgLoanAgeMos")),
                Map("seniority", ("trancheContract", "seniority")),
                Map("attachmentPoint", ("trancheContract", "attachmentPoint")),
                Map("detachmentPoint", ("trancheContract", "detachmentPoint")),
                Map("reinvestmentPeriodEnd", ("trancheContract", "reinvestmentPeriodEnd")),
                Map("nonCallPeriodEnd", ("trancheContract", "nonCallPeriodEnd")),
                new SecurityEconomicTermFlattening(
                    "structuredCreditKind", [("trancheContract", "structuredCreditKind")],
                    LabelFromFlags: [("structuredProduct", "isInterestOnly", "InterestOnly"),
                        ("structuredProduct", "isPrincipalOnly", "PrincipalOnly")])
            ],
            EconomicFlatteningOrder = 18
        };
}
