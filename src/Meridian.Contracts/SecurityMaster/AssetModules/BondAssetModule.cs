using Meridian.Contracts.Domain.Enums;
using static Meridian.Contracts.SecurityMaster.SecurityAssetModuleSchema;
using static Meridian.Contracts.SecurityMaster.SecurityAssetModuleRates;
using static Meridian.Contracts.SecurityMaster.SecurityAssetModuleCodec;
using static Meridian.Contracts.SecurityMaster.SecurityAssetModuleEconomics;

namespace Meridian.Contracts.SecurityMaster;

/// <summary>Compile-time contract contributions for Bond.</summary>
internal static class BondAssetModule
{
    private const string AssetClass = SecurityAssetClassNames.Bond;

    internal static SecurityAssetClassModule Create()
        => new(
            Descriptor: new(
                AssetClass: AssetClass,
                CarriesBondCouponStructure: true,
                AssetPackIds: ["fixed-income"],
                SupportsCanonicalBulletAmortization: true,
                InstrumentTypes: [InstrumentType.Bond],
                LotUnit: SecurityLotUnitKind.FaceValue,
                CashFlowBehavior: SecurityCashFlowBehavior.ContractualSchedule,
                ProjectionKind: SecurityTermsProjectionKind.Declarative,
                DefaultValuationModel: SecurityValuationModelKind.DiscountedCashFlow,
                SupportsBasicCreateWorkflow: false,
                PreferredIdentifierKinds:
                [
                    SecurityIdentifierKind.Isin,
                    SecurityIdentifierKind.Cusip,
                    SecurityIdentifierKind.Figi,
                    SecurityIdentifierKind.Bbgid,
                    SecurityIdentifierKind.ProviderSymbol,
                    SecurityIdentifierKind.InternalCode
                ],
                AssetOperationsCapabilities: AssetOperationsCapabilitySet.FixedIncome,
                AmortizesTowardPar: true,
                RequiresMaturity: true,
                // Legacy classification spellings that reach the accounting adapter as a sub-type or
                // type name rather than as the canonical asset class.
                Aliases: ["CorporateBond", "MunicipalBond"],
                AccountingInstrumentClass: SecurityAccountingInstrumentClasses.Bond,
                // Version 2 adds the optional scheduleConventions block and version 3 the optional
                // callSchedule and putSchedule arrays. Version 4 adds the explicit calculation companion,
                // and version 5 the signed bond workflow conventions. S2 retains floor 1 for documents
                // that declare none of those containers.
                TermsVersion: 5,
                TermsUpgrades:
                [
                    SecurityAssetClassTermsUpgrade.Identity(
                        fromVersion: 1,
                        rationale: "Version 2 adds the optional scheduleConventions block and changes no version-1 term, so a version-1 payload reads it as unset (ADR-024, 2026-10-25)."),
                    SecurityAssetClassTermsUpgrade.Identity(
                        fromVersion: 2,
                        rationale: "Version 3 adds the optional callSchedule and putSchedule arrays and changes no version-2 term, so a version-2 payload reads them as unset (ADR-024, 2026-10-25)."),
                    SecurityAssetClassTermsUpgrade.Identity(fromVersion: 3, rationale: "Version 4 adds the optional calculation-only cashFlowContract companion without changing earlier terms (ADR-024/025, 2026-10-29)."),
                    SecurityAssetClassTermsUpgrade.Identity(fromVersion: 4, rationale: "Version 5 adds explicit overnight and inflation workflow conventions without changing earlier terms; historical calculations retain their versions.")
                ])
            {
                StampFloor = 1,
                SupportsRecurringFixedIncomeAccrual = true,
                CashFlowTermRoles =
                [
                    new(SecurityCashFlowTermRole.Maturity, "maturity"),
                    new(SecurityCashFlowTermRole.Maturity, "legalFinalMaturity"),
                    new(SecurityCashFlowTermRole.Issue, "issueDate"),
                    new(SecurityCashFlowTermRole.PrincipalFace, "par"),
                    new(SecurityCashFlowTermRole.CouponRate, "couponRate"),
                    new(SecurityCashFlowTermRole.PaymentFrequency, "paymentFrequency"),
                    new(SecurityCashFlowTermRole.DayCount, "dayCount"),
                    new(SecurityCashFlowTermRole.InflationIndex, "inflationIndex"),
                    new(SecurityCashFlowTermRole.InflationBaseIndexValue, "inflationBaseIndexValue"),
                    new(SecurityCashFlowTermRole.InflationIndexRatio, "inflationIndexRatio")
                ]
            },
            Fields: [
                Req("maturity", SecurityAssetTermFieldType.Date),
                Opt("issueDate", SecurityAssetTermFieldType.Date),
                // couponType/couponRate/floatingIndex/spreadBps/dayCount are emitted flat by the
                // serializer; the legacy nested "coupon" object shape is read as a fallback.
                // couponType selects the BondCouponStructure case and has no escape: an
                // unrecognized value collapses to Fixed, dropping the label AND every field the
                // named structure owns (floatingIndex, the step schedule, the inflation block).
                OptOneOf("couponType", "Fixed", "Floating", "ZeroCoupon", "Step", "InflationLinked"),
                Opt("couponRate", SecurityAssetTermFieldType.Decimal),
                Opt("floatingIndex", SecurityAssetTermFieldType.String).LinksUnderlying(SecurityUnderlyingRole.Index),
                Opt("spreadBps", SecurityAssetTermFieldType.Decimal),
                Opt("capRate", SecurityAssetTermFieldType.Decimal),
                Opt("floorRate", SecurityAssetTermFieldType.Decimal),
                // Step-coupon schedule ({effectiveDate, rate}); empty for non-step coupons. The
                // inflation fields carry linker indexation (couponType = "InflationLinked"); null
                // otherwise. These are what make StepRate/FixedToFloat/InflationLinked subclasses
                // computable rather than labels.
                Opt("stepSchedule", SecurityAssetTermFieldType.Array),
                Opt("inflationIndex", SecurityAssetTermFieldType.String).LinksUnderlying(SecurityUnderlyingRole.Index),
                Opt("inflationBaseIndexValue", SecurityAssetTermFieldType.Decimal),
                Opt("inflationIndexRatio", SecurityAssetTermFieldType.Decimal),
                Opt("dayCount", SecurityAssetTermFieldType.String, "dayCountConvention").WithConvention(SecurityTermConventionKind.DayCount),
                Req("isCallable", SecurityAssetTermFieldType.Boolean).ReadsAbsentAs("false"),
                Opt("callDate", SecurityAssetTermFieldType.Date),
                Opt("issuerName", SecurityAssetTermFieldType.String),
                Opt("seniority", SecurityAssetTermFieldType.String),
                Req("subclass", SecurityAssetTermFieldType.String).ReadsAbsentAs("Corporate"),
                Opt("par", SecurityAssetTermFieldType.Decimal),
                Opt("paymentFrequency", SecurityAssetTermFieldType.String).WithConvention(SecurityTermConventionKind.CouponFrequency),
                Opt("legalFinalMaturity", SecurityAssetTermFieldType.Date),
                Opt("preRefundDate", SecurityAssetTermFieldType.Date),
                Opt("mandatoryPutDate", SecurityAssetTermFieldType.Date),
                // Contractual principal instalments ({paymentDate, amount}) for sinking-fund and
                // other scheduled-principal subclasses; empty for bullet bonds.
                Opt("principalSchedule", SecurityAssetTermFieldType.Array),
                // How the schedule kernel builds the coupon periods (asset-class terms version 2,
                // readable before writable: ADR-024). Inner shape: ScheduleConventionFields.
                Opt(SecurityVersionedTerms.ScheduleConventionsKey, SecurityAssetTermFieldType.Object).Since(2),
                // Declared call and put schedules (asset-class terms version 3, readable before
                // writable: ADR-024). Entry shapes: CallScheduleEntryFields, PutScheduleEntryFields.
                Opt(SecurityVersionedTerms.CallScheduleKey, SecurityAssetTermFieldType.Array).Since(3),
                Opt(SecurityVersionedTerms.PutScheduleKey, SecurityAssetTermFieldType.Array).Since(3),
                Opt("cashFlowContract", SecurityAssetTermFieldType.Object).Since(4).ReadOnly(),
                Opt("bondWorkflow", SecurityAssetTermFieldType.Object).Since(5)
            ],
            SchemaOrder: 3,
            ElementShapes:
            [
                new("principalSchedule", 3, [
                    Req("paymentDate", SecurityAssetTermFieldType.Date),
                    Req("amount", SecurityAssetTermFieldType.Decimal)
                ]),
                new("stepSchedule", 5, [
                    Req("effectiveDate", SecurityAssetTermFieldType.Date, "stepDate", "date"),
                    Req("rate", SecurityAssetTermFieldType.Decimal, "couponRate")
                ]),
                new(SecurityVersionedTerms.ScheduleConventionsKey, 10, ScheduleConventionFields),
                new("bondWorkflow", 11, [
                    OptOneOf("floatingMethod", "Simple", "CompoundedOvernight"),
                    Opt("fixingCalendarId", SecurityAssetTermFieldType.String),
                    Opt("lookbackBusinessDays", SecurityAssetTermFieldType.Integer),
                    OptOneOf("observationMethod", "LookbackWithoutShift", "ObservationShift"),
                    OptOneOf("observationWeighting", "ObservationPeriod", "InterestPeriod"),
                    Opt("lockoutBusinessDays", SecurityAssetTermFieldType.Integer),
                    Opt("lagMonths", SecurityAssetTermFieldType.Integer),
                    Opt("interpolateMonthly", SecurityAssetTermFieldType.Boolean),
                    Opt("couponFloorRatio", SecurityAssetTermFieldType.Decimal),
                    Opt("redemptionFloorRatio", SecurityAssetTermFieldType.Decimal),
                    Opt("exCouponBusinessDays", SecurityAssetTermFieldType.Integer)
                ]),
                new(SecurityVersionedTerms.CallScheduleKey, 14, CallScheduleEntryFields),
                new(SecurityVersionedTerms.PutScheduleKey, 15, PutScheduleEntryFields)
            ])
        {
            RateFields =
            [
                // Bond coupon structure. The coupon, cap, floor, step rates and floating fixings follow the
                // record's couponRateUnit; there is no default because stored bonds write both 4.25 and 0.0425.
                Coupon(AssetClass, "couponRate", "Annual fixed coupon rate"),
                SignedRate(AssetClass, "capRate", "Floating coupon cap", consumedBy: LegacyCashFlow + ", " + Preview),
                SignedRate(AssetClass, "floorRate", "Floating coupon floor", consumedBy: LegacyCashFlow + ", " + Preview),
                Coupon(AssetClass, "stepSchedule.rate", "Step-coupon rate from its effective date", Preview),
                SignedRate(AssetClass, "currentIndexRate", "Observed floating index fixing", consumedBy: LegacyCashFlow),
                SignedRate(AssetClass, "resetSchedule.fixingRate", "Observed floating index fixing on a reset date", consumedBy: LegacyCashFlow + ", " + Preview),
                Bps(AssetClass, "spreadBps", "Floating coupon spread over the index", allowsNegative: true, LegacyCashFlow + ", " + Preview),
                Bps(AssetClass, "callSchedule.makeWholeSpreadBps", "Make-whole call spread over the benchmark", allowsNegative: false)
            ],
            CodecBinding = new(AssetClass, false,
            [
                B("Dates.IssueDate", "issueDate", SecurityTermRead.OptionalDate),
                B("Dates.MaturityDate", "maturity", SecurityTermRead.RequiredDate, via: "Maturity"),
                // The coupon structure is one union written as ten keys; couponType discriminates.
                Fixed("CouponStructure.Structure", "ToBondCouponStructure(bound, mode, economicSchedules)"),
                WrittenAs("CouponStructure.Structure", "couponType", "FlatWriterParts.couponType terms.Coupon"),
                WrittenAs("CouponStructure.Structure", "couponRate", "BondTerms.couponRate terms"),
                WrittenAs("CouponStructure.Structure", "floatingIndex", "FlatWriterParts.floatingIndex terms.Coupon"),
                WrittenAs("CouponStructure.Structure", "spreadBps", "FlatWriterParts.floatingSpreadBps terms.Coupon"),
                WrittenAs("CouponStructure.Structure", "capRate", "FlatWriterParts.floatingCapRate terms.Coupon"),
                WrittenAs("CouponStructure.Structure", "floorRate", "FlatWriterParts.floatingFloorRate terms.Coupon"),
                WrittenAs("CouponStructure.Structure", "stepSchedule", "FlatWriterParts.stepSchedule terms.Coupon"),
                WrittenAs("CouponStructure.Structure", "inflationIndex", "FlatWriterParts.inflationIndex terms.Coupon"),
                WrittenAs("CouponStructure.Structure", "inflationBaseIndexValue", "FlatWriterParts.inflationBaseIndexValue terms.Coupon"),
                WrittenAs("CouponStructure.Structure", "inflationIndexRatio", "FlatWriterParts.inflationIndexRatio terms.Coupon"),
                WrittenAs("CouponStructure.Structure", "dayCount",
                    "BondTerms.dayCount terms |> Option.map Meridian.Contracts.SecurityMaster.DayCountConventions.TermLabel"),
                Computed("CouponStructure.PaymentFrequency", "paymentFrequency",
                    "ToPaymentFrequencyOption(bound.OptionalString(\"paymentFrequency\"))",
                    "terms.PaymentFrequency |> Option.map PaymentFrequency.label"),
                Module("Callability",
                    "EconomicCallTermsModule.Scalar(bound.OptionalBoolean(\"isCallable\") ?? false, ToOption(bound.OptionalDateOnly(\"callDate\")))"),
                WrittenFrom("Callability.IsCallable", "isCallable", "IsCallable"),
                WrittenFrom("Callability.FirstCallDate", "callDate", "CallDate"),
                B("IssuerTerms.IssuerName", "issuerName", SecurityTermRead.OptionalString),
                Computed("DebtClassification.Subclass", "subclass",
                    "ParseBondSubclass(bound.OptionalString(\"subclass\"))", "BondSubclass.label terms.Subclass"),
                B("DebtClassification.Seniority", "seniority", SecurityTermRead.OptionalString),
                B("RedemptionFacts.Par", "par", SecurityTermRead.OptionalDecimal),
                B("RedemptionFacts.LegalFinalMaturity", "legalFinalMaturity", SecurityTermRead.OptionalDate),
                B("RedemptionFacts.PreRefundDate", "preRefundDate", SecurityTermRead.OptionalDate),
                B("RedemptionFacts.MandatoryPutDate", "mandatoryPutDate", SecurityTermRead.OptionalDate),
                Listed("Principal.Payments", "principalSchedule", "PrincipalPayments(economicSchedules)", "PrincipalSchedule",
                    PrincipalPaymentItems),
                Module("DeclaredSchedule",
                    "new DeclaredScheduleTerms(ReadScheduleConventions(json, assetClass, versionedTerms),\n                ReadCallPutSchedules(json, assetClass, versionedTerms))"),
                // Written after stamping by the snapshot wrapper (absent rather than null when undeclared).
                Module("Workflow", "ReadBondWorkflowTerms(json, versionedTerms)"),
            ], VersionedSchedules: true, Inputs: SecurityCodecInputs.EconomicSchedules),
            CodecOrder = 3,
            EconomicFlattenings = [
                Map("maturity", MaturityDate),
                Map("issueDate", ("maturity", "issueDate")),
                Map("couponType", ("coupon", "couponType")),
                Map("couponRate", ("coupon", "couponRate")),
                Map("dayCount", ("coupon", "dayCount"), ("accrual", "dayCount")),
                Map("paymentFrequency", ("coupon", "paymentFrequency"), ("payment", "paymentFrequency")),
                Map("isCallable", ("call", "isCallable")),
                Map("callDate", ("call", "firstCallDate")),
                Map("issuerName", ("issuer", "issuerName")),
                Map("subclass", ("debtClassification", "subclass")),
                Map("seniority", ("debtClassification", "seniority")),
                Map("par", ("bondRedemption", "par")),
                Map("legalFinalMaturity", ("bondRedemption", "legalFinalMaturity")),
                Map("preRefundDate", ("bondRedemption", "preRefundDate")),
                Map("mandatoryPutDate", ("bondRedemption", "mandatoryPutDate"))
            ],
            EconomicFlatteningOrder = 7
        };
}
