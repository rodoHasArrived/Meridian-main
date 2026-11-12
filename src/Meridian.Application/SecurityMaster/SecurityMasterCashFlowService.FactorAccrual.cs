using Meridian.Contracts.SecurityMaster;

namespace Meridian.Application.SecurityMaster;

public sealed partial class SecurityMasterCashFlowService
{
    /// <summary>
    /// The v5 fixed-rate walk retains the balance that actually earned each part of a coupon.
    /// Factor dates describe balances after that date's principal payment; interest through the
    /// date is measured first. Old receipt models keep the frozen walk in the main partial.
    /// </summary>
    private static StructuredCashFlowProjectionDto BuildFactorAwareFixedProjection(
        SecurityDetailDto security, StructuredCashFlowSourceKind sourceKind,
        StructuredCashFlowScenario scenario, StructuredCashFlowStaleness staleness,
        DateTimeOffset? sourceLastUpdatedUtc, DateTimeOffset asOf, DateOnly asOfDate,
        StructuredCashFlowTerms terms,
        IReadOnlyList<(DateOnly AccrualStart, DateOnly PaymentDate)> periods,
        decimal principalBasis, decimal annualRate, bool hasAssertedPrincipalSchedule,
        IReadOnlyList<StructuredPrincipalScheduleEntry> contractualPrincipal)
    {
        StructuredCashFlowProjectionDto Result(IReadOnlyList<StructuredCashFlowScheduleEntry> rows, string? reason = null)
            => new(security.SecurityId, sourceKind, scenario, asOf, rows, staleness,
                sourceLastUpdatedUtc, terms.FactorSchedule, terms, BlockedReason: reason);
        StructuredCashFlowProjectionDto Refused(string reason)
            => Result([], "Fixed-rate accrual balance history is unavailable: " + reason);

        var firstStart = periods[0].AccrualStart;
        var maturity = periods[^1].PaymentDate;
        var factors = new SortedDictionary<DateOnly, decimal>();
        foreach (var point in terms.FactorSchedule.Where(point => point.AsOfDate <= maturity))
        {
            if (point.Factor is < 0m or > 1m
                || factors.TryGetValue(point.AsOfDate, out var existing) && existing != point.Factor)
                return Refused("dated factors must be unambiguous values in [0, 1].");
            factors[point.AsOfDate] = point.Factor;
        }

        var scalarDate = SecurityTermReader.ReadDate(
            StructuredCashFlowTermsResolver.EnumerateTermSources(security).SkipLast(1).ToArray(),
            ["factorDate", "currentFactorDate"]);
        if (terms.CurrentFactor is { } scalar)
        {
            if (scalar is < 0m or > 1m)
                return Refused("the current factor must be in [0, 1].");
            if (scalarDate is { } date && date <= maturity)
            {
                if (factors.TryGetValue(date, out var existing) && existing != scalar)
                    return Refused("the scalar factor contradicts a dated factor on the same date.");
                factors[date] = scalar;
            }
        }

        var baseline = factors.FirstOrDefault(point => point.Key <= firstStart);
        var hasBaseline = factors.Keys.Any(date => date <= firstStart);
        var hasFactorHistory = factors.Values.Any(static factor => factor != 1m);
        if (!hasBaseline && (hasFactorHistory || terms.CurrentFactor is { } current && current != 1m))
            return Refused("a dated factor at or before the first coupon's accrual start is required; an undated current factor is not historical evidence.");

        var payments = contractualPrincipal
            .GroupBy(static payment => payment.PaymentDate)
            .ToDictionary(static group => group.Key, static group => group.Sum(static payment => payment.Amount));
        if (payments.Values.Any(static amount => amount < 0m))
            return Refused("principal payments cannot be negative.");

        // The earliest retained factor establishes an authoritative post-payment balance. Replay
        // every later event, even before the first requested coupon, rather than letting the most
        // recent factor hide a contradictory retained principal payment on the same date.
        var outstanding = principalBasis * (hasBaseline ? baseline.Value : 1m);
        var historyEvents = factors.Keys.Concat(payments.Keys)
            .Where(date => date <= firstStart && (!hasBaseline || date > baseline.Key))
            .Distinct().OrderBy(static date => date);
        foreach (var date in historyEvents)
        {
            if (!TryApplyPrincipalEvent(date, outstanding, out var after, out _, out var reason))
                return Refused(reason!);
            outstanding = after;
        }

        var syntheticSinker = sourceKind == StructuredCashFlowSourceKind.CalculatedSinker
            && !hasAssertedPrincipalSchedule;
        if (syntheticSinker && (hasFactorHistory || terms.CurrentFactor is { } factor && factor != 1m))
            return Refused("a synthetic sinker cannot be reconciled to factored principal; retain explicit principal payments.");
        if (syntheticSinker)
        {
            var equalPrincipal = RoundCash(outstanding / periods.Count);
            var remaining = outstanding;
            for (var i = 0; i < periods.Count; i++)
            {
                var payment = i == periods.Count - 1 ? remaining : decimal.Min(remaining, equalPrincipal);
                payments[periods[i].PaymentDate] = payment;
                remaining -= payment;
            }
        }

        // A factor increase is a restatement, not an accrued principal paydown. It needs a
        // separately governed correction rather than inventing a negative principal payment.
        var lastFactor = hasBaseline ? baseline.Value : 1m;
        foreach (var point in factors.Where(point => point.Key > firstStart))
        {
            if (point.Value > lastFactor)
                return Refused("factor increases require a separately supported balance restatement.");
            lastFactor = point.Value;
        }

        var entries = new List<StructuredCashFlowScheduleEntry>();
        foreach (var (periodStart, periodEnd) in periods)
        {
            var events = factors.Keys.Concat(payments.Keys)
                .Where(date => date > periodStart && date <= periodEnd)
                .Append(periodEnd).Distinct().OrderBy(static date => date);
            var cursor = periodStart;
            var segments = new List<StructuredCashFlowInterestSegment>();
            foreach (var date in events)
            {
                // Anchor every difference to the original coupon period. In particular, splitting
                // Thirty360 into independent subperiods need not preserve the original day count.
                var measure = DayCountConventions.Fraction(terms.DayCountConvention, periodStart, date)
                    - DayCountConventions.Fraction(terms.DayCountConvention, periodStart, cursor);
                if (measure < 0m)
                    return Refused("the day-count measurement is not monotone within the coupon period.");
                var segment = new StructuredCashFlowInterestSegment(cursor, date, outstanding,
                    outstanding * annualRate * measure, outstanding / principalBasis, periodStart, periodEnd);

                if (!TryApplyPrincipalEvent(date, outstanding, out var after, out var payment, out var reason))
                    return Refused(reason!);

                if (date == maturity)
                {
                    payment += after;
                    after = 0m;
                }

                segments.Add(segment with { FactorAfter = after / principalBasis });
                outstanding = after;
                if (date == periodEnd)
                {
                    entries.Add(new StructuredCashFlowScheduleEntry(ToUtcDateTimeOffset(date), RoundCash(payment),
                        RoundCash(segments.Sum(static segment => segment.UnroundedInterestAmount)),
                        RoundCash(outstanding / principalBasis))
                    {
                        InterestMeasuredFrom = periodStart,
                        InterestMeasuredTo = periodEnd,
                        InterestSegments = segments
                    });
                }
                else if (payment > 0m && date >= asOfDate)
                {
                    entries.Add(new StructuredCashFlowScheduleEntry(ToUtcDateTimeOffset(date), RoundCash(payment),
                        0m, RoundCash(outstanding / principalBasis)));
                }

                cursor = date;
            }
        }

        return Result(entries.OrderBy(static row => row.PeriodDate).ToArray());

        bool TryApplyPrincipalEvent(DateOnly date, decimal before, out decimal after,
            out decimal payment, out string? reason)
        {
            payment = payments.GetValueOrDefault(date);
            after = before - payment;
            reason = null;
            if (payment > before)
            {
                reason = "a principal payment exceeds the outstanding balance.";
                return false;
            }
            if (factors.TryGetValue(date, out var datedFactor))
            {
                var factorBalance = principalBasis * datedFactor;
                if (factorBalance > before)
                {
                    reason = "a dated factor increases the principal balance.";
                    return false;
                }
                if ((hasAssertedPrincipalSchedule || payments.ContainsKey(date))
                    && RoundCash(after) != RoundCash(factorBalance))
                {
                    reason = "dated factors and explicit principal payments do not reconcile.";
                    return false;
                }
                after = factorBalance;
                payment = before - after;
            }
            return true;
        }
    }
}
