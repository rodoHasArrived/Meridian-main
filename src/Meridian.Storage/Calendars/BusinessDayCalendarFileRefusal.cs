namespace Meridian.Storage.Calendars;

/// <summary>
/// A calendar file <see cref="FileBusinessDayCalendarSource"/> refused at load. The calendar it names
/// stays unresolvable; nothing is substituted for it.
/// </summary>
/// <param name="FileName">The refused file's name, without its directory.</param>
/// <param name="Reason">Why the file was refused.</param>
public sealed record BusinessDayCalendarFileRefusal(string FileName, string Reason);
