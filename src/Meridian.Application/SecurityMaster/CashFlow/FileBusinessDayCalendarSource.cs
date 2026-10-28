using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
using Meridian.Contracts.Calendars;

namespace Meridian.Application.SecurityMaster.CashFlow;

// Loaded once per host. Editing a file never changes an already-running source's answers.
internal sealed class FileBusinessDayCalendarSource : IBusinessDayCalendarSource
{
    private readonly StaticBusinessDayCalendarSource _source;

    private FileBusinessDayCalendarSource(IEnumerable<IBusinessDayCalendar> calendars)
        => _source = new StaticBusinessDayCalendarSource(calendars);

    internal static IBusinessDayCalendarSource Load(string? file)
    {
        if (string.IsNullOrWhiteSpace(file))
            return new FileBusinessDayCalendarSource([BusinessDayCalendars.WeekendsOnly]);
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            Converters = { new JsonStringEnumConverter<DayOfWeek>(allowIntegerValues: false) }
        };
        var definitions = JsonSerializer.Deserialize<Definition[]>(File.ReadAllText(file), options)
            ?? throw new InvalidDataException("The business-day calendar file must contain an array.");
        var calendars = new List<IBusinessDayCalendar> { BusinessDayCalendars.WeekendsOnly };
        foreach (var definition in definitions)
        {
            if (definition is null || definition.Holidays is null || definition.CoverageFrom > definition.CoverageTo)
                throw new InvalidDataException("Invalid business-day calendar definition or coverage.");
            if (definition.Holidays.Any(date => date < definition.CoverageFrom || date > definition.CoverageTo))
                throw new InvalidDataException("A holiday lies outside its declared calendar coverage.");
            calendars.Add(new HolidayBusinessDayCalendar(definition.CalendarId, definition.Version,
                definition.Holidays, definition.WeekendDays,
                new CalendarCoverage(definition.CoverageFrom, definition.CoverageTo)));
        }
        return new FileBusinessDayCalendarSource(calendars);
    }

    public bool TryGet(string calendarId, [NotNullWhen(true)] out IBusinessDayCalendar? calendar)
        => _source.TryGet(calendarId, out calendar);

    public bool TryResolve(IReadOnlyList<string> calendarIds, [NotNullWhen(true)] out IBusinessDayCalendar? calendar)
        => _source.TryResolve(calendarIds, out calendar);

    private sealed record Definition(
        [property: JsonRequired] string CalendarId,
        [property: JsonRequired] int Version,
        [property: JsonRequired] DateOnly[] Holidays,
        [property: JsonRequired] DateOnly CoverageFrom,
        [property: JsonRequired] DateOnly CoverageTo,
        DayOfWeek[]? WeekendDays = null);
}
