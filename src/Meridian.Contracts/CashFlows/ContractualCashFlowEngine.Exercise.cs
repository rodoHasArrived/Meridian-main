using Meridian.Contracts.Calendars;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.SecurityMaster.Schedules;

namespace Meridian.Contracts.CashFlows;

public static partial class ContractualCashFlowEngine
{
    private sealed record ResolvedExercise(ContractualExerciseEntry Entry, CashFlowExerciseEligibility Eligibility);

    private static IReadOnlyList<ResolvedExercise> ResolveExercises(ContractualCashFlowContract contract,
        ContractualCashFlowStream stream, ScheduleGenerationResult schedule, IBusinessDayCalendar? calendar)
    {
        var result = new List<ResolvedExercise>();
        foreach (var group in (contract.Exercises ?? []).Where(e => e.StreamId == stream.StreamId).GroupBy(e => e.Side))
        {
            var ordered = group.OrderBy(e => e.StartDate).ToArray();
            for (var i = 0; i < ordered.Length; i++)
            {
                var e = ordered[i];
                var end = e.EndDate ?? (i + 1 < ordered.Length ? ordered[i + 1].StartDate.AddDays(-1) : contract.MaturityDate);
                if (i + 1 < ordered.Length && end >= ordered[i + 1].StartDate)
                    throw new ArgumentException("Exercise windows for one side may not overlap.");
                if ((e.Style == CashFlowExerciseStyle.American || e.NoticeDayType == "Business") && calendar is null)
                    throw Refuse("ExerciseCalendarRequired", "American exercise and business-day notice require declared payment calendars.");
                IReadOnlyList<DateOnly> dates;
                if (e.Style == CashFlowExerciseStyle.European)
                    dates = [e.StartDate];
                else if (e.Style == CashFlowExerciseStyle.American)
                    dates = [];
                else if (e.ExerciseMonths is null)
                    dates = schedule.Periods.Select(p => p.PaymentDate).Where(d => d >= e.StartDate && d <= end).Distinct().Order().ToArray();
                else
                {
                    // The kernel provides anchored roll dates, excluding the terminal short stub.
                    var grid = ScheduleGenerator.Generate(schedule.Spec with
                    {
                        EffectiveDate = e.StartDate,
                        TerminationDate = end.AddDays(1),
                        Tenor = ScheduleTenor.Months(e.ExerciseMonths.Value),
                        Direction = ScheduleDirection.Forward,
                        FirstRegularDate = null,
                        LastRegularDate = null,
                        StubPolicy = ScheduleStubPolicy.Short,
                        AccrualAdjustment = null,
                        EffectiveDateAdjustment = null,
                        TerminationDateAdjustment = null,
                        PaymentLagBusinessDays = 0
                    });
                    if (!grid.Succeeded)
                        throw Refuse("ExerciseGridRefused", grid.Failure!.Message);
                    dates = grid.Periods.Select(p => p.UnadjustedStart)
                        .Select(d => schedule.Spec.PaymentAdjustment?.Apply(d) ?? d)
                        .Where(d => d >= e.StartDate && d <= end).Distinct().Order().ToArray();
                }
                result.Add(new(e, new(e.Side, e.StartDate, stream.StreamId, end, e.Style, dates,
                    e.Style == CashFlowExerciseStyle.American ? calendar!.CalendarId : null)));
            }
        }
        return result;
    }

    private static ResolvedExercise ValidateSelection(SelectedCashFlowExercise selection,
        IReadOnlyList<ResolvedExercise> entries, IBusinessDayCalendar? calendar)
    {
        var resolved = entries.SingleOrDefault(e => e.Entry.Side == selection.Side && e.Entry.StartDate == selection.EntryStartDate)
            ?? throw new ArgumentException("Selected exercise entry does not exist.");
        var eligible = resolved.Eligibility;
        if (selection.ExerciseDate < eligible.EntryStartDate || selection.ExerciseDate > eligible.WindowEnd
            || (eligible.Style == CashFlowExerciseStyle.American ? !calendar!.IsBusinessDay(selection.ExerciseDate) : !eligible.Dates.Contains(selection.ExerciseDate)))
            throw new ArgumentException("Selected date is not eligible for the exercise entry.");
        var notice = NoticeWindow(resolved.Entry, selection.ExerciseDate, calendar);
        if (selection.NoticeDate > selection.ExerciseDate || notice.Earliest is { } earliest && selection.NoticeDate < earliest
            || notice.Latest is { } latest && selection.NoticeDate > latest)
            throw new ArgumentException("Notice/election date is outside the inclusive notice window.");
        return resolved;
    }

