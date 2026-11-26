using Meridian.Contracts.Domain.Enums;
using static Meridian.Contracts.SecurityMaster.SecurityAssetModuleSchema;
using static Meridian.Contracts.SecurityMaster.SecurityAssetModuleRates;
using static Meridian.Contracts.SecurityMaster.SecurityAssetModuleCodec;
using static Meridian.Contracts.SecurityMaster.SecurityAssetModuleEconomics;

namespace Meridian.Contracts.SecurityMaster;

/// <summary>Compile-time contract contributions for Deposit.</summary>
internal static class DepositAssetModule
{
    private const string AssetClass = SecurityAssetClassNames.Deposit;

    internal static SecurityAssetClassModule Create()
        => new(
            Descriptor: new(
                AssetClass: AssetClass,
                QuotesRatesAsFractions: true,
                AssetPackIds: ["cash-bank"],
                InstrumentTypes: [InstrumentType.Deposit],
                LotUnit: SecurityLotUnitKind.FaceValue,
                CashFlowBehavior: SecurityCashFlowBehavior.ContractualSchedule,
                ProjectionKind: SecurityTermsProjectionKind.Declarative,
                SupportsBasicCreateWorkflow: false,
                PreferredIdentifierKinds:
                [
                    SecurityIdentifierKind.InternalCode,
                    SecurityIdentifierKind.ProviderSymbol,
                    SecurityIdentifierKind.Isin,
                    SecurityIdentifierKind.Cusip
                ],
                TermsVersion: 2,
                TermsUpgrades: [SecurityAssetClassTermsUpgrade.Identity(fromVersion: 1, rationale: "Version 2 adds the optional governed scalar rateUnit declaration; existing values and units are unchanged.")])
            {
                CashFlowTermRoles =
                [
                    new(SecurityCashFlowTermRole.Maturity, "maturity"),
                    new(SecurityCashFlowTermRole.CouponRate, "interestRate"),
                    new(SecurityCashFlowTermRole.DayCount, "dayCount")
                ],
                StampFloor = 1,
                AccountingOutsideSlice = true
            },
            Fields: [
                OptOneOf("rateUnit", "Fraction", "Percent", "BasisPoints").WithVerbatimUndeclaredValue().Since(2),
                Req("depositType", SecurityAssetTermFieldType.String),
                Req("institutionName", SecurityAssetTermFieldType.String),
                Opt("maturity", SecurityAssetTermFieldType.Date),
                Opt("interestRate", SecurityAssetTermFieldType.Decimal),
                Opt("dayCount", SecurityAssetTermFieldType.String).WithConvention(SecurityTermConventionKind.DayCount),
                Req("isCallable", SecurityAssetTermFieldType.Boolean).ReadsAbsentAs("false")
            ],
            SchemaOrder: 5,
            ElementShapes: [])
        {
            RateFields =
            [
                // Cash and money-market rates. Deposit and CashSweep quote rates as fractions (catalog
                // QuotesRatesAsFractions); a negative deposit rate is a supported economic fact.
                SignedRate(AssetClass, "interestRate", "Annual deposit interest rate", RateUnit.Fraction, CouponReaders + ", " + Obligations)
            ],
            CouponRateFieldKey = "interestRate",
            CodecBinding = Class(AssetClass,
                B("Dates.MaturityDate", "maturity", SecurityTermRead.OptionalDate),
                B("IssuerTerms.InstitutionName", "institutionName", SecurityTermRead.RequiredString, via: "InstitutionName"),
                B("IssuerTerms.IssuerProgram", "depositType", SecurityTermRead.RequiredString, via: "DepositType"),
                Fixed("CouponTerms.CouponType", "FSharpOption<CouponKind>.Some(CouponKind.NewOtherCoupon(\"SimpleInterest\"))"),
                B("CouponTerms.CouponRate", "interestRate", SecurityTermRead.OptionalDecimal),
                B("CouponTerms.DayCount", "dayCount", SecurityTermRead.DayCount),
                Module("Callability", "EconomicCallTermsModule.Scalar(bound.OptionalBoolean(\"isCallable\") ?? false, null)"),
                WrittenFrom("Callability.IsCallable", "isCallable", "IsCallable")),
            CodecOrder = 5,
            EconomicFlattenings = [
                Map("depositType", ("issuer", "issuerProgram")),
                Map("institutionName", ("issuer", "institutionName")),
                Map("maturity", MaturityDate),
                Map("interestRate", ("coupon", "couponRate")),
                Map("dayCount", ("coupon", "dayCount")),
                Map("isCallable", ("call", "isCallable"))
            ],
            EconomicFlatteningOrder = 8
        };
}
