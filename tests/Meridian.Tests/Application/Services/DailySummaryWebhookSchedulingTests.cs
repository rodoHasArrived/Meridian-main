using FluentAssertions;
using Meridian.Application.Services;
using Xunit;

namespace Meridian.Tests.Application.Services;

public sealed class DailySummaryWebhookSchedulingTests
{
    private static readonly TimeZoneInfo EasternTime = ResolveEasternTimeZone();

    [Fact]
    public void CalculateNextScheduledRunUtc_RecomputesWallClockTimeAcrossSpringDstChange()
    {
        var scheduledTime = new TimeOnly(16, 30);
        var beforeChange = DailySummaryWebhook.CalculateNextScheduledRunUtc(
            new DateTimeOffset(2026, 3, 7, 12, 0, 0, TimeSpan.Zero),
            scheduledTime,
            EasternTime);
        var afterFirstRun = DailySummaryWebhook.CalculateNextScheduledRunUtc(
            beforeChange.AddMinutes(1),
            scheduledTime,
            EasternTime);

        beforeChange.Should().Be(new DateTimeOffset(2026, 3, 7, 21, 30, 0, TimeSpan.Zero));
        afterFirstRun.Should().Be(new DateTimeOffset(2026, 3, 8, 20, 30, 0, TimeSpan.Zero));
        (afterFirstRun - beforeChange).Should().Be(TimeSpan.FromHours(23));
    }

    [Fact]
    public void CalculateNextScheduledRunUtc_RecomputesWallClockTimeAcrossFallDstChange()
    {
        var scheduledTime = new TimeOnly(16, 30);
        var beforeChange = DailySummaryWebhook.CalculateNextScheduledRunUtc(
            new DateTimeOffset(2026, 10, 31, 12, 0, 0, TimeSpan.Zero),
            scheduledTime,
            EasternTime);
        var afterFirstRun = DailySummaryWebhook.CalculateNextScheduledRunUtc(
            beforeChange.AddMinutes(1),
            scheduledTime,
            EasternTime);

        beforeChange.Should().Be(new DateTimeOffset(2026, 10, 31, 20, 30, 0, TimeSpan.Zero));
        afterFirstRun.Should().Be(new DateTimeOffset(2026, 11, 1, 21, 30, 0, TimeSpan.Zero));
        (afterFirstRun - beforeChange).Should().Be(TimeSpan.FromHours(25));
    }

    [Fact]
    public void CalculateNextScheduledRunUtc_MovesInvalidSpringTimeToFirstValidMinute()
    {
        var nextRun = DailySummaryWebhook.CalculateNextScheduledRunUtc(
            new DateTimeOffset(2026, 3, 8, 5, 0, 0, TimeSpan.Zero),
            new TimeOnly(2, 30),
            EasternTime);

        nextRun.Should().Be(new DateTimeOffset(2026, 3, 8, 7, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public async Task DisposeAsync_CancelsAndJoinsOwnedSchedulerLoop()
    {
        var webhook = new DailySummaryWebhook(new DailySummaryWebhookConfig
        {
            EnableScheduledSummary = true,
            ScheduledTime = "16:30",
            Webhooks = Array.Empty<WebhookConfig>()
        });

        var firstDispose = webhook.DisposeAsync().AsTask();
        var secondDispose = webhook.DisposeAsync().AsTask();

        await Task.WhenAll(firstDispose, secondDispose).WaitAsync(TimeSpan.FromSeconds(5));
    }

    private static TimeZoneInfo ResolveEasternTimeZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
        }
    }
}
