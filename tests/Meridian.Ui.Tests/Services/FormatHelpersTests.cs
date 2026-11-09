using FluentAssertions;
using Meridian.Ui.Services;

namespace Meridian.Ui.Tests.Services;

/// <summary>
/// Tests for the UTC timestamp helpers in <see cref="FormatHelpers"/>: workstation screens show
/// times in UTC, so a value renders the same UTC label whatever offset or kind it carries.
/// </summary>
public sealed class FormatHelpersTests
{
    [Fact]
    public void FormatUtcTimestamp_ConvertsAnOffsetValueToUtc()
    {
        var eastern = new DateTimeOffset(2026, 7, 5, 8, 0, 0, TimeSpan.FromHours(-4));

        FormatHelpers.FormatUtcTimestamp(eastern).Should().Be("2026-07-05 12:00 UTC");
        FormatHelpers.FormatUtcTimestamp(new DateTimeOffset(2026, 1, 1, 0, 5, 0, TimeSpan.Zero))
            .Should().Be("2026-01-01 00:05 UTC");
    }

    [Fact]
    public void FormatUtcTimestamp_ReadsAnUnspecifiedDateTimeAsUtc()
    {
        var unspecified = new DateTime(2026, 7, 5, 12, 0, 0, DateTimeKind.Unspecified);

        FormatHelpers.FormatUtcTimestamp(unspecified).Should().Be("2026-07-05 12:00 UTC");
        FormatHelpers.AsUtc(unspecified).Kind.Should().Be(DateTimeKind.Utc);
    }

    [Fact]
    public void FormatUtcTimestamp_ConvertsALocalDateTimeAndKeepsAUtcOne()
    {
        var utc = new DateTime(2026, 7, 5, 12, 0, 0, DateTimeKind.Utc);

        FormatHelpers.FormatUtcTimestamp(utc).Should().Be("2026-07-05 12:00 UTC");
        FormatHelpers.FormatUtcTimestamp(utc.ToLocalTime()).Should().Be("2026-07-05 12:00 UTC");
    }

    [Fact]
    public void FormatUtcTimestamp_ReturnsTheFallbackForAMissingValue()
    {
        FormatHelpers.FormatUtcTimestamp((DateTimeOffset?)null, "Never").Should().Be("Never");
        FormatHelpers.FormatUtcTimestamp((DateTime?)null, "Not scheduled").Should().Be("Not scheduled");
        FormatHelpers.FormatUtcTimestamp((DateTimeOffset?)new DateTimeOffset(2026, 7, 5, 14, 0, 0, TimeSpan.FromHours(2)), "Never")
            .Should().Be("2026-07-05 12:00 UTC");
    }
}
