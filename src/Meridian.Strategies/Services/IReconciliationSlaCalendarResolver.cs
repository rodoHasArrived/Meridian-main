using Meridian.Contracts.Workstation;

namespace Meridian.Strategies.Services;

public interface IReconciliationSlaCalendar
{
    bool IsBusinessDay(DateOnly date);
}

public interface IReconciliationSlaCalendarResolver
{
    IReconciliationSlaCalendar Resolve(ReconciliationSlaPolicy policy);
}

public sealed class WeekdayReconciliationSlaCalendarResolver : IReconciliationSlaCalendarResolver, IReconciliationSlaCalendar
{
    public static WeekdayReconciliationSlaCalendarResolver Instance { get; } = new();
    public IReconciliationSlaCalendar Resolve(ReconciliationSlaPolicy policy)
    {
        if (policy.BusinessCalendarId is not null || policy.HolidayCalendarIds?.Count > 0)
            throw new InvalidOperationException("Named SLA calendars require a configured calendar resolver.");
        return this;
    }
    public bool IsBusinessDay(DateOnly date) => date.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday);
}
