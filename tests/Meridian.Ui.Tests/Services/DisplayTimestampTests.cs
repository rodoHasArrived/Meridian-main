using FluentAssertions;
using Meridian.Ui.Services;

namespace Meridian.Ui.Tests.Services;

/// <summary>These tests set the process-wide display zone, so they never run beside other tests.</summary>
[CollectionDefinition("DisplayTimeZone serial", DisableParallelization = true)]
public sealed class DisplayTimeZoneSerialCollection;

[Collection("DisplayTimeZone serial")]
public sealed class DisplayTimestampTests : IDisposable
{
    private static readonly DateTimeOffset SummerNoonUtc = new(2026, 7, 5, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset WinterNoonUtc = new(2026, 1, 15, 12, 0, 0, TimeSpan.Zero);

    public DisplayTimestampTests() => DisplayTimeZone.Set(null);

    public void Dispose() => DisplayTimeZone.Set(null);

    private static TimeZoneInfo Zone(string ianaId) => TimeZoneInfo.FindSystemTimeZoneById(ianaId);

    [Fact]
    public void UtcDisplayZone_KeepsTodaysUtcLabelExactly()
    {
        FormatHelpers.FormatDisplayTimestamp(SummerNoonUtc).Should().Be("2026-07-05 12:00 UTC");
        FormatHelpers.FormatDisplayTimestamp(SummerNoonUtc).Should().Be(FormatHelpers.FormatUtcTimestamp(SummerNoonUtc));
        FormatHelpers.FormatDisplayTimestamp(new DateTimeOffset(2026, 7, 5, 14, 0, 0, TimeSpan.FromHours(2)))
            .Should().Be("2026-07-05 12:00 UTC", "an offset value is converted, not relabelled");
    }

    [Fact]
    public void LondonDisplayZone_ShowsLocalTimeWithItsOffsetAcrossDaylightSaving()
    {
        DisplayTimeZone.Set(Zone("Europe/London"));

        FormatHelpers.FormatDisplayTimestamp(SummerNoonUtc).Should().Be("2026-07-05 13:00 UTC+01:00");
        FormatHelpers.FormatDisplayTimestamp(WinterNoonUtc).Should().Be("2026-01-15 12:00 UTC+00:00",
            "London in winter is not UTC itself, so the label still names the offset");
    }

    [Fact]
    public void NegativeAndHalfHourOffsets_AreLabelledExactly()
    {
        DisplayTimeZone.Set(Zone("America/New_York"));
        FormatHelpers.FormatDisplayTimestamp(SummerNoonUtc).Should().Be("2026-07-05 08:00 UTC-04:00");

        DisplayTimeZone.Set(Zone("Asia/Kolkata"));
        FormatHelpers.FormatDisplayTimestamp(SummerNoonUtc).Should().Be("2026-07-05 17:30 UTC+05:30");
    }

    [Fact]
    public void DateTimeOverload_ReadsUnspecifiedKindAsUtc()
    {
        DisplayTimeZone.Set(Zone("Asia/Tokyo"));
        var unspecified = new DateTime(2026, 7, 5, 12, 0, 0, DateTimeKind.Unspecified);

        FormatHelpers.FormatDisplayTimestamp(unspecified).Should().Be("2026-07-05 21:00 UTC+09:00");
        FormatHelpers.FormatDisplayTimestamp((DateTime?)null, "Never").Should().Be("Never");
    }

    [Fact]
    public void CustomPattern_AndTimeRange_KeepOneZoneLabel()
    {
        DisplayTimeZone.Set(Zone("Europe/London"));

        FormatHelpers.FormatDisplayTimestamp(SummerNoonUtc, "MMM d, HH:mm").Should().Be("Jul 5, 13:00 UTC+01:00");
        FormatHelpers.FormatDisplayTimeRange(SummerNoonUtc, SummerNoonUtc.AddMinutes(90))
            .Should().Be("13:00 - 14:30 UTC+01:00");

        DisplayTimeZone.Set(null);
        FormatHelpers.FormatDisplayTimeRange(SummerNoonUtc, SummerNoonUtc.AddMinutes(90))
            .Should().Be("12:00 - 13:30 UTC");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Europe/London")]
    [InlineData("America/New_York")]
    [InlineData("Asia/Kolkata")]
    public void TryParseDisplayTimestamp_ReadsBackEitherLabelForm(string? ianaId)
    {
        DisplayTimeZone.Set(ianaId is null ? null : Zone(ianaId));
        var label = FormatHelpers.FormatDisplayTimestamp(SummerNoonUtc);

        FormatHelpers.TryParseDisplayTimestamp(label, out var parsed).Should().BeTrue();
        parsed.Should().Be(SummerNoonUtc);
        parsed.Offset.Should().Be(TimeSpan.Zero);
    }

    [Theory]
    [InlineData("")]
    [InlineData("-")]
    [InlineData("Jul 5, 13:00 UTC+01:00")]
    [InlineData("2026-07-05 13:00")]
    public void TryParseDisplayTimestamp_RejectsOtherText(string text)
    {
        FormatHelpers.TryParseDisplayTimestamp(text, out _).Should().BeFalse();
    }

    [Fact]
    public void Set_RaisesChangedOnlyWhenTheZoneDiffers()
    {
        var raised = 0;
        void OnChanged(object? sender, EventArgs e) => raised++;
        DisplayTimeZone.Changed += OnChanged;
        try
        {
            DisplayTimeZone.Set(null);
            DisplayTimeZone.Set(Zone("Europe/London"));
            DisplayTimeZone.Set(Zone("Europe/London"));
            DisplayTimeZone.Set(null);

            raised.Should().Be(2);
        }
        finally
        {
            DisplayTimeZone.Changed -= OnChanged;
        }
    }

    [Theory]
    [InlineData("UTC", true)]
    [InlineData("Etc/UTC", true)]
    [InlineData("Europe/London", false)]
    [InlineData("Africa/Abidjan", false)]
    public void IsUtc_MatchesUtcAndItsAliasesOnly(string ianaId, bool expected)
    {
        DisplayTimeZone.IsUtc(Zone(ianaId)).Should().Be(expected);
    }
}
