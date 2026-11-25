using FluentAssertions;
using Meridian.Ui.Services;
using Meridian.Ui.Services.Services;

namespace Meridian.Ui.Tests.Services;

/// <summary>
/// Quiet hours are wall-clock times in the operator's display time zone. These tests set the
/// process-wide display zone, so they share the serial collection with the timestamp tests.
/// </summary>
[Collection("DisplayTimeZone serial")]
public sealed class QuietHoursTests : IDisposable
{
    private static readonly TimeSpan TenPm = new(22, 0, 0);
    private static readonly TimeSpan SevenAm = new(7, 0, 0);

    public QuietHoursTests() => DisplayTimeZone.Set(null);

    public void Dispose() => DisplayTimeZone.Set(null);

    private static TimeZoneInfo Zone(string ianaId) => TimeZoneInfo.FindSystemTimeZoneById(ianaId);

    private static DateTimeOffset Utc(int hour, int minute = 0) => new(2026, 7, 5, hour, minute, 0, TimeSpan.Zero);

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class ClockedNotificationService(TimeProvider clock) : NotificationServiceBase(clock);

    [Theory]
    [InlineData(21, 59, false)]
    [InlineData(22, 0, true)]
    [InlineData(23, 30, true)]
    [InlineData(0, 0, true)]
    [InlineData(6, 59, true)]
    [InlineData(7, 0, false, "the end minute is not quiet")]
    [InlineData(12, 0, false)]
    public void OvernightWindow_RunsPastMidnight(int hour, int minute, bool expected, string because = "")
    {
        QuietHours.IsActive(Utc(hour, minute), TimeZoneInfo.Utc, TenPm, SevenAm).Should().Be(expected, because);
    }

    [Fact]
    public void DaytimeWindow_AndEmptyWindow()
    {
        var noon = new TimeSpan(12, 0, 0);
        var onePm = new TimeSpan(13, 0, 0);

        QuietHours.IsActive(Utc(12, 30), TimeZoneInfo.Utc, noon, onePm).Should().BeTrue();
        QuietHours.IsActive(Utc(13, 0), TimeZoneInfo.Utc, noon, onePm).Should().BeFalse();
        QuietHours.IsActive(Utc(12, 0), TimeZoneInfo.Utc, noon, noon).Should().BeFalse("an equal start and end is an empty window");
    }

    [Fact]
    public void Window_IsReadInTheGivenZone()
    {
        // 21:30 UTC in July is 22:30 in London and 17:30 in New York.
        QuietHours.IsActive(Utc(21, 30), Zone("Europe/London"), TenPm, SevenAm).Should().BeTrue();
        QuietHours.IsActive(Utc(21, 30), Zone("America/New_York"), TenPm, SevenAm).Should().BeFalse();
        QuietHours.IsActive(Utc(21, 30), TimeZoneInfo.Utc, TenPm, SevenAm).Should().BeFalse();
    }

    [Theory]
    [InlineData("22:00", true)]
    [InlineData(" 06:45 ", true)]
    [InlineData("00:00", true)]
    [InlineData("23:59", true)]
    [InlineData("24:00", false)]
    [InlineData("7:00", false)]
    [InlineData("07:60", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void TryParseClock_AcceptsOnly24HourHHmm(string? text, bool expected)
    {
        QuietHours.TryParseClock(text, out var time).Should().Be(expected);
        if (expected)
        {
            QuietHours.FormatClock(time).Should().Be(text!.Trim());
        }
    }

    [Fact]
    public void NotificationService_HoldsPopUpsDuringQuietHoursInTheDisplayZone()
    {
        var clock = new FixedClock(Utc(21, 30));
        var service = new ClockedNotificationService(clock);
        var shown = 0;
        service.NotificationReceived += (_, _) => shown++;
        service.UpdateSettings(new NotificationSettings { QuietHoursEnabled = true, QuietHoursStart = TenPm, QuietHoursEnd = SevenAm });

        service.IsQuietHoursNow.Should().BeFalse("21:30 UTC is before 22:00 UTC");
        service.NotifyInfo("Backfill", "first");
        shown.Should().Be(1);

        DisplayTimeZone.Set(Zone("Europe/London"));
        service.IsQuietHoursNow.Should().BeTrue("21:30 UTC is 22:30 in London");
        service.NotifyInfo("Gap", "held");
        shown.Should().Be(1);

        clock.Now = Utc(6, 0);
        service.IsQuietHoursNow.Should().BeFalse("06:00 UTC is 07:00 in London, the end of the window");
    }

    [Fact]
    public void UpdateSettings_RaisesSettingsChanged_AndCopyIsIndependent()
    {
        var service = new ClockedNotificationService(new FixedClock(Utc(12)));
        var raised = 0;
        service.SettingsChanged += (_, _) => raised++;
        var original = new NotificationSettings { NotifyDataGaps = false };

        service.UpdateSettings(original);
        var copy = service.GetSettings().Copy();
        copy.NotifyDataGaps = true;

        raised.Should().Be(1);
        service.GetSettings().NotifyDataGaps.Should().BeFalse();
    }
}
