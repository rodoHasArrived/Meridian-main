using System.Globalization;
using System.Text.Json;
using Meridian.Contracts.Calendars;
using Meridian.Contracts.FixedIncome;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.SecurityMaster.Schedules;

namespace Meridian.Contracts.FixedIncome;

/// <summary>Discrete fixed-price exercise scenarios. Continuous, make-whole and partial calls refuse.</summary>
public sealed record BondRedemptionScenario(string Kind, DateOnly ExerciseDate, decimal RedemptionPrice,
    decimal AccruedPer100, DateOnly? LatestNoticeDate, DateOnly? EarliestNoticeDate, decimal TimeInYears);

public static class BondExerciseEconomics
{
    /// <summary>Contractual redemption cash, excluding any separately paid coupon.</summary>
    public static decimal RedemptionCash(decimal outstandingFace, decimal pricePercentOfPar, decimal accruedCash = 0m)
    {
        if (outstandingFace < 0m || pricePercentOfPar <= 0m || accruedCash < 0m)
            throw new ArgumentException("Redemption requires nonnegative outstanding face and accrued cash and a positive price.");
        return checked(outstandingFace * pricePercentOfPar / 100m + accruedCash);
    }

    public static bool TryCalculate(SecurityDetailDto security, StructuredCashFlowTerms terms,
        DeclaredScheduleResolution declared, ScheduleGenerationResult schedule, DateOnly settlement,
        decimal annualRate, BondYieldConvention convention, int frequency, decimal dirtyPrice,
        IReadOnlyList<BondAnalyticsCashFlow> flows, IBusinessDayCalendarSource? calendars,
        out IReadOnlyList<BondExerciseYield> yields, out string? refusal)
        => TryCalculate(security, terms, declared, schedule, settlement, annualRate, convention, frequency, dirtyPrice,
            flows, calendars, BondEconomicsCompatibility.Current, out yields, out refusal);

    public static bool TryCalculate(SecurityDetailDto security, StructuredCashFlowTerms terms,
        DeclaredScheduleResolution declared, ScheduleGenerationResult schedule, DateOnly settlement,
        decimal annualRate, BondYieldConvention convention, int frequency, decimal dirtyPrice,
        IReadOnlyList<BondAnalyticsCashFlow> flows, IBusinessDayCalendarSource? calendars,
        BondEconomicsCompatibility compatibility,
        out IReadOnlyList<BondExerciseYield> yields, out string? refusal)
    {
        yields = [];
        if (!TryResolveScenarios(security, terms, declared, schedule, settlement, annualRate, convention,
                frequency, flows, calendars, compatibility, out var scenarios, out refusal))
            return false;
        var result = new List<BondExerciseYield>();
        foreach (var item in scenarios)
        {
            var cash = flows.Where(flow => flow.PaymentDate <= item.ExerciseDate && flow.Interest > 0m)
                .Select(flow => new BondPricingCashFlow(flow.Interest, flow.TimeInYears)).ToList();
            cash.Add(new(RedemptionCash(100m, item.RedemptionPrice, item.AccruedPer100), item.TimeInYears));
            if (!BondPricingMath.TrySolveYield(cash, dirtyPrice, convention, frequency, out var yield, out refusal))
                return false;
            result.Add(new(item.Kind, item.ExerciseDate, item.RedemptionPrice, yield,
                item.LatestNoticeDate, item.EarliestNoticeDate));
        }
        yields = result;
        return true;
    }

    public static bool TryResolveScenarios(SecurityDetailDto security, StructuredCashFlowTerms terms,
        DeclaredScheduleResolution declared, ScheduleGenerationResult schedule, DateOnly settlement,
        decimal annualRate, BondYieldConvention convention, int frequency,
        IReadOnlyList<BondAnalyticsCashFlow> flows, IBusinessDayCalendarSource? calendars,
        out IReadOnlyList<BondRedemptionScenario> yields, out string? refusal)
        => TryResolveScenarios(security, terms, declared, schedule, settlement, annualRate, convention, frequency,
            flows, calendars, BondEconomicsCompatibility.Current, out yields, out refusal);

