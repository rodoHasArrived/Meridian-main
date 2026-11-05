using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.Calendars;
using Meridian.Contracts.FixedIncome;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.SecurityMaster.Schedules;
using Meridian.Ui.Shared.Services;
using NSubstitute;

namespace Meridian.Tests.FixedIncome;

/// <summary>
/// Scenario tests for the first notice-date consumer of declared call and put schedules: a five-year 5%
/// semiannual bullet issued 2026-01-15 whose declared entries carry notice bounds.
/// </summary>
public sealed class BondExerciseNoticeWatchTests
{
    private static readonly DateTimeOffset RecordedAt = new(2026, 1, 15, 0, 0, 0, TimeSpan.Zero);
    private static readonly IBusinessDayCalendarSource Calendars =
        new StaticBusinessDayCalendarSource([BusinessDayCalendars.WeekendsOnly]);
    private static readonly DateOnly CallDate = new(2027, 1, 15);

    [Theory]
    // 30–60 calendar days before 2027-01-15: earliest 2026-11-16, latest 2026-12-16.
    [InlineData("2026-10-01", 90, BondExerciseNoticeStatus.Upcoming, 76)]
    [InlineData("2026-11-16", 90, BondExerciseNoticeStatus.Open, 30)]
    [InlineData("2026-12-01", 90, BondExerciseNoticeStatus.Open, 15)]
    [InlineData("2026-12-16", 90, BondExerciseNoticeStatus.Open, 0)]
    [InlineData("2026-12-20", 1, BondExerciseNoticeStatus.DeadlineElapsed, -4)]
    public void EuropeanCall_MovesFromUpcomingToOpenToElapsed(string asOf, int horizon,
        BondExerciseNoticeStatus status, int daysToDeadline)
    {
        var result = BondExerciseNoticeWatch.Evaluate(Bond(Calls(European(30, 60, "Calendar"))), Calendars,
            DateOnly.Parse(asOf), horizon);

        result.Blockers.Should().BeEmpty();
        var window = result.Windows.Should().ContainSingle().Subject;
        window.Should().Be(new BondExerciseNoticeWindow("Call", CallDate, 101m,
            new DateOnly(2026, 11, 16), new DateOnly(2026, 12, 16), status, daysToDeadline));
        result.Diagnostics.Should().Contain(BondExerciseNoticeWatch.AuthorityDiagnostic);
        if (status == BondExerciseNoticeStatus.DeadlineElapsed)
            result.Diagnostics.Should().Contain(BondExerciseNoticeWatch.ElapsedDiagnostic);
        else
            result.Diagnostics.Should().NotContain(BondExerciseNoticeWatch.ElapsedDiagnostic);
    }

    [Fact]
    public void WindowOpeningAfterTheHorizon_IsLeftOut()
    {
        // Earliest notice 2026-11-16 is after 2026-10-01 + 30 days.
        var result = BondExerciseNoticeWatch.Evaluate(Bond(Calls(European(30, 60, "Calendar"))), Calendars,
            new DateOnly(2026, 10, 1), 30);

        result.IsBlocked.Should().BeFalse();
        result.Windows.Should().BeEmpty();
    }

    [Fact]
    public void ExercisedDateInThePast_IsNoLongerAWindow()
    {
        var result = BondExerciseNoticeWatch.Evaluate(Bond(Calls(European(30, 60, "Calendar"))), Calendars,
            new DateOnly(2027, 1, 20), 90);

        result.IsBlocked.Should().BeFalse();
        result.Windows.Should().BeEmpty();
    }

