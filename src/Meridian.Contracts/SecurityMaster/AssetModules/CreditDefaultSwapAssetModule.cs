using Meridian.Contracts.Domain.Enums;
using static Meridian.Contracts.SecurityMaster.SecurityAssetModuleSchema;
using static Meridian.Contracts.SecurityMaster.SecurityAssetModuleRates;
using static Meridian.Contracts.SecurityMaster.SecurityAssetModuleCodec;
using static Meridian.Contracts.SecurityMaster.SecurityAssetModuleEconomics;

namespace Meridian.Contracts.SecurityMaster;

/// <summary>Compile-time contract contributions for CreditDefaultSwap.</summary>
internal static class CreditDefaultSwapAssetModule
{
    private const string AssetClass = SecurityAssetClassNames.CreditDefaultSwap;

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
                    SecurityIdentifierKind.Lei,
                    SecurityIdentifierKind.ProviderSymbol,
                    SecurityIdentifierKind.Uti,
                    SecurityIdentifierKind.Upi,
                    SecurityIdentifierKind.MarkitRedEntity,
                    SecurityIdentifierKind.MarkitRedPair
                ],
                RequiresMaturity: true)
            { SupportsCreditLifecycle = true, AccountingOutsideSlice = true },
            Fields: [
                Req("referenceEntity", SecurityAssetTermFieldType.String).LinksUnderlying(SecurityUnderlyingRole.ReferenceEntity),
                Opt("referenceObligation", SecurityAssetTermFieldType.String).LinksUnderlying(SecurityUnderlyingRole.ReferenceObligation),
                ReqOneOf("protectionSide", "Buy", "Sell").WithVerbatimUndeclaredValue(),
                Req("notional", SecurityAssetTermFieldType.Decimal, "notionalAmount"),
                Req("fixedCouponBps", SecurityAssetTermFieldType.Decimal, "couponBps", "runningSpreadBps"),
                Req("effectiveDate", SecurityAssetTermFieldType.Date),
                Req("maturityDate", SecurityAssetTermFieldType.Date, "scheduledTerminationDate"),
                Opt("paymentFrequency", SecurityAssetTermFieldType.String, "frequency").WithConvention(SecurityTermConventionKind.CouponFrequency),
                Opt("dayCount", SecurityAssetTermFieldType.String, "dayCountConvention").WithConvention(SecurityTermConventionKind.DayCount),
                Opt("seniority", SecurityAssetTermFieldType.String),
                Opt("restructuringClause", SecurityAssetTermFieldType.String, "docClause"),
                Opt("upfrontPoints", SecurityAssetTermFieldType.Decimal),
                Opt("recoveryRateAssumption", SecurityAssetTermFieldType.Decimal, "recoveryRate"),
                Opt("indexName", SecurityAssetTermFieldType.String).LinksUnderlying(SecurityUnderlyingRole.Index)
            ],
            SchemaOrder: 27,
            ElementShapes: [])
        {
            RateFields =
            [
                Bps(AssetClass, "fixedCouponBps", "CDS running coupon", allowsNegative: false),
                Ratio(AssetClass, "recoveryRateAssumption", "Assumed recovery rate", 1m)
            ],
            CodecBinding = Class(AssetClass,
                B("Dates.EffectiveDate", "effectiveDate", SecurityTermRead.RequiredDate, via: "EffectiveDate"),
                Also("Dates.IssueDate", "effectiveDate", SecurityTermRead.RequiredDate),
                B("Dates.MaturityDate", "maturityDate", SecurityTermRead.RequiredDate, via: "MaturityDate"),
                B("CreditProtection.ReferenceEntity", "referenceEntity", SecurityTermRead.RequiredString),
                B("CreditProtection.ReferenceObligation", "referenceObligation", SecurityTermRead.ConventionString),
                B("CreditProtection.ProtectionSide", "protectionSide", SecurityTermRead.RequiredString),
                B("CreditProtection.Seniority", "seniority", SecurityTermRead.ConventionString),
                B("CreditProtection.RestructuringClause", "restructuringClause", SecurityTermRead.ConventionString),
                B("CreditProtection.UpfrontPoints", "upfrontPoints", SecurityTermRead.StrictDecimal),
                B("CreditProtection.RecoveryRateAssumption", "recoveryRateAssumption", SecurityTermRead.StrictDecimal),
                B("CreditProtection.IndexName", "indexName", SecurityTermRead.ConventionString),
                B("Notional.Notional", "notional", SecurityTermRead.RequiredDecimal),
                B("ContractualRate.FixedCouponBps", "fixedCouponBps", SecurityTermRead.RequiredDecimal, via: "FixedCouponBps"),
                B("ContractualRate.PaymentFrequency", "paymentFrequency", SecurityTermRead.ConventionString),
                B("ContractualRate.DayCount", "dayCount", SecurityTermRead.DayCount)),
            CodecOrder = 26,
            EconomicFlattenings = [
            Map("effectiveDate", ("maturity", "effectiveDate")),
            Map("maturityDate", MaturityDate),
            Map("paymentFrequency", ("coupon", "paymentFrequency")),
            Map("dayCount", ("coupon", "dayCount")),
            // The upgrade stores FixedCouponBps / 10000 as coupon.couponRate.
            new SecurityEconomicTermFlattening("fixedCouponBps", [("coupon", "couponRate")], Scale: 10000m),
            Map("notional", ("notional", "notional")),
            Map("referenceEntity", ("creditProtection", "referenceEntity")),
            Map("referenceObligation", ("creditProtection", "referenceObligation")),
            Map("protectionSide", ("creditProtection", "protectionSide")),
            Map("seniority", ("creditProtection", "seniority")),
            Map("restructuringClause", ("creditProtection", "restructuringClause")),
            Map("upfrontPoints", ("creditProtection", "upfrontPoints")),
            Map("recoveryRateAssumption", ("creditProtection", "recoveryRateAssumption")),
            Map("indexName", ("creditProtection", "indexName"))],
            EconomicFlatteningOrder = 26
        };
}