    /// <summary>
    /// <see cref="BondEconomicsCompatibility.AnalyticsV1"/> reproduces the retained v1 exercise
    /// arithmetic: no delayed-coupon entitlement refusal, exercise accrual prorated from the whole
    /// coupon, and exercise discount time measured on the coupon grid to the exercise date.
    /// </summary>
    public static bool TryResolveScenarios(SecurityDetailDto security, StructuredCashFlowTerms terms,
        DeclaredScheduleResolution declared, ScheduleGenerationResult schedule, DateOnly settlement,
        decimal annualRate, BondYieldConvention convention, int frequency,
        IReadOnlyList<BondAnalyticsCashFlow> flows, IBusinessDayCalendarSource? calendars,
        BondEconomicsCompatibility compatibility,
        out IReadOnlyList<BondRedemptionScenario> yields, out string? refusal)
    {
        yields = [];
        refusal = null;
        var legacy = compatibility == BondEconomicsCompatibility.AnalyticsV1;
        var payload = security.AssetSpecificTerms;
        var result = new List<BondRedemptionScenario>();
        var hasCalls = Array(payload, SecurityVersionedTerms.CallScheduleKey, out var calls);
        var hasPuts = Array(payload, SecurityVersionedTerms.PutScheduleKey, out var puts);
        if (payload.TryGetProperty("isCallable", out var callable) && callable.ValueKind == JsonValueKind.True && !hasCalls
            || Date(payload, "mandatoryPutDate") is not null || Date(payload, "preRefundDate") is not null)
        {
            refusal = "Callable, mandatory-put and pre-refunded bonds need a supported declared redemption schedule; a holder put cannot substitute for an issuer call or mandatory redemption.";
            return false;
        }
        if (!hasCalls && !hasPuts)
        {
            return true;
        }
        if (!payload.TryGetProperty("assetClassTermsVersion", out var version) || !version.TryGetInt32(out var termsVersion)
            || termsVersion < 3)
        {
            refusal = "Declared exercise schedules require bond terms version 3 or later.";
            return false;
        }
        try
        {
            // A stored row read back is its own retained declaration: analytics apply the rules it was
            // stored under, not the write-time gap rules (ADR-025).
            ScheduleTermsWriteRules.ValidateDeclarations(security.AssetClass, payload, termsVersion, retained: payload);
            foreach (var (kind, rows) in new[] { ("Call", calls), ("Put", puts) })
            {
                var ordered = rows.Where(static row => row.ValueKind == JsonValueKind.Object)
                    .OrderBy(row => Date(row, kind == "Call" ? "callDate" : "putDate")).ToArray();
                for (var index = 0; index < ordered.Length; index++)
                {
                    var row = ordered[index];
                    if (!TryReadEntry(ordered, index, kind, terms.MaturityDate, terms.PaymentFrequency,
                            out var price, out var dates, out refusal))
                        return false;
                    foreach (var unadjusted in dates)
                    {
                        var exercise = schedule.Spec.PaymentAdjustment is { Calendar: { } calendar } adjustment
                            ? calendar.Adjust(unadjusted, adjustment.Convention) : unadjusted;
                        if (exercise <= settlement || exercise > schedule.Periods[^1].PaymentDate)
                            continue;
                        if (!TryNotice(row, exercise, declared, calendars, out var latestNotice, out var earliestNotice, out refusal))
                            return false;
                        // An elapsed notice deadline does not prove a call was not already announced.
                        // Keep the contractual scenario and expose its deadline for evidence review.
                        if (!legacy && flows.Any(flow => flow.Interest > 0m && flow.AccrualEnd <= exercise
                                && exercise < flow.PaymentDate))
                        {
                            refusal = "Exercise crosses an earned but unpaid coupon; delayed-payment entitlement requires supported retained cash evidence.";
                            return false;
                        }
                        var couponAccrued = 0m;
                        var matchingPayments = flows.Where(flow => flow.PaymentDate == exercise).ToArray();
                        // A payment-lag coupon can be paid after the next period has begun. Its
                        // separately paid full coupon does not discharge that new period's accrual.
                        // Strict boundaries keep ordinary, unlagged coupon-date accrual at zero.
                        var period = schedule.Periods.FirstOrDefault(period =>
                            period.AccrualStart < exercise && exercise < period.AccrualEnd);
                        if (period is not null && annualRate != 0m)
                        {
                            var dayCount = DayCountConventions.Parse(terms.DayCountConvention);
                            if (legacy)
                            {
                                if (!BondContractualEconomics.TryCoupon(period, schedule, dayCount, annualRate,
                                        declared.Conventions.CouponCashBasis, out var wholeCoupon, out refusal)
                                    || !BondContractualEconomics.TryProratedCoupon(period, schedule, dayCount, wholeCoupon, exercise,
                                        "Exercise accrual period has no positive fraction.", out couponAccrued, out refusal))
                                    return false;
                            }
                            else if (!BondContractualEconomics.TryAccruedCoupon(period, schedule, dayCount, annualRate,
                                         declared.Conventions.CouponCashBasis, exercise, out couponAccrued, out refusal))
                                return false;
                        }
                        decimal time;
                        if (legacy)
                        {
                            if (!BondContractualEconomics.TryTime(schedule, settlement, exercise,
                                    DayCountConventions.Parse(terms.DayCountConvention), annualRate == 0m,
                                    convention, out time, out refusal))
                                return false;
                        }
                        else if (matchingPayments.Length > 0)
                        {
                            // Redemption and the coupon paid on the same date must use the same
                            // retained discount time, including the actual/365 payment lag.
                            time = matchingPayments[0].TimeInYears;
                            if (matchingPayments.Any(flow => flow.TimeInYears != time))
                            {
                                refusal = "Payments on the exercise date carry inconsistent retained discount times.";
                                return false;
                            }
                        }
                        else if (!BondContractualEconomics.TryTime(schedule, settlement, exercise,
                                     DayCountConventions.Parse(terms.DayCountConvention), terms.CouponKind == SecurityCouponKind.ZeroCoupon,
                                     convention, out time, out refusal))
                            return false;
                        result.Add(new(kind, exercise, price, couponAccrued, latestNotice, earliestNotice, time));
                        if (result.Count > 1200)
                        { refusal = "Exercise scenario count exceeds 1200."; return false; }
                    }
                }
            }
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or OverflowException)
        {
            refusal = "Declared exercise schedule could not be evaluated: " + exception.Message;
            return false;
        }
        yields = result.OrderBy(static item => item.ExerciseDate).ThenBy(static item => item.Kind, StringComparer.Ordinal).ToArray();
        return true;
    }

