using FluentAssertions;
using Meridian.Contracts.Calendars;
using Meridian.Storage.Calendars;
using Meridian.Testing;
using Meridian.Tests.TestHelpers;
using Microsoft.Extensions.Logging;

namespace Meridian.Tests.Calendars;

/// <summary>
/// Pins the file-backed business-day calendar source (ADR-025): <c>&lt;dataRoot&gt;/calendars/{ID}.json</c>,
/// one calendar per file, loaded once with no fallback. Every refused file logs a Warning naming the
/// file and leaves its id unresolvable while the other files still load.
/// </summary>
/// <remarks>
/// Calendars that load use the fictitious ZZ ids with invented dates; ids of real business centres
/// appear only in files the source refuses, so no test presents invented holidays as a real centre's.
/// </remarks>
public sealed class FileBusinessDayCalendarSourceTests : IDisposable
{
    private const string ValidZzTest = """
        { "calendarId": "ZZTEST", "version": 1, "weekendDays": ["Saturday","Sunday"],
          "coverage": { "from": "2026-01-01", "to": "2026-12-31" },
          "holidays": ["2026-03-03", "2026-01-02", "2026-07-15"] }
        """;

    private readonly TestArtifactDirectory _artifacts = TestArtifactDirectory.Create(nameof(FileBusinessDayCalendarSourceTests));

    private static DateOnly D(int year, int month, int day) => new(year, month, day);

    public void Dispose() => _artifacts.Dispose();

    [Fact]
    public void MissingDirectory_LoadsOnlyWeekendsWithoutWarning()
    {
        var logger = new RecordingLogger<FileBusinessDayCalendarSource>();

        var source = FileBusinessDayCalendarSource.Load(_artifacts.RootPath, logger);

        source.CalendarDirectory.Should().Be(Path.Combine(_artifacts.RootPath, "calendars"));
        source.Calendars.Should().BeEmpty();
        source.RefusedFiles.Should().BeEmpty();
        source.TryGet("WEEKENDS", out var weekends).Should().BeTrue();
        weekends.Should().BeSameAs(BusinessDayCalendars.WeekendsOnly);
        source.TryGet("ZZTEST", out _).Should().BeFalse();
        logger.Entries.Should().BeEmpty("a missing calendars directory is the default and is not a warning");
    }

    [Fact]
    public void ValidFile_LoadsWithTheHolidayCalendarHashAndOneInformationLine()
    {
        WriteCalendar("ZZTEST.json", ValidZzTest);
        var logger = new RecordingLogger<FileBusinessDayCalendarSource>();

        var source = FileBusinessDayCalendarSource.Load(_artifacts.RootPath, logger);

        var expected = new HolidayBusinessDayCalendar(
            "ZZTEST",
            1,
            [D(2026, 1, 2), D(2026, 3, 3), D(2026, 7, 15)],
            [DayOfWeek.Saturday, DayOfWeek.Sunday],
            new CalendarCoverage(D(2026, 1, 1), D(2026, 12, 31)));
        var loaded = source.Calendars.Should().ContainSingle().Subject;
        loaded.CalendarId.Should().Be("ZZTEST");
        loaded.Version.Should().Be(1);
        loaded.ContentHash.Should().Be(expected.ContentHash);
        loaded.Coverage.Should().Be(expected.Coverage);
        source.RefusedFiles.Should().BeEmpty();
        source.TryGet("ZZTEST", out var resolved).Should().BeTrue();
        resolved.Should().BeSameAs(loaded);
        resolved!.IsBusinessDay(D(2026, 3, 3)).Should().BeFalse("2026-03-03 is a holiday in the file");
        resolved.IsBusinessDay(D(2026, 3, 4)).Should().BeTrue();

        var line = logger.Entries.Should().ContainSingle().Subject;
        line.LogLevel.Should().Be(LogLevel.Information);
        line.Message.Should().Contain("ZZTEST").And.Contain("version 1").And.Contain(expected.ContentHash)
            .And.Contain("coverage 2026-01-01 to 2026-12-31").And.Contain("ZZTEST.json");
    }

