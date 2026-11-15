using System.Text.Json;
using Meridian.Contracts.FixedIncome;

namespace Meridian.Contracts.SecurityMaster;

/// <summary>A tested calculation profile selected by a compiled asset module.</summary>
public enum SecurityRecurringAccrualProfile
{
    ExistingFixedIncome,
    FixedBulletActual365
}

/// <summary>Reviewed class admission selects a calculation; it does not approve or post journals.</summary>
public sealed record SecurityRecurringAccrualPolicy
{
    internal SecurityRecurringAccrualPolicy(string id, SecurityRecurringAccrualProfile profile)
        => (Id, Profile) = (id, profile);

    public string Id { get; }
    public SecurityRecurringAccrualProfile Profile { get; }
}

/// <summary>Closed calculation policies shared by operation diagnostics and recurring draft production.</summary>
public static class SecurityRecurringAccrualPolicies
{
    public const string FixedBulletReceiptModelVersion = "bond-cash-flow-context-v6";

    public static SecurityRecurringAccrualPolicy ExistingFixedIncome { get; } =
        new("existing-fixed-income-v1", SecurityRecurringAccrualProfile.ExistingFixedIncome);

    public static SecurityRecurringAccrualPolicy FixedBulletActual365 { get; } =
        new("fixed-bullet-actual365-v1", SecurityRecurringAccrualProfile.FixedBulletActual365);

    public static IReadOnlyList<SecurityRecurringAccrualPolicy> All { get; } =
        [ExistingFixedIncome, FixedBulletActual365];

    /// <summary>Whether this record explicitly declares the module's expanded scalar profile contract.</summary>
    public static bool IsProfileDeclared(SecurityDetailDto security, SecurityRecurringAccrualPolicy? policy)
    {
        if (policy != FixedBulletActual365)
            return false;
        var assetClass = SecurityAssetClassCatalog.Canonicalize(security.AssetClass);
        if (!SecurityAssetTermsCodecBindings.ByAssetClass.TryGetValue(assetClass, out var binding)
            || binding.VersionedOptionalScalarKeys.Count == 0)
            return false;
        var declaresSince = binding.VersionedOptionalScalarKeys.Select(key =>
            SecurityAssetTermsSchema.Field(assetClass, key)?.SinceTermsVersion
                ?? throw new InvalidOperationException($"Versioned scalar '{assetClass}.{key}' has no declared schema field.")).Max();
        return SecurityTermsEnvelope.ResolveAssetClassTermsVersion(security.AssetSpecificTerms) >= declaresSince;
    }

    public static SecurityCashFlowBlockerDto? EvaluateTerms(SecurityDetailDto security, SecurityRecurringAccrualPolicy? policy)
    {
        try
        {
            return EvaluateTermsCore(security, policy);
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException or JsonException or OverflowException)
        {
            return Blocked("AccrualTermsUnreadable", "The selected terms cannot establish supported recurring accrual economics: " + exception.Message);
        }
    }

