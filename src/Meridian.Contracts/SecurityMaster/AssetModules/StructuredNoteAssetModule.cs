using Meridian.Contracts.Domain.Enums;
using static Meridian.Contracts.SecurityMaster.SecurityAssetModuleSchema;
using static Meridian.Contracts.SecurityMaster.SecurityAssetModuleRates;
using static Meridian.Contracts.SecurityMaster.SecurityAssetModuleCodec;
using static Meridian.Contracts.SecurityMaster.SecurityAssetModuleEconomics;

namespace Meridian.Contracts.SecurityMaster;

/// <summary>Compile-time contract contributions for StructuredNote.</summary>
internal static class StructuredNoteAssetModule
{
    private const string AssetClass = SecurityAssetClassNames.StructuredNote;

    internal static SecurityAssetClassModule Create()
        => new(
            Descriptor: new(
                AssetClass: AssetClass,
                AssetPackIds: ["fixed-income"],
                LotUnit: SecurityLotUnitKind.FaceValue,
                CashFlowBehavior: SecurityCashFlowBehavior.None,
                ProjectionKind: SecurityTermsProjectionKind.Declarative,
                SupportsBasicCreateWorkflow: false,
                PreferredIdentifierKinds:
                [
                    SecurityIdentifierKind.Isin,
                    SecurityIdentifierKind.Cusip,
                    SecurityIdentifierKind.InternalCode
                ],
                // The owner chose valuation-mark FVTPL (2026-11-11). This is deliberately outside the
                // coupon/accrual event slice: FixedIncome taxonomy must not route it into bond posting.
                RequiresMaturity: true,
                TermsVersion: 2,
                TermsUpgrades: [SecurityAssetClassTermsUpgrade.Identity(fromVersion: 1, rationale: "Version 2 adds the optional governed scalar rateUnit declaration; existing values and units are unchanged.")])
            {
                StampFloor = 1,
                AccountingOutsideSlice = true,
                ValuationAccountingTreatment = SecurityValuationAccountingTreatment.FairValueThroughProfitOrLoss
            },
            Fields: [
                OptOneOf("rateUnit", "Fraction", "Percent", "BasisPoints").WithVerbatimUndeclaredValue().Since(2),
                Req("issuerName", SecurityAssetTermFieldType.String, "issuer"),
                Req("underlyingDescription", SecurityAssetTermFieldType.String, "underlying", "referenceAsset").LinksUnderlying(SecurityUnderlyingRole.Underlying),
                Req("maturity", SecurityAssetTermFieldType.Date, "maturityDate"),
                Opt("issueDate", SecurityAssetTermFieldType.Date),
                Opt("denomination", SecurityAssetTermFieldType.Decimal, "par", "faceAmount"),
                Opt("principalProtectionPct", SecurityAssetTermFieldType.Decimal, "principalProtection"),
                Opt("participationRate", SecurityAssetTermFieldType.Decimal, "participation"),
                Opt("returnCap", SecurityAssetTermFieldType.Decimal, "cap"),
                Opt("couponRate", SecurityAssetTermFieldType.Decimal, "coupon"),
                Opt("paymentFrequency", SecurityAssetTermFieldType.String, "frequency").WithConvention(SecurityTermConventionKind.CouponFrequency),
                Opt("barrierLevel", SecurityAssetTermFieldType.Decimal, "barrier")
            ],
            SchemaOrder: 30,
            ElementShapes: [])
        {
            RateFields =
            [
                // Structured notes: participation and caps are leverage ratios, not annual rates.
                Ratio(AssetClass, "principalProtectionPct", "Protected share of principal", null),
                Ratio(AssetClass, "participationRate", "Participation in the underlying's return", null),
                Ratio(AssetClass, "returnCap", "Cap on the participating return", null),
                Coupon(AssetClass, "couponRate", "Annual note coupon rate", "")
            ],
            CodecBinding = Class(AssetClass,
                B("Dates.IssueDate", "issueDate", SecurityTermRead.StrictDate),
                B("Dates.MaturityDate", "maturity", SecurityTermRead.RequiredDate, via: "Maturity"),
                B("StructuredPayoff.UnderlyingDescription", "underlyingDescription", SecurityTermRead.RequiredString),
                B("StructuredPayoff.PrincipalProtectionPct", "principalProtectionPct", SecurityTermRead.StrictDecimal),
                B("StructuredPayoff.ParticipationRate", "participationRate", SecurityTermRead.StrictDecimal),
                B("StructuredPayoff.ReturnCap", "returnCap", SecurityTermRead.StrictDecimal),
                B("StructuredPayoff.BarrierLevel", "barrierLevel", SecurityTermRead.StrictDecimal),
                B("Notional.Denomination", "denomination", SecurityTermRead.StrictDecimal),
                B("IssuerTerms.IssuerName", "issuerName", SecurityTermRead.RequiredString, via: "IssuerName"),
                B("ContractualRate.CouponRate", "couponRate", SecurityTermRead.StrictDecimal),
                B("ContractualRate.PaymentFrequency", "paymentFrequency", SecurityTermRead.ConventionString)),
            CodecOrder = 29,
            EconomicFlattenings = [
            Map("maturity", MaturityDate),
            Map("issueDate", ("maturity", "issueDate")),
            Map("issuerName", ("issuer", "issuerName")),
            Map("couponRate", ("coupon", "couponRate")),
            Map("paymentFrequency", ("coupon", "paymentFrequency")),
            Map("denomination", ("notional", "denomination")),
            Map("underlyingDescription", ("structuredPayoff", "underlyingDescription")),
            Map("principalProtectionPct", ("structuredPayoff", "principalProtectionPct")),
            Map("participationRate", ("structuredPayoff", "participationRate")),
            Map("returnCap", ("structuredPayoff", "returnCap")),
            Map("barrierLevel", ("structuredPayoff", "barrierLevel"))],
            EconomicFlatteningOrder = 29
        };
}