    /// <summary>
    /// The exercise scenarios of a record whose class declares call and put schedules but has no
    /// coupon schedule kernel (its terms carry no issue date or payment frequency): each declared
    /// entry's dates under the entry rules <see cref="TryResolveScenarios(SecurityDetailDto, StructuredCashFlowTerms, DeclaredScheduleResolution, ScheduleGenerationResult, DateOnly, decimal, BondYieldConvention, int, IReadOnlyList{BondAnalyticsCashFlow}, IBusinessDayCalendarSource?, out IReadOnlyList{BondRedemptionScenario}, out string?)"/>
    /// applies to a bond, with its notice bounds. No coupon is accrued and no discount time is taken,
    /// so <see cref="BondRedemptionScenario.AccruedPer100"/> and
    /// <see cref="BondRedemptionScenario.TimeInYears"/> are zero: the result supports notice windows
    /// and dated rows, not pricing.
    /// </summary>
    /// <remarks>
    /// An exercise date is adjusted only where <paramref name="declared"/> resolved a payment
    /// adjustment; otherwise it is the declared date. A Bermudan entry needs its own
    /// <c>exerciseFrequency</c>, because there is no coupon frequency to fall back to. Business-day
    /// notice needs the declared payment calendars, as for a bond. Dates on or before
    /// <paramref name="settlement"/> or after <paramref name="maturity"/> are dropped.
    /// </remarks>
    public static bool TryResolveDeclaredDates(SecurityDetailDto security, DateOnly maturity,
        DeclaredScheduleResolution declared, DateOnly settlement, IBusinessDayCalendarSource? calendars,
        out IReadOnlyList<BondRedemptionScenario> scenarios, out string? refusal)
    {
        ArgumentNullException.ThrowIfNull(security);
        ArgumentNullException.ThrowIfNull(declared);
        scenarios = [];
        refusal = null;
        var payload = security.AssetSpecificTerms;
        var hasCalls = Array(payload, SecurityVersionedTerms.CallScheduleKey, out var calls);
        var hasPuts = Array(payload, SecurityVersionedTerms.PutScheduleKey, out var puts);
        if (!hasCalls && !hasPuts)
        {
            return true;
        }

        // The arrays are read only under a stamp that decodes them, as the terms codec reads them.
        var decoding = SecurityVersionedTerms.Decoding(security.AssetClass, payload, writeMode: false);
        foreach (var (present, key) in new[] { (hasCalls, SecurityVersionedTerms.CallScheduleKey), (hasPuts, SecurityVersionedTerms.PutScheduleKey) })
        {
            if (present && (SecurityAssetTermsSchema.Field(security.AssetClass, key) is not { } field || !decoding.Decodes(field)))
            {
                refusal = SecurityAssetTermsSchema.Field(security.AssetClass, key) is { } declaredField
                    ? $"Declared exercise schedules require terms version {declaredField.SinceTermsVersion} or later for this asset class."
                    : "This asset class's terms declare no call or put schedule.";
                return false;
            }
        }

        var result = new List<BondRedemptionScenario>();
        try
        {
            // A stored row read back is its own retained declaration, as for a bond (ADR-025).
            ScheduleTermsWriteRules.ValidateDeclarations(security.AssetClass, payload,
                SecurityTermsEnvelope.ResolveAssetClassTermsVersion(payload), retained: payload);
            var adjustment = declared.Spec?.PaymentAdjustment;
            var lastDate = adjustment is { Calendar: { } maturityCalendar }
                ? maturityCalendar.Adjust(maturity, adjustment.Convention)
                : maturity;
            foreach (var (kind, rows) in new[] { ("Call", calls), ("Put", puts) })
            {
                var ordered = rows.Where(static row => row.ValueKind == JsonValueKind.Object)
                    .OrderBy(row => Date(row, kind == "Call" ? "callDate" : "putDate")).ToArray();
                for (var index = 0; index < ordered.Length; index++)
                {
                    var row = ordered[index];
                    if (!TryReadEntry(ordered, index, kind, maturity, couponFrequency: null, out var price, out var dates, out refusal))
                        return false;
                    foreach (var unadjusted in dates)
                    {
                        var exercise = adjustment is { Calendar: { } calendar }
                            ? calendar.Adjust(unadjusted, adjustment.Convention) : unadjusted;
                        if (exercise <= settlement || exercise > lastDate)
                            continue;
                        if (!TryNotice(row, exercise, declared, calendars, out var latestNotice, out var earliestNotice, out refusal))
                            return false;
                        result.Add(new(kind, exercise, price, 0m, latestNotice, earliestNotice, 0m));
                        if (result.Count > 1200)
                        { refusal = "Exercise scenario count exceeds 1200."; return false; }
                    }
                }
            }
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or OverflowException)
        {
            refusal = "Declared exercise schedule could not be evaluated: " + exception.Message;
            return false;
        }
        scenarios = result.OrderBy(static item => item.ExerciseDate).ThenBy(static item => item.Kind, StringComparer.Ordinal).ToArray();
        return true;
    }