    private static SecurityCashFlowBlockerDto? EvaluateTermsCore(SecurityDetailDto security, SecurityRecurringAccrualPolicy? policy)
    {
        if (policy is null || !All.Contains(policy))
            return Blocked("AccrualPolicyUnsupported", "The asset module has no registered recurring fixed-income accrual policy.");
        // Keep every existing Bond/StructuredCredit calculation and refusal with its current owner.
        if (policy.Profile == SecurityRecurringAccrualProfile.ExistingFixedIncome)
            return null;
        if (!SecurityBoundedProfileCommonTerms.IsIdentityContext(security.CommonTerms))
            return Blocked("AccrualEconomicsUnsupported", "Supplemental common-term economics require a separately supported accrual profile.");
        var descriptor = SecurityAssetClassCatalog.GetOrDefault(security.AssetClass);
        if (descriptor.LotUnit != SecurityLotUnitKind.FaceValue)
            return Blocked("AccrualLotUnitUnsupported", "The fixed-bullet accrual profile requires actual face-value lots.");
        var document = security.AssetSpecificTerms;
        if (document.ValueKind != JsonValueKind.Object || SecurityTermsEnvelope.IsProfileGovernedPayload(security.AssetClass, document))
            return Blocked("AccrualTermsUnsupported", "The fixed-bullet accrual profile requires canonical flat terms.");
        var version = SecurityTermsEnvelope.ResolveAssetClassTermsVersion(document);
        foreach (var key in new[] { "issueDate", "principal", "paymentFrequency" })
        {
            if (SecurityAssetTermsSchema.Field(security.AssetClass, key) is not { } field
                || version < field.SinceTermsVersion || !document.TryGetProperty(key, out var value)
                || !SecurityAssetTermsSchema.IsPopulatedTermValue(value))
                return Blocked("AccrualAnchorMissing", $"Fixed-bullet accrual requires an explicitly reviewed '{key}' in the record's declared terms version.");
        }

        // No unmodelled coupon, fee, discount, OID or supplemental economic payload may disappear
        // behind the scalar profile. The supported schema remains the authority for field names.
        var permitted = SecurityAssetTermsSchema.Fields(security.AssetClass).Select(static field => field.Key)
            .Concat(new[] { SecurityTermsEnvelope.SchemaVersionKey, SecurityTermsEnvelope.FormatKey, SecurityTermsEnvelope.AssetClassTermsVersionKey })
            .ToHashSet(StringComparer.Ordinal);
        if (document.EnumerateObject().Any(property => !permitted.Contains(property.Name)))
            return Blocked("AccrualEconomicsUnsupported", "Supplemental or unmodelled economics require a separately supported accrual profile.");
        foreach (var key in new[] { "callableDate", "callSchedule", "putSchedule", "scheduleConventions", "cashFlowContract" })
        {
            if (document.TryGetProperty(key, out var value) && SecurityAssetTermsSchema.IsPopulatedTermValue(value))
                return Blocked("AccrualEconomicsUnsupported", $"Fixed-bullet accrual does not admit '{key}'; declared schedules and optional redemption retain their existing gates.");
        }

        var terms = StructuredCashFlowTermsResolver.Resolve(security);
        if (terms.HasLegs || terms.PrincipalSchedule is not null || terms.HasFactorSchedule
            || terms.CurrentFactor is not null || terms.HasStepCouponSchedule || terms.InflationIndex is not null
            || terms.CouponKind is not (SecurityCouponKind.Unknown or SecurityCouponKind.Fixed))
            return Blocked("AccrualEconomicsUnsupported", "The fixed-bullet profile does not admit floating, structured, amortizing or factored economics.");
        if (terms.IssueDate is not { } issue || issue == DateOnly.MinValue
            || terms.MaturityDate is not { } maturity || maturity <= issue || terms.PrincipalFace is not > 0m)
            return Blocked("AccrualAnchorInvalid", "Fixed-bullet accrual requires positive principal and explicit issue and maturity dates in chronological order.");
        if (DayCountConventions.Parse(terms.DayCountConvention) != DayCountConvention.Actual365)
            return Blocked("AccrualDayCountUnsupported", "This fixed-bullet profile supports ACT/365 only.");
        if (!SecurityTermConventions.TryParsePaymentFrequency(terms.PaymentFrequency, out var cadence)
            || cadence != SecurityPaymentFrequency.AtMaturity)
            return Blocked("AccrualFrequencyUnsupported", "This fixed-bullet profile requires interest payable AtMaturity.");
        if (SecurityRecordRateUnit.Read(security.AssetClass, document) is not { } unit || unit == RateUnit.Unrecognized)
            return Blocked(RateUnitNormalizer.UndeclaredCode, "Fixed-bullet accrual requires an explicit scalar rateUnit declaration.");
        if (terms.CouponRate is not { } coupon
            || !RateUnitNormalizer.Normalize(security.AssetClass, "couponRate", coupon, unit).TryGetFraction(out var rate)
            || rate <= 0m)
            return Blocked("AccrualCouponUnsupported", "Fixed-bullet accrual requires a positive supported fixed coupon under its declared unit.");
        return null;
    }

    public static SecurityCashFlowBlockerDto? EvaluateHoldings(SecurityRecurringAccrualPolicy policy,
        IReadOnlyList<FaceValueAccrualHolding> holdings)
    {
        if (policy.Profile != SecurityRecurringAccrualProfile.FixedBulletActual365)
            return null;
        if (holdings.Any(static holding => holding.AmortizationMethod != BondAmortizationMethod.NoAmortization
            || holding.Lot.PricePercentOfPar != holding.Lot.ParBasis || holding.Lot.BookedFactor != 1m
            || holding.AccrualFactor != 1m))
            return Blocked("AccrualLotEconomicsUnsupported", "This fixed-bullet profile requires par-priced, unfactored face lots with explicit NoAmortization; premium, discount, fees and OID are outside the profile.");
        // The existing retained bridge verifies closing factors against each segment, including
        // zero after the contractual maturity repayment; that closing zero is not factored interest.
        return null;
    }

    private static SecurityCashFlowBlockerDto Blocked(string code, string reason) => new(code, reason);
}
