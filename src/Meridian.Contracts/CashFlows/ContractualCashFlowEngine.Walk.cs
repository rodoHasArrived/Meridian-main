using Meridian.Contracts.Calendars;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.SecurityMaster.Schedules;

namespace Meridian.Contracts.CashFlows;

public static partial class ContractualCashFlowEngine
{
    private static void Walk(ContractualCashFlowContract contract, ContractualCashFlowStream stream,
        ContractualCashFlowRequest request, ScheduleGenerationResult schedule, IBusinessDayCalendar? calendar,
        IReadOnlyList<CashFlowCurveSnapshot> curves, IReadOnlyList<ResolvedExercise> exerciseEntries,
        List<ContractualCashFlowEvent> output, List<CashFlowExerciseCalculation> calculations, bool continuation = false)
    {
        var periods = schedule.Periods;
        var selected = (request.Exercises ?? []).Where(e => e.StreamId == stream.StreamId).OrderBy(e => e.ExerciseDate).ToArray();
        var resolved = selected.ToDictionary(e => e.ExerciseDate, e => ValidateSelection(e, exerciseEntries, calendar));
        var assumptions = (request.Prepayments ?? []).Where(p => p.StreamId == stream.StreamId).ToArray();
        if (assumptions.Length > 0 && !stream.PrepaymentAllowed)
            throw new ArgumentException("Voluntary prepayments are prohibited by the contract.");
        if (assumptions.Any(p => p.Policy == CashFlowRecastPolicy.Recast) && stream.Amortization is not (CashFlowAmortization.EqualPrincipal or CashFlowAmortization.LevelPayment))
            throw new ArgumentException("Recast requires an equal-principal or level-payment amortization formula.");
        if (stream.NotionalSchedule is { Count: > 0 } && (selected.Length > 0 || assumptions.Length > 0))
            throw Refuse("ElectiveNotionalBalanceUnsupported", "Elective reductions require a principal cash schedule rather than absolute notional resets.");
        if (stream.CouponAmounts is { Count: > 0 } && (stream.IndexName is not null || stream.RateSteps is { Count: > 0 } || stream.FixedRate is not null || stream.PikFraction > 0))
            throw new ArgumentException("Explicit coupon cash amounts replace the rate formula and cannot also declare PIK.");
        if (stream.CouponAmounts is { Count: > 0 } amounts && (amounts.Count != periods.Count || amounts.Any(a => !periods.Any(p => p.PaymentDate == a.PaymentDate))))
            throw new ArgumentException("Explicit coupon amounts must name every coupon payment date exactly once.");
        var principal = (stream.PrincipalSchedule ?? []).GroupBy(p => p.PaymentDate).ToDictionary(g => g.Key, g => g.Sum(p => p.Amount));
        var notionals = (stream.NotionalSchedule ?? []).ToDictionary(p => p.EffectiveDate, p => p.Notional);
        var couponAmounts = (stream.CouponAmounts ?? []).ToDictionary(p => p.PaymentDate, p => p.Amount);
        var cashAccrued = periods.ToDictionary(p => p.Sequence, _ => 0m);
        var pikAccrued = periods.ToDictionary(p => p.Sequence, _ => 0m);
        var start = continuation ? request.ValuationDate : contract.IssueDate;
        var balance = continuation ? stream.OpeningBalance!.Value : stream.Principal;
        var timeline = periods.SelectMany(p => new[] { p.AccrualStart, p.AccrualEnd, p.PaymentDate })
            .Concat(principal.Keys).Concat(notionals.Keys).Concat((stream.RateSteps ?? []).Select(s => s.EffectiveDate))
            .Concat((stream.Resets ?? []).Select(s => s.ResetDate)).Concat(selected.Select(s => s.ExerciseDate))
            .Concat(assumptions.Where(p => p.Method == CashFlowPrepaymentMethod.Amount).Select(p => p.StartDate))
            .Concat(stream.OpeningBalanceDate is { } balanceDate ? new[] { balanceDate } : [])
            .Append(start).Distinct().Where(d => d >= start).Order().ToArray();
        if (timeline.Length > 10000)
            throw new ArgumentException("Event timeline exceeds 10000 dates.");
        var previous = start;
        var lastPrepay = request.ValuationDate > start ? request.ValuationDate : start;
        var regularPrincipal = periods.Count == 0 ? 0 : stream.Principal / periods.Count;
        var scheduledPayment = stream.Amortization == CashFlowAmortization.LevelPayment ? LevelPayment(balance, periods, stream, request, schedule, curves) : 0;
        var terminated = false;
        var hasElectiveReduction = continuation;
        foreach (var date in timeline)
        {
            var opening = balance;
            foreach (var p in periods.Where(p => p.AccrualStart < date && p.AccrualEnd > previous))
            {
                var left = previous > p.AccrualStart ? previous : p.AccrualStart;
                var right = date < p.AccrualEnd ? date : p.AccrualEnd;
                if (right <= left || balance == 0 || terminated)
                    continue;
                decimal interest;
                if (couponAmounts.TryGetValue(p.PaymentDate, out var amount))
                    interest = amount * Fraction(stream, p, schedule, left, right) / Fraction(stream, p, schedule, p.AccrualStart, p.AccrualEnd);
                else
                {
                    var rate = AnnualRate(stream, p, left, request, curves);
                    var fraction = Fraction(stream, p, schedule, left, right);
                    if (stream.Conventions?.CouponCashBasis == "Level" && !p.IsStub)
                    {
                        if (stream.PaymentMonths == 0)
                            throw Refuse("LevelTermUnsupported", "A Level coupon requires regular cadence.");
                        fraction = fraction / Fraction(stream, p, schedule, p.AccrualStart, p.AccrualEnd) * stream.PaymentMonths / 12m;
                    }
                    interest = balance * rate * fraction;
                }
                cashAccrued[p.Sequence] += interest * (1 - stream.PikFraction);
                pikAccrued[p.Sequence] += interest * stream.PikFraction;
            }
            // Reconstruct from origination; dated balance evidence must reconcile, never replace history silently.
            if (!continuation && stream.OpeningBalanceDate == date && stream.OpeningBalance is { } evidence && Cash(balance) != Cash(evidence))
                throw Refuse("OpeningBalanceHistoryMissing", "Dated outstanding balance does not reconcile to the retained contractual history.");
            var capitalized = 0m;
            foreach (var p in periods.Where(p => p.AccrualEnd == date))
            {
                capitalized += pikAccrued[p.Sequence];
                pikAccrued[p.Sequence] = 0;
            }
            balance += capitalized;
            var cashInterest = 0m;
            foreach (var p in periods.Where(p => p.PaymentDate == date))
            {
                cashInterest += cashAccrued[p.Sequence];
                cashAccrued[p.Sequence] = 0;
            }
            var scheduled = 0m;
            if (!terminated)
            {
                if (principal.TryGetValue(date, out var due))
                {
                    if (due > balance && !hasElectiveReduction)
                        throw new ArgumentException("Contractual principal exceeds the outstanding balance.");
                    scheduled = decimal.Min(balance, due);
                }
                if (notionals.TryGetValue(date, out var target))
                {
                    if (stream.ExchangesPrincipal)
                        scheduled += balance - target;
                    balance = target;
                }
                else
                    balance -= scheduled;
                if (periods.Any(p => p.PaymentDate == date) && stream.Amortization is CashFlowAmortization.EqualPrincipal or CashFlowAmortization.LevelPayment)
                {
                    var repayment = stream.Amortization == CashFlowAmortization.EqualPrincipal ? regularPrincipal : decimal.Max(0m, scheduledPayment - cashInterest - capitalized);
                    repayment = decimal.Min(balance, repayment);
                    scheduled += repayment;
                    balance -= repayment;
                }
            }
            var redeemed = 0m;
            var premium = 0m;
            if (selected.SingleOrDefault(e => e.ExerciseDate == date) is { } election)
            {
                if (terminated || balance <= 0)
                    throw new ArgumentException("Selected exercise follows full payoff.");
                var accrued = cashAccrued.Values.Sum() + pikAccrued.Values.Sum();
                var price = PriceExercise(contract, stream, request, election, resolved[date], schedule, calendar, curves, balance, accrued);
                calculations.Add(price);
                var proportion = election.Amount / balance;
                foreach (var key in cashAccrued.Keys.ToArray())
                    cashAccrued[key] *= 1m - proportion;
                foreach (var key in pikAccrued.Keys.ToArray())
                    pikAccrued[key] *= 1m - proportion;
                cashInterest += price.AccruedInterest;
                premium = price.Premium;
                redeemed = election.Amount;
                balance -= redeemed;
                hasElectiveReduction = true;
                if (balance == 0)
                    terminated = true;
            }
            var prepayment = 0m;
            var penalty = 0m;
            var policy = CashFlowRecastPolicy.PreservePayments;
            if (!terminated && balance > 0)
            {
                foreach (var assumption in assumptions.Where(p => p.Method == CashFlowPrepaymentMethod.Amount && p.StartDate == date))
                {
                    prepayment += decimal.Min(balance - prepayment, assumption.Value);
                    policy = assumption.Policy;
                }
                if (periods.Any(p => p.PaymentDate == date))
                {
                    var survival = 1m;
                    foreach (var assumption in assumptions.Where(p => p.Method != CashFlowPrepaymentMethod.Amount && p.StartDate < date && p.EndDate > lastPrepay))
                    {
                        survival *= 1m - PrepaymentFraction(assumption, lastPrepay, date);
                        policy = assumption.Policy;
                    }
                    prepayment += (balance - prepayment) * (1m - survival);
                    lastPrepay = date;
                }
                balance -= prepayment;
                hasElectiveReduction |= prepayment > 0;
                penalty = prepayment * stream.PrepaymentPenaltyRate;
                if (prepayment > 0 && policy == CashFlowRecastPolicy.Recast)
                {
                    var remaining = periods.Where(p => p.PaymentDate > date).ToArray();
                    if (remaining.Length > 0)
                    {
                        regularPrincipal = balance / remaining.Length;
                        scheduledPayment = stream.Amortization == CashFlowAmortization.LevelPayment ? LevelPayment(balance, remaining, stream, request, schedule, curves) : 0;
                    }
                }
            }
            if (stream.ExchangesPrincipal && date == periods[^1].PaymentDate && balance > 0)
            {
                scheduled += balance;
                balance = 0;
            }
            if (balance == 0 && (stream.ExchangesPrincipal && stream.NotionalSchedule is not { Count: > 0 } || redeemed > 0))
                terminated = true;
            balance = Cash(balance);
            var period = periods.FirstOrDefault(p => p.PaymentDate == date || p.AccrualStart < date && p.AccrualEnd >= date);
            if (date >= request.ValuationDate && (request.Horizon is null || date <= request.Horizon)
                && (cashInterest != 0 || capitalized != 0 || scheduled != 0 || prepayment != 0 || redeemed != 0 || opening != balance))
            {
                var periodId = period is null ? null : $"{stream.StreamId}:{period.UnadjustedStart:yyyy-MM-dd}/{period.UnadjustedEnd:yyyy-MM-dd}";
                output.Add(new($"{stream.StreamId}:{date:yyyy-MM-dd}", stream.StreamId, periodId, date,
                    period?.AccrualStart, period?.AccrualEnd, Cash(opening), Cash(cashInterest), Cash(capitalized),
                    Cash(scheduled), Cash(prepayment), Cash(redeemed), Cash(premium), Cash(penalty), balance, stream.Direction));
            }
            previous = date;
        }
    }

