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
/// <remarks>
/// <c>bond-analytics-v2</c> is the current model. <c>bond-analytics-v1</c> remains dispatchable for
/// retained settlement-receipt verification and replays through the same shared kernels in
/// <see cref="BondEconomicsCompatibility.AnalyticsV1"/> mode; its output is pinned by the
/// bond-analytics-versions golden fixture.
/// </remarks>
public sealed class BondAnalyticsService : IBondAnalyticsService
{
    public const string ModelVersion = "bond-analytics-v2";
    public const string LegacyModelVersion = "bond-analytics-v1";
    /// <summary>Floating-rate analytics over observed fixings and a retained forward curve.</summary>
    public const string FloatingModelVersion = "bond-analytics-v3";
    private readonly ISecurityMasterQueryService _query;
    private readonly IBusinessDayCalendarSource? _calendars;
    private readonly IBondCalculationPolicy? _policy;
    private readonly ISecurityForwardCurveProvider? _forwardCurves;

    public BondAnalyticsService(ISecurityMasterQueryService query, IBusinessDayCalendarSource? calendars = null,
        IBondCalculationPolicy? policy = null, ISecurityForwardCurveProvider? forwardCurves = null)
    {
        _query = query;
        _calendars = calendars;
        _policy = policy;
        _forwardCurves = forwardCurves;
    }

