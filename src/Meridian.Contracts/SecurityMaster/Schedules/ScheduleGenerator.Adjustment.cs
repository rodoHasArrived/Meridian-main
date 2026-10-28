using System.Globalization;
using Meridian.Contracts.Calendars;

namespace Meridian.Contracts.SecurityMaster.Schedules;

// Business-day adjustment, payment dates and calendar evidence for the schedule kernel.
public static partial class ScheduleGenerator
{
    // Adjusted dates are derived from the unadjusted grid, never chained: the first boundary uses the
    // effective-date adjustment, the last the termination-date adjustment, inner boundaries the accrual
    // adjustment. A payment lag counts business days from the adjusted accrual end (FpML
    // paymentDaysOffset), so the convention is applied only when there is no lag.
    private static ScheduleGenerationResult Assemble(ScheduleSpec spec, List<RawPeriod> raw)
    {
        var evidence = new CalendarEvidence();
        var accrual = spec.AccrualAdjustment ?? BusinessDayAdjustment.Unadjusted;
        var effective = spec.EffectiveDateAdjustment ?? accrual;
        var termination = spec.TerminationDateAdjustment ?? accrual;
        var payment = spec.PaymentAdjustment ?? BusinessDayAdjustment.Unadjusted;
        var count = raw.Count;

        var adjusted = new DateOnly[count + 1];
        for (var i = 0; i <= count; i++)
        {
            var date = i == 0 ? raw[0].Start : raw[i - 1].End;
            var (adjustment, purpose) = i == 0
                ? (effective, ScheduleCalendarPurpose.EffectiveDate)
                : i == count ? (termination, ScheduleCalendarPurpose.TerminationDate) : (accrual, ScheduleCalendarPurpose.Accrual);
            if (!evidence.TryAdjust(adjustment, date, purpose, out adjusted[i], out var failure))
            {
                return Failed(spec, failure, evidence.Diagnostics, evidence.Usages);
            }
        }

        var periods = new SchedulePeriod[count];
        for (var i = 0; i < count; i++)
        {
            var (accrualStart, accrualEnd) = (adjusted[i], adjusted[i + 1]);
            if (accrualEnd <= accrualStart)
            {
                return Failed(
                    spec,
                    Failure(
                        ScheduleFailureCode.AdjustedPeriodCollapsed,
                        $"Period {(i + 1).ToString(CultureInfo.InvariantCulture)} [{Format(raw[i].Start)}, {Format(raw[i].End)}] collapses to [{Format(accrualStart)}, {Format(accrualEnd)}] after business-day adjustment.",
                        raw[i].End),
                    evidence.Diagnostics,
                    evidence.Usages);
            }

            DateOnly paymentDate;
            ScheduleFailure? paymentFailure;
            var paid = spec.PaymentLagBusinessDays == 0
                ? evidence.TryAdjust(payment, accrualEnd, ScheduleCalendarPurpose.Payment, out paymentDate, out paymentFailure)
                : evidence.TryLag(payment.Calendar!, accrualEnd, spec.PaymentLagBusinessDays, out paymentDate, out paymentFailure);
            if (!paid)
            {
                return Failed(spec, paymentFailure!, evidence.Diagnostics, evidence.Usages);
            }

            periods[i] = new SchedulePeriod(
                i + 1,
                raw[i].Start,
                raw[i].End,
                accrualStart,
                accrualEnd,
                paymentDate,
                raw[i].Kind,
                raw[i].References,
                IsFinal: i == count - 1);
        }

        return new ScheduleGenerationResult(spec, periods, null, evidence.Diagnostics, evidence.Usages);
    }

    // Records which calendars were used for what, and every date a calendar was asked about outside
    // its coverage (once per calendar and date). Generation still succeeds past the coverage edge.
    private sealed class CalendarEvidence
    {
        private readonly Dictionary<IBusinessDayCalendar, ObservedCalendar> _observed = new(ReferenceEqualityComparer.Instance);
        private readonly HashSet<ScheduleCalendarUsage> _usageSet = [];
        private readonly HashSet<(string CalendarId, DateOnly Date)> _uncovered = [];

        public List<ScheduleDiagnostic> Diagnostics { get; } = [];

        public List<ScheduleCalendarUsage> Usages { get; } = [];

        public bool TryAdjust(
            BusinessDayAdjustment adjustment,
            DateOnly date,
            ScheduleCalendarPurpose purpose,
            out DateOnly result,
            [System.Diagnostics.CodeAnalysis.NotNullWhen(false)] out ScheduleFailure? failure)
        {
            failure = null;
            if (adjustment.IsUnadjusted)
            {
                result = date;
                return true;
            }

            var calendar = Use(adjustment.Calendar!, purpose);
            if (BusinessDayCalendars.TryAdjust(calendar, date, adjustment.Convention, out result, out var error))
            {
                return true;
            }

            failure = Failure(ScheduleFailureCode.BusinessDayAdjustmentFailed, error!, date);
            return false;
        }

        public bool TryLag(
            IBusinessDayCalendar calendar,
            DateOnly date,
            int businessDays,
            out DateOnly result,
            [System.Diagnostics.CodeAnalysis.NotNullWhen(false)] out ScheduleFailure? failure)
        {
            failure = null;
            if (BusinessDayCalendars.TryAddBusinessDays(Use(calendar, ScheduleCalendarPurpose.Payment), date, businessDays, out result, out var error))
            {
                return true;
            }

            failure = Failure(ScheduleFailureCode.BusinessDayAdjustmentFailed, error!, date);
            return false;
        }

        private ObservedCalendar Use(IBusinessDayCalendar calendar, ScheduleCalendarPurpose purpose)
        {
            var usage = new ScheduleCalendarUsage(calendar.CalendarId, calendar.ContentHash, purpose);
            if (_usageSet.Add(usage))
            {
                Usages.Add(usage);
            }

            if (!_observed.TryGetValue(calendar, out var observed))
            {
                observed = new ObservedCalendar(calendar, this);
                _observed.Add(calendar, observed);
            }

            return observed;
        }

        private void Asked(IBusinessDayCalendar calendar, DateOnly date)
        {
            if (!calendar.Coverage.Covers(date) && _uncovered.Add((calendar.CalendarId, date)))
            {
                Diagnostics.Add(new ScheduleDiagnostic(
                    ScheduleDiagnosticCode.CalendarCoverageExceeded,
                    calendar.CalendarId,
                    date,
                    $"Calendar '{calendar.CalendarId}' was asked about {Format(date)}, outside its coverage; a holiday on that date may be missing."));
            }
        }

        private sealed class ObservedCalendar(IBusinessDayCalendar inner, CalendarEvidence evidence) : IBusinessDayCalendar
        {
            public string CalendarId => inner.CalendarId;

            public string ContentHash => inner.ContentHash;

            public CalendarCoverage Coverage => inner.Coverage;

            public bool IsBusinessDay(DateOnly date)
            {
                evidence.Asked(inner, date);
                return inner.IsBusinessDay(date);
            }
        }
    }
}