    [Fact]
    public void BusinessDayNotice_UsesTheDeclaredCalendar_AndMatchesTheAnalyticsScenario()
    {
        var security = Bond(Calls(European(10, null, "Business")));
        var asOf = new DateOnly(2026, 12, 1);

        var result = BondExerciseNoticeWatch.Evaluate(security, Calendars, asOf, 60);

        result.Blockers.Should().BeEmpty();
        var window = result.Windows.Should().ContainSingle().Subject;
        // Ten weekday-only business days before Friday 2027-01-15 is Friday 2027-01-01.
        window.LatestNoticeDate.Should().Be(new DateOnly(2027, 1, 1));
        window.EarliestNoticeDate.Should().BeNull();
        window.Status.Should().Be(BondExerciseNoticeStatus.Open, "a minimum-only notice may be given any time up to its deadline");
        window.LatestNoticeDate.Should().Be(AnalyticsScenarios(security, asOf).Single().LatestNoticeDate);
    }

    [Fact]
    public void BusinessDayNotice_WithoutTheDeclaredCalendar_Refuses()
    {
        var result = BondExerciseNoticeWatch.Evaluate(Bond(Calls(European(10, null, "Business"))), null,
            new DateOnly(2026, 12, 1), 60);

        result.IsBlocked.Should().BeTrue();
        result.Windows.Should().BeEmpty();
    }

    [Fact]
    public void BermudanCall_ExpandsToEachExerciseDate_AndTheHorizonKeepsTheNearOnes()
    {
        var security = Bond(Calls(new Dictionary<string, object?>
        {
            ["callDate"] = "2027-01-15",
            ["callEndDate"] = "2029-01-15",
            ["callPrice"] = 100m,
            ["exerciseStyle"] = "Bermudan",
            ["priceBasis"] = "PercentOfPar",
            ["minimumNoticeDays"] = 30,
            ["noticeDayType"] = "Calendar"
        }));
        var asOf = new DateOnly(2026, 12, 1);

        var result = BondExerciseNoticeWatch.Evaluate(security, Calendars, asOf, 250);

        result.Blockers.Should().BeEmpty();
        result.Windows.Select(static window => window.ExerciseDate).Should().Equal(
            new DateOnly(2027, 1, 15), new DateOnly(2027, 7, 15));
        result.Windows.Select(static window => window.LatestNoticeDate).Should().Equal(
            new DateOnly(2026, 12, 16), new DateOnly(2027, 6, 15));
        result.Windows.Should().OnlyContain(window => window.Status == BondExerciseNoticeStatus.Open);
        AnalyticsScenarios(security, asOf).Should().HaveCountGreaterThan(result.Windows.Count,
            "later Bermudan dates stay in analytics but fall outside the notice horizon");
    }

    [Fact]
    public void HolderPut_IsReportedAsAPutWindow()
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
        var result = BondExerciseNoticeWatch.Evaluate(Bond(puts: [put]), Calendars, new DateOnly(2026, 12, 15), 30);

