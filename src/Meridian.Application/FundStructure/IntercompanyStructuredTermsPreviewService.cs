using Meridian.Contracts.Calendars;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.SecurityMaster.Schedules;

namespace Meridian.Application.FundStructure;

/// <summary>Pure shared date preview. Declared terms never fetch fixings, calculate charges or authorize journals.</summary>
public sealed class IntercompanyStructuredTermsPreviewService(IBusinessDayCalendarSource calendars)
    : IIntercompanyStructuredTermsPreviewService
{
    public IntercompanyStructuredTermsPreviewDto Preview(IntercompanyAgreementDraftDto draft, string subjectHash)
    {
        ArgumentNullException.ThrowIfNull(draft);
        var terms = draft.Terms?.StructuredTerms;
        var schedule = terms?.Schedule;
        var format = terms is null ? "Unstructured" : "StructuredV1";
        IntercompanyStructuredTermsPreviewDto Unavailable(string reason, string? calendarHash = null)
            => new("Unavailable", format, subjectHash, ScheduleGenerator.KernelVersion,
                schedule?.CalendarId, calendarHash, [], [reason]);
        if (terms is null)
            return Unavailable("This retained revision has unstructured legal terms; no dates were inferred from prose.");
        if (draft.Action == IntercompanyAgreementActionDto.Terminate || draft.AgreementType == IntercompanyAgreementTypeDto.Guarantee)
            return Unavailable("Termination and guarantee disclosure records do not generate obligation schedules.");
        try
        {
            IntercompanyAgreementRules.ValidateComplete(draft);
            if (schedule is null)
                return Unavailable("A declared schedule is required.");
            if (!calendars.TryGet(schedule.CalendarId, out var calendar))
                return Unavailable("The declared business-day calendar is unavailable; no fallback was applied.");
            var spec = BuildSpec(schedule, calendar);
            var result = ScheduleGenerator.Generate(spec);
            if (!result.Succeeded)
                return Unavailable(result.Failure.Message, calendar.ContentHash);
            if (result.Diagnostics.Count > 0)
                return new("Unavailable", format, subjectHash, result.KernelVersion, calendar.CalendarId,
                    calendar.ContentHash, [], result.Diagnostics.Select(item => item.Message).ToArray());
            if (terms.DayCount is { } declaredDayCount)
            {
                var convention = Enum.Parse<DayCountConvention>(declaredDayCount.ToString());
                foreach (var period in result.Periods)
                    if (!ScheduleDayCount.TryPeriodFraction(convention, period, result, out _, out var refusal))
                        return Unavailable(refusal, calendar.ContentHash);
            }
            return new("Ready", format, subjectHash, result.KernelVersion, calendar.CalendarId, calendar.ContentHash,
                result.Periods.Select(period => new IntercompanyDatePeriodDto(period.Sequence, period.AccrualStart,
                    period.AccrualEnd, period.PaymentDate)).ToArray(),
                ["Dates only. Rates, fixings, allocation drivers, charge amounts, ledger due dates and postings are not calculated."]);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or InvalidDataException)
        {
            return Unavailable(ex.Message);
        }
    }

    /// <summary>The one mapping from a declared schedule to the shared kernel; date and amount previews share it.</summary>
    internal static ScheduleSpec BuildSpec(IntercompanyScheduleDto schedule, IBusinessDayCalendar calendar)
    {
        var tenor = schedule.Frequency switch
        {
            IntercompanyFrequencyDto.Monthly => ScheduleTenor.Months(1),
            IntercompanyFrequencyDto.Quarterly => ScheduleTenor.Months(3),
            IntercompanyFrequencyDto.SemiAnnual => ScheduleTenor.Months(6),
            IntercompanyFrequencyDto.Annual => ScheduleTenor.Years(1),
            IntercompanyFrequencyDto.AtMaturity => ScheduleTenor.Term,
            _ => throw new IntercompanyAgreementValidationException("Unsupported schedule frequency.")
        };
        return new ScheduleSpec(schedule.StartDate, schedule.EndDate, tenor,
            PaymentAdjustment: BusinessDayAdjustment.Of(schedule.BusinessDayConvention, calendar),
            PaymentLagBusinessDays: schedule.SettlementBusinessDays);
    }
}
