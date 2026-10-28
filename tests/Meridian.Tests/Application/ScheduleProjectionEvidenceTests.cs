using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Meridian.Application.SecurityMaster.CashFlow;
using Meridian.Contracts.Calendars;
using Meridian.Contracts.SecurityMaster;

namespace Meridian.Tests.Application;

public sealed class ScheduleProjectionEvidenceTests
{
    private static readonly DateOnly Start = new(2026, 1, 31);
    private static readonly DateOnly End = new(2026, 7, 31);
    private static readonly JsonElement Block = JsonSerializer.SerializeToElement(new
    {
        businessDayConvention = "Following", paymentCalendars = new[] { "TEST" },
        couponRateUnit = "Percent", couponCashBasis = "DayCount"
    });
    private static readonly SecurityDetailDto Security = new(
        Guid.Parse("3627b7ef-f85d-40c4-9dc8-67d19056daed"), "Bond", SecurityStatusDto.Active,
        "Evidence bond", "USD", JsonSerializer.SerializeToElement(new { currency = "USD" }),
        JsonSerializer.SerializeToElement(new { assetClassTermsVersion = 2, couponRate = 6m, scheduleConventions = Block }),
        [], [], 9, new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), null);

    [Fact]
    public void JsonRoundTrip_PreservesSourceReceiptPeriodIdentityAndReferenceDates()
    {
        var evidence = Create(new HolidayBusinessDayCalendar("TEST", 1));
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        var json = JsonSerializer.Serialize(evidence, options);
        var restored = JsonSerializer.Deserialize<StructuredCashFlowScheduleEvidence>(json, options)!;

        restored.Should().BeEquivalentTo(evidence);
        restored.SourceSecurityVersion.Should().Be(9);
        restored.SourceTermsVersion.Should().Be(2);
        restored.SourceTermsHash.Should().MatchRegex("^[a-f0-9]{64}$");
        restored.Periods.Should().HaveCount(2);
        for (var index = 0; index < restored.Periods.Count; index++)
        {
            restored.Periods[index].PeriodId.Should().Be(evidence.Periods[index].PeriodId);
            restored.Periods[index].Period.Should().Be(evidence.Periods[index].Period);
            restored.Periods[index].Period.ReferencePeriods.Should().NotBeEmpty();
        }
        restored.Calendars.Should().Equal(evidence.Calendars);
    }

    [Fact]
    public void CalendarCorrection_ChangesPaymentAndCalendarReceiptWithoutReidentifyingThePeriod()
    {
        var original = Create(new HolidayBusinessDayCalendar("TEST", 1));
        var corrected = Create(new HolidayBusinessDayCalendar("TEST", 2, [new DateOnly(2026, 4, 30)]));

        corrected.SourceTermsHash.Should().Be(original.SourceTermsHash);
        corrected.Periods.Select(entry => entry.PeriodId).Should().Equal(original.Periods.Select(entry => entry.PeriodId));
        corrected.Periods[0].Period.UnadjustedEnd.Should().Be(original.Periods[0].Period.UnadjustedEnd);
        corrected.Periods[0].Period.PaymentDate.Should().Be(new DateOnly(2026, 5, 1));
        original.Periods[0].Period.PaymentDate.Should().Be(new DateOnly(2026, 4, 30));
        corrected.Calendars.Single().ContentHash.Should().NotBe(original.Calendars.Single().ContentHash);
    }

    [Fact]
    public void SameRetainedPayload_IsDeterministicAndDifferentStreamHasDifferentPeriodIdentities()
    {
        var calendar = new HolidayBusinessDayCalendar("TEST", 1);
        var first = Create(calendar);
        var repeated = Create(calendar);
        var otherStream = Create(calendar, "receive-leg");

        repeated.Should().BeEquivalentTo(first);
        repeated.SourceTermsHash.Should().Be(first.SourceTermsHash);
        otherStream.Periods.Select(entry => entry.PeriodId)
            .Intersect(first.Periods.Select(entry => entry.PeriodId)).Should().BeEmpty();
        var revised = Security with
        {
            Version = 10,
            AssetSpecificTerms = JsonSerializer.SerializeToElement(new
            {
                assetClassTermsVersion = 2, couponRate = 7m, scheduleConventions = Block
            })
        };
        var plan = ScheduleSpecFactory.Create(Block, Start, End, "Quarterly",
            new StaticBusinessDayCalendarSource([calendar]))!;
        var changed = ScheduleProjectionEvidence.Create(revised, "coupon", plan, "30/360");
        changed.SourceSecurityVersion.Should().Be(10);
        changed.SourceTermsHash.Should().NotBe(first.SourceTermsHash);
        changed.Periods.Select(entry => entry.PeriodId).Should().Equal(first.Periods.Select(entry => entry.PeriodId));
    }

    private static StructuredCashFlowScheduleEvidence Create(IBusinessDayCalendar calendar, string stream = "coupon")
    {
        var plan = ScheduleSpecFactory.Create(Block, Start, End, "Quarterly",
            new StaticBusinessDayCalendarSource([calendar]))!;
        return ScheduleProjectionEvidence.Create(Security, stream, plan, "30/360");
    }
}
