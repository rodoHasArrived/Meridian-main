using System.Text.Json;
using FluentAssertions;
using Meridian.Application.FixedIncome;
using Meridian.Contracts.SecurityMaster;
using Meridian.Storage.SecurityMaster;
using NSubstitute;

namespace Meridian.Tests.FixedIncome;

public sealed class BondProjectionServiceTests
{
    [Fact]
    public async Task GetReferenceAsync_ReturnsLifecycleAndAccrualConvention_ForBondProjection()
    {
        var securityId = Guid.NewGuid();
        var securityStore = Substitute.For<ISecurityMasterStore>();
        var projectionStore = Substitute.For<IBondReferenceProjectionStore>();
        securityStore.GetProjectionAsync(securityId, Arg.Any<CancellationToken>())
            .Returns(CreateBondProjection(securityId));
        projectionStore.GetBondAsync(securityId, Arg.Any<CancellationToken>())
            .Returns(new BondProjectionRow(
                securityId,
                "Meridian 2032 Senior Notes",
                "USD",
                "M2032",
                "Meridian Treasury LLC",
                "SeniorUnsecured",
                new DateOnly(2032, 1, 15),
                9));
        projectionStore.GetLifecycleAsync(securityId, Arg.Any<CancellationToken>())
            .Returns(new BondLifecycleProjectionRow(
                securityId,
                "Callable",
                new DateOnly(2024, 1, 15),
                new DateOnly(2029, 1, 15),
                new DateOnly(2032, 1, 15),
                true,
                9));
        projectionStore.GetAccrualConventionAsync(securityId, Arg.Any<CancellationToken>())
            .Returns(new BondAccrualConventionProjectionRow(
                securityId,
                "30/360",
                2,
                "NYSE",
                "Fixed",
                5.125m,
                null,
                null,
                9));

        var service = new BondProjectionService(securityStore, projectionStore);

        var result = await service.GetReferenceAsync(securityId);

        result.Should().NotBeNull();
        result!.IssuerName.Should().Be("Meridian Treasury LLC");
        result.Seniority.Should().Be("SeniorUnsecured");
        result.Lifecycle.Should().NotBeNull();
        result.Lifecycle!.LifecycleStat.Should().Be(Meridian.Contracts.FixedIncome.BondLifecycleStat.Callable);
        result.AccrualConvention.Should().NotBeNull();
        result.AccrualConvention!.DayCountConvention.Should().Be("30/360");
        result.AccrualConvention.FixedCouponRate.Should().Be(5.125m);
    }

    [Fact]
    public async Task GetAccrualConventionAsync_ReturnsFloatingBondAccrualFields()
    {
        var securityId = Guid.NewGuid();
        var securityStore = Substitute.For<ISecurityMasterStore>();
        var projectionStore = Substitute.For<IBondReferenceProjectionStore>();
        projectionStore.GetAccrualConventionAsync(securityId, Arg.Any<CancellationToken>())
            .Returns(new BondAccrualConventionProjectionRow(
                securityId,
                "ACT/360",
                2,
                "TARGET2",
                "Floating",
                null,
                "SOFR",
                125m,
                4));

        var service = new BondProjectionService(securityStore, projectionStore);

        var result = await service.GetAccrualConventionAsync(securityId);

        result.Should().NotBeNull();
        result!.CouponKind.Should().Be("Floating");
        result.FloatingRateIndex.Should().Be("SOFR");
        result.FloatingSpreadBps.Should().Be(125m);
        result.SettlementCycleDays.Should().Be(2);
        result.HolidayCalendarId.Should().Be("TARGET2");
    }

    private static SecurityProjectionRecord CreateBondProjection(Guid securityId)
        => new(
            securityId,
            "Bond",
            SecurityStatusDto.Active,
            "Meridian 2032 Senior Notes",
            "USD",
            "Ticker",
            "M2032",
            JsonSerializer.SerializeToElement(new
            {
                displayName = "Meridian 2032 Senior Notes",
                currency = "USD",
                issuerName = "Meridian Treasury LLC",
                settlementCycleDays = 2,
                holidayCalendarId = "NYSE"
            }),
            JsonSerializer.SerializeToElement(new
            {
                schemaVersion = 1,
                issuerName = "Meridian Treasury LLC",
                seniority = "SeniorUnsecured",
                issueDate = "2024-01-15",
                maturity = "2032-01-15",
                isCallable = true,
                callDate = "2029-01-15",
                coupon = new
                {
                    kind = "Fixed",
                    rate = 5.125m,
                    dayCountConvention = "30/360"
                }
            }),
            JsonSerializer.SerializeToElement(new { sourceSystem = "tests", updatedBy = "codex" }),
            9,
            DateTimeOffset.UtcNow.AddDays(-1),
            null,
            Array.Empty<SecurityIdentifierDto>(),
            Array.Empty<SecurityAliasDto>());
}
