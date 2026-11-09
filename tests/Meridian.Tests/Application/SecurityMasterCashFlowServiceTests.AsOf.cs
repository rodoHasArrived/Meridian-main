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
        // D3 (decided 2026-11-05): undeclared non-zero rates block, and the pinned swap's legs cannot
        // declare a unit (the engine refuses per-leg conventions, LegConventionsNotFollowed). The
        // projection's rows are blocked until per-leg units are declarable (pinned before D3: four
        // quarters, the first on 2026-04-01); its as-of date and the as-of curve lookup are still asserted.
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
        projection.BlockedReason.Should().Be(
            "Rate legs.fixedRate = 0.04 on Swap has no declared unit (Fraction, Percent or BasisPoints). "
            + "A legacy value is flagged rather than converted; its unit cannot be inferred from its magnitude.");
        projection.Schedule.Should().BeEmpty();
        await curves.Received(1).GetForwardCurveAsync("SOFR", new DateOnly(2026, 1, 1), Arg.Any<CancellationToken>());
        await curves.DidNotReceive().GetForwardCurveAsync(Arg.Any<string>(), DateOnly.FromDateTime(DateTime.UtcNow.Date), Arg.Any<CancellationToken>());

        var today = await service.GetProjectionAsync(security.SecurityId, StructuredCashFlowScenario.Base);
        today!.AsOf.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(1), "the existing entry point still projects as of now");
    }
}
