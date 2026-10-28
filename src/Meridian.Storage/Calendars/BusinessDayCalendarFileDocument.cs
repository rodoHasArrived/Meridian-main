using System.Text.Json.Serialization;

namespace Meridian.Storage.Calendars;

/// <summary>
/// One business-day calendar file, <c>&lt;dataRoot&gt;/calendars/{ID}.json</c>, as written by the data
/// owner. Every member is nullable so a missing member reads as null and is refused by
/// <see cref="FileBusinessDayCalendarSource"/> with a reason, rather than defaulting.
/// </summary>
/// <param name="CalendarId">The canonical calendar id; must equal the file name stem exactly.</param>
/// <param name="Version">The calendar data version, at least 1; bumped on any change to the file.</param>
/// <param name="WeekendDays">Weekend days as exact English day names (<c>Saturday</c>, <c>Sunday</c>).</param>
/// <param name="Coverage">The data owner's attestation that the holiday list is complete over this range.</param>
/// <param name="Holidays">Holiday dates as ISO <c>yyyy-MM-dd</c> strings.</param>
internal sealed record BusinessDayCalendarFileDocument(
    string? CalendarId,
    int? Version,
    IReadOnlyList<string>? WeekendDays,
    BusinessDayCalendarFileCoverage? Coverage,
    IReadOnlyList<DateOnly>? Holidays);

/// <summary>The inclusive range a calendar file attests its holiday list is complete for.</summary>
/// <param name="From">The first covered date.</param>
/// <param name="To">The last covered date.</param>
internal sealed record BusinessDayCalendarFileCoverage(DateOnly? From, DateOnly? To);

/// <summary>
/// Strict reader for calendar files: camelCase members matched exactly, and an unknown or repeated
/// member fails the read, so a misspelt key cannot silently drop data from a calendar.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    AllowDuplicateProperties = false,
    GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(BusinessDayCalendarFileDocument))]
internal sealed partial class BusinessDayCalendarFileJsonContext : JsonSerializerContext;