    private static CashFlowNoticeWindow NoticeWindow(ContractualExerciseEntry entry, DateOnly date, IBusinessDayCalendar? calendar)
    {
        DateOnly Back(int count)
        {
            if (count > 36600)
                throw new ArgumentException("Notice exceeds the supported 100-year bound.");
            if (entry.NoticeDayType == "Calendar")
                return date.AddDays(-count);
            var cursor = date;
            var walked = 0;
            while (count > 0)
            {
                if (++walked > 36600)
                    throw Refuse("NoticeCalendarDegenerate", "Business-day notice walk exceeded 100 years.");
                cursor = cursor.AddDays(-1);
                if (calendar!.IsBusinessDay(cursor))
                    count--;
            }
            return cursor;
        }
        return new(entry.MaximumNoticeDays is { } max ? Back(max) : null, entry.MinimumNoticeDays is { } min ? Back(min) : date);
    }

    private static CashFlowExerciseCalculation PriceExercise(ContractualCashFlowContract contract,
        ContractualCashFlowStream stream, ContractualCashFlowRequest request, SelectedCashFlowExercise selection,
        ResolvedExercise resolved, ScheduleGenerationResult schedule, IBusinessDayCalendar? calendar,
        IReadOnlyList<CashFlowCurveSnapshot> curves, decimal outstanding, decimal accrued)
    {
        var e = resolved.Entry;
        if (selection.Amount > outstanding || e.PartialRedemption is null or "WholeOnly" && selection.Amount != outstanding)
            throw new ArgumentException("Exercise amount violates the outstanding balance or whole-only restriction.");
        if (e.PartialRedemption == "ByLot" && (selection.SelectedLotAmounts is not { Count: > 0 } lots || lots.Any(a => a <= 0) || lots.Sum() != selection.Amount))
            throw new ArgumentException("ByLot redemption requires selected positive lot amounts summing to the exercised principal.");
        if (e.CleanPrice is null)
            throw Refuse("ExercisePriceMissing", "A fixed price or make-whole clean floor must be stated.");
        var basis = e.PriceBasis == "PercentOfAccretedValue"
            ? e.AccretedValue ?? throw Refuse("AccretedValueMissing", "PercentOfAccretedValue needs retained accreted-value evidence per unit of principal.") : 1m;
        var floor = selection.Amount * basis * e.CleanPrice.Value / 100m;
        var allocatedAccrued = outstanding == 0 ? 0 : accrued * selection.Amount / outstanding;
        decimal pv = floor + allocatedAccrued;
        if (e.MakeWhole is { } mw)
        {
            if (!Enum.IsDefined(mw.Formula) || mw.AccruedInterestTreatment != "CleanFloorAddAccrued" || mw.CompoundsPerYear is < 0 or > 365)
                throw Refuse("MakeWholeFormulaUnsupported", "Unsupported formula, compounding or accrued-interest treatment.");
            if (!Enum.IsDefined(mw.DayCount) || mw.DayCount is DayCountConvention.Unknown or DayCountConvention.ActualActualIcma or DayCountConvention.Business252)
                throw Refuse("MakeWholeDayCountUnsupported", "Make-whole discounting requires a period-independent day count.");
            var market = FindCurve(curves, request, mw.BenchmarkKey)
                ?? throw Refuse("BenchmarkUnavailable", "The make-whole benchmark must be pinned.");
            var horizon = mw.ParCallDate ?? contract.MaturityDate;
            if (horizon <= selection.ExerciseDate || horizon > contract.MaturityDate)
                throw new ArgumentException("Make-whole horizon must follow exercise and be on/before maturity.");
            // Contractual continuation excludes every elective exercise and voluntary prepayment.
            var future = new List<ContractualCashFlowEvent>();
            var continuationStream = stream with { OpeningBalance = outstanding, OpeningBalanceDate = selection.ExerciseDate };
            Walk(contract, continuationStream, request with { ValuationDate = selection.ExerciseDate, Exercises = null, Prepayments = null, Horizon = null },
                schedule, calendar, curves, [], future, [], continuation: true);
            var flows = future.Where(f => f.Date > selection.ExerciseDate && f.Date <= horizon).Select(f => (f.Date, Cash: f.CashInterest + f.ScheduledPrincipal)).ToList();
            if (flows.Count > 0)
                flows[0] = (flows[0].Date, flows[0].Cash + accrued);
            if (horizon < contract.MaturityDate)
            {
                // Pay par at the stated horizon, including accrued coupon through that date.
                var remaining = future.Where(f => f.Date <= horizon).LastOrDefault()?.ClosingBalance ?? outstanding;
                var period = schedule.Periods.FirstOrDefault(p => p.AccrualStart < horizon && p.AccrualEnd > horizon);
                var tailInterest = period is null ? 0 : remaining * AnnualRate(stream, period, horizon, request, curves) * Fraction(stream, period, schedule, period.AccrualStart, horizon);
                flows.Add((horizon, remaining + tailInterest));
            }
            pv = 0;
            foreach (var flow in flows)
            {
                var years = DayCountConventions.Fraction(mw.DayCount, selection.ExerciseDate, flow.Date);
                decimal df;
                if (mw.Formula == MakeWholeFormula.BenchmarkCurvePvV1)
                {
                    if (market.Definition.Kind != SecurityCurveKind.Discount)
                        throw Refuse("BenchmarkKindMismatch", "Curve-PV make-whole requires discount factors.");
                    df = CurveValue(market, flow.Date, extrapolate: false) / CurveValue(market, selection.ExerciseDate, extrapolate: false)
                        * Discount(mw.SpreadBps / 10000m, years, mw.CompoundsPerYear);
                }
                else
                {
                    if (market.Definition.Kind != SecurityCurveKind.Forward)
                        throw Refuse("BenchmarkKindMismatch", "Yield-PV make-whole requires a yield benchmark stored as fractional Forward values.");
                    df = Discount(CurveValue(market, horizon, extrapolate: false) + mw.SpreadBps / 10000m, years, mw.CompoundsPerYear);
                }
                pv += flow.Cash * selection.Amount / outstanding * df;
            }
        }
        var clean = decimal.Max(floor, pv - allocatedAccrued);
        var dirty = Cash(clean + allocatedAccrued);
        return new(selection, NoticeWindow(e, selection.ExerciseDate, calendar), Cash(pv), Cash(allocatedAccrued), Cash(floor), dirty, Cash(clean - selection.Amount));
    }