    [Fact]
    public void ValidFile_HashIgnoresHolidayOrderWeekendOrderMemberOrderAndFormatting()
    {
        WriteCalendar("ZZTEST.json", ValidZzTest);
        var first = FileBusinessDayCalendarSource.Load(_artifacts.RootPath).Calendars.Single();

        using var other = TestArtifactDirectory.Create(nameof(FileBusinessDayCalendarSourceTests));
        Directory.CreateDirectory(Path.Combine(other.RootPath, "calendars"));
        File.WriteAllText(Path.Combine(other.RootPath, "calendars", "ZZTEST.json"), """
            {
                "holidays": [ "2026-07-15", "2026-03-03", "2026-01-02", "2026-03-03" ],
                "coverage": { "to": "2026-12-31", "from": "2026-01-01" },
                "weekendDays": [ "Sunday", "Saturday" ],
                "version": 1,
                "calendarId": "ZZTEST"
            }
            """);
        var second = FileBusinessDayCalendarSource.Load(other.RootPath).Calendars.Single();

        second.ContentHash.Should().Be(first.ContentHash);
    }

    public static TheoryData<string, string, string> RefusedFileCases()
    {
        var data = new TheoryData<string, string, string>
        {
            { "ZZBAD.json", """{ "calendarId": "ZZBAD", "version": 1, """, "not a valid calendar document" },
            { "ZZBAD.json", Document("ZZBAD", extra: "\"region\": \"test\""), "region" },
            { "ZZBAD.json", Document("ZZBAD", version: "0"), "version must be at least 1 (found 0)" },
            {
                "ZZBAD.json",
                Document("ZZBAD", weekendDays: """["Monday","Tuesday","Wednesday","Thursday","Friday","Saturday","Sunday"]"""),
                "every day of the week is a weekend day"
            },
            {
                "ZZBAD.json",
                Document("ZZBAD", coverage: """{ "from": "2026-12-31", "to": "2026-01-01" }"""),
                "coverage from 2026-12-31 is after coverage to 2026-01-01"
            },
            { "ZZBAD.json", Document("ZZBAD", holidays: """["01/02/2026"]"""), "not a valid calendar document" },
            { "usny.json", Document("USNY"), "does not equal the file name stem 'usny' exactly" },
            { "usny.json", Document("usny"), "'usny' is not a canonical calendar id" },
            { "US-NY.json", Document("US-NY"), "'US-NY' is not a canonical calendar id" },
            { "USNY+GBLO.json", Document("USNY+GBLO"), "'USNY+GBLO' is not a canonical calendar id" },
            { "Z.json", Document("Z"), "'Z' is not a canonical calendar id" },
            { "ZZTESTABCDEFGHIJK.json", Document("ZZTESTABCDEFGHIJK"), "'ZZTESTABCDEFGHIJK' is not a canonical calendar id" },
            { "9ZTEST.json", Document("9ZTEST"), "'9ZTEST' is not a canonical calendar id" },
            { "WEEKENDS.json", Document("WEEKENDS"), "reserved for the built-in weekends-only calendar" },
            { "ZZBAD.json", Document("ZZBAD", weekendDays: "[6, 0]"), "not a valid calendar document" },
            { "ZZBAD.json", Document("ZZBAD", weekendDays: """["saturday"]"""), "'saturday' is not exactly one of the English day names" },
            { "ZZBAD.json", Document("ZZBAD", weekendDays: """["Sat"]"""), "'Sat' is not exactly one of the English day names" },
            { "ZZBAD.json", "null", "holds no calendar document" },
            { "ZZBAD.json", "[]", "not a valid calendar document" },
            { "ZZBAD.json", Document("ZZBAD", extra: "\"version\": 2"), "not a valid calendar document" }
        };

        foreach (var member in new[] { "calendarId", "version", "weekendDays", "coverage", "holidays" })
        {
            data.Add("ZZBAD.json", Document("ZZBAD", omit: member), "required member(s) missing: " + member + ".");
        }

        data.Add("ZZBAD.json", Document("ZZBAD", coverage: """{ "to": "2026-12-31" }"""), "required member(s) missing: coverage.from.");
        data.Add("ZZBAD.json", Document("ZZBAD", coverage: """{ "from": "2026-01-01" }"""), "required member(s) missing: coverage.to.");
        data.Add("ZZBAD.json", Document("ZZBAD", holidays: "null"), "required member(s) missing: holidays.");
        return data;
    }

