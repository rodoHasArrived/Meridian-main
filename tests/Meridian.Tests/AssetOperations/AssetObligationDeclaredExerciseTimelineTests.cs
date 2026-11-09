using System.Text.Json;
using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.Calendars;
using Meridian.Contracts.FixedIncome;
using Meridian.Contracts.SecurityMaster;
using Meridian.Instruments.AssetOperations;
using Meridian.Instruments.FixedIncome;
using Meridian.Storage.AssetOperations;
using Meridian.Storage.SecurityMaster;
using Meridian.Tests.FixedIncome;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Meridian.Tests.AssetOperations;

/// <summary>
/// The obligation timeline of a Bond that declares a call or put schedule: the five-year 5% semiannual
/// bullet of <see cref="BondExerciseNoticeWatchTests"/>, issued 2026-01-15, read through the asset
/// operations read service with the shared Security Master cash-flow calculation. The declared schedule
/// is the authority for the bond's option events: they come from <see cref="BondExerciseNoticeWatch"/>,
/// the scalar <c>CallPutReview</c> probe is not consulted, and a schedule that cannot be resolved is a
/// typed blocked outcome with no option event.
/// </summary>
[Trait("Category", "Unit")]
public sealed class AssetObligationDeclaredExerciseTimelineTests
{
    private const string DeclaredStamp = "asset-obligation-declared-v3+asset-terms-obligations-timeline-declared-v1";
    private static readonly DateTimeOffset AsOf = new(2026, 12, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly IBusinessDayCalendarSource Calendars =
        new StaticBusinessDayCalendarSource([BusinessDayCalendars.WeekendsOnly]);

    [Fact]
    public async Task DeclaredCall_WithAScalarCallDate_RaisesTheDeclaredEventsOnce_AndNoScalarReview()
    {
        // The fixture also carries the scalar callDate 2027-01-15: the same exercise must not appear twice.
        var security = BondExerciseNoticeWatchTests.Bond([EuropeanCall(30, 60, "Calendar")]);

        var detail = await ReadAsync(security);

        var timeline = detail.TermsObligationsTimeline!;
        timeline.EngineVersion.Should().Be(DeclaredStamp);
        OptionEvents(timeline).Should().Equal(
            ("IssuerCallNoticeDeadline", new DateOnly(2026, 12, 16)),
            ("IssuerCallExercise", new DateOnly(2027, 1, 15)));
        timeline.Events.Should().NotContain(row => row.EventKind == "CallPutReview");

        var exercise = timeline.Events.Single(row => row.EventKind == "IssuerCallExercise");
        exercise.EventLane.Should().Be("Obligations");
        exercise.Status.Should().Be("Projected");
        exercise.ExpectedAmount.Should().BeNull("a redemption price is a percent of par, not a cash amount");
        exercise.Summary.Should().Be("Issuer may redeem at 101 percent of par on this declared call date.");
        var payload = exercise.ExtensionPayload!.Value;
        payload.GetProperty("engineVersion").GetString().Should().Be("asset-terms-obligations-timeline-declared-v1");
        payload.GetProperty("exerciseKind").GetString().Should().Be("Call");
        payload.GetProperty("exerciseDate").GetString().Should().Be("2027-01-15");
        payload.GetProperty("redemptionPrice").GetDecimal().Should().Be(101m);
        payload.GetProperty("earliestNoticeDate").GetString().Should().Be("2026-11-16");
        payload.GetProperty("latestNoticeDate").GetString().Should().Be("2026-12-16");
        payload.GetProperty("noticeStatus").GetString().Should().Be("Open");
        payload.GetProperty("daysToDeadline").GetInt32().Should().Be(15);

        var deadline = timeline.Events.Single(row => row.EventKind == "IssuerCallNoticeDeadline");
        deadline.Status.Should().Be("Projected");
        deadline.Summary.Should().Be(
            "Last day to give notice for the 2027-01-15 declared call date. The notice window opens on 2026-11-16.");

        var outcome = timeline.ExtensionPayload!.Value.GetProperty("declaredExercise");
        outcome.GetProperty("status").GetString().Should().Be("Resolved");
        outcome.GetProperty("windowCount").GetInt32().Should().Be(1);
        outcome.GetProperty("blockers").GetArrayLength().Should().Be(0);
        timeline.Warnings.Should().NotContain(warning => warning.Contains("Declared call/put schedule", StringComparison.Ordinal));
    }

    [Fact]
    public async Task DeclaredPut_WithoutAScalarDate_RaisesAHolderPutAndItsNoticeDeadline()
    {
        var detail = await ReadAsync(BondExerciseNoticeWatchTests.Bond(puts: [Put()]));

        var timeline = detail.TermsObligationsTimeline!;
        timeline.EngineVersion.Should().Be(DeclaredStamp);
        OptionEvents(timeline).Should().Equal(
            ("HolderPutNoticeDeadline", new DateOnly(2026, 12, 31)),
            ("HolderPutExercise", new DateOnly(2027, 1, 15)));
        timeline.Events.Single(row => row.EventKind == "HolderPutExercise").Summary
            .Should().Be("Holder may put at 100 percent of par on this declared put date.");
        timeline.Events.Single(row => row.EventKind == "HolderPutNoticeDeadline").Summary
            .Should().Be("Last day to give notice for the 2027-01-15 declared put date. The notice window opens on 2026-12-01.");
    }

    [Fact]
    public async Task CallAndPutOnTheSameDate_AreSeparateEvents()
    {
        var detail = await ReadAsync(BondExerciseNoticeWatchTests.Bond([EuropeanCall(30, 60, "Calendar")], [Put()]));

        OptionEvents(detail.TermsObligationsTimeline!).Should().BeEquivalentTo(new[]
        {
            ("IssuerCallNoticeDeadline", new DateOnly(2026, 12, 16)),
            ("HolderPutNoticeDeadline", new DateOnly(2026, 12, 31)),
            ("HolderPutExercise", new DateOnly(2027, 1, 15)),
            ("IssuerCallExercise", new DateOnly(2027, 1, 15))
        });
    }

    [Fact]
    public async Task BermudanCall_ShowsEveryRemainingExerciseDate_BeyondTheNoticeWatchHorizon_OnTheWatchDates()
    {
        var security = BondExerciseNoticeWatchTests.Bond([new Dictionary<string, object?>
        {
            ["callDate"] = "2027-01-15",
            ["callEndDate"] = "2029-01-15",
            ["callPrice"] = 100m,
            ["exerciseStyle"] = "Bermudan",
            ["priceBasis"] = "PercentOfPar",
            ["minimumNoticeDays"] = 30,
            ["noticeDayType"] = "Calendar"
        }]);

        var detail = await ReadAsync(security);

        var events = OptionEvents(detail.TermsObligationsTimeline!);
        events.Where(static row => row.Kind == "IssuerCallExercise").Select(static row => row.Date).Should().Equal(
            new DateOnly(2027, 1, 15), new DateOnly(2027, 7, 15), new DateOnly(2028, 1, 15),
            new DateOnly(2028, 7, 15), new DateOnly(2029, 1, 15));
        events.Where(static row => row.Kind == "IssuerCallNoticeDeadline").Select(static row => row.Date).Should().Equal(
            new DateOnly(2026, 12, 16), new DateOnly(2027, 6, 15), new DateOnly(2027, 12, 16),
            new DateOnly(2028, 6, 15), new DateOnly(2028, 12, 16));

        // The dates are the notice watch's own: the timeline adds no date arithmetic.
        var watch = BondExerciseNoticeWatch.EvaluateToMaturity(security, Calendars, DateOnly.FromDateTime(AsOf.UtcDateTime));
        events.Where(static row => row.Kind == "IssuerCallExercise").Select(static row => row.Date)
            .Should().Equal(watch.Windows.Select(static window => window.ExerciseDate));
        events.Where(static row => row.Kind == "IssuerCallNoticeDeadline").Select(static row => row.Date)
            .Should().Equal(watch.Windows.Select(static window => window.LatestNoticeDate!.Value));
        detail.TermsObligationsTimeline!.Events.Select(static row => row.TimelineEventId).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task TwoExercisesSharingANoticeDeadline_KeepSeparateDeadlineEvents()
    {
        // 30 days before 2027-01-15 and 40 days before 2027-01-25 are both 2026-12-16.
        var later = EuropeanCall(40, null, "Calendar");
        later["callDate"] = "2027-01-25";
        var detail = await ReadAsync(BondExerciseNoticeWatchTests.Bond([EuropeanCall(30, null, "Calendar"), later]));

        var timeline = detail.TermsObligationsTimeline!;
        OptionEvents(timeline).Should().Equal(
            ("IssuerCallNoticeDeadline", new DateOnly(2026, 12, 16)),
            ("IssuerCallNoticeDeadline", new DateOnly(2026, 12, 16)),
            ("IssuerCallExercise", new DateOnly(2027, 1, 15)),
            ("IssuerCallExercise", new DateOnly(2027, 1, 25)));
        timeline.Events.Select(static row => row.TimelineEventId).Should().OnlyHaveUniqueItems();
        timeline.Events.Where(static row => row.EventKind == "IssuerCallNoticeDeadline")
            .Select(static row => row.ExtensionPayload!.Value.GetProperty("exerciseDate").GetString())
            .Should().BeEquivalentTo("2027-01-15", "2027-01-25");
    }

    [Fact]
    public async Task TwoEntriesAdjustedOntoOneExerciseDate_KeepDistinctEventIdentities()
    {
        // Saturday 2027-01-16 and Sunday 2027-01-17 both roll Following to Monday 2027-01-18, with the
        // same notice bounds: kind, adjusted exercise date and deadline are all equal.
        var saturday = EuropeanCall(30, null, "Calendar");
        saturday["callDate"] = "2027-01-16";
        var sunday = EuropeanCall(30, null, "Calendar");
        sunday["callDate"] = "2027-01-17";
        sunday["callPrice"] = 100.5m;
        var security = WithConventions(BondExerciseNoticeWatchTests.Bond([saturday, sunday]), new
        {
            accrualAdjusted = false,
            businessDayConvention = "Following",
            couponRateUnit = "Percent",
            couponCashBasis = "Level",
            paymentCalendars = new[] { "WEEKENDS" }
        });

        var detail = await ReadAsync(security);
        var again = await ReadAsync(security);

        var timeline = detail.TermsObligationsTimeline!;
        OptionEvents(timeline).Should().Equal(
            ("IssuerCallNoticeDeadline", new DateOnly(2026, 12, 19)),
            ("IssuerCallNoticeDeadline", new DateOnly(2026, 12, 19)),
            ("IssuerCallExercise", new DateOnly(2027, 1, 18)),
            ("IssuerCallExercise", new DateOnly(2027, 1, 18)));
        timeline.Events.Select(static row => row.TimelineEventId).Should().OnlyHaveUniqueItems();
        again.TermsObligationsTimeline!.Events.Select(static row => row.TimelineEventId)
            .Should().Equal(timeline.Events.Select(static row => row.TimelineEventId), "identities are deterministic across reads");
    }

    [Fact]
    public async Task BusinessDayNotice_UsesTheDeclaredCalendar()
    {
        var detail = await ReadAsync(BondExerciseNoticeWatchTests.Bond([EuropeanCall(10, null, "Business")]));

        // Ten weekday-only business days before Friday 2027-01-15 is Friday 2027-01-01.
        OptionEvents(detail.TermsObligationsTimeline!).Should().Equal(
            ("IssuerCallNoticeDeadline", new DateOnly(2027, 1, 1)),
            ("IssuerCallExercise", new DateOnly(2027, 1, 15)));
        detail.TermsObligationsTimeline!.Events.Single(row => row.EventKind == "IssuerCallNoticeDeadline").Summary
            .Should().Be("Last day to give notice for the 2027-01-15 declared call date.");
    }

    [Fact]
    public async Task EntryWithoutNoticeBounds_RaisesTheExerciseOnly()
    {
        var detail = await ReadAsync(BondExerciseNoticeWatchTests.Bond([EuropeanCall(null, null, null)]));

        OptionEvents(detail.TermsObligationsTimeline!).Should().Equal(("IssuerCallExercise", new DateOnly(2027, 1, 15)));
        detail.TermsObligationsTimeline!.Events.Single(row => row.EventKind == "IssuerCallExercise")
            .ExtensionPayload!.Value.GetProperty("noticeStatus").GetString().Should().Be("NotStated");
    }

    [Fact]
    public async Task ElapsedNoticeDeadline_IsPastDueEvidence_WhileTheExerciseStaysProjected()
    {
        var detail = await ReadAsync(BondExerciseNoticeWatchTests.Bond([EuropeanCall(30, 60, "Calendar")]),
            asOf: new DateTimeOffset(2026, 12, 20, 0, 0, 0, TimeSpan.Zero));

        var timeline = detail.TermsObligationsTimeline!;
        var deadline = timeline.Events.Single(row => row.EventKind == "IssuerCallNoticeDeadline");
        deadline.EffectiveDate.Should().Be(new DateOnly(2026, 12, 16));
        deadline.Status.Should().Be("MissingEvidence");
        deadline.NextAction.Should().StartWith("Past due: Retain notice evidence");
        deadline.ExtensionPayload!.Value.GetProperty("noticeStatus").GetString().Should().Be("DeadlineElapsed");
        timeline.Events.Single(row => row.EventKind == "IssuerCallExercise").Status.Should().Be("Projected");
        timeline.Warnings.Should().Contain(BondExerciseNoticeWatch.ElapsedDiagnostic);
    }

    [Fact]
    public async Task ReadsAreStable_EventIdentitiesDoNotChangeBetweenReads()
    {
        var security = BondExerciseNoticeWatchTests.Bond([EuropeanCall(30, 60, "Calendar")], [Put()]);

        var first = await ReadAsync(security);
        var again = await ReadAsync(security);

        JsonSerializer.Serialize(again.TermsObligationsTimeline).Should().Be(JsonSerializer.Serialize(first.TermsObligationsTimeline));
    }

    [Fact]
    public async Task DeclaredRunAndFlowStamps_KeepTheirValues()
    {
        var detail = await ReadAsync(BondExerciseNoticeWatchTests.Bond([EuropeanCall(30, 60, "Calendar")]));

        detail.CashFlowProjectionRuns.Should().ContainSingle().Which.EngineVersion.Should().Be("asset-obligation-declared-v3");
        // A fixed bond's flows carry no rate evidence, so each is stamped v2 while the run is v3.
        detail.ProjectedCashFlows.Should().NotBeEmpty().And.OnlyContain(flow =>
            flow.ExtensionPayload!.Value.GetProperty("engineVersion").GetString() == "asset-obligation-declared-v2");
    }

    public static TheoryData<string, Dictionary<string, object?>?, bool, string> UnresolvableSchedules() => new()
    {
        // Business-day notice with no calendar source on the host.
        { "no calendar source", null, false, "DeclaredScheduleRequired" },
        // A scalar mandatory put beside a declared call is refused by the shared resolver, not shown as a review.
        { "mandatory put scalar", new() { ["mandatoryPutDate"] = "2027-03-15" }, true, "ExerciseScheduleRefused" },
        { "pre-refund scalar", new() { ["preRefundDate"] = "2027-03-15" }, true, "ExerciseScheduleRefused" },
        // Structures the resolver refuses.
        { "floating coupon", new() { ["couponType"] = "Floating" }, true, "UnsupportedCouponStructure" },
        { "factored principal", new() { ["currentFactor"] = 0.9m }, true, "UnsupportedPrincipalStructure" }
    };

    [Theory]
    [MemberData(nameof(UnresolvableSchedules))]
    public async Task UnresolvableDeclaredSchedule_IsATypedBlockedOutcome_WithNoOptionEvent_AndNoScalarFallback(
        string because, Dictionary<string, object?>? extra, bool withCalendars, string code)
    {
        var security = BondExerciseNoticeWatchTests.Bond([EuropeanCall(10, null, "Business")], extra: extra);

        var detail = await new AssetObligationProjectionService(calendars: withCalendars ? Calendars : null)
            .ProjectFromSecurityMasterAsync(security, new BondCalculationContext(AsOf), CompletedProjection(security));

        AssertBlocked(detail, code, because);
        detail.CashFlowProjectionRuns.Should().ContainSingle().Which.Status.Should().Be("Completed", because);
    }

    [Theory]
    [InlineData("American")]
    [InlineData("Continuous")]
    public async Task UnsupportedExerciseStyle_IsBlocked_NotShown(string style)
    {
        var call = EuropeanCall(30, 60, "Calendar");
        call["exerciseStyle"] = style;
        call["callEndDate"] = "2029-01-15";

        var detail = await ReadAsync(BondExerciseNoticeWatchTests.Bond([call]));

        var blocker = AssertBlocked(detail, null, style);
        blocker.Should().BeOneOf("ExerciseScheduleRefused", "InvalidEconomicSchedule");
    }

    [Theory]
    [InlineData("[42]")]
    [InlineData("[{\"callDate\":\"not-a-date\",\"callPrice\":101,\"exerciseStyle\":\"European\"}]")]
    [InlineData("[{\"callDate\":\"2027-01-15\",\"callPrice\":\"101\",\"exerciseStyle\":\"European\"}]")]
    [InlineData("[{\"callDate\":\"2027-01-15\",\"callPrice\":1e400,\"exerciseStyle\":\"European\"}]")]
    public async Task MalformedDeclaredSchedule_NeverThrows_AndNeverFallsBackToTheScalar(string callScheduleJson)
    {
        var security = BondExerciseNoticeWatchTests.Bond(extra: new()
        {
            [SecurityVersionedTerms.CallScheduleKey] = JsonSerializer.Deserialize<JsonElement>(callScheduleJson),
            ["callDate"] = "2027-01-15",
            ["isCallable"] = true
        });

        var detail = await ReadAsync(security, completed: false);

        var timeline = detail.TermsObligationsTimeline!;
        timeline.EngineVersion.Should().EndWith("+asset-terms-obligations-timeline-declared-v1");
        OptionEvents(timeline).Should().BeEmpty();
        timeline.ExtensionPayload!.Value.GetProperty("declaredExercise").GetProperty("status").GetString().Should().Be("Blocked");
    }

    [Fact]
    public void RetainedScheduleTheSharedResolverRejects_IsBlocked_NotAFault()
    {
        // The shared terms resolver throws for this owned schedule; the watch turns it into a blocker.
        var security = BondExerciseNoticeWatchTests.Bond([EuropeanCall(30, 60, "Calendar")], extra: new()
        {
            ["profileFields"] = new Dictionary<string, object?>
            {
                ["principalSchedule"] = JsonSerializer.Deserialize<JsonElement>("[{\"paymentDate\":\"2027-01-15\",\"amount\":0}]")
            },
            ["principalSchedule"] = Array.Empty<object>()
        });
        var service = new AssetObligationProjectionService(calendars: Calendars);

        var outcome = service.ResolveDeclaredExercise(security, new BondCalculationContext(AsOf));

        outcome!.Windows.Should().BeEmpty();
        outcome.Blockers.Should().ContainSingle().Which.Code.Should().Be("InvalidEconomicSchedule");
    }

    [Theory]
    [InlineData("[null]")]
    [InlineData("[null, null]")]
    [InlineData("[{}, null]")]
    public async Task AllUnsetSchedule_DeclaresNothing_SoTheScalarReviewStays(string callScheduleJson)
    {
        // A write admits an array of unset entries as declaring nothing. Reading it as a declaration
        // would resolve to zero scenarios and silently drop the bond's scalar review.
        var security = BondExerciseNoticeWatchTests.Bond(extra: new()
        {
            [SecurityVersionedTerms.CallScheduleKey] = JsonSerializer.Deserialize<JsonElement>(callScheduleJson),
            ["callDate"] = "2027-01-15",
            ["isCallable"] = true
        });

        var detail = await ReadAsync(security, completed: false);

        var timeline = detail.TermsObligationsTimeline!;
        timeline.EngineVersion.Should().EndWith("+asset-terms-obligations-timeline-v1");
        timeline.ExtensionPayload.Should().BeNull();
        OptionEvents(timeline).Should().Equal(("CallPutReview", new DateOnly(2027, 1, 15)));
    }

    [Fact]
    public async Task UnsetEntryBesideARealOne_IsIgnored_AndTheRealEntryResolves()
    {
        var security = BondExerciseNoticeWatchTests.Bond(extra: new()
        {
            [SecurityVersionedTerms.CallScheduleKey] = new object?[] { null, EuropeanCall(30, 60, "Calendar") },
            ["callDate"] = "2027-01-15",
            ["isCallable"] = true
        });

        var detail = await ReadAsync(security);

        detail.TermsObligationsTimeline!.EngineVersion.Should().Be(DeclaredStamp);
        OptionEvents(detail.TermsObligationsTimeline).Should().Equal(
            ("IssuerCallNoticeDeadline", new DateOnly(2026, 12, 16)),
            ("IssuerCallExercise", new DateOnly(2027, 1, 15)));
    }

    [Fact]
    public async Task CutoverPolicyBlock_BlocksTheDeclaredExerciseEvents()
    {
        var security = BondExerciseNoticeWatchTests.Bond([EuropeanCall(30, 60, "Calendar")]);
        var policy = Substitute.For<IBondCalculationPolicy>();
        policy.GetBlockerAsync(security, Arg.Any<CancellationToken>()).Returns("Reviewed cutover blocks this bond.");

        var detail = await ReadAsync(security, policy, completed: false);

        AssertBlocked(detail, "CalculationPolicyBlocked", "the retained cutover policy gates every bond calculation surface");
        detail.TermsObligationsTimeline!.Warnings.Should().Contain(warning =>
            warning.Contains("Reviewed cutover blocks this bond.", StringComparison.Ordinal));
        detail.CashFlowProjectionRuns.Should().ContainSingle().Which.Status.Should().Be("Blocked");
    }

    [Fact]
    public async Task CutoverPolicyThatPermits_LeavesTheDeclaredEvents()
    {
        var security = BondExerciseNoticeWatchTests.Bond([EuropeanCall(30, 60, "Calendar")]);
        var policy = Substitute.For<IBondCalculationPolicy>();
        policy.GetBlockerAsync(security, Arg.Any<CancellationToken>()).Returns((string?)null);

        var detail = await ReadAsync(security, policy);

        OptionEvents(detail.TermsObligationsTimeline!).Should().HaveCount(2);
    }

    [Fact]
    public void SynchronousEntryPoint_UnderALivePolicy_BlocksTheDeclaredExerciseEvents()
    {
        var security = BondExerciseNoticeWatchTests.Bond([EuropeanCall(30, 60, "Calendar")]);
        var service = new AssetObligationProjectionService(Substitute.For<IBondCalculationPolicy>(), calendars: Calendars);

        var detail = service.ProjectFromSecurityMaster(security, new BondCalculationContext(AsOf), CompletedProjection(security));

        AssertBlocked(detail, "CalculationPolicyBlocked", "the synchronous entry point cannot evaluate the live policy");
    }

    [Fact]
    public async Task BlockedRun_BlocksTheDeclaredExerciseEvents_EvenWhenTheScheduleResolves()
    {
        // The cutover policy reaches the projection as a blocked run; so does a missing shared calculation.
        var security = BondExerciseNoticeWatchTests.Bond([EuropeanCall(30, 60, "Calendar")]);

        var detail = await new AssetObligationProjectionService(calendars: Calendars)
            .ProjectFromSecurityMasterAsync(security, new BondCalculationContext(AsOf));

        AssertBlocked(detail, "ProjectionBlocked", "a blocked run exposes no projected economics");
    }

    [Theory]
    [InlineData(1, null)]
    [InlineData(-30, -1)]
    [InlineData(-30, 0)]
    public async Task TermsNotEffectiveAtTheEvaluationDate_AreBlocked(int effectiveFromOffsetDays, int? effectiveToOffsetDays)
    {
        var security = BondExerciseNoticeWatchTests.Bond([EuropeanCall(30, 60, "Calendar")]) with
        {
            EffectiveFrom = AsOf.AddDays(effectiveFromOffsetDays),
            EffectiveTo = effectiveToOffsetDays is { } offset ? AsOf.AddDays(offset) : null
        };

        var detail = await new AssetObligationProjectionService(calendars: Calendars)
            .ProjectFromSecurityMasterAsync(security, new BondCalculationContext(AsOf), CompletedProjection(security));

        AssertBlocked(detail, "TermsNotEffective", "the notice-watch read service refuses terms that are not effective at the evaluation date");
    }

    [Theory]
    [InlineData(0, null)]
    [InlineData(-30, 1)]
    public async Task TermsEffectiveAtTheEvaluationDate_Resolve(int effectiveFromOffsetDays, int? effectiveToOffsetDays)
    {
        // Effective from the evaluation instant itself, or until any instant after it.
        var security = BondExerciseNoticeWatchTests.Bond([EuropeanCall(30, 60, "Calendar")]) with
        {
            EffectiveFrom = AsOf.AddDays(effectiveFromOffsetDays),
            EffectiveTo = effectiveToOffsetDays is { } offset ? AsOf.AddDays(offset) : null
        };

        var detail = await new AssetObligationProjectionService(calendars: Calendars)
            .ProjectFromSecurityMasterAsync(security, new BondCalculationContext(AsOf), CompletedProjection(security));

        OptionEvents(detail.TermsObligationsTimeline!).Should().HaveCount(2);
    }

    [Fact]
    public async Task BondReferencePath_ForADeclaredScheduleWithoutConventions_IsBlocked_NotReadFromTheScalar()
    {
        // No scheduleConventions block, so the read service takes the legacy bond-reference path. The
        // declared call schedule is still the authority: the reference's scalar call date is not shown.
        var security = WithoutConventions(BondExerciseNoticeWatchTests.Bond([EuropeanCall(30, 60, "Calendar")]));
        var query = Substitute.For<ISecurityMasterQueryService>();
        query.GetByIdAsync(security.SecurityId, Arg.Any<CancellationToken>()).Returns(security);
        var bonds = Substitute.For<IBondReferenceService>();
        bonds.GetReferenceAsync(security.SecurityId, Arg.Any<CancellationToken>()).Returns(new BondReferenceDto(
            security.SecurityId, "Notice-watch bond", "USD", "Meridian Funding LLC", "Senior", "InternalCode:WATCH",
            new BondLifecycleDto(security.SecurityId, BondLifecycleStat.Active, new DateOnly(2026, 1, 15), new DateOnly(2027, 1, 15),
                new DateOnly(2031, 1, 15), true, 3, Par: 100m, PaymentFrequency: "SemiAnnual"),
            new BondAccrualConventionDto(security.SecurityId, "30/360", 2, "US", "Fixed", 5m, null, null, 3), 3));

        var detail = await new AssetOperationsReadService(securityMasterQueryService: query, bondReferenceService: bonds,
            calendars: Calendars).GetOperationsAsOfAsync(security.SecurityId, new BondCalculationContext(AsOf));

        detail!.TermsObligationsTimeline!.EngineVersion.Should().Be(
            "fixed-income-cash-flow-projection-v2+asset-terms-obligations-timeline-declared-v1");
        AssertBlocked(detail, "DeclaredScheduleRequired", "exercise dates are never inferred from legacy defaults");
        detail.ProjectedCashFlows.Should().NotBeEmpty("the legacy loop's cash flows are unaffected");
    }

    [Fact]
    public async Task PublishedProjection_KeepsItsOwnTermsTimeline()
    {
        // A reviewed publication is read back as stored: its timeline is derived from the published
        // terms payload, not from the live Security Master row, and stays on the v1 stamp.
        var security = BondExerciseNoticeWatchTests.Bond([EuropeanCall(30, 60, "Calendar")]);
        var synthesized = await ReadAsync(security);
        var stored = synthesized with { TermsObligationsTimeline = null };
        var store = Substitute.For<IAssetOperationsProjectionStore>();
        store.GetAsync(security.SecurityId, Arg.Any<CancellationToken>()).Returns(stored);
        var clock = Substitute.For<TimeProvider>();
        clock.GetUtcNow().Returns(AsOf);

        var detail = await new AssetOperationsReadService(store, clock: clock, calendars: Calendars)
            .GetOperationsAsOfAsync(security.SecurityId, new BondCalculationContext(AsOf));

        var timeline = detail!.TermsObligationsTimeline!;
        timeline.EngineVersion.Should().Be("asset-obligation-declared-v3+asset-terms-obligations-timeline-v1");
        timeline.ExtensionPayload.Should().BeNull();
        OptionEvents(timeline).Should().Equal(("CallPutReview", new DateOnly(2027, 1, 15)));
    }

    [Theory]
    [InlineData("Bond", 3, "callSchedule", "[{\"callDate\":\"2027-01-15\"}]", true)]
    [InlineData("Bond", 3, "putSchedule", "[{\"putDate\":\"2027-01-15\"}]", true)]
    [InlineData("Bond", 4, "callSchedule", "[{\"callDate\":\"2027-01-15\"}]", true)]
    [InlineData("Bond", 2, "callSchedule", "[{\"callDate\":\"2027-01-15\"}]", false)]
    [InlineData("Bond", 1, "putSchedule", "[{\"putDate\":\"2027-01-15\"}]", false)]
    [InlineData("Bond", 3, "callSchedule", "[]", false)]
    [InlineData("Bond", 3, "callSchedule", "[null]", false)]
    [InlineData("Bond", 3, "callSchedule", "[null, null]", false)]
    [InlineData("Bond", 3, "callSchedule", "[{}]", false)]
    [InlineData("Bond", 3, "putSchedule", "[null, {\"putDate\":null}]", false)]
    [InlineData("Bond", 3, "callSchedule", "[null, {\"callDate\":\"2027-01-15\"}]", true)]
    [InlineData("Bond", 3, "callSchedule", "[42]", true)]
    [InlineData("Bond", 3, "callSchedule", "null", false)]
    [InlineData("Bond", 3, "callSchedule", "{\"callDate\":\"2027-01-15\"}", false)]
    [InlineData("Bond", 3, "callDate", "\"2027-01-15\"", false)]
    [InlineData("CertificateOfDeposit", 3, "callSchedule", "[{\"callDate\":\"2027-01-15\"}]", false)]
    [InlineData("StructuredCredit", 4, "callSchedule", "[{\"callDate\":\"2027-01-15\"}]", false)]
    public void DeclaresExerciseSchedule_FollowsTheSharedStampPredicate_ForBondCouponStructuresOnly(
        string assetClass, int stamp, string key, string valueJson, bool expected)
    {
        var terms = new Dictionary<string, object?>
        {
            ["assetClassTermsVersion"] = stamp,
            [key] = JsonSerializer.Deserialize<JsonElement>(valueJson)
        };
        var security = new SecurityDetailDto(Guid.NewGuid(), assetClass, SecurityStatusDto.Active, "Predicate", "USD",
            JsonSerializer.SerializeToElement(new { }), JsonSerializer.SerializeToElement(terms), [], [], 1, AsOf.AddYears(-1), null);

        AssetObligationProjectionService.DeclaresExerciseSchedule(security).Should().Be(expected);
        new AssetObligationProjectionService(calendars: Calendars)
            .ResolveDeclaredExercise(security, new BondCalculationContext(AsOf)).Should().Match(result => (result != null) == expected);
    }

    /// <summary>Asserts the typed blocked outcome on every surface that reports it, and returns the blocker code.</summary>
    private static string AssertBlocked(AssetOperationsDetailDto detail, string? code, string because)
    {
        var timeline = detail.TermsObligationsTimeline!;
        timeline.EngineVersion.Should().EndWith("+asset-terms-obligations-timeline-declared-v1", because);
        OptionEvents(timeline).Should().BeEmpty(because);
        var outcome = timeline.ExtensionPayload!.Value.GetProperty("declaredExercise");
        outcome.GetProperty("status").GetString().Should().Be("Blocked", because);
        outcome.GetProperty("windowCount").GetInt32().Should().Be(0);
        var blocker = outcome.GetProperty("blockers").EnumerateArray().Should().ContainSingle().Subject;
        var actual = blocker.GetProperty("code").GetString()!;
        if (code is not null)
            actual.Should().Be(code, because);
        var reason = blocker.GetProperty("reason").GetString()!;
        var warning = $"Declared call/put schedule is blocked ({actual}): {reason} No call or put event is shown and the scalar call date is not used.";
        timeline.Warnings.Should().Contain(warning, because);
        timeline.Status.Should().Be("ReviewRequired");
        detail.Readiness.Warnings.Should().Contain(warning, because);
        return actual;
    }

    private static (string Kind, DateOnly Date)[] OptionEvents(AssetTermsObligationsTimelineDto timeline)
        => timeline.Events
            .Where(static row => row.EventKind.Contains("Call", StringComparison.Ordinal) || row.EventKind.Contains("Put", StringComparison.Ordinal))
            .Select(static row => (row.EventKind, row.EffectiveDate))
            .ToArray();

    private static Dictionary<string, object?> EuropeanCall(int? minimum, int? maximum, string? dayType)
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

    private static Dictionary<string, object?> Put() => new()
    {
        ["putDate"] = "2027-01-15",
        ["putPrice"] = 100m,
        ["exerciseStyle"] = "European",
        ["priceBasis"] = "PercentOfPar",
        ["minimumNoticeDays"] = 15,
        ["maximumNoticeDays"] = 45,
        ["noticeDayType"] = "Calendar"
    };

    private static SecurityDetailDto WithoutConventions(SecurityDetailDto security)
    {
        var terms = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(security.AssetSpecificTerms)!;
        terms.Remove(SecurityVersionedTerms.ScheduleConventionsKey);
        return security with { AssetSpecificTerms = JsonSerializer.SerializeToElement(terms) };
    }

    private static SecurityDetailDto WithConventions(SecurityDetailDto security, object block)
    {
        var terms = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(security.AssetSpecificTerms)!;
        terms[SecurityVersionedTerms.ScheduleConventionsKey] = JsonSerializer.SerializeToElement(block);
        return security with { AssetSpecificTerms = JsonSerializer.SerializeToElement(terms) };
    }

    /// <summary>A completed shared calculation, so the run is not what blocks the declared exercise.</summary>
    private static StructuredCashFlowProjectionDto CompletedProjection(SecurityDetailDto security)
        => new(security.SecurityId, StructuredCashFlowSourceKind.CalculatedBullet, StructuredCashFlowScenario.Base, AsOf, []);

    private static async Task<AssetOperationsDetailDto> ReadAsync(SecurityDetailDto security,
        IBondCalculationPolicy? policy = null, bool completed = true, DateTimeOffset? asOf = null)
    {
        var query = Substitute.For<ISecurityMasterQueryService>();
        query.GetByIdAsync(security.SecurityId, Arg.Any<CancellationToken>()).Returns(security);
        var service = new AssetOperationsReadService(securityMasterQueryService: query, cashFlows: CashFlows(security, query),
            bondPolicy: policy, calendars: Calendars);
        var detail = await service.GetOperationsAsOfAsync(security.SecurityId, new BondCalculationContext(asOf ?? AsOf));
        detail.Should().NotBeNull();
        if (completed)
            detail!.CashFlowProjectionRuns.Should().ContainSingle().Which.Status.Should().Be("Completed",
                string.Join("; ", detail.Readiness.Warnings));
        return detail!;
    }

    private static SecurityMasterCashFlowService CashFlows(SecurityDetailDto security, ISecurityMasterQueryService query)
    {
        var store = Substitute.For<ISecurityMasterCashFlowStore>();
        store.GetSourceAsync(security.SecurityId, Arg.Any<CancellationToken>()).Returns(new SecurityCashFlowSourceDto(
            security.SecurityId, StructuredCashFlowSourceKind.CalculatedBullet, security.EffectiveFrom, false, null, null));
        return new(Calendars, store, [], query, NullLogger<SecurityMasterCashFlowService>.Instance);
    }
}