    private static decimal Discount(decimal rate, decimal years, int compounds)
    {
        if (compounds > 0 && 1m + rate / compounds <= 0)
            throw Refuse("DiscountRateInvalid", "Periodic discount base must be positive.");
        var value = compounds == 0 ? Math.Exp(-(double)(rate * years)) : Math.Pow((double)(1m + rate / compounds), -(double)(compounds * years));
        if (!double.IsFinite(value) || value <= 0)
            throw Refuse("DiscountRateInvalid", "Discount factor is outside the supported range.");
        return (decimal)value;
    }

    private static decimal CurveValue(CashFlowCurveSnapshot market, DateOnly date, bool extrapolate)
    {
        var points = market.Version.Points.OrderBy(p => p.Date).ToArray();
        if (points.Length == 0)
            throw Refuse("CurveEmpty", "Curve has no pillars.");
        if (date < market.Version.AsOf || !extrapolate && date > points[^1].Date)
            throw Refuse("CurveCoverageMissing", "Pinned curve does not cover the requested date.");
        if (date <= points[0].Date)
        {
            if (market.Definition.Kind != SecurityCurveKind.Discount || points[0].Date == market.Version.AsOf)
                return points[0].Value;
            return 1m + (points[0].Value - 1m) * (date.DayNumber - market.Version.AsOf.DayNumber) / (points[0].Date.DayNumber - market.Version.AsOf.DayNumber);
        }
        for (var i = 1; i < points.Length; i++)
            if (date <= points[i].Date)
                return points[i - 1].Value + (points[i].Value - points[i - 1].Value) * (date.DayNumber - points[i - 1].Date.DayNumber) / (points[i].Date.DayNumber - points[i - 1].Date.DayNumber);
        return points[^1].Value;
    }

    private static CashFlowCurveSnapshot? FindCurve(IReadOnlyList<CashFlowCurveSnapshot> curves,
        ContractualCashFlowRequest request, string role, SecurityCurveKind? kind = null)
    {
        var pin = request.CurvePins?.SingleOrDefault(p => p.BenchmarkKey == role);
        return curves.SingleOrDefault(c => (kind is null || c.Definition.Kind == kind)
            && (pin is null ? c.Definition.CurveKey == role
                : c.Definition.CurveId == pin.CurveId && c.Version.CurveVersionId == pin.CurveVersionId));
    }
}
