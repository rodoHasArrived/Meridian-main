using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json;
using Meridian.Contracts.Calendars;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meridian.Storage.Calendars;

/// <summary>
/// The business-day calendars held as files under <c>&lt;dataRoot&gt;/calendars</c>, one calendar per
/// <c>{ID}.json</c> file, loaded once (there is no hot reload) plus the built-in
/// <see cref="BusinessDayCalendars.WeekendsOnly"/> calendar (ADR-025).
/// </summary>
/// <remarks>
/// <para>
/// A file is refused, with a Warning naming the file and the reason, when its JSON is malformed or
/// has an unknown or repeated member, a member is missing, the version is below 1, the id is not
/// canonical (<c>^[A-Z][A-Z0-9]{1,15}$</c>) or not ordinally equal to the file name stem, the id is
/// the reserved <c>WEEKENDS</c>, a weekend day is not exactly one of the seven English day names,
/// every day is a weekend day, coverage starts after it ends, <see cref="HolidayBusinessDayCalendar"/>
/// rejects the data, or another file that passes these checks defines the same id (both are refused).
/// </para>
/// <para>
/// There is no fallback: a refused file loads nothing, an id no loaded file defines stays
/// unresolvable and is never replaced by weekends only, and loading never throws for file content.
/// A refused file does not block an id another file loads; its Warning names that file, and any other
/// id the refused file names (its calendarId and its file name stem can differ) that stays
/// unresolvable. A missing directory loads only <c>WEEKENDS</c>; a <c>calendars</c> path that is a
/// file, or a directory that cannot be listed, does too, with a Warning.
/// </para>
/// <para>
/// Ids are canonical because the content hash includes the id as written and joint ids sort their
/// members ordinally, so nodes holding <c>usny</c> and <c>USNY</c> would otherwise report different
/// hashes and joint ids for the same data.
/// </para>
/// </remarks>
public sealed class FileBusinessDayCalendarSource : IBusinessDayCalendarSource
{
    /// <summary>The directory under the data root that holds calendar files.</summary>
    public const string DirectoryName = "calendars";

    private const string FileExtension = ".json";
    private const int MaxCalendarIdLength = 16;

    private static readonly IReadOnlyDictionary<string, DayOfWeek> DayNames = new Dictionary<string, DayOfWeek>(StringComparer.Ordinal)
    {
        ["Monday"] = DayOfWeek.Monday,
        ["Tuesday"] = DayOfWeek.Tuesday,
        ["Wednesday"] = DayOfWeek.Wednesday,
        ["Thursday"] = DayOfWeek.Thursday,
        ["Friday"] = DayOfWeek.Friday,
        ["Saturday"] = DayOfWeek.Saturday,
        ["Sunday"] = DayOfWeek.Sunday
    };

    private readonly StaticBusinessDayCalendarSource _calendars;

    private FileBusinessDayCalendarSource(
        string calendarDirectory,
        IReadOnlyList<HolidayBusinessDayCalendar> calendars,
        IReadOnlyList<BusinessDayCalendarFileRefusal> refusedFiles)
    {
        CalendarDirectory = calendarDirectory;
        Calendars = calendars;
        RefusedFiles = refusedFiles;
        _calendars = new StaticBusinessDayCalendarSource([BusinessDayCalendars.WeekendsOnly, .. calendars]);
    }

    /// <summary>The directory the calendars were read from: <c>&lt;dataRoot&gt;/calendars</c>.</summary>
    public string CalendarDirectory { get; }

    /// <summary>
    /// The calendars loaded from files, sorted by ordinal id. The built-in <c>WEEKENDS</c> calendar is
    /// not listed here but always resolves.
    /// </summary>
    public IReadOnlyList<HolidayBusinessDayCalendar> Calendars { get; }

    /// <summary>The refused calendar files, in ordinal file-name order.</summary>
    public IReadOnlyList<BusinessDayCalendarFileRefusal> RefusedFiles { get; }

    /// <summary>
    /// Reads every top-level <c>*.json</c> file under <c>&lt;dataRoot&gt;/calendars</c> once, in ordinal
    /// file-name order, logging one Information line per loaded calendar (id, version, hash and
    /// coverage) and one Warning per refused file. Subdirectories and other files are ignored.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="dataRoot"/> is null or blank.</exception>
    public static FileBusinessDayCalendarSource Load(string dataRoot, ILogger<FileBusinessDayCalendarSource>? logger = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataRoot);
        var log = (ILogger?)logger ?? NullLogger.Instance;
        var directory = Path.Combine(dataRoot, DirectoryName);

