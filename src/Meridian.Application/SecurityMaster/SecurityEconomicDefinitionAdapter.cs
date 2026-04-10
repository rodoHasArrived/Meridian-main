using System.Text.Json;
using Meridian.Contracts.SecurityMaster;
using Meridian.FSharp.Domain;

namespace Meridian.Application.SecurityMaster;

internal static class SecurityEconomicDefinitionAdapter
{
    public static SecurityEconomicDefinition ToEconomicDefinition(SecurityProjectionRecord projection)
        => SecurityMasterLegacyUpgrade.toEconomicDefinition(SecurityMasterMapping.ToRecord(projection));

    public static SecurityEconomicDefinitionRecord ToEconomicRecord(SecurityProjectionRecord projection)
    {
        var definition = ToEconomicDefinition(projection);

        return new SecurityEconomicDefinitionRecord(
            projection.SecurityId,
            definition.Classification.AssetClass.ToString(),
            definition.Classification.Family?.ToString(),
            definition.Classification.SubType.ToString(),
            definition.Classification.TypeName,
            definition.Classification.IssuerType is null ? null : definition.Classification.IssuerType.Value,
            definition.Classification.RiskCountry is null ? null : definition.Classification.RiskCountry.Value,
            projection.Status,
            projection.DisplayName,
            projection.Currency,
            BuildClassificationJson(definition),
            projection.CommonTerms,
            BuildEconomicTermsJson(definition),
            projection.Provenance,
            projection.Version,
            projection.EffectiveFrom,
            projection.EffectiveTo,
            projection.Identifiers,
            projection.AssetClass,
            projection.AssetSpecificTerms);
    }

    public static string GetAssetClass(SecurityProjectionRecord projection)
        => ToEconomicDefinition(projection).Classification.AssetClass.ToString();

    public static string GetSubType(SecurityProjectionRecord projection)
        => ToEconomicDefinition(projection).Classification.SubType.ToString();

    public static SecurityProjectionRecord ToProjection(
        SecurityEconomicDefinitionRecord economic,
        IReadOnlyList<SecurityAliasDto>? aliases = null)
    {
        var legacyAssetClass = string.IsNullOrWhiteSpace(economic.LegacyAssetClass)
            ? economic.TypeName
            : economic.LegacyAssetClass;

        var assetSpecificTerms = economic.LegacyAssetSpecificTerms?.Clone() ?? economic.EconomicTerms;
        var primaryIdentifier = economic.Identifiers.FirstOrDefault(identifier => identifier.IsPrimary);

        return new SecurityProjectionRecord(
            economic.SecurityId,
            legacyAssetClass!,
            economic.Status,
            economic.DisplayName,
            economic.Currency,
            primaryIdentifier?.Kind.ToString() ?? string.Empty,
            primaryIdentifier?.Value ?? string.Empty,
            economic.CommonTerms,
            assetSpecificTerms,
            economic.Provenance,
            economic.Version,
            economic.EffectiveFrom,
            economic.EffectiveTo,
            economic.Identifiers,
            aliases ?? Array.Empty<SecurityAliasDto>());
    }

    private static JsonElement BuildClassificationJson(SecurityEconomicDefinition definition)
        => JsonSerializer.SerializeToElement(new
        {
            assetClass = definition.Classification.AssetClass.ToString(),
            assetFamily = definition.Classification.Family?.ToString(),
            subType = definition.Classification.SubType.ToString(),
            typeName = definition.Classification.TypeName,
            issuerType = definition.Classification.IssuerType is null ? null : definition.Classification.IssuerType.Value,
            riskCountry = definition.Classification.RiskCountry is null ? null : definition.Classification.RiskCountry.Value
        });