        result.Blockers.Should().BeEmpty();
        result.Windows.Should().ContainSingle().Which.Should().Be(new BondExerciseNoticeWindow("Put", CallDate, 100m,
            new DateOnly(2026, 12, 1), new DateOnly(2026, 12, 31), BondExerciseNoticeStatus.Open, 16));
    }

    [Fact]
    public void EntryWithoutNoticeBounds_IsNotStated_AndAnchoredOnTheExerciseDate()
    {
        var security = Bond(Calls(European(null, null, null)));

        var near = BondExerciseNoticeWatch.Evaluate(security, Calendars, new DateOnly(2026, 12, 1), 60);
        var far = BondExerciseNoticeWatch.Evaluate(security, Calendars, new DateOnly(2026, 10, 1), 60);

        near.Windows.Should().ContainSingle().Which.Should().Be(new BondExerciseNoticeWindow("Call", CallDate, 101m,
            null, null, BondExerciseNoticeStatus.NotStated, null));
        far.Windows.Should().BeEmpty();
    }

    [Fact]
    public void BondWithoutDeclaredSchedules_HasNoWindowsAndNoRefusal()
    {
        var result = BondExerciseNoticeWatch.Evaluate(Bond(), Calendars, new DateOnly(2026, 12, 1), 90);

        result.IsBlocked.Should().BeFalse();
        result.Windows.Should().BeEmpty();
    }

    [Fact]
    public void ScheduleOnARowStampedBelowItsVersion_IsRefused_NotDecodedAsDeclared()
    {
        var result = BondExerciseNoticeWatch.Evaluate(Bond(Calls(European(30, 60, "Calendar")), stamp: 2),
            Calendars, new DateOnly(2026, 12, 1), 90);

        result.Blockers.Should().ContainSingle().Which.Code.Should().Be("ExerciseScheduleRefused");
        result.Blockers[0].Reason.Should().Contain("version 3");
        result.Windows.Should().BeEmpty();
    }

    [Fact]
    public void CallableFlagWithoutADeclaredSchedule_Refuses()
    {
        var result = BondExerciseNoticeWatch.Evaluate(Bond(isCallable: true), Calendars, new DateOnly(2026, 12, 1), 90);

        result.Blockers.Should().ContainSingle().Which.Code.Should().Be("ExerciseScheduleRefused");
        result.Windows.Should().BeEmpty();
    }

    [Fact]
    public void DeclaredContractualCompanion_Refuses()
    {
        var security = Bond(Calls(European(30, 60, "Calendar")), stamp: 4,
            extra: new() { ["cashFlowContract"] = new { version = 1 } });

        BondExerciseNoticeWatch.Evaluate(security, Calendars, new DateOnly(2026, 12, 1), 90)
            .Blockers.Should().ContainSingle().Which.Code.Should().Be("UnsupportedCashFlowContract");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(367)]
    public void HorizonOutsideBounds_Refuses(int horizon)
        => BondExerciseNoticeWatch.Evaluate(Bond(Calls(European(30, 60, "Calendar"))), Calendars,
                new DateOnly(2026, 12, 1), horizon)
            .Blockers.Should().ContainSingle().Which.Code.Should().Be("InvalidHorizon");

    [Fact]
    public void EvaluationOnOrAfterMaturity_Refuses()
        => BondExerciseNoticeWatch.Evaluate(Bond(Calls(European(30, 60, "Calendar"))), Calendars,
                new DateOnly(2031, 1, 15), 90)
            .Blockers.Should().ContainSingle().Which.Code.Should().Be("EvaluationOutsideLife");

    [Fact]
    public void NonBondAssetClass_Refuses()
    {
        var security = Bond(Calls(European(30, 60, "Calendar"))) with { AssetClass = "Equity" };

        BondExerciseNoticeWatch.Evaluate(security, Calendars, new DateOnly(2026, 12, 1), 90)
            .Blockers.Should().ContainSingle().Which.Code.Should().Be("UnsupportedAssetClass");
    }

    [Fact]
    public async Task ReadService_ReturnsNullForUnknownSecurity_AndRefusesTermsNotYetEffective()
    {
        var security = Bond(Calls(European(30, 60, "Calendar")));
        var query = Substitute.For<ISecurityMasterQueryService>();
        query.GetByIdAsync(security.SecurityId, Arg.Any<CancellationToken>()).Returns(security);
        var service = new BondExerciseNoticeReadService(query, Calendars);

        (await service.GetAsync(Guid.NewGuid(), new DateTimeOffset(2026, 12, 1, 0, 0, 0, TimeSpan.Zero), 90)).Should().BeNull();
        var early = await service.GetAsync(security.SecurityId, new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), 90);
        early!.Blockers.Should().ContainSingle().Which.Code.Should().Be("TermsNotEffective");
        var current = await service.GetAsync(security.SecurityId, new DateTimeOffset(2026, 12, 1, 0, 0, 0, TimeSpan.Zero), 90);
        current!.Windows.Should().ContainSingle().Which.Status.Should().Be(BondExerciseNoticeStatus.Open);
        current.SecurityVersion.Should().Be(security.Version);
    }

    [Fact]
    public async Task ReadService_WithKnowledgeCutoff_ReadsTheRecordedRow_OrRefusesWhenNoneWasEligible()
    {
        var security = Bond(Calls(European(30, 60, "Calendar")));
        var knownAt = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
        var asOf = new DateTimeOffset(2026, 12, 1, 0, 0, 0, TimeSpan.Zero);
        var query = Substitute.For<ISecurityMasterQueryService>();
        query.GetByIdAsync(security.SecurityId, Arg.Any<CancellationToken>()).Returns(security);
        query.GetRecordedByIdAsOfAsync(security.SecurityId, knownAt, Arg.Any<CancellationToken>()).Returns(security);
        var service = new BondExerciseNoticeReadService(query, Calendars);

        (await service.GetAsync(security.SecurityId, asOf, 90, knownAt))!.Windows.Should().ContainSingle();
        (await service.GetAsync(security.SecurityId, asOf, 90, knownAt.AddDays(-1)))!
            .Blockers.Should().ContainSingle().Which.Code.Should().Be("HistoricalTermsUnavailable");
    }

    private static IReadOnlyList<BondRedemptionScenario> AnalyticsScenarios(SecurityDetailDto security, DateOnly asOf)
    {
        var terms = StructuredCashFlowTermsResolver.Resolve(security);
        var declared = ScheduleSpecFactory.Resolve(security, terms, Calendars);
        declared.Status.Should().Be(DeclaredScheduleStatus.Declared);
        var schedule = ScheduleGenerator.Generate(declared.Spec!);
        BondContractualEconomics.TryBuild(security.SecurityId, terms, declared, schedule, asOf, 0.05m, false,
            BondYieldConvention.NominalPeriodic, out var flows, out _, out var reason).Should().BeTrue(reason);
        BondExerciseEconomics.TryResolveScenarios(security, terms, declared, schedule, asOf, 0.05m,
            BondYieldConvention.NominalPeriodic, 2, flows, Calendars, out var scenarios, out reason).Should().BeTrue(reason);
        return scenarios;
    }

    private static Dictionary<string, object?>[] Calls(params Dictionary<string, object?>[] entries) => entries;

    private static Dictionary<string, object?> European(int? minimum, int? maximum, string? dayType)
    {
        var entry = new Dictionary<string, object?>
        {
            ["callDate"] = "2027-01-15",
            ["callPrice"] = 101m,
            ["exerciseStyle"] = "European",
            ["priceBasis"] = "PercentOfPar"
        };
        if (minimum is not null)
            entry["minimumNoticeDays"] = minimum;
        if (maximum is not null)
            entry["maximumNoticeDays"] = maximum;
        if (dayType is not null)
            entry["noticeDayType"] = dayType;
        return entry;
    }

    internal static SecurityDetailDto Bond(Dictionary<string, object?>[]? calls = null,
        Dictionary<string, object?>[]? puts = null, int stamp = 3, bool? isCallable = null,
        Dictionary<string, object?>? extra = null)
    {
        var payload = new Dictionary<string, object?>
        {
            ["assetClassTermsVersion"] = stamp,
            ["issueDate"] = "2026-01-15",
            ["maturity"] = "2031-01-15",
            ["couponType"] = "Fixed",
            ["couponRate"] = 5m,
            ["dayCount"] = "30/360",
            ["par"] = 100m,
            ["paymentFrequency"] = "SemiAnnual",
            ["isCallable"] = isCallable ?? calls is { Length: > 0 },
            ["scheduleConventions"] = new
            {
                accrualAdjusted = false,
                couponRateUnit = "Percent",
                couponCashBasis = "Level",
                paymentLagBusinessDays = 0,
                paymentCalendars = new[] { "WEEKENDS" }
            }
        };
        if (calls is { Length: > 0 })
        {
            payload[SecurityVersionedTerms.CallScheduleKey] = calls;
            payload["callDate"] = calls[0]["callDate"];
        }
        if (puts is { Length: > 0 })
            payload[SecurityVersionedTerms.PutScheduleKey] = puts;
        foreach (var (key, value) in extra ?? [])
            payload[key] = value;
        return new(Guid.NewGuid(), "Bond", SecurityStatusDto.Active, "Notice-watch bond", "USD",
            JsonSerializer.SerializeToElement(new { }), JsonSerializer.SerializeToElement(payload), [], [], 3, RecordedAt, null);
    }
}