        if (File.Exists(directory))
        {
            log.LogWarning(
                "Business-day calendar path {CalendarDirectory} is a file, not a directory; only the built-in {WeekendsCalendarId} calendar is loaded.",
                directory,
                BusinessDayCalendars.WeekendsOnlyId);
            return new FileBusinessDayCalendarSource(directory, [], []);
        }

        if (!Directory.Exists(directory))
        {
            return new FileBusinessDayCalendarSource(directory, [], []);
        }

        string[] files;
        try
        {
            files = Directory
                .EnumerateFiles(directory, "*" + FileExtension, FileEnumerationOptions)
                .OrderBy(static file => Path.GetFileName(file), StringComparer.Ordinal)
                .ToArray();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            log.LogWarning(
                ex,
                "Business-day calendar directory {CalendarDirectory} could not be listed; only the built-in {WeekendsCalendarId} calendar is loaded.",
                directory,
                BusinessDayCalendars.WeekendsOnlyId);
            return new FileBusinessDayCalendarSource(directory, [], []);
        }

        var outcomes = files.Select(ReadFile).ToArray();

        // An exact stem and an uppercase grammar make two files with one id impossible on a
        // case-sensitive file system except through the extension's case; refuse both defensively.
        var duplicateIds = outcomes
            .Where(static outcome => outcome.Calendar is not null)
            .GroupBy(static outcome => outcome.Calendar!.CalendarId, StringComparer.OrdinalIgnoreCase)
            .Where(static group => group.Count() > 1)
            .ToDictionary(
                static group => group.Key,
                static group => group.Select(static outcome => outcome.FileName).ToArray(),
                StringComparer.OrdinalIgnoreCase);

        // The calendars that load, by id ignoring case as lookups resolve them, so a refused file's
        // Warning can say whether the ids it names still resolve from another file.
        var loaded = outcomes
            .Where(outcome => outcome.Calendar is { } calendar && !duplicateIds.ContainsKey(calendar.CalendarId))
            .ToDictionary(static outcome => outcome.Calendar!.CalendarId, StringComparer.OrdinalIgnoreCase);

        var calendars = new List<HolidayBusinessDayCalendar>();
        var refused = new List<BusinessDayCalendarFileRefusal>();
        foreach (var outcome in outcomes)
        {
            var reason = outcome.Reason;
            if (outcome.Calendar is { } candidate && duplicateIds.TryGetValue(candidate.CalendarId, out var sharing))
            {
                reason = $"calendar id '{candidate.CalendarId}' is defined by more than one file ({string.Join(", ", sharing)}).";
            }

            if (reason is not null)
            {
                refused.Add(new BusinessDayCalendarFileRefusal(outcome.FileName, reason));
                LogRefusal(log, outcome, reason, loaded);
                continue;
            }

            var calendar = outcome.Calendar!;
            calendars.Add(calendar);
            log.LogInformation(
                "Loaded business-day calendar {CalendarId} version {CalendarVersion} hash {CalendarContentHash} coverage {CoverageFrom} to {CoverageTo} from {CalendarFile}",
                calendar.CalendarId,
                calendar.Version,
                calendar.ContentHash,
                FormatDate(calendar.Coverage.From),
                FormatDate(calendar.Coverage.To),
                outcome.FilePath);
        }

