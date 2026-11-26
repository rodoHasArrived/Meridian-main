using Meridian.Contracts.Domain.Enums;
using static Meridian.Contracts.SecurityMaster.SecurityAssetModuleSchema;
using static Meridian.Contracts.SecurityMaster.SecurityAssetModuleRates;
using static Meridian.Contracts.SecurityMaster.SecurityAssetModuleCodec;
using static Meridian.Contracts.SecurityMaster.SecurityAssetModuleEconomics;

namespace Meridian.Contracts.SecurityMaster;

/// <summary>Compile-time contract contributions for CertificateOfDeposit.</summary>
internal static class CertificateOfDepositAssetModule
{
    private const string AssetClass = SecurityAssetClassNames.CertificateOfDeposit;

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
                // callSchedule and putSchedule arrays; S2 keeps undeclared documents at floor 1 after the raise.
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
                    SecurityAssetClassTermsUpgrade.Identity(fromVersion: 4, rationale: "Version 5 adds the optional governed scalar rateUnit declaration without changing earlier terms.")
                ])
            {
                CashFlowTermRoles =
                [
                    new(SecurityCashFlowTermRole.Maturity, "maturity"),
                    new(SecurityCashFlowTermRole.CouponRate, "couponRate"),
                    new(SecurityCashFlowTermRole.DayCount, "dayCount")
                ],
                StampFloor = 1
            },
            Fields: [
                OptOneOf("rateUnit", "Fraction", "Percent", "BasisPoints").WithVerbatimUndeclaredValue().Since(5),
                Req("issuerName", SecurityAssetTermFieldType.String),
                Req("maturity", SecurityAssetTermFieldType.Date),
                Opt("couponRate", SecurityAssetTermFieldType.Decimal),
                Opt("callableDate", SecurityAssetTermFieldType.Date),
                Opt("dayCount", SecurityAssetTermFieldType.String).WithConvention(SecurityTermConventionKind.DayCount),
                // Asset-class terms version 2, readable before writable (ADR-024).
                Opt(SecurityVersionedTerms.ScheduleConventionsKey, SecurityAssetTermFieldType.Object).Since(2),
                // Declared call and put schedules (asset-class terms version 3, readable before
                // writable: ADR-024). Entry shapes: CallScheduleEntryFields, PutScheduleEntryFields.
                Opt(SecurityVersionedTerms.CallScheduleKey, SecurityAssetTermFieldType.Array).Since(3),
                Opt(SecurityVersionedTerms.PutScheduleKey, SecurityAssetTermFieldType.Array).Since(3),
                Opt("cashFlowContract", SecurityAssetTermFieldType.Object).Since(4).ReadOnly()
            ],
            SchemaOrder: 7,
            ElementShapes:
            [
                new(SecurityVersionedTerms.ScheduleConventionsKey, 12, ScheduleConventionFields),
                new(SecurityVersionedTerms.CallScheduleKey, 16, CallScheduleEntryFields),
                new(SecurityVersionedTerms.PutScheduleKey, 17, PutScheduleEntryFields)
            ])
        {
            RateFields =
            [
                Coupon(AssetClass, "couponRate", "Annual CD coupon rate")
            ],
            CodecBinding = ScheduledClass(AssetClass,
                B("Dates.MaturityDate", "maturity", SecurityTermRead.RequiredDate, via: "Maturity"),
                B("IssuerTerms.IssuerName", "issuerName", SecurityTermRead.RequiredString, via: "IssuerName"),
                Fixed("CouponTerms.CouponType", "FSharpOption<CouponKind>.Some(CouponKind.Fixed)"),
                B("CouponTerms.CouponRate", "couponRate", SecurityTermRead.OptionalDecimal),
                B("CouponTerms.DayCount", "dayCount", SecurityTermRead.DayCount),
                Module("Callability",
                    "EconomicCallTermsModule.Scalar(bound.OptionalDateOnly(\"callableDate\").HasValue, ToOption(bound.OptionalDateOnly(\"callableDate\")))"),
                WrittenFrom("Callability.FirstCallDate", "callableDate", "CallableDate"),
                Module("DeclaredSchedule",
                    "new DeclaredScheduleTerms(ReadScheduleConventions(json, assetClass, versionedTerms),\n                ReadCallPutSchedules(json, assetClass, versionedTerms))")),
            CodecOrder = 7,
            EconomicFlattenings = [
                Map("issuerName", ("issuer", "issuerName")),
                Map("maturity", MaturityDate),
                Map("couponRate", ("coupon", "couponRate")),
                Map("callableDate", ("call", "firstCallDate")),
                Map("dayCount", ("coupon", "dayCount"))
            ],
            EconomicFlatteningOrder = 10
        };
}