    /// <summary>
    /// Reads one declared call or put entry under the supported-entry rules and expands it to its
    /// unadjusted exercise dates. The rules are the same for every class: a dated European or
    /// Bermudan entry, a positive fixed price in percent of par, whole redemption only. An entry's
    /// window ends at its own end date, else the day before the next entry starts, else the maturity.
    /// </summary>
    private static bool TryReadEntry(JsonElement[] ordered, int index, string kind, DateOnly? maturity,
        string? couponFrequency, out decimal price, out IReadOnlyList<DateOnly> dates, out string? refusal)
    {
        price = 0m;
        dates = [];
        var row = ordered[index];
        var prefix = kind == "Call" ? "call" : "put";
        var start = Date(row, prefix + "Date");
        var next = index + 1 < ordered.Length ? Date(ordered[index + 1], prefix + "Date") : null;
        var end = Date(row, prefix + "EndDate") ?? next?.AddDays(-1) ?? maturity;
        var style = BondContractualEconomics.Text(row, "exerciseStyle");
        var declaredPrice = Number(row, prefix + "Price");
        if (start is null || end is null || declaredPrice is null or <= 0m
            || style is not ("European" or "Bermudan")
            || BondContractualEconomics.Text(row, "priceBasis") is not (null or "PercentOfPar")
            || BondContractualEconomics.Text(row, "priceType") is not (null or "Fixed")
            || BondContractualEconomics.Text(row, "partialRedemption") is not (null or "WholeOnly"))
        {
            refusal = "Exercise analytics requires dated European/Bermudan whole fixed-price redemption in percent of par; continuous, make-whole and accreted-value exercise is unsupported.";
            return false;
        }

        price = declaredPrice.Value;
        return TryExerciseDates(row, start.Value, end.Value, style, couponFrequency, out dates, out refusal);
    }

