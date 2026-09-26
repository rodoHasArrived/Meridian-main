using System.Text.Json;
using System.Text.Json.Serialization;
using Meridian.Contracts.Workstation;
using Meridian.FinancialOperations.Reconciliation;
using Meridian.Strategies.Services;

namespace Meridian.Ui.Shared.Services;

/// <summary>One host-lifetime snapshot of operator-declared calendars; named calendars never fall back.</summary>
public sealed class FileReconciliationSlaCalendarResolver : IReconciliationSlaCalendarResolver
{
    public const string RelativePath = "reconciliation/sla-calendars.json";
    private readonly Dictionary<string, CoveredCalendar> _business = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, CoveredCalendar> _holidays = new(StringComparer.OrdinalIgnoreCase);

    public FileReconciliationSlaCalendarResolver(string dataDirectory)
    {
        var path = Path.Combine(dataDirectory, "reconciliation", "sla-calendars.json");
        if (!File.Exists(path))
            return;
        using var stream = File.OpenRead(path);
        if (stream.Length > 4 * 1024 * 1024)
            throw new InvalidDataException("SLA calendar configuration exceeds 4 MiB.");
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            Converters = { new JsonStringEnumConverter() },
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
        };
        var file = JsonSerializer.Deserialize<CalendarFile>(stream, options)
            ?? throw new InvalidDataException("SLA calendars must be a JSON object.");
        foreach (var definition in file.BusinessCalendars ?? [])
            Add(_business, definition, definition?.WeekendDays);
        foreach (var definition in file.HolidayCalendars ?? [])
            Add(_holidays, definition, []);
    }

    private static void Add(Dictionary<string, CoveredCalendar> target, CalendarDefinition? definition, DayOfWeek[]? weekend)
    {
        if (definition is null || string.IsNullOrWhiteSpace(definition.Id) || definition.Id != definition.Id.Trim()
            || definition.ValidFrom is not { } from || definition.ValidThrough is not { } through || from > through
            || definition.Holidays?.Any(day => day < from || day > through) == true)
            throw new InvalidDataException("SLA calendars require an exact ID, inclusive validity bounds, and in-range holidays.");
        var calendar = new CoveredCalendar(definition.Id, from, through,
            new BusinessDayAccountingCalendar(definition.Holidays, weekend));
        if (!target.TryAdd(definition.Id, calendar))
            throw new InvalidDataException("Duplicate SLA calendar ID: " + definition.Id);
    }

    public IReconciliationSlaCalendar Resolve(ReconciliationSlaPolicy policy)
    {
        var calendars = new List<IReconciliationSlaCalendar>
        {
            policy.BusinessCalendarId is null ? WeekdayReconciliationSlaCalendarResolver.Instance
                : Find(_business, policy.BusinessCalendarId)
        };
        foreach (var id in policy.HolidayCalendarIds ?? [])
            calendars.Add(Find(_holidays, id));
        return new CombinedCalendar(calendars);
    }

    private static CoveredCalendar Find(Dictionary<string, CoveredCalendar> source, string id)
        => source.TryGetValue(id, out var calendar) ? calendar
            : throw new InvalidDataException("Unknown SLA calendar: " + id);

    private sealed record CoveredCalendar(string Id, DateOnly From, DateOnly Through, BusinessDayAccountingCalendar Calendar) : IReconciliationSlaCalendar
    {
        public bool IsBusinessDay(DateOnly date)
        {
            if (date < From || date > Through)
                throw new InvalidDataException($"SLA calendar '{Id}' does not cover {date:yyyy-MM-dd}.");
            return Calendar.IsBusinessDay(date);
        }
    }

    private sealed record CombinedCalendar(IReadOnlyList<IReconciliationSlaCalendar> Calendars) : IReconciliationSlaCalendar
    {
        public bool IsBusinessDay(DateOnly date)
        {
            var result = true;
            // Evaluate every selected calendar, including overlays on closed base days, so missing
            // coverage cannot be hidden by short-circuiting.
            foreach (var calendar in Calendars)
                result &= calendar.IsBusinessDay(date);
            return result;
        }
    }

    private sealed record CalendarFile(CalendarDefinition[]? BusinessCalendars, CalendarDefinition[]? HolidayCalendars);
    private sealed record CalendarDefinition(string Id, DateOnly? ValidFrom, DateOnly? ValidThrough,
        DateOnly[]? Holidays, DayOfWeek[]? WeekendDays);
}
