using FluentAssertions;
using Meridian.Contracts.Calendars;

namespace Meridian.Tests.Calendars;

/// <summary>
/// The canonical calendar id grammar, shared by calendar files and declared payment calendars
/// (priority 5, increment 5b-1): matched exactly, with no trimming or case folding.
/// </summary>
[Trait("Category", "Unit")]
public sealed class BusinessDayCalendarsCanonicalIdTests
{
    [Theory]
    [InlineData("USNY", true)]
    [InlineData("GBLO", true)]
    [InlineData("ZZTEST", true)]
    [InlineData("WEEKENDS", true)]
    [InlineData("ABCDEFGHIJKLMNO9", true)]
    [InlineData("usny", false)]
    [InlineData(" USNY", false)]
    [InlineData("USNY ", false)]
    [InlineData("US-NY", false)]
    [InlineData("USNY+GBLO", false)]
    [InlineData("Z", false)]
    [InlineData("ABCDEFGHIJKLMNOPQ", false)]
    [InlineData("9ZTEST", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsCanonicalCalendarId_AcceptsExactlyTheGrammar(string? calendarId, bool expected)
    {
        BusinessDayCalendars.IsCanonicalCalendarId(calendarId).Should().Be(expected);
    }

    [Fact]
    public void TheGrammarBounds_AreTheDescribedOnes()
    {
        BusinessDayCalendars.MaxCalendarIdLength.Should().Be(16);
        BusinessDayCalendars.CanonicalCalendarIdDescription.Should().Be(
            "an uppercase ASCII letter followed by 1 to 15 uppercase ASCII letters or digits");
        BusinessDayCalendars.IsCanonicalCalendarId(BusinessDayCalendars.WeekendsOnlyId).Should().BeTrue("weekends only is declarable");
    }
}
