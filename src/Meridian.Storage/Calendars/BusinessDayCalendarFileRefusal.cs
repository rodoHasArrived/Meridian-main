namespace Meridian.Storage.Calendars;

/// <summary>
/// A calendar file <see cref="FileBusinessDayCalendarSource"/> refused at load. Nothing from it is
/// loaded and nothing is substituted for it; the id it names resolves only from another file that
/// loads, or as the built-in <c>WEEKENDS</c>.
/// </summary>
/// <param name="FileName">The refused file's name, without its directory.</param>
/// <param name="Reason">Why the file was refused.</param>
public sealed record BusinessDayCalendarFileRefusal(string FileName, string Reason);