    private static decimal AnnualRate(ContractualCashFlowStream stream, SchedulePeriod period, DateOnly date,
        ContractualCashFlowRequest request, IReadOnlyList<CashFlowCurveSnapshot> curves)
    {
        decimal rate;
        if (stream.IndexName is { } index)
        {
            var reset = (stream.Resets ?? []).Where(r => r.ResetDate >= period.AccrualStart && r.ResetDate <= date)
                .OrderBy(r => r.ResetDate).LastOrDefault();
            var resetDate = reset?.ResetDate ?? period.AccrualStart;
            if (resetDate <= request.ValuationDate && reset?.FixingRate is { } observed)
                rate = observed;
            else if (resetDate < request.ValuationDate)
                rate = reset?.FixingRate ?? throw Refuse("HistoricalFixingMissing", "Historical reset requires its observed fixing.");
            else
            {
                var curve = FindCurve(curves, request, index, SecurityCurveKind.Forward)
                    ?? throw Refuse("ForwardCurveMissing", "Future resets require an identified pinned forward curve.");
                rate = CurveValue(curve, resetDate, false) + ScenarioShift(request.RateScenario);
            }
            rate += stream.SpreadBps / 10000m;
        }
        else
            rate = (stream.RateSteps ?? []).Where(r => r.EffectiveDate <= date).OrderBy(r => r.EffectiveDate).LastOrDefault()?.Rate
            ?? stream.FixedRate ?? throw Refuse("CouponRateMissing", "Fixed coupon rate is missing; zero is not a substitute.");
        if (stream.Floor is { } floor)
            rate = decimal.Max(floor, rate);
        if (stream.Cap is { } cap)
            rate = decimal.Min(cap, rate);
        return rate;
    }

