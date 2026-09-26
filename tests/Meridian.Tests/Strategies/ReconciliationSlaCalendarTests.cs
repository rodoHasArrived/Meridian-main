using FluentAssertions;
using Meridian.Contracts.Workstation;
using Meridian.Strategies.Services;
using Meridian.Ui.Shared.Services;

namespace Meridian.Tests.Strategies;

public sealed class ReconciliationSlaCalendarTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "meridian-sla-" + Guid.NewGuid().ToString("N"));
    private static ReconciliationBreakQueueItem Item(DateTimeOffset detected) => new("break", "run", "statement",
        ReconciliationBreakCategory.CashMismatch, ReconciliationBreakQueueStatus.Open, 10m, "variance", null, detected, detected);
    private static ReconciliationSlaPolicy Policy(ReconciliationBreakQueueItem item) => ReconciliationSlaCalculator.DefaultPolicyFor(item)
        with { DueBusinessHours = 4, WarningBusinessHours = 2 };

    private FileReconciliationSlaCalendarResolver Resolver(string json)
    {
        Directory.CreateDirectory(Path.Combine(_root, "reconciliation"));
        File.WriteAllText(Path.Combine(_root, FileReconciliationSlaCalendarResolver.RelativePath), json);
        return new FileReconciliationSlaCalendarResolver(_root);
    }

    [Fact]
    public void Holiday_and_fractional_close_are_excluded_from_age_and_deadline()
    {
        var resolver = Resolver("""{"businessCalendars":[{"id":"market","validFrom":"2026-01-01","validThrough":"2026-12-31","holidays":["2026-09-07"]}]}""");
        var item = Item(new DateTimeOffset(2026, 9, 4, 16, 30, 0, TimeSpan.Zero));
        var result = ReconciliationSlaCalculator.Compute(item, Policy(item) with { BusinessCalendarId = "market" },
            new DateTimeOffset(2026, 9, 8, 11, 0, 0, TimeSpan.Zero), resolver);
        result.DueAt.Should().Be(new DateTimeOffset(2026, 9, 8, 12, 30, 0, TimeSpan.Zero));
        result.BusinessAgeHours.Should().Be(2.5);
        result.State.Should().Be(ReconciliationCaseSlaState.Warning);
    }

    [Fact]
    public void Occurrence_start_drives_age_instead_of_latest_break_detection()
    {
        var start = new DateTimeOffset(2026, 9, 1, 9, 0, 0, TimeSpan.Zero);
        var item = Item(start.AddDays(1)) with
        { Lineage = new ReconciliationBreakLineageDto("line", "scope", "occ", 1, "Aging", start, start, start.AddDays(1), "run") };
        var result = ReconciliationSlaCalculator.Compute(item, Policy(item), start.AddDays(1));
        result.BusinessAgeHours.Should().Be(8);
        result.DueAt.Should().Be(start.AddHours(4));
        var recurrence = item with { Lineage = item.Lineage! with { OccurrenceFirstObservedAt = start.AddDays(1), OccurrenceNumber = 2 } };
        ReconciliationSlaCalculator.Compute(recurrence, Policy(item), start.AddDays(1)).BusinessAgeHours.Should().Be(0);
    }

    [Fact]
    public void Daylight_saving_offset_is_resolved_for_each_business_date()
    {
        var item = Item(new DateTimeOffset(2026, 3, 6, 21, 30, 0, TimeSpan.Zero));
        var result = ReconciliationSlaCalculator.Compute(item, Policy(item) with { TimeZoneId = "America/New_York" }, item.DetectedAt);
        result.DueAt.Should().Be(new DateTimeOffset(2026, 3, 9, 16, 30, 0, TimeSpan.Zero));
    }

    [Fact]
    public void Named_calendars_never_fall_back_and_closed_days_still_require_overlay_coverage()
    {
        var item = Item(new DateTimeOffset(2026, 9, 5, 9, 0, 0, TimeSpan.Zero));
        var policy = Policy(item) with { BusinessCalendarId = "missing" };
        var missing = () => ReconciliationSlaCalculator.Compute(item, policy, item.DetectedAt);
        missing.Should().Throw<InvalidOperationException>();
        var resolver = Resolver("""{"holidayCalendars":[{"id":"bank","validFrom":"2026-09-06","validThrough":"2026-12-31","holidays":[]}]}""");
        var expired = () => ReconciliationSlaCalculator.Compute(item, Policy(item) with { HolidayCalendarIds = ["bank"] }, item.DetectedAt, resolver);
        expired.Should().Throw<InvalidDataException>();
    }

    [Theory]
    [InlineData("{\"businessCalendars\":[{\"id\":\"market\"}]}")]
    [InlineData("{\"unknown\":true}")]
    [InlineData("{\"businessCalendars\":[null]}")]
    [InlineData("{\"businessCalendars\":[{\"id\":\"m\",\"validFrom\":\"2026-01-01\",\"validThrough\":\"2026-12-31\",\"weekendDays\":[7]}]}")]
    public void Invalid_calendar_configuration_is_refused(string json)
    {
        var act = () => Resolver(json);
        act.Should().Throw<Exception>();
    }

    [Fact]
    public void Calendar_configuration_is_a_host_snapshot()
    {
        var resolver = Resolver("""{"holidayCalendars":[{"id":"bank","validFrom":"2026-01-01","validThrough":"2026-12-31","holidays":["2026-09-07"]}]}""");
        var item = Item(new DateTimeOffset(2026, 9, 7, 9, 0, 0, TimeSpan.Zero));
        var policy = Policy(item) with { HolidayCalendarIds = ["bank"] };
        var updated = Resolver("""{"holidayCalendars":[{"id":"bank","validFrom":"2026-01-01","validThrough":"2026-12-31","holidays":[]}]}""");
        ReconciliationSlaCalculator.Compute(item, policy, item.DetectedAt, resolver).DueAt.Should().Be(item.DetectedAt.AddDays(1).AddHours(4));
        ReconciliationSlaCalculator.Compute(item, policy, item.DetectedAt, updated).DueAt.Should().Be(item.DetectedAt.AddHours(4));
    }

    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
