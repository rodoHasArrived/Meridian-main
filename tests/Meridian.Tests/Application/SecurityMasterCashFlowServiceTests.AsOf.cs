using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.SecurityMaster;
using Meridian.Storage.SecurityMaster;
using Meridian.Tests.SecurityMaster;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SecurityMasterQueryContract = Meridian.Contracts.SecurityMaster.ISecurityMasterQueryService;

namespace Meridian.Tests.Application;

/// <summary>
/// <c>GetProjectionAsOfAsync</c> projects as of the requested date, not the wall clock, and reads the
/// forward curve as of that date too; <c>GetProjectionAsync</c> keeps using today.
/// </summary>
public sealed partial class SecurityMasterCashFlowServiceTests
{
    [Fact]
    public async Task GetProjectionAsOfAsync_UsesTheRequestedDate_ForBothTheProjectionAndTheCurveLookup()
    {
        var asOf = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var security = ValuationTestSecurities.FloatingSwap(new DateOnly(2026, 1, 1));
        var store = Substitute.For<ISecurityMasterCashFlowStore>();
        store.GetSourceAsync(security.SecurityId, Arg.Any<CancellationToken>())
            .Returns(new SecurityCashFlowSourceDto(security.SecurityId, StructuredCashFlowSourceKind.CalculatedBullet, DateTimeOffset.UtcNow, false, null, null));
        var query = Substitute.For<SecurityMasterQueryContract>();
        query.GetByIdAsync(security.SecurityId, Arg.Any<CancellationToken>()).Returns(security);
        var curves = Substitute.For<ISecurityForwardCurveProvider>();
        curves.GetForwardCurveAsync(Arg.Any<string>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns((SecurityForwardCurve?)null);
        var service = new SecurityMasterCashFlowService(
            store, Array.Empty<IStructuredCashFlowProvider>(), query,
            NullLogger<SecurityMasterCashFlowService>.Instance, forwardCurves: curves);

        var projection = await service.GetProjectionAsOfAsync(security.SecurityId, StructuredCashFlowScenario.Base, asOf);

        projection.Should().NotBeNull();
        projection!.AsOf.Should().Be(asOf, "the projection is dated as requested, not today");
        projection.Schedule.Should().HaveCount(4, "every quarter of the pinned year is on or after the as-of");
        projection.Schedule[0].PeriodDate.Should().Be(new DateTimeOffset(2026, 4, 1, 0, 0, 0, TimeSpan.Zero));
        await curves.Received(1).GetForwardCurveAsync("SOFR", new DateOnly(2026, 1, 1), Arg.Any<CancellationToken>());
        await curves.DidNotReceive().GetForwardCurveAsync(Arg.Any<string>(), DateOnly.FromDateTime(DateTime.UtcNow.Date), Arg.Any<CancellationToken>());

        var today = await service.GetProjectionAsync(security.SecurityId, StructuredCashFlowScenario.Base);
        today!.AsOf.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(1), "the existing entry point still projects as of now");
    }
}
