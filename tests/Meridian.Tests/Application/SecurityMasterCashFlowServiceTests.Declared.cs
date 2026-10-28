using System.Text.Json;
using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.Calendars;
using Meridian.Contracts.SecurityMaster;
using Meridian.Storage.SecurityMaster;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Meridian.Tests.Application;

public sealed partial class SecurityMasterCashFlowServiceTests
{
    [Theory]
    [InlineData("Percent", 0.5, 0.5)]
    [InlineData("Fraction", 0.005, 0.5)]
    public async Task DeclaredCoupon_UsesExplicitRateUnitAndAccrualEnd(string unit, decimal rate, decimal expected)
    {
        var projection = await DeclaredProjection(new
        {
            assetClassTermsVersion = 2, issueDate = "2026-01-31", maturityDate = "2027-01-31",
            par = 100m, couponRate = rate, paymentFrequency = "Annual", dayCountConvention = "30/360",
            scheduleConventions = new
            {
                couponRateUnit = unit, couponCashBasis = "DayCount", businessDayConvention = "Following",
                paymentCalendars = new[] { "WEEKENDS" }, paymentLagBusinessDays = 2
            }
        });
        projection.BlockedReason.Should().Contain("preview-only");
        projection.Schedule.Should().ContainSingle();
        projection.Schedule[0].InterestAmount.Should().Be(expected);
        projection.Schedule[0].PeriodDate.Should().Be(new DateTimeOffset(2027, 2, 2, 0, 0, 0, TimeSpan.Zero));
        projection.Schedule[0].PrincipalAmount.Should().Be(100m);
    }

    [Fact]
    public async Task DeclaredCalendar_UnknownIdReturnsBlockedEmptySchedule()
    {
        var projection = await DeclaredProjection(new
        {
            assetClassTermsVersion = 2, issueDate = "2026-01-31", maturityDate = "2027-01-31",
            par = 100m, couponRate = 6m, paymentFrequency = "Annual",
            scheduleConventions = new
            {
                couponRateUnit = "Percent", couponCashBasis = "Level", paymentCalendars = new[] { "UNKNOWN" }
            }
        });
        projection.BlockedReason.Should().Contain("cannot be resolved");
        projection.Schedule.Should().BeEmpty();
    }

    [Fact]
    public async Task UndeclaredVersion_StrayBlockKeepsLegacyDatesAndRateNormalization()
    {
        var projection = await DeclaredProjection(new
        {
            assetClassTermsVersion = 1, issueDate = "2026-01-31", maturityDate = "2027-01-31",
            par = 100m, couponRate = 0.5m, paymentFrequency = "Annual", dayCountConvention = "30/360",
            scheduleConventions = new { businessDayConvention = "NoSuchConvention" }
        });
        projection.BlockedReason.Should().BeNull();
        projection.Schedule[0].PeriodDate.Should().Be(new DateTimeOffset(2027, 1, 31, 0, 0, 0, TimeSpan.Zero));
        projection.Schedule[0].InterestAmount.Should().Be(50m);
    }

    [Fact]
    public async Task DeclaredSwapLegs_UseIndependentUnitsAndPaymentConventions()
    {
        var projection = await DeclaredProjection(new
        {
            assetClassTermsVersion = 2, issueDate = "2026-01-31", maturityDate = "2027-01-31",
            notional = 100m, paymentFrequency = "Annual",
            legs = new object[]
            {
                new { legId = "receive", legType = "Fixed", direction = "Receive", fixedRate = 0.5m,
                    scheduleConventions = new { couponRateUnit = "Percent", couponCashBasis = "Level" } },
                new { legId = "pay", legType = "Fixed", direction = "Pay", fixedRate = 0.002m,
                    scheduleConventions = new { couponRateUnit = "Fraction", couponCashBasis = "Level" } }
            }
        }, "Swap");
        projection.BlockedReason.Should().Contain("preview-only");
        projection.LegSchedules.Should().HaveCount(2);
        projection.Schedule.Should().ContainSingle();
        projection.Schedule[0].InterestAmount.Should().Be(0.3m);
    }

    private static async Task<StructuredCashFlowProjectionDto> DeclaredProjection(object payload, string assetClass = "Bond")
    {
        var securityId = Guid.NewGuid();
        var store = Substitute.For<ISecurityMasterCashFlowStore>();
        store.GetSourceAsync(securityId, Arg.Any<CancellationToken>()).Returns(new SecurityCashFlowSourceDto(
            securityId, StructuredCashFlowSourceKind.CalculatedBullet, DateTimeOffset.UtcNow, false, null, null));
        var query = Substitute.For<ISecurityMasterQueryService>();
        query.GetByIdAsync(securityId, Arg.Any<CancellationToken>()).Returns(BuildSecurity(securityId,
            JsonSerializer.SerializeToElement(payload)) with { AssetClass = assetClass });
        var service = new SecurityMasterCashFlowService(store, [], query,
            NullLogger<SecurityMasterCashFlowService>.Instance,
            calendars: new StaticBusinessDayCalendarSource([BusinessDayCalendars.WeekendsOnly]));
        return (await service.GetProjectionAsOfAsync(securityId, StructuredCashFlowScenario.Base,
            new DateTimeOffset(2026, 1, 31, 0, 0, 0, TimeSpan.Zero)))!;
    }
}