        return new FileBusinessDayCalendarSource(
            directory,
            calendars.OrderBy(static calendar => calendar.CalendarId, StringComparer.Ordinal).ToArray(),
            refused);
    }

    /// <inheritdoc />
    public bool TryGet(string calendarId, [NotNullWhen(true)] out IBusinessDayCalendar? calendar)
        => _calendars.TryGet(calendarId, out calendar);

    /// <inheritdoc />
    public bool TryResolve(IReadOnlyList<string> calendarIds, [NotNullWhen(true)] out IBusinessDayCalendar? calendar)
        => _calendars.TryResolve(calendarIds, out calendar);

    // The calendar directory listing: top-level files only, matched ignoring case, hidden and system
    // files included, and access denied raised rather than read as an empty directory.
    internal static EnumerationOptions FileEnumerationOptions => new()
    {
        MatchCasing = MatchCasing.CaseInsensitive,
        MatchType = MatchType.Simple,
        RecurseSubdirectories = false,
        AttributesToSkip = 0,
        IgnoreInaccessible = false
    };

    // One Warning per refused file, saying whether the ids it names (its calendarId, then its file name
    // stem when some casing of it is a calendar id) still resolve, from another file or the built-in
    // calendar, or stay unresolvable. A calendarId and a stem that name different ids can split, one
    // still resolving while the other stays blocked, and the Warning then names both.
    private static void LogRefusal(
        ILogger log,
        FileOutcome outcome,
        string reason,
        IReadOnlyDictionary<string, FileOutcome> loaded)
    {
        var named = new List<string>(2);
        if (outcome.CalendarId?.Trim() is { Length: > 0 } calendarId)
        {
            named.Add(calendarId);
        }

        // A stem such as "ZZTEST - Copy" is no calendar id in any casing, so no record can name it.
        var stem = Path.GetFileNameWithoutExtension(outcome.FileName).Trim();
        if (IsCanonicalCalendarId(stem.ToUpperInvariant()) && !named.Contains(stem, StringComparer.OrdinalIgnoreCase))
        {
            named.Add(stem);
        }

        // The first named id that resolves (from a loaded file, or to the built-in calendar when its
        // file is null), and the first that does not.
        (string CalendarId, string? LoadedFile)? resolves = null;
        string? unresolvable = null;
        foreach (var id in named)
        {
            if (loaded.TryGetValue(id, out var other))
            {
                resolves ??= (other.Calendar!.CalendarId, other.FilePath);
            }
            else if (string.Equals(id, BusinessDayCalendars.WeekendsOnlyId, StringComparison.OrdinalIgnoreCase))
            {
                resolves ??= (BusinessDayCalendars.WeekendsOnlyId, null);
            }
            else
            {
                unresolvable ??= id;
            }
        }

        if (resolves is not { } still)
        {
            log.LogWarning(
                "Refused business-day calendar file {CalendarFile}: {RefusalReason} The calendar it names stays unresolvable; no weekends-only calendar is substituted.",
                outcome.FilePath,
                reason);
        }
        else if (still.LoadedFile is { } loadedFile && unresolvable is null)
        {
            log.LogWarning(
                "Refused business-day calendar file {CalendarFile}: {RefusalReason} Nothing from this file is loaded; calendar {CalendarId} still resolves, from {LoadedCalendarFile}.",
                outcome.FilePath,
                reason,
                still.CalendarId,
                loadedFile);
        }
        else if (still.LoadedFile is { } splitLoadedFile)
        {
            log.LogWarning(
                "Refused business-day calendar file {CalendarFile}: {RefusalReason} Nothing from this file is loaded; calendar {CalendarId} still resolves, from {LoadedCalendarFile}, but calendar {UnresolvableCalendarId} stays unresolvable; no weekends-only calendar is substituted.",
                outcome.FilePath,
                reason,
                still.CalendarId,
                splitLoadedFile,
                unresolvable);
        }
        else if (unresolvable is null)
        {
            log.LogWarning(
                "Refused business-day calendar file {CalendarFile}: {RefusalReason} Nothing from this file is loaded; calendar {CalendarId} still resolves, to the built-in weekends-only calendar.",
                outcome.FilePath,
                reason,
                still.CalendarId);
        }
        else
        {
            log.LogWarning(
                "Refused business-day calendar file {CalendarFile}: {RefusalReason} Nothing from this file is loaded; calendar {CalendarId} still resolves, to the built-in weekends-only calendar, but calendar {UnresolvableCalendarId} stays unresolvable; no weekends-only calendar is substituted.",
                outcome.FilePath,
                reason,
                still.CalendarId,
                unresolvable);
        }
    }

    private static FileOutcome ReadFile(string path)
    {
        var fileName = Path.GetFileName(path);
        BusinessDayCalendarFileDocument? document;
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            document = JsonSerializer.Deserialize(stream, BusinessDayCalendarFileJsonContext.Default.BusinessDayCalendarFileDocument);
        }
        catch (JsonException ex)
        {
            return FileOutcome.Refused(path, fileName, "the file is not a valid calendar document: " + ex.Message);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return FileOutcome.Refused(path, fileName, "the file could not be read: " + ex.Message);
        }

        var reason = Validate(document, Path.GetFileNameWithoutExtension(fileName), out var weekendDays);
        if (reason is not null)
        {
            return FileOutcome.Refused(path, fileName, reason, document?.CalendarId);
        }

        try
        {
            return new FileOutcome(path, fileName, new HolidayBusinessDayCalendar(
                document!.CalendarId!,
                document.Version!.Value,
                document.Holidays,
                weekendDays,
                new CalendarCoverage(document.Coverage!.From, document.Coverage.To)), null);
        }
        catch (ArgumentException ex)
        {
            return FileOutcome.Refused(path, fileName, "the calendar data is invalid: " + ex.Message, document?.CalendarId);
        }
    }

    private static string? Validate(BusinessDayCalendarFileDocument? document, string stem, out DayOfWeek[] weekendDays)
    {
        weekendDays = [];
        if (document is null)
        {
            return "the file holds no calendar document.";
        }

        var missing = new List<string>();
        if (document.CalendarId is null)
            missing.Add("calendarId");
        if (document.Version is null)
            missing.Add("version");
        if (document.WeekendDays is null)
            missing.Add("weekendDays");
        if (document.Coverage is null)
            missing.Add("coverage");
        if (document.Coverage is { From: null })
            missing.Add("coverage.from");
        if (document.Coverage is { To: null })
            missing.Add("coverage.to");
        if (document.Holidays is null)
            missing.Add("holidays");
        if (missing.Count > 0)
        {
            return "required member(s) missing: " + string.Join(", ", missing) + ".";
        }

        var calendarId = document.CalendarId!;
        if (document.Version!.Value < 1)
        {
            return string.Create(CultureInfo.InvariantCulture, $"version must be at least 1 (found {document.Version.Value}).");
        }

        if (!IsCanonicalCalendarId(calendarId))
        {
            return $"calendarId '{calendarId}' is not a canonical calendar id (an uppercase ASCII letter followed by 1 to 15 uppercase ASCII letters or digits).";
        }

        if (!string.Equals(calendarId, stem, StringComparison.Ordinal))
        {
            return $"calendarId '{calendarId}' does not equal the file name stem '{stem}' exactly.";
        }

        if (string.Equals(calendarId, BusinessDayCalendars.WeekendsOnlyId, StringComparison.Ordinal))
        {
            return $"calendarId '{calendarId}' is reserved for the built-in weekends-only calendar.";
        }

        var days = new List<DayOfWeek>(document.WeekendDays!.Count);
        foreach (var name in document.WeekendDays!)
        {
            if (name is null || !DayNames.TryGetValue(name, out var day))
            {
                return $"weekendDays entry '{name}' is not exactly one of the English day names Monday to Sunday.";
            }

            days.Add(day);
        }

        weekendDays = days.Distinct().ToArray();
        if (weekendDays.Length >= DayNames.Count)
        {
            return "every day of the week is a weekend day; a calendar needs at least one working day.";
        }

        if (document.Coverage!.From!.Value > document.Coverage.To!.Value)
        {
            return $"coverage from {FormatDate(document.Coverage.From)} is after coverage to {FormatDate(document.Coverage.To)}.";
        }

        return null;
    }

    private static bool IsCanonicalCalendarId(string calendarId)
    {
        if (calendarId.Length is < 2 or > MaxCalendarIdLength || calendarId[0] is < 'A' or > 'Z')
        {
            return false;
        }

        foreach (var ch in calendarId)
        {
            if (ch is not ((>= 'A' and <= 'Z') or (>= '0' and <= '9')))
            {
                return false;
            }
        }

        return true;
    }

    private static string FormatDate(DateOnly? date)
        => date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "unbounded";

    // CalendarId is the id a refused file's document names, when it parsed with one.
    private sealed record FileOutcome(
        string FilePath,
        string FileName,
        HolidayBusinessDayCalendar? Calendar,
        string? Reason,
        string? CalendarId = null)
    {
        public static FileOutcome Refused(string path, string fileName, string reason, string? calendarId = null)
            => new(path, fileName, null, reason, calendarId);
    }
}