    /// <summary>
    /// Expands one declared redemption row into its exercise dates: a European row is its single date; a
    /// Bermudan row is its start date followed by the regular-grid period ends up to its end date, on the
    /// row's <c>exerciseFrequency</c> or, absent that, the coupon frequency. Shared by analytics and the
    /// acquisition-price yield-to-worst comparison so both lists are the same dates.
    /// </summary>
    public static bool TryExerciseDates(JsonElement row, DateOnly start, DateOnly end, string style,
        string? couponFrequency, out IReadOnlyList<DateOnly> dates, out string? refusal, string? exerciseFrequency = null)
    {
        dates = [start];
        refusal = null;
        if (style == "European")
            return true;
        var frequency = exerciseFrequency ?? BondContractualEconomics.Text(row, "exerciseFrequency") ?? couponFrequency;
        if (!SecurityTermConventions.TryParsePaymentFrequency(frequency, out var cadence)
            || cadence.PeriodsPerYear() is null)
        {
            refusal = "Bermudan exercise requires a supported regular frequency.";
            return false;
        }
        if (start == end)
            return true;
        if (cadence.MonthsPerPeriod() is not { } months)
        { refusal = "Bermudan exercise requires a supported month-based regular grid."; return false; }
        var grid = ScheduleGenerator.Generate(new ScheduleSpec(start, end, ScheduleTenor.Months(months)));
        if (!grid.Succeeded)
        { refusal = grid.Failure.Message; return false; }
        dates = grid.Periods.Where(static period => !period.IsStub).Select(static period => period.UnadjustedEnd).Prepend(start).ToArray();
        return true;
    }

    private static bool TryNotice(JsonElement row, DateOnly exercise, DeclaredScheduleResolution declared,
        IBusinessDayCalendarSource? calendars, out DateOnly? latest, out DateOnly? earliest, out string? refusal)
    {
        latest = null;
        earliest = null;
        refusal = null;
        var minimum = Integer(row, "minimumNoticeDays");
        var maximum = Integer(row, "maximumNoticeDays");
        if (minimum is null && maximum is null)
            return true;
        if (minimum is < 0 || maximum is < 0 || minimum > maximum)
        {
            refusal = "Exercise notice bounds are invalid.";
            return false;
        }
        var type = BondContractualEconomics.Text(row, "noticeDayType");
        if (type == "Business")
        {
            if (calendars is null || !calendars.TryResolve(declared.Conventions.PaymentCalendars, out var calendar))
            {
                refusal = "Business-day notice requires the declared payment calendars.";
                return false;
            }
            latest = calendar.AddBusinessDays(exercise, -(minimum ?? 0));
            earliest = maximum is { } maximumBusiness ? calendar.AddBusinessDays(exercise, -maximumBusiness) : null;
        }
        else if (type == "Calendar")
        {
            latest = exercise.AddDays(-(minimum ?? 0));
            earliest = maximum is { } maximumCalendar ? exercise.AddDays(-maximumCalendar) : null;
        }
        else
        { refusal = "Exercise notice requires an explicit Business or Calendar day type."; return false; }
        return true;
    }

    private static bool Array(JsonElement payload, string name, out JsonElement[] rows)
    {
        rows = [];
        if (payload.ValueKind != JsonValueKind.Object || !payload.TryGetProperty(name, out var array)
            || array.ValueKind != JsonValueKind.Array)
            return false;
        rows = array.EnumerateArray().ToArray();
        return rows.Length > 0;
    }

    private static DateOnly? Date(JsonElement value, string name)
        => DateOnly.TryParseExact(BondContractualEconomics.Text(value, name), "yyyy-MM-dd", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var date) ? date : null;
    private static decimal? Number(JsonElement value, string name)
        => value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out var member)
            && member.ValueKind == JsonValueKind.Number && member.TryGetDecimal(out var number) ? number : null;
    private static int? Integer(JsonElement value, string name)
        => value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out var member)
            && member.ValueKind == JsonValueKind.Number && member.TryGetInt32(out var number) ? number : null;
}
