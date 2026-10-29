using Meridian.Contracts.Calendars;
using Meridian.Contracts.FixedIncome;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.SecurityMaster.Schedules;

namespace Meridian.Instruments.FixedIncome;

/// <summary>
/// Fixed/zero-coupon analytics on declared contractual schedules. Every input is resolved once;
/// no journal, source assignment, price observation or trade is mutated by a calculation.
/// </summary>
internal sealed class LegacyBondAnalyticsService : IBondAnalyticsService
{
    public const string ModelVersion = "bond-analytics-v1";
    private readonly ISecurityMasterQueryService _query;
    private readonly IBusinessDayCalendarSource? _calendars;
    private readonly IBondCalculationPolicy? _policy;

    public LegacyBondAnalyticsService(ISecurityMasterQueryService query, IBusinessDayCalendarSource? calendars = null,
        IBondCalculationPolicy? policy = null)
    {
        _query = query;
        _calendars = calendars;
        _policy = policy;
    }

    public async Task<BondAnalyticsResult?> CalculateAsync(BondAnalyticsRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Context);
        try
        { return await CalculateCoreAsync(request, ct).ConfigureAwait(false); }
        catch (ArithmeticException)
        {
            return Blocked(request, request.Context, string.Empty, "CalculationBoundsExceeded",
                "Bond inputs exceed the supported decimal calculation bounds.");
        }
    }

    private async Task<BondAnalyticsResult?> CalculateCoreAsync(BondAnalyticsRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Context);
        var context = request.Context;
        var settlement = context.SettlementDate ?? DateOnly.FromDateTime(context.AsOf.UtcDateTime);
        context = context with { SettlementDate = settlement };
        var security = context.KnownAt is { } knownAt
            ? await _query.GetRecordedByIdAsOfAsync(request.SecurityId, knownAt, ct).ConfigureAwait(false)
            : await _query.GetByIdAsync(request.SecurityId, ct).ConfigureAwait(false);
        if (security is null)
        {
            if (context.KnownAt is null)
                return null;
            var current = await _query.GetByIdAsync(request.SecurityId, ct).ConfigureAwait(false);
            return current is null ? null : Blocked(request, context, current.Currency,
                "HistoricalTermsUnavailable", "No retained security terms are eligible at the requested knowledge cutoff.");
        }
        ct.ThrowIfCancellationRequested();
        var inputs = new List<BondCalculationInput>
        {
            new("SecurityTerms", security.SecurityId.ToString("D"), security.Version,
                Sha256Digest.ComputeUtf8(security.CommonTerms.GetRawText() + "\n" + security.AssetSpecificTerms.GetRawText())),
            new("CalculationModel", ModelVersion), new("PricingModel", BondPricingMath.ModelVersion)
        };
        BondAnalyticsResult Refuse(string code, string reason)
            => Blocked(request, context, security.Currency, code, reason, inputs);
        if (context.AsOf == default || context.AsOf.Offset != TimeSpan.Zero
            || context.KnownAt is { Offset: var offset } && offset != TimeSpan.Zero)
            return Refuse("InvalidContext", "Economic and knowledge cutoffs must be explicit UTC timestamps.");
        if (security.EffectiveFrom > context.AsOf || security.EffectiveTo is { } effectiveTo && context.AsOf >= effectiveTo)
            return Refuse("TermsNotEffective", "The selected retained terms are not effective at the economic cutoff; a historically eligible terms version is required.");
        if (context.SecurityVersion is { } version && version != security.Version)
            return Refuse("SecurityVersionMismatch", "The resolved terms differ from the requested retained security version.");
        context = context with { SecurityVersion = security.Version };
        if (_policy is not null && await _policy.GetBlockerAsync(security, ct).ConfigureAwait(false) is { } policyBlocker)
            return Refuse("CalculationPolicyBlocked", policyBlocker);
        if (!string.Equals(security.AssetClass, "Bond", StringComparison.OrdinalIgnoreCase))
            return Refuse("UnsupportedAssetClass", "This analytics model supports canonical Bond securities only.");
        if (Meridian.Contracts.CashFlows.SecurityContractualCashFlowTerms.IsDeclared(security.AssetClass, security.AssetSpecificTerms))
            return Refuse("UnsupportedCashFlowContract", "This record declares a cashFlowContract; use its contractual cash-flow preview rather than fixed/zero yield analytics.");
        if ((request.CleanPrice is null) == (request.Yield is null))
            return Refuse("PriceOrYieldRequired", "Supply exactly one clean price per 100 face or annual yield fraction.");
        if (request.CleanPrice is <= 0m)
            return Refuse("InvalidCleanPrice", "Clean price must be positive.");
        if (context.Curves is { Count: > 0 })
            return Refuse("UnsupportedCurveInput", "Yield-based analytics does not consume curves; use curve valuation for pinned market inputs.");

        var terms = StructuredCashFlowTermsResolver.Resolve(security);
        var couponKind = BondAnalyticsSchedule.Text(security.AssetSpecificTerms, "couponType");
        var zeroCoupon = couponKind == "ZeroCoupon";
        if (couponKind is not ("Fixed" or "ZeroCoupon"))
            return Refuse("UnsupportedCouponStructure", "Yield analytics supports explicitly typed Fixed and ZeroCoupon bonds; floating, step and indexed economics require another model.");
        if (terms.HasPrincipalSchedule || terms.HasFactorSchedule || terms.CurrentFactor is not null
            || terms.HasLegs || terms.HasStepCouponSchedule || terms.InflationIndex is not null)
            return Refuse("UnsupportedPrincipalStructure", "Yield analytics requires unfactored bullet principal and a fixed or zero coupon.");
        if (terms.IssueDate is not { } issue || terms.MaturityDate is not { } maturity
            || settlement < issue || settlement >= maturity)
            return Refuse("SettlementOutsideLife", "Settlement must be on or after the retained issue date and before maturity.");
        var declared = ScheduleSpecFactory.Resolve(security, terms, _calendars);
        if (declared.Status != DeclaredScheduleStatus.Declared)
            return Refuse("DeclaredScheduleRequired", declared.Refusal?.Message
                ?? "Analytics requires reviewed explicit schedule conventions; legacy defaults are not inferred.");
        var convention = request.YieldConvention ?? (zeroCoupon ? null : BondYieldConvention.NominalPeriodic);
        if (convention is null || !Enum.IsDefined(convention.Value))
            return Refuse("YieldConventionRequired", "Zero-coupon analytics requires an explicit annual-effective, simple or periodic yield convention.");
        var frequency = declared.Spec!.Tenor.PeriodsPerYear;
        if (convention == BondYieldConvention.NominalPeriodic && frequency is null)
            return Refuse("CompoundingFrequencyRequired", "Nominal periodic yield requires a regular coupon frequency.");
        var paymentsPerYear = frequency ?? 1;
        var annualRate = zeroCoupon ? 0m : declared.Conventions.CouponRateUnit switch
        {
            "Percent" => terms.CouponRate / 100m,
            "Fraction" => terms.CouponRate,
            _ => null
        };
        if (annualRate is null or < 0m)
            return Refuse("CouponUnitRequired", "A fixed bond requires a nonnegative retained coupon and an explicit Percent or Fraction coupon unit.");
        if (!zeroCoupon && declared.Conventions.CouponCashBasis is not ("Level" or "DayCount"))
            return Refuse("CouponCashBasisRequired", "Declare Level or DayCount coupon cash basis before analytics.");

        var schedule = ScheduleGenerator.Generate(declared.Spec);
        if (!schedule.Succeeded)
            return Refuse("ScheduleRefused", schedule.Failure.Message);
        inputs.Add(new("ScheduleKernel", schedule.KernelVersion));
        inputs.Add(new("ScheduleMapping", ScheduleSpecFactory.DeclaredMappingVersion));
        foreach (var calendar in schedule.CalendarsUsed)
            inputs.Add(new("Calendar", calendar.CalendarId + ":" + calendar.Purpose, ContentSha256: calendar.ContentHash));
        foreach (var calendarId in declared.Conventions.PaymentCalendars)
            if (_calendars is not null && _calendars.TryGet(calendarId, out var calendar))
                inputs.Add(new("DeclaredCalendar", calendarId, ContentSha256: calendar.ContentHash));
        if (!BondAnalyticsSchedule.TryBuild(security.SecurityId, terms, declared, schedule, settlement,
                annualRate.Value, zeroCoupon, convention.Value, out var flows, out var accrued, out var refusal))
            return Refuse("CashFlowRefused", refusal!);
        var priceFlows = flows.Select(static flow => new BondPricingCashFlow(flow.Interest + flow.Principal, flow.TimeInYears)).ToArray();
        var yield = request.Yield.GetValueOrDefault();
        var targetDirty = request.CleanPrice + accrued;
        if (request.CleanPrice is not null && !BondPricingMath.TrySolveYield(priceFlows, targetDirty!.Value,
                convention.Value, paymentsPerYear, out yield, out refusal))
            return Refuse("YieldNotConverged", refusal!);
        if (!BondPricingMath.TryPrice(priceFlows, yield, convention.Value, paymentsPerYear, out var measures, out refusal))
            return Refuse("PricingRefused", refusal!);
        if (!BondExerciseAnalytics.TryCalculate(security, terms, declared, schedule, settlement,
                annualRate.Value, convention.Value, paymentsPerYear, measures!.DirtyPrice,
                flows, _calendars, out var exerciseYields, out refusal))
            return Refuse("ExerciseScheduleRefused", refusal!);
        var clean = measures!.DirtyPrice - accrued;
        if (clean <= 0m)
            return Refuse("InvalidCleanPrice", "Calculated clean price is not positive.");
        // A holder put is an upside election, not an issuer-controlled worst case. YTW compares
        // maturity with eligible issuer calls; put yields stay separate for operator review.
        var worst = exerciseYields.Where(static item => item.Kind == "Call").Select(static item => item.Yield)
            .Append(yield).Min();
        var diagnostics = schedule.Diagnostics.Select(static item => item.Message).ToList();
        diagnostics.Add("Analytical scenarios grant no exercise, payment, posting or trading authority.");
        if (exerciseYields.Any(item => item.LatestNoticeDate < DateOnly.FromDateTime(context.AsOf.UtcDateTime)))
            diagnostics.Add("An exercise notice deadline precedes the analysis date; retained notice evidence is required to determine actual exercise status.");
        if (context.KnownAt is not null)
            diagnostics.Add("Recorded timestamps establish retained-row eligibility, not historical transaction visibility.");
        return new(security.SecurityId, security.Currency, ModelVersion, context, convention,
            clean, measures.DirtyPrice, accrued, yield, annualRate * 100m / clean,
            measures.MacaulayDuration, measures.ModifiedDuration, measures.Convexity, measures.Dv01,
            worst, flows, exerciseYields, inputs, [], Capabilities(true), diagnostics);
    }

    private static BondAnalyticsResult Blocked(BondAnalyticsRequest request, BondCalculationContext context,
        string currency, string code, string reason, IReadOnlyList<BondCalculationInput>? inputs = null)
        => new(request.SecurityId, currency, ModelVersion, context, request.YieldConvention,
            null, null, null, null, null, null, null, null, null, null,
            [], [], inputs ?? [], [new(code, reason)], Capabilities(false, reason), []);

    private static IReadOnlyList<BondOperationCapability> Capabilities(bool analytical, string? reason = null)
        => [new("Reference", true), new("Analytics", analytical, reason), new("Projection", analytical, reason),
            new("Amortization", false, "Requires retained lot acquisition terms and the governed amortization workflow."),
            new("Posting", false, "Analytics is read-only; posting requires reviewed accounting candidates.")];
}
