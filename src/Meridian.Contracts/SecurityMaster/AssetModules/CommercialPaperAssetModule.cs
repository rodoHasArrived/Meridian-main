using Meridian.Contracts.Domain.Enums;
using static Meridian.Contracts.SecurityMaster.SecurityAssetModuleSchema;
using static Meridian.Contracts.SecurityMaster.SecurityAssetModuleRates;
using static Meridian.Contracts.SecurityMaster.SecurityAssetModuleCodec;
using static Meridian.Contracts.SecurityMaster.SecurityAssetModuleEconomics;

namespace Meridian.Contracts.SecurityMaster;

/// <summary>Compile-time contract contributions for CommercialPaper.</summary>
internal static class CommercialPaperAssetModule
{
    private const string AssetClass = "CommercialPaper";

    internal static SecurityAssetClassModule Create()
        => new(
            Descriptor: new(
                AssetClass: AssetClass,
                AssetPackIds: ["fixed-income"],
                SupportsCanonicalBulletAmortization: true,
                InstrumentTypes: [InstrumentType.Bond],
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
                TermsVersion: 2,
                TermsUpgrades: [SecurityAssetClassTermsUpgrade.Identity(fromVersion: 1, rationale: "Version 2 adds the optional governed scalar rateUnit declaration; existing values and units are unchanged.")])
            {
                CashFlowTermRoles =
                [
                    new(SecurityCashFlowTermRole.Maturity, "maturity"),
                    new(SecurityCashFlowTermRole.DayCount, "dayCount")
                ],
                PaysNoContractualCoupon = true,
                StampFloor = 1
            },
            Fields: [
                OptOneOf("rateUnit", "Fraction", "Percent", "BasisPoints").WithVerbatimUndeclaredValue().Since(2),
                Req("issuerName", SecurityAssetTermFieldType.String),
                Req("maturity", SecurityAssetTermFieldType.Date),
                Opt("discountRate", SecurityAssetTermFieldType.Decimal),
                Opt("dayCount", SecurityAssetTermFieldType.String).WithConvention(SecurityTermConventionKind.DayCount),
                Req("isAssetBacked", SecurityAssetTermFieldType.Boolean).ReadsAbsentAs("false")
            ],
            SchemaOrder: 8,
            ElementShapes: [])
        {
            RateFields =
            [
                Coupon(AssetClass, "discountRate", "Discount yield (not a coupon; accretion only)", "")
            ],
            CouponRateFieldKey = "discountRate",
            CodecBinding = Class(AssetClass,
                B("Dates.MaturityDate", "maturity", SecurityTermRead.RequiredDate, via: "Maturity"),
                B("IssuerTerms.IssuerName", "issuerName", SecurityTermRead.RequiredString, via: "IssuerName"),
                Converted("IssuerTerms.IssuerProgram", "isAssetBacked",
                    "ToOption((bound.OptionalBoolean(\"isAssetBacked\") ?? false) ? \"AssetBacked\" : null)", "IsAssetBacked"),
                B("DiscountTerms.DiscountRate", "discountRate", SecurityTermRead.OptionalDecimal),
                Fixed("CouponTerms.CouponType", "FSharpOption<CouponKind>.Some(CouponKind.DiscountNote)"),
                B("CouponTerms.DayCount", "dayCount", SecurityTermRead.DayCount)),
            CodecOrder = 8,
            EconomicFlattenings = [
                Map("issuerName", ("issuer", "issuerName")),
                Map("maturity", MaturityDate),
                Map("discountRate", ("discount", "discountRate")),
                Map("dayCount", ("coupon", "dayCount")),
                new SecurityEconomicTermFlattening("isAssetBacked", [("issuer", "issuerProgram")], EqualsLabel: "AssetBacked")
            ],
            EconomicFlatteningOrder = 11
        };
}