    [Theory]
    [MemberData(nameof(RefusedFileCases))]
    public void RefusedFile_LogsAWarningNamingTheFileAndOtherFilesStillLoad(string fileName, string content, string reason)
    {
        WriteCalendar("ZZTEST.json", ValidZzTest);
        WriteCalendar(fileName, content);
        var logger = new RecordingLogger<FileBusinessDayCalendarSource>();

        var source = FileBusinessDayCalendarSource.Load(_artifacts.RootPath, logger);

        source.Calendars.Should().ContainSingle().Which.CalendarId.Should().Be("ZZTEST");
        var refusal = source.RefusedFiles.Should().ContainSingle().Subject;
        refusal.FileName.Should().Be(fileName);
        refusal.Reason.Should().Contain(reason);

        var warning = logger.Entries.Should().ContainSingle(entry => entry.LogLevel == LogLevel.Warning).Subject;
        warning.Message.Should().Contain(fileName).And.Contain(refusal.Reason);
        logger.Entries.Should().ContainSingle(entry => entry.LogLevel == LogLevel.Information)
            .Which.Message.Should().Contain("ZZTEST");

        // No fallback: the refused file's id stays unresolvable, alone or in a joint.
        var stem = Path.GetFileNameWithoutExtension(fileName);
        if (!string.Equals(stem, "WEEKENDS", StringComparison.Ordinal))
        {
            source.TryGet(stem, out _).Should().BeFalse();
            source.TryResolve([stem], out _).Should().BeFalse();
            source.TryResolve(["ZZTEST", stem], out _).Should().BeFalse();
        }
    }

    [Fact]
    public void WeekendsFile_DoesNotReplaceTheBuiltInCalendar()
    {
        WriteCalendar("WEEKENDS.json", Document("WEEKENDS", weekendDays: """["Friday"]"""));

        var source = FileBusinessDayCalendarSource.Load(_artifacts.RootPath);

        source.RefusedFiles.Should().ContainSingle().Which.FileName.Should().Be("WEEKENDS.json");
        source.TryGet("WEEKENDS", out var weekends).Should().BeTrue();
        weekends.Should().BeSameAs(BusinessDayCalendars.WeekendsOnly);
    }

    [Fact]
    public void TwoFilesWithOneId_AreBothRefused()
    {
        WriteCalendar("ZZTEST.json", ValidZzTest);
        var upper = Path.Combine(_artifacts.RootPath, "calendars", "ZZTEST.JSON");
        if (File.Exists(upper))
        {
            // A case-insensitive file system cannot hold both spellings; the exact stem and the
            // uppercase grammar already make a second file with this id impossible there.
            return;
        }

        File.WriteAllText(upper, ValidZzTest);
        var logger = new RecordingLogger<FileBusinessDayCalendarSource>();

        var source = FileBusinessDayCalendarSource.Load(_artifacts.RootPath, logger);

        source.Calendars.Should().BeEmpty();
        source.RefusedFiles.Select(static refusal => refusal.FileName).Should().Equal("ZZTEST.JSON", "ZZTEST.json");
        source.RefusedFiles.Should().OnlyContain(static refusal => refusal.Reason.Contains("defined by more than one file"));
        logger.Entries.Should().HaveCount(2).And.OnlyContain(static entry => entry.LogLevel == LogLevel.Warning);
        source.TryGet("ZZTEST", out _).Should().BeFalse();
    }

