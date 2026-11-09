using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.Calendars;
using Meridian.Contracts.FixedIncome;
using Meridian.Contracts.SecurityMaster;
using Meridian.Instruments.AssetOperations;
using Meridian.Storage.SecurityMaster;
using Meridian.Tests.FixedIncome;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Meridian.Tests.AssetOperations;

/// <summary>
/// The obligation timeline of a Bond that declares a call or put schedule: the five-year 5% semiannual
/// bullet of <see cref="BondExerciseNoticeWatchTests"/>, issued 2026-01-15, read through the asset
/// operations read service with the shared Security Master cash-flow calculation.
/// </summary>
[Trait("Category", "Unit")]
public sealed class AssetObligationDeclaredExerciseTimelineTests
{
    private static readonly DateTimeOffset AsOf = new(2026, 12, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly IBusinessDayCalendarSource Calendars =
        new StaticBusinessDayCalendarSource([BusinessDayCalendars.WeekendsOnly]);

    [Fact]
    public async Task DeclaredCall_WithAScalarCallDate_RaisesOnlyTheScalarReview()
    {
        var detail = await ReadAsync(BondExerciseNoticeWatchTests.Bond([EuropeanCall()]));

        var timeline = detail.TermsObligationsTimeline!;
        timeline.EngineVersion.Should().Be("asset-obligation-declared-v3+asset-terms-obligations-timeline-v1");
        OptionEvents(timeline).Should().Equal(("CallPutReview", new DateOnly(2027, 1, 15)));
    }

    [Fact]
    public async Task DeclaredPut_WithoutAScalarDate_RaisesNoOptionEvent()
    {
        var put = new Dictionary<string, object?>
        {
            ["putDate"] = "2027-01-15",
            ["putPrice"] = 100m,
            ["exerciseStyle"] = "European",
            ["priceBasis"] = "PercentOfPar",
            ["minimumNoticeDays"] = 15,
            ["maximumNoticeDays"] = 45,
            ["noticeDayType"] = "Calendar"
        };
        var detail = await ReadAsync(BondExerciseNoticeWatchTests.Bond(puts: [put]));

        var timeline = detail.TermsObligationsTimeline!;
        timeline.EngineVersion.Should().Be("asset-obligation-declared-v3+asset-terms-obligations-timeline-v1");
        OptionEvents(timeline).Should().BeEmpty();
    }

    private static (string Kind, DateOnly Date)[] OptionEvents(AssetTermsObligationsTimelineDto timeline)
        => timeline.Events
            .Where(static row => row.EventKind.Contains("Call", StringComparison.Ordinal) || row.EventKind.Contains("Put", StringComparison.Ordinal))
            .Select(static row => (row.EventKind, row.EffectiveDate))
            .ToArray();

    private static Dictionary<string, object?> EuropeanCall() => new()
    {
        ["callDate"] = "2027-01-15",
        ["callPrice"] = 101m,
        ["exerciseStyle"] = "European",
        ["priceBasis"] = "PercentOfPar",
        ["minimumNoticeDays"] = 30,
        ["maximumNoticeDays"] = 60,
        ["noticeDayType"] = "Calendar"
    };

    private static async Task<AssetOperationsDetailDto> ReadAsync(SecurityDetailDto security)
    {
        var query = Substitute.For<ISecurityMasterQueryService>();
        query.GetByIdAsync(security.SecurityId, Arg.Any<CancellationToken>()).Returns(security);
        var service = new AssetOperationsReadService(securityMasterQueryService: query, cashFlows: CashFlows(security, query));
        var detail = await service.GetOperationsAsOfAsync(security.SecurityId, new BondCalculationContext(AsOf));
        detail.Should().NotBeNull();
        detail!.CashFlowProjectionRuns.Should().ContainSingle().Which.Status.Should().Be("Completed");
        return detail;
    }

    private static SecurityMasterCashFlowService CashFlows(SecurityDetailDto security, ISecurityMasterQueryService query)
    {
        var store = Substitute.For<ISecurityMasterCashFlowStore>();
        store.GetSourceAsync(security.SecurityId, Arg.Any<CancellationToken>()).Returns(new SecurityCashFlowSourceDto(
            security.SecurityId, StructuredCashFlowSourceKind.CalculatedBullet, security.EffectiveFrom, false, null, null));
        return new(Calendars, store, [], query, NullLogger<SecurityMasterCashFlowService>.Instance);
    }
}
