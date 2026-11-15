using Meridian.Contracts.Domain.Enums;
using static Meridian.Contracts.SecurityMaster.SecurityAssetModuleSchema;
using static Meridian.Contracts.SecurityMaster.SecurityAssetModuleRates;
using static Meridian.Contracts.SecurityMaster.SecurityAssetModuleCodec;
using static Meridian.Contracts.SecurityMaster.SecurityAssetModuleEconomics;

namespace Meridian.Contracts.SecurityMaster;

/// <summary>Compile-time contract contributions for CertificateOfDeposit.</summary>
internal static class CertificateOfDepositAssetModule
{
    private const string AssetClass = "CertificateOfDeposit";

    internal static SecurityAssetClassModule Create()
        => new(
            Descriptor: new(
                AssetClass: AssetClass,
                AssetPackIds: ["fixed-income"],
                InstrumentTypes: [InstrumentType.Deposit],
                LotUnit: SecurityLotUnitKind.FaceValue,
                CashFlowBehavior: SecurityCashFlowBehavior.ContractualSchedule,
                ProjectionKind: SecurityTermsProjectionKind.Declarative,
                SupportsBasicCreateWorkflow: false,
                PreferredIdentifierKinds:
                [
                    SecurityIdentifierKind.Isin,
                    SecurityIdentifierKind.Cusip,
                    SecurityIdentifierKind.Figi,
                    SecurityIdentifierKind.ProviderSymbol,
                    SecurityIdentifierKind.InternalCode
                ],
                AmortizesTowardPar: true,
                RequiresMaturity: true,
                AccountingInstrumentClass: SecurityAccountingInstrumentClasses.Bond,
                // Version 2 adds the optional scheduleConventions block and version 3 the optional
                // callSchedule and putSchedule arrays. Version 6 adds explicitly reviewed scalar
                // accrual anchors; S2 keeps undeclared documents at floor 1 after each raise.
                TermsVersion: 6,
                TermsUpgrades:
                [
                    SecurityAssetClassTermsUpgrade.Identity(
                        fromVersion: 1,
                        rationale: "Version 2 adds the optional scheduleConventions block and changes no version-1 term, so a version-1 payload reads it as unset (ADR-024, 2026-10-25)."),
                    SecurityAssetClassTermsUpgrade.Identity(
                        fromVersion: 2,
                        rationale: "Version 3 adds the optional callSchedule and putSchedule arrays and changes no version-2 term, so a version-2 payload reads them as unset (ADR-024, 2026-10-25)."),
                    SecurityAssetClassTermsUpgrade.Identity(fromVersion: 3, rationale: "Version 4 adds the optional calculation-only cashFlowContract companion without changing earlier terms (ADR-024/025, 2026-10-29)."),
                    SecurityAssetClassTermsUpgrade.Identity(fromVersion: 4, rationale: "Version 5 adds the optional governed scalar rateUnit declaration without changing earlier terms."),
                    SecurityAssetClassTermsUpgrade.Identity(fromVersion: 5, rationale: "Version 6 adds optional explicit issue date, principal and payment frequency for reviewed fixed-bullet accrual. Earlier records retain absent anchors.")
                ])
            {
                CashFlowTermRoles =
                [
                    new(SecurityCashFlowTermRole.Maturity, "maturity"),
                    new(SecurityCashFlowTermRole.Issue, "issueDate"),
                    new(SecurityCashFlowTermRole.PrincipalFace, "principal"),
                    new(SecurityCashFlowTermRole.CouponRate, "couponRate"),
                    new(SecurityCashFlowTermRole.PaymentFrequency, "paymentFrequency"),
                    new(SecurityCashFlowTermRole.DayCount, "dayCount")
                ],
                RecurringAccrualPolicy = SecurityRecurringAccrualPolicies.FixedBulletActual365,
                StampFloor = 1
            },
            Fields: [
                OptOneOf("rateUnit", "Fraction", "Percent", "BasisPoints").WithVerbatimUndeclaredValue().Since(5),
                Req("issuerName", SecurityAssetTermFieldType.String),
                Req("maturity", SecurityAssetTermFieldType.Date),
                Opt("couponRate", SecurityAssetTermFieldType.Decimal),
                Opt("callableDate", SecurityAssetTermFieldType.Date),
                Opt("dayCount", SecurityAssetTermFieldType.String).WithConvention(SecurityTermConventionKind.DayCount),
                Opt("issueDate", SecurityAssetTermFieldType.Date).Since(6),
                Opt("principal", SecurityAssetTermFieldType.Decimal).Since(6),
                Opt("paymentFrequency", SecurityAssetTermFieldType.String).WithConvention(SecurityTermConventionKind.CouponFrequency).Since(6),
                // Asset-class terms version 2, readable before writable (ADR-024).
                Opt("scheduleConventions", SecurityAssetTermFieldType.Object).Since(2),
                // Declared call and put schedules (asset-class terms version 3, readable before
                // writable: ADR-024). Entry shapes: CallScheduleEntryFields, PutScheduleEntryFields.
                Opt("callSchedule", SecurityAssetTermFieldType.Array).Since(3),
                Opt("putSchedule", SecurityAssetTermFieldType.Array).Since(3),
                Opt("cashFlowContract", SecurityAssetTermFieldType.Object).Since(4).ReadOnly()
            ],
            SchemaOrder: 7,
            ElementShapes:
            [
                new("scheduleConventions", 12, ScheduleConventionFields),
                new("callSchedule", 16, CallScheduleEntryFields),
                new("putSchedule", 17, PutScheduleEntryFields)
            ])
        {
            RateFields =
            [
                Coupon(AssetClass, "couponRate", "Annual CD coupon rate")
            ],
            CodecBinding = ScheduledClass(AssetClass,
                B("Dates.IssueDate", "issueDate", SecurityTermRead.StrictDate),
                B("Dates.MaturityDate", "maturity", SecurityTermRead.RequiredDate, via: "Maturity"),
                B("Notional.Notional", "principal", SecurityTermRead.StrictDecimal),
                B("IssuerTerms.IssuerName", "issuerName", SecurityTermRead.RequiredString, via: "IssuerName"),
                Fixed("CouponTerms.CouponType", "FSharpOption<CouponKind>.Some(CouponKind.Fixed)"),
                B("CouponTerms.CouponRate", "couponRate", SecurityTermRead.OptionalDecimal),
                Computed("CouponTerms.PaymentFrequency", "paymentFrequency",
                    "ToPaymentFrequencyOption(bound.OptionalConventionString(\"paymentFrequency\"))",
                    "terms.CouponTerms.PaymentFrequency |> Option.map PaymentFrequency.label"),
                B("CouponTerms.DayCount", "dayCount", SecurityTermRead.DayCount),
                Module("Callability",
                    "EconomicCallTermsModule.Scalar(bound.OptionalDateOnly(\"callableDate\").HasValue, ToOption(bound.OptionalDateOnly(\"callableDate\")))"),
                WrittenFrom("Callability.FirstCallDate", "callableDate", "CallableDate"),
                Module("DeclaredSchedule",
                    "new DeclaredScheduleTerms(ReadScheduleConventions(json, assetClass, versionedTerms),\n                ReadCallPutSchedules(json, assetClass, versionedTerms))")) with
            {
                VersionedOptionalScalarKeys = ["issueDate", "principal", "paymentFrequency"]
            },
            CodecOrder = 7,
            EconomicFlattenings = [
                Map("issuerName", ("issuer", "issuerName")),
                Map("maturity", MaturityDate),
                Map("issueDate", ("maturity", "issueDate")),
                Map("principal", ("notional", "notional")),
                Map("couponRate", ("coupon", "couponRate")),
                Map("paymentFrequency", ("coupon", "paymentFrequency")),
                Map("callableDate", ("call", "firstCallDate")),
                Map("dayCount", ("coupon", "dayCount"))
            ],
            EconomicFlatteningOrder = 10
        };
}