    public async Task<BondAnalyticsResult?> CalculateAsync(BondAnalyticsRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Context);
        var model = request.CalculationVersion switch
        {
            null or ModelVersion => CurrentModel,
            LegacyModelVersion => LegacyModel,
            FloatingModelVersion => FloatingModel,
            _ => null
        };
        if (model is null)
            return Blocked(request, request.Context, string.Empty, "UnsupportedCalculationVersion",
                "The requested analytics version is unsupported.", CurrentModel);
        try
        { return await CalculateCoreAsync(request, model, ct).ConfigureAwait(false); }
        catch (SecurityScheduleValidationException exception)
        {
            return Blocked(request, request.Context, string.Empty, "InvalidEconomicSchedule", exception.Message, model);
        }
        catch (ArithmeticException)
        {
            return Blocked(request, request.Context, string.Empty, "CalculationBoundsExceeded",
                "Bond inputs exceed the supported decimal calculation bounds.", model);
        }
    }

    /// <summary>Everything that differs between retained analytics versions.</summary>
    private sealed record AnalyticsModel(
        string Version,
        BondEconomicsCompatibility Compatibility,
        bool RecordsContractualEconomicsInput,
        string CompanionRefusal,
        bool Floating = false);
    private static readonly AnalyticsModel FloatingModel = new(FloatingModelVersion, BondEconomicsCompatibility.Current, true,
        "This bond declares a contractual cash-flow companion; floating-rate yield analytics cannot represent its economics. Use the retained contractual cash-flow preview.", Floating: true);

    private static readonly AnalyticsModel CurrentModel = new(ModelVersion, BondEconomicsCompatibility.Current, true,
        "This bond declares a contractual cash-flow companion; vanilla yield analytics cannot represent its economics. Use the retained contractual cash-flow preview.");

    private static readonly AnalyticsModel LegacyModel = new(LegacyModelVersion, BondEconomicsCompatibility.AnalyticsV1, false,
        "This record declares a cashFlowContract; use its contractual cash-flow preview rather than fixed/zero yield analytics.");

    private async Task<BondAnalyticsResult?> CalculateCoreAsync(BondAnalyticsRequest request, AnalyticsModel model, CancellationToken ct)
    {
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
                "HistoricalTermsUnavailable", "No retained security terms are eligible at the requested knowledge cutoff.", model);
        }
        ct.ThrowIfCancellationRequested();
        var inputs = new List<BondCalculationInput>
        {
            new("SecurityTerms", security.SecurityId.ToString("D"), security.Version,
                Sha256Digest.ComputeUtf8(security.CommonTerms.GetRawText() + "\n" + security.AssetSpecificTerms.GetRawText())),
            new("CalculationModel", model.Version), new("PricingModel", BondPricingMath.ModelVersion)
        };
        if (model.RecordsContractualEconomicsInput)
            inputs.Add(new("ContractualEconomics", BondContractualEconomics.ModelVersion));
        BondAnalyticsResult Refuse(string code, string reason)
            => Blocked(request, context, security.Currency, code, reason, model, inputs);
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
        if (!SecurityAssetClassCatalog.GetOrDefault(security.AssetClass).CarriesBondCouponStructure)
            return Refuse("UnsupportedAssetClass", "This analytics model supports canonical Bond securities only.");
        if (Meridian.Contracts.CashFlows.SecurityContractualCashFlowTerms.IsDeclared(security.AssetClass, security.AssetSpecificTerms))
            return Refuse("UnsupportedCashFlowContract", model.CompanionRefusal);
        if ((request.CleanPrice is null) == (request.Yield is null))
            return Refuse("PriceOrYieldRequired", "Supply exactly one clean price per 100 face or annual yield fraction.");
        if (request.CleanPrice is <= 0m)
            return Refuse("InvalidCleanPrice", "Clean price must be positive.");
        // v1/v2 consume no curves. v3 admits one pin per index, checked once its floating leg names the index.
        if (!model.Floating && context.Curves is { Count: > 0 })
            return Refuse("UnsupportedCurveInput", "Yield-based analytics does not consume curves; use curve valuation for pinned market inputs.");

        var terms = StructuredCashFlowTermsResolver.Resolve(security);
        var couponKind = BondContractualEconomics.Text(security.AssetSpecificTerms, "couponType");
        var zeroCoupon = couponKind == "ZeroCoupon";
        if (model.Floating && couponKind != "Floating")
            return Refuse("UnsupportedCouponStructure", "Floating-rate analytics supports explicitly typed Floating bonds; use bond-analytics-v2 for fixed and zero-coupon bonds.");
        if (!model.Floating && couponKind is not ("Fixed" or "ZeroCoupon"))
            return Refuse("UnsupportedCouponStructure", "Yield analytics supports explicitly typed Fixed and ZeroCoupon bonds; floating bonds use bond-analytics-v3, and step or indexed economics require another model.");
        if (terms.HasPrincipalSchedule || terms.HasFactorSchedule || terms.CurrentFactor is not null
            || (!model.Floating && terms.HasLegs) || terms.HasStepCouponSchedule || terms.InflationIndex is not null)
            return Refuse("UnsupportedPrincipalStructure", "Yield analytics requires unfactored bullet principal and a fixed, zero or single floating coupon.");
        if (model.Floating)
            return await CalculateFloatingAsync(request, context, security, terms, settlement, inputs, model, Refuse, ct).ConfigureAwait(false);
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
        if (!BondContractualEconomics.TryBuild(security.SecurityId, terms, declared, schedule, settlement,
                annualRate.Value, zeroCoupon, convention.Value, model.Compatibility, out var flows, out var accrued, out var refusal))
            return Refuse("CashFlowRefused", refusal!);
        var priceFlows = flows.Select(static flow => new BondPricingCashFlow(flow.Interest + flow.Principal, flow.TimeInYears)).ToArray();
        var yield = request.Yield.GetValueOrDefault();
        var targetDirty = request.CleanPrice + accrued;
        if (request.CleanPrice is not null && !BondPricingMath.TrySolveYield(priceFlows, targetDirty!.Value,
                convention.Value, paymentsPerYear, out yield, out refusal))
            return Refuse("YieldNotConverged", refusal!);
        if (!BondPricingMath.TryPrice(priceFlows, yield, convention.Value, paymentsPerYear, out var measures, out refusal))
            return Refuse("PricingRefused", refusal!);
        if (!BondExerciseEconomics.TryCalculate(security, terms, declared, schedule, settlement,
                annualRate.Value, convention.Value, paymentsPerYear, measures!.DirtyPrice,
                flows, _calendars, model.Compatibility, out var exerciseYields, out refusal))
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
        return new(security.SecurityId, security.Currency, model.Version, context, convention,
            clean, measures.DirtyPrice, accrued, yield, annualRate * 100m / clean,
            measures.MacaulayDuration, measures.ModifiedDuration, measures.Convexity, measures.Dv01,
            worst, flows, exerciseYields, inputs, [], Capabilities(true), diagnostics);
    }

    /// <summary>
    /// bond-analytics-v3: each coupon is the period's observed fixing (already-reset periods) or the retained
    /// forward curve's projected fixing (future periods) plus the spread, capped and floored; the yield
    /// discounts those projected flows and is not a discount margin. Duration, convexity and DV01 are rate
    /// measures of the projected flows; SpreadDv01 reprices the flows with the spread raised one basis
    /// point at the same yield. A missing historical fixing or forward curve refuses the calculation.
    /// </summary>
    private async Task<BondAnalyticsResult> CalculateFloatingAsync(BondAnalyticsRequest request, BondCalculationContext context,
        SecurityDetailDto security, StructuredCashFlowTerms terms, DateOnly settlement, List<BondCalculationInput> inputs,
        AnalyticsModel model, Func<string, string, BondAnalyticsResult> refuse, CancellationToken ct)
    {
        var legs = (terms.Legs ?? []).Where(leg => leg.RateKind == CashFlowLegRateKind.Floating).ToArray();
        if (legs.Length != 1 || string.IsNullOrWhiteSpace(legs[0].IndexName))
            return refuse("UnsupportedCouponStructure", "Floating-rate analytics requires exactly one floating coupon leg with a named index.");
        var leg = legs[0];
        var indexName = leg.IndexName!.Trim();
        var forwardRole = "Forward:" + indexName;
        if (context.Curves?.Any(pin => !string.Equals(pin.Role?.Trim(), forwardRole, StringComparison.OrdinalIgnoreCase)) == true)
            return refuse("UnsupportedCurveInput", $"Floating-rate analytics consumes only a '{forwardRole}' pin for the bond's own index; discount or other curve roles belong to curve valuation.");
        if (context.Curves?.Count(pin => string.Equals(pin.Role?.Trim(), forwardRole, StringComparison.OrdinalIgnoreCase)) > 1)
            return refuse("UnsupportedCurveInput", $"More than one '{forwardRole}' pin was supplied; one retained curve version selects the projection.");
        if (leg.RateUnit is not (SecurityCouponRateUnit.Percent or SecurityCouponRateUnit.Fraction))
            return refuse("CouponUnitRequired", "A floating bond requires an explicit Percent or Fraction coupon rate unit.");
        if (leg.CapRate is { } capRate && leg.FloorRate is { } floorRate && floorRate > capRate)
            return refuse("InvalidRateBounds", "The declared floor rate exceeds the cap rate; contradictory coupon bounds cannot be priced.");
        if (leg.ResetSchedule?.GroupBy(static row => row.ResetDate).Any(static group => group.Count() > 1) == true)
            return refuse("DuplicateFixing", "Floating reset dates must be unique; conflicting retained fixings for one reset date cannot be priced.");
        if (terms.IssueDate is not { } issue || terms.MaturityDate is not { } maturity || settlement < issue || settlement >= maturity)
            return refuse("SettlementOutsideLife", "Settlement must be on or after the retained issue date and before maturity.");
        var declared = ScheduleSpecFactory.Resolve(security, terms, _calendars);
        if (declared.Status != DeclaredScheduleStatus.Declared)
            return refuse("DeclaredScheduleRequired", declared.Refusal?.Message ?? "Analytics requires reviewed explicit schedule conventions; legacy defaults are not inferred.");
        if (declared.Conventions.CouponCashBasis is not ("Level" or "DayCount"))
            return refuse("CouponCashBasisRequired", "Declare Level or DayCount coupon cash basis before analytics.");
        var convention = request.YieldConvention ?? BondYieldConvention.NominalPeriodic;
        if (!Enum.IsDefined(convention))
            return refuse("YieldConventionRequired", "An annual-effective, simple or periodic yield convention is required.");
        var frequency = declared.Spec!.Tenor.PeriodsPerYear;
        if (convention == BondYieldConvention.NominalPeriodic && frequency is null)
            return refuse("CompoundingFrequencyRequired", "Nominal periodic yield requires a regular coupon frequency.");
        var paymentsPerYear = frequency ?? 1;
        var dayCount = DayCountConventions.Parse(terms.DayCountConvention);
        if (dayCount == DayCountConvention.Unknown)
            return refuse("CashFlowRefused", "A recognized coupon day-count convention is required.");
        var schedule = ScheduleGenerator.Generate(declared.Spec);
        if (!schedule.Succeeded)
            return refuse("ScheduleRefused", schedule.Failure.Message);
        inputs.Add(new("ScheduleKernel", schedule.KernelVersion));
        inputs.Add(new("ScheduleMapping", ScheduleSpecFactory.DeclaredMappingVersion));
        inputs.Add(new("FloatingCoupon", BondFloatingRateEvidence.ModelVersion));
        foreach (var calendar in schedule.CalendarsUsed)
            inputs.Add(new("Calendar", calendar.CalendarId + ":" + calendar.Purpose, ContentSha256: calendar.ContentHash));
        // Reset status follows the economic cutoff: a reset dated after AsOf is a future, projected fixing even
        // when settlement falls after it. Settlement still governs entitlement, accrued interest and discount times.
        var economicDate = DateOnly.FromDateTime(context.AsOf.UtcDateTime);
        var needsCurve = schedule.Periods.Any(period => period.PaymentDate > settlement && period.AccrualStart > economicDate);
        SecurityForwardCurve? curve = null;
        if (needsCurve)
        {
            curve = _forwardCurves is null ? null : await _forwardCurves.GetForwardCurveAsync(indexName, context, ct).ConfigureAwait(false);
            if (curve is not { Points.Count: > 0 })
                return refuse("ForwardCurveRequired", $"A retained forward curve for '{indexName}' eligible at the requested cutoffs is required to project future resets.");
            // A stored curve identifies its exact retained version; only an unretained curve falls back to a content hash.
            inputs.Add(curve.RetainedInput ?? new BondCalculationInput("ForwardCurveContent", curve.IndexName,
                ContentSha256: Sha256Digest.ComputeUtf8(System.Text.Json.JsonSerializer.Serialize(curve, SecurityForwardCurveJsonContext.Default.SecurityForwardCurve)),
                AsOf: new DateTimeOffset(curve.AsOfDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero)));
        }
        var spread = leg.SpreadBps.GetValueOrDefault() / 10_000m;
        if (!TryBuildFloating(security.SecurityId, leg, declared, schedule, settlement, economicDate, dayCount, convention, curve, spread,
                out var flows, out var accrued, out var currentRate, out var refusal, out var code))
            return refuse(code!, refusal!);
        var priceFlows = flows.Select(static flow => new BondPricingCashFlow(flow.Interest + flow.Principal, flow.TimeInYears)).ToArray();
        var yield = request.Yield.GetValueOrDefault();
        var targetDirty = request.CleanPrice + accrued;
        if (request.CleanPrice is not null && !BondPricingMath.TrySolveYield(priceFlows, targetDirty!.Value, convention, paymentsPerYear, out yield, out refusal))
            return refuse("YieldNotConverged", refusal!);
        if (!BondPricingMath.TryPrice(priceFlows, yield, convention, paymentsPerYear, out var measures, out refusal))
            return refuse("PricingRefused", refusal!);
        var clean = measures!.DirtyPrice - accrued;
        if (clean <= 0m)
            return refuse("InvalidCleanPrice", "Calculated clean price is not positive.");
        decimal? spreadDv01 = null;
        if (TryBuildFloating(security.SecurityId, leg, declared, schedule, settlement, economicDate, dayCount, convention, curve, spread + 0.0001m,
                out var bumpedFlows, out _, out _, out _, out _)
            && BondPricingMath.TryPrice(bumpedFlows.Select(static flow => new BondPricingCashFlow(flow.Interest + flow.Principal, flow.TimeInYears)).ToArray(),
                yield, convention, paymentsPerYear, out var bumped, out _))
            spreadDv01 = decimal.Round(bumped!.DirtyPrice - measures.DirtyPrice, 12, MidpointRounding.AwayFromZero);
        var diagnostics = schedule.Diagnostics.Select(static item => item.Message).ToList();
        diagnostics.Add("Analytical scenarios grant no exercise, payment, posting or trading authority.");
        diagnostics.Add("Future coupons are projected from the retained forward curve; the yield discounts projected flows and is not a discount margin. Exercise scenarios are not calculated for floating bonds.");
        if (context.KnownAt is not null)
            diagnostics.Add("Recorded timestamps establish retained-row eligibility, not historical transaction visibility.");
        return new BondAnalyticsResult(security.SecurityId, security.Currency, model.Version, context, convention,
            clean, measures.DirtyPrice, accrued, yield, currentRate * 100m / clean,
            measures.MacaulayDuration, measures.ModifiedDuration, measures.Convexity, measures.Dv01,
            yield, flows, [], inputs, [], Capabilities(true), diagnostics)
        { SpreadDv01 = spreadDv01 };
    }

    private static bool TryBuildFloating(Guid securityId, StructuredCashFlowLeg leg, DeclaredScheduleResolution declared,
        ScheduleGenerationResult schedule, DateOnly settlement, DateOnly economicDate, DayCountConvention dayCount, BondYieldConvention convention,
        SecurityForwardCurve? curve, decimal spread, out IReadOnlyList<BondAnalyticsCashFlow> flows, out decimal accrued,
        out decimal currentRate, out string? refusal, out string? code)
    {
        flows = [];
        accrued = 0m;
        currentRate = 0m;
        refusal = null;
        code = null;
        var result = new List<BondAnalyticsCashFlow>();
        foreach (var period in schedule.Periods)
        {
            if (period.PaymentDate <= settlement)
                continue;
            if (period.AccrualEnd <= settlement)
            { code = "CashFlowRefused"; refusal = "Settlement within a delayed-payment coupon window needs separate entitlement rules."; return false; }
            var alreadyReset = period.AccrualStart <= economicDate;
            var exactReset = leg.ResetSchedule?.SingleOrDefault(row => row.ResetDate == period.AccrualStart)?.FixingRate;
            var retainedScalar = !leg.HasDeclaredResetSchedule && leg.CurrentIndexResetDate == period.AccrualStart
                && !string.IsNullOrWhiteSpace(leg.CurrentIndexRateSource) ? leg.CurrentIndexRate : null;
            var index = alreadyReset ? exactReset ?? retainedScalar : curve?.RateAt(period.AccrualStart);
            if (index is null)
            {
                code = alreadyReset ? "MissingFixing" : "ForwardCurveRequired";
                refusal = alreadyReset
                    ? $"No observed fixing is retained for the floating period that reset on {period.AccrualStart:yyyy-MM-dd}; historical fixings are never forecast."
                    : "A retained forward curve is required to project future floating resets.";
                return false;
            }
            // Observed fixings carry the leg's declared unit; curve points are annual fractions.
            var indexFraction = alreadyReset && leg.RateUnit == SecurityCouponRateUnit.Percent ? index.Value / 100m : index.Value;
            var rate = indexFraction + spread;
            if (leg.CapRate is { } cap)
                rate = Math.Min(rate, leg.RateUnit == SecurityCouponRateUnit.Percent ? cap / 100m : cap);
            if (leg.FloorRate is { } floor)
                rate = Math.Max(rate, leg.RateUnit == SecurityCouponRateUnit.Percent ? floor / 100m : floor);
            if (rate < 0m)
            { code = "CashFlowRefused"; refusal = "Negative floating coupon cash requires a supported signed-interest model."; return false; }
            if (!BondContractualEconomics.TryCoupon(period, schedule, dayCount, rate, declared.Conventions.CouponCashBasis, out var interest, out refusal))
            { code = "CashFlowRefused"; return false; }
            if (period.AccrualStart <= settlement && settlement < period.AccrualEnd)
            {
                currentRate = rate;
                if (!BondContractualEconomics.TryAccruedCoupon(period, schedule, dayCount, rate, declared.Conventions.CouponCashBasis, settlement, out accrued, out refusal))
                { code = "CashFlowRefused"; return false; }
            }
            var discountDate = convention == BondYieldConvention.NominalPeriodic && period.PaymentDate > period.AccrualEnd ? period.AccrualEnd : period.PaymentDate;
            if (!BondContractualEconomics.TryTime(schedule, settlement, discountDate, dayCount, false, convention, out var time, out refusal))
            { code = "CashFlowRefused"; return false; }
            if (discountDate < period.PaymentDate)
                time += (period.PaymentDate.DayNumber - discountDate.DayNumber) / DayCountConventions.DaysPerYear365;
            if (interest > 0m || period.IsFinal)
            {
                var curveSource = curve?.Source;
                var fixingSource = alreadyReset
                    ? exactReset.HasValue ? "Retained reset schedule" : leg.CurrentIndexRateSource!
                    : !string.IsNullOrWhiteSpace(curveSource) ? curveSource
                    : curve?.RetainedInput is not null ? "Retained forward curve" : "Content-bound forward curve";
                result.Add(new(SchedulePeriodId.Derive(securityId, "bond-analytics", period), period.PaymentDate,
                    period.AccrualStart, period.AccrualEnd, interest, period.IsFinal ? 100m : 0m, time)
                {
                    FloatingCoupon = new(period.AccrualStart, alreadyReset, leg.IndexName!.Trim(), indexFraction,
                        spread * 10_000m, rate, fixingSource)
                });
            }
        }
        if (result.Count == 0)
        { code = "CashFlowRefused"; refusal = "No payments remain after settlement."; return false; }
        accrued = decimal.Round(accrued, 12, MidpointRounding.AwayFromZero);
        flows = result;
        return true;
    }

    private static BondAnalyticsResult Blocked(BondAnalyticsRequest request, BondCalculationContext context,
        string currency, string code, string reason, AnalyticsModel model, IReadOnlyList<BondCalculationInput>? inputs = null)
        => new(request.SecurityId, currency, model.Version, context, request.YieldConvention,
            null, null, null, null, null, null, null, null, null, null,
            [], [], inputs ?? [], [new(code, reason)], Capabilities(false, reason), []);

    private static IReadOnlyList<BondOperationCapability> Capabilities(bool analytical, string? reason = null)
        => [new("Reference", true), new("Analytics", analytical, reason), new("Projection", analytical, reason),
            new("Amortization", false, "Requires retained lot acquisition terms and the governed amortization workflow."),
            new("Posting", false, "Analytics is read-only; posting requires reviewed accounting candidates.")];
}