    private static decimal ScenarioShift(StructuredCashFlowScenario scenario) => scenario switch
    {
        StructuredCashFlowScenario.Base => 0,
        StructuredCashFlowScenario.Up100 => .01m,
        StructuredCashFlowScenario.Up200 => .02m,
        StructuredCashFlowScenario.Up300 => .03m,
        StructuredCashFlowScenario.Down100 => -.01m,
        StructuredCashFlowScenario.Down200 => -.02m,
        StructuredCashFlowScenario.Down300 => -.03m,
        _ => throw Refuse("ScenarioUnsupported", "Stress requires an explicitly defined rate path.")
    };

    private static decimal Fraction(ContractualCashFlowStream stream, SchedulePeriod period, ScheduleGenerationResult schedule, DateOnly start, DateOnly end)
    {
        if (!ScheduleDayCount.TryFraction(stream.DayCount, period, schedule, start, end, out var fraction, out var refusal))
            throw Refuse("DayCountRefused", refusal!);
        return fraction;
    }

    private static decimal LevelPayment(decimal balance, IReadOnlyList<SchedulePeriod> periods, ContractualCashFlowStream stream,
        ContractualCashFlowRequest request, ScheduleGenerationResult schedule, IReadOnlyList<CashFlowCurveSnapshot> curves)
    {
        decimal discount = 1, sum = 0;
        foreach (var p in periods)
        {
            var accrual = Fraction(stream, p, schedule, p.AccrualStart, p.AccrualEnd);
            var factor = 1m + AnnualRate(stream, p, p.AccrualStart, request, curves) * accrual;
            if (factor <= 0)
                throw Refuse("AmortizationRateInvalid", "Level-payment discount base must be positive.");
            discount /= factor;
            sum += discount;
        }
        return balance / sum;
    }
}