    [Fact]
    public void JointResolution_SortsMembersAndNeverFallsBack()
    {
        WriteCalendar("ZZTEST.json", ValidZzTest);
        WriteCalendar("ZZALT.json", Document("ZZALT", holidays: """["2026-03-04"]"""));

        var source = FileBusinessDayCalendarSource.Load(_artifacts.RootPath);

        source.Calendars.Select(static calendar => calendar.CalendarId).Should().Equal("ZZALT", "ZZTEST");
        source.TryResolve(["ZZTEST", "ZZALT"], out var joint).Should().BeTrue();
        joint!.CalendarId.Should().Be("ZZALT+ZZTEST");
        joint.IsBusinessDay(D(2026, 3, 3)).Should().BeFalse("a ZZTEST holiday");
        joint.IsBusinessDay(D(2026, 3, 4)).Should().BeFalse("a ZZALT holiday");
        joint.IsBusinessDay(D(2026, 3, 5)).Should().BeTrue();

        source.TryResolve([], out _).Should().BeFalse();
        source.TryResolve(["MISSING"], out _).Should().BeFalse();
        source.TryResolve(["ZZTEST", "MISSING"], out _).Should().BeFalse();
    }

    [Fact]
    public void NonJsonFilesAndSubdirectories_AreIgnored()
    {
        WriteCalendar("ZZTEST.json", ValidZzTest);
        WriteCalendar("README.txt", "not a calendar");
        WriteCalendar("ZZTEST.json.bak", "{");
        var nested = Path.Combine(_artifacts.RootPath, "calendars", "archive");
        Directory.CreateDirectory(nested);
        File.WriteAllText(Path.Combine(nested, "ZZOLD.json"), Document("ZZOLD"));
        Directory.CreateDirectory(Path.Combine(_artifacts.RootPath, "calendars", "ZZDIR.json"));
        var logger = new RecordingLogger<FileBusinessDayCalendarSource>();

        var source = FileBusinessDayCalendarSource.Load(_artifacts.RootPath, logger);

        source.Calendars.Should().ContainSingle().Which.CalendarId.Should().Be("ZZTEST");
        source.RefusedFiles.Should().BeEmpty();
        source.TryGet("ZZOLD", out _).Should().BeFalse();
        logger.Entries.Should().NotContain(static entry => entry.LogLevel == LogLevel.Warning);
    }

    [Fact]
    public void CalendarsPathThatIsAFile_LoadsOnlyWeekendsWithAWarning()
    {
        File.WriteAllText(Path.Combine(_artifacts.RootPath, "calendars"), ValidZzTest);
        var logger = new RecordingLogger<FileBusinessDayCalendarSource>();

        var source = FileBusinessDayCalendarSource.Load(_artifacts.RootPath, logger);

        source.Calendars.Should().BeEmpty();
        source.TryGet("WEEKENDS", out _).Should().BeTrue();
        source.TryGet("ZZTEST", out _).Should().BeFalse();
        logger.Entries.Should().ContainSingle().Which.LogLevel.Should().Be(LogLevel.Warning);
        logger.Entries[0].Message.Should().Contain("is a file, not a directory");
    }

    private void WriteCalendar(string fileName, string content)
    {
        var directory = Path.Combine(_artifacts.RootPath, "calendars");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, fileName), content);
    }

    /// <summary>
    /// A calendar document with every member present unless <paramref name="omit"/> names one; the
    /// default body is weekends Saturday and Sunday, 2026 coverage and no holidays.
    /// </summary>
    private static string Document(
        string calendarId,
        string version = "1",
        string weekendDays = """["Saturday","Sunday"]""",
        string coverage = """{ "from": "2026-01-01", "to": "2026-12-31" }""",
        string holidays = "[]",
        string? omit = null,
        string? extra = null)
    {
        var members = new List<string>();
        if (omit != "calendarId")
            members.Add($"\"calendarId\": \"{calendarId}\"");
        if (omit != "version")
            members.Add($"\"version\": {version}");
        if (omit != "weekendDays")
            members.Add($"\"weekendDays\": {weekendDays}");
        if (omit != "coverage")
            members.Add($"\"coverage\": {coverage}");
        if (omit != "holidays")
            members.Add($"\"holidays\": {holidays}");
        if (extra is not null)
            members.Add(extra);
        return "{ " + string.Join(", ", members) + " }";
    }
}