    private static JsonElement BuildEconomicTermsJson(SecurityEconomicDefinition definition)
        => JsonSerializer.SerializeToElement(new
        {
            schemaVersion = 2,
            maturity = definition.Terms.Maturity is null ? null : new
            {
                effectiveDate = definition.Terms.Maturity.Value.EffectiveDate,
                issueDate = definition.Terms.Maturity.Value.IssueDate,
                maturityDate = definition.Terms.Maturity.Value.MaturityDate
            },
            coupon = definition.Terms.Coupon is null ? null : new
            {
                couponType = definition.Terms.Coupon.Value.CouponType is null ? null : CouponKindModule.Label(definition.Terms.Coupon.Value.CouponType.Value),
                couponRate = definition.Terms.Coupon.Value.CouponRate,
                paymentFrequency = definition.Terms.Coupon.Value.PaymentFrequency is null ? null : PaymentFrequencyModule.Label(definition.Terms.Coupon.Value.PaymentFrequency.Value),
                dayCount = definition.Terms.Coupon.Value.DayCount is null ? null : DayCountConventionModule.Label(definition.Terms.Coupon.Value.DayCount.Value)
            },
            discount = definition.Terms.Discount is null ? null : new
            {
                discountRate = definition.Terms.Discount.Value.DiscountRate,
                yieldRate = definition.Terms.Discount.Value.YieldRate
            },
            accrual = definition.Terms.Accrual is null ? null : new
            {
                accrualMethod = definition.Terms.Accrual.Value.AccrualMethod is null ? null : definition.Terms.Accrual.Value.AccrualMethod.Value,
                accrualStartDate = definition.Terms.Accrual.Value.AccrualStartDate,
                exDividendDays = definition.Terms.Accrual.Value.ExDividendDays,
                businessDayConvention = definition.Terms.Accrual.Value.BusinessDayConvention is null ? null : definition.Terms.Accrual.Value.BusinessDayConvention.Value,
                holidayCalendar = definition.Terms.Accrual.Value.HolidayCalendar is null ? null : definition.Terms.Accrual.Value.HolidayCalendar.Value,
                dayCount = definition.Terms.Accrual.Value.DayCount is null ? null : DayCountConventionModule.Label(definition.Terms.Accrual.Value.DayCount.Value)
            },
            payment = definition.Terms.Payment is null ? null : new
            {
                paymentFrequency = definition.Terms.Payment.Value.PaymentFrequency is null ? null : PaymentFrequencyModule.Label(definition.Terms.Payment.Value.PaymentFrequency.Value),
                paymentLagDays = definition.Terms.Payment.Value.PaymentLagDays,
                paymentCurrency = definition.Terms.Payment.Value.PaymentCurrency is null ? null : definition.Terms.Payment.Value.PaymentCurrency.Value
            },
            redemption = definition.Terms.Redemption is null ? null : new
            {
                redemptionType = definition.Terms.Redemption.Value.RedemptionType is null ? null : RedemptionStyleModule.Label(definition.Terms.Redemption.Value.RedemptionType.Value),
                redemptionPrice = definition.Terms.Redemption.Value.RedemptionPrice,
                isBullet = definition.Terms.Redemption.Value.IsBullet,
                isAmortizing = definition.Terms.Redemption.Value.IsAmortizing
            },
            call = definition.Terms.Call is null ? null : new
            {
                isCallable = definition.Terms.Call.Value.IsCallable,
                firstCallDate = definition.Terms.Call.Value.FirstCallDate,
                callPrice = definition.Terms.Call.Value.CallPrice
            },
            auction = definition.Terms.Auction is null ? null : new
            {
                auctionDate = definition.Terms.Auction.Value.AuctionDate,
                auctionType = definition.Terms.Auction.Value.AuctionType is null ? null : definition.Terms.Auction.Value.AuctionType.Value
            },
            sweep = definition.Terms.Sweep is null ? null : new
            {
                programName = definition.Terms.Sweep.Value.ProgramName is null ? null : definition.Terms.Sweep.Value.ProgramName.Value,
                sweepVehicleType = definition.Terms.Sweep.Value.SweepVehicleType is null ? null : SweepVehicleModule.Label(definition.Terms.Sweep.Value.SweepVehicleType.Value),
                sweepFrequency = definition.Terms.Sweep.Value.SweepFrequency is null ? null : PaymentFrequencyModule.Label(definition.Terms.Sweep.Value.SweepFrequency.Value),
                targetAccountType = definition.Terms.Sweep.Value.TargetAccountType is null ? null : definition.Terms.Sweep.Value.TargetAccountType.Value
            },
            financing = definition.Terms.Financing is null ? null : new
            {
                counterparty = definition.Terms.Financing.Value.Counterparty is null ? null : definition.Terms.Financing.Value.Counterparty.Value,
                collateralType = definition.Terms.Financing.Value.CollateralType is null ? null : definition.Terms.Financing.Value.CollateralType.Value,
                haircut = definition.Terms.Financing.Value.Haircut,
                openDate = definition.Terms.Financing.Value.OpenDate,
                closeDate = definition.Terms.Financing.Value.CloseDate
            },
            issuer = definition.Terms.Issuer is null ? null : new
            {
                issuerName = definition.Terms.Issuer.Value.IssuerName is null ? null : definition.Terms.Issuer.Value.IssuerName.Value,
                institutionName = definition.Terms.Issuer.Value.InstitutionName is null ? null : definition.Terms.Issuer.Value.InstitutionName.Value,
                issuerProgram = definition.Terms.Issuer.Value.IssuerProgram is null ? null : definition.Terms.Issuer.Value.IssuerProgram.Value
            },
            equityBehavior = definition.Terms.EquityBehavior is null ? null : new
            {
                shareClass = definition.Terms.EquityBehavior.Value.ShareClass is null ? null : definition.Terms.EquityBehavior.Value.ShareClass.Value,
                votingRights = definition.Terms.EquityBehavior.Value.VotingRights is null ? null : definition.Terms.EquityBehavior.Value.VotingRights.Value,
                distributionType = definition.Terms.EquityBehavior.Value.DistributionType is null ? null : DistributionPolicyModule.Label(definition.Terms.EquityBehavior.Value.DistributionType.Value)
            },
            fund = definition.Terms.Fund is null ? null : new
            {
                fundFamily = definition.Terms.Fund.Value.FundFamily is null ? null : definition.Terms.Fund.Value.FundFamily.Value,
                weightedAverageMaturityDays = definition.Terms.Fund.Value.WeightedAverageMaturityDays,
                sweepEligible = definition.Terms.Fund.Value.SweepEligible,
                liquidityFeeEligible = definition.Terms.Fund.Value.LiquidityFeeEligible
            }
        });
}
