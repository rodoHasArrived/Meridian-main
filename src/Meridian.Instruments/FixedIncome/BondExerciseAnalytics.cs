using System.Globalization;
using System.Text.Json;
using Meridian.Contracts.Calendars;
using Meridian.Contracts.FixedIncome;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.SecurityMaster.Schedules;

namespace Meridian.Instruments.FixedIncome;

/// <summary>Discrete fixed-price exercise scenarios. Continuous, make-whole and partial calls refuse.</summary>
internal static class BondExerciseAnalytics
{
    public static bool TryCalculate(SecurityDetailDto security, StructuredCashFlowTerms terms,
        DeclaredScheduleResolution declared, ScheduleGenerationResult schedule, DateOnly settlement,
        decimal annualRate, BondYieldConvention convention, int frequency, decimal dirtyPrice,
        IReadOnlyList<BondAnalyticsCashFlow> flows, IBusinessDayCalendarSource? calendars,
        out IReadOnlyList<BondExerciseYield> yields, out string? refusal)
    {
        yields = [];
        refusal = null;
        var payload = security.AssetSpecificTerms;
        var result = new List<BondExerciseYield>();
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
            ScheduleTermsWriteRules.ValidateDeclarations(security.AssetClass, payload, termsVersion);
            foreach (var (kind, rows) in new[] { ("Call", calls), ("Put", puts) })
            {
                var ordered = rows.Where(static row => row.ValueKind == JsonValueKind.Object)
                    .OrderBy(row => Date(row, kind == "Call" ? "callDate" : "putDate")).ToArray();
                for (var index = 0; index < ordered.Length; index++)
                {
                    var row = ordered[index];
                    var prefix = kind == "Call" ? "call" : "put";
                    var start = Date(row, prefix + "Date");
                    var next = index + 1 < ordered.Length ? Date(ordered[index + 1], prefix + "Date") : null;
                    var end = Date(row, prefix + "EndDate") ?? next?.AddDays(-1) ?? terms.MaturityDate;
                    var style = BondAnalyticsSchedule.Text(row, "exerciseStyle");
                    var price = Number(row, prefix + "Price");
                    if (start is null || end is null || price is null or <= 0m
                        || style is not ("European" or "Bermudan")
                        || BondAnalyticsSchedule.Text(row, "priceBasis") is not (null or "PercentOfPar")
                        || BondAnalyticsSchedule.Text(row, "priceType") is not (null or "Fixed")
                        || BondAnalyticsSchedule.Text(row, "partialRedemption") is not (null or "WholeOnly"))
                    {
                        refusal = "Exercise analytics requires dated European/Bermudan whole fixed-price redemption in percent of par; continuous, make-whole and accreted-value exercise is unsupported.";
                        return false;
                    }
                    if (!TryExerciseDates(row, start.Value, end.Value, style, terms.PaymentFrequency,
                            out var dates, out refusal))
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
                        var scenario = flows.Where(flow => flow.PaymentDate <= exercise && flow.Interest > 0m)
                            .Select(static flow => new BondPricingCashFlow(flow.Interest, flow.TimeInYears)).ToList();
                        var couponAccrued = 0m;
                        if (!flows.Any(flow => flow.PaymentDate == exercise))
                        {
                            var period = schedule.Periods.FirstOrDefault(period =>
                                period.AccrualStart < exercise && exercise < period.AccrualEnd);
                            if (period is not null && annualRate != 0m)
                            {
                                var dayCount = DayCountConventions.Parse(terms.DayCountConvention);
                                if (!BondAnalyticsSchedule.TryCoupon(period, schedule, dayCount, annualRate,
                                        declared.Conventions.CouponCashBasis, out var wholeCoupon, out refusal)
                                    || !ScheduleDayCount.TryFraction(dayCount, period, schedule, period.AccrualStart,
                                        exercise, out var partial, out refusal)
                                    || !ScheduleDayCount.TryPeriodFraction(dayCount, period, schedule, out var full, out refusal))
                                    return false;
                                if (full <= 0m)
                                { refusal = "Exercise accrual period has no positive fraction."; return false; }
                                couponAccrued = wholeCoupon * partial / full;
                            }
                        }
                        if (!BondAnalyticsSchedule.TryTime(schedule, settlement, exercise,
                                DayCountConventions.Parse(terms.DayCountConvention), annualRate == 0m,
                                convention, out var time, out refusal))
                            return false;
                        scenario.Add(new(price.Value + couponAccrued, time));
                        if (!BondPricingMath.TrySolveYield(scenario, dirtyPrice, convention, frequency,
                                out var yield, out refusal))
                            return false;
                        result.Add(new(kind, exercise, price.Value, yield, latestNotice, earliestNotice));
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

    private static bool TryExerciseDates(JsonElement row, DateOnly start, DateOnly end, string style,
        string? couponFrequency, out IReadOnlyList<DateOnly> dates, out string? refusal)
    {
        dates = [start];
        refusal = null;
        if (style == "European")
            return true;
        var frequency = BondAnalyticsSchedule.Text(row, "exerciseFrequency") ?? couponFrequency;
        if (!SecurityTermConventions.TryParsePaymentFrequency(frequency, out var cadence)
            || cadence.PeriodsPerYear() is not { } periods)
        {
            refusal = "Bermudan exercise requires a supported regular frequency.";
            return false;
        }
        if (start == end)
            return true;
        var grid = ScheduleGenerator.Generate(new ScheduleSpec(start, end, ScheduleTenor.Months(12 / periods)));
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
        var type = BondAnalyticsSchedule.Text(row, "noticeDayType");
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
        => DateOnly.TryParseExact(BondAnalyticsSchedule.Text(value, name), "yyyy-MM-dd", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var date) ? date : null;
    private static decimal? Number(JsonElement value, string name)
        => value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out var member)
            && member.ValueKind == JsonValueKind.Number && member.TryGetDecimal(out var number) ? number : null;
    private static int? Integer(JsonElement value, string name)
        => value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out var member)
            && member.ValueKind == JsonValueKind.Number && member.TryGetInt32(out var number) ? number : null;
}
