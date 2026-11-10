using System.Text.Json;
using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.Calendars;
using Meridian.Contracts.FixedIncome;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Workstation;
using Meridian.Instruments.AssetOperations;
using Meridian.Tests.FixedIncome;
using Meridian.Ui.Shared.Services;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using static Meridian.Tests.Ui.SecurityMasterWorkbenchScheduleBookHarness;

namespace Meridian.Tests.Ui;

/// <summary>
/// The Security Master workbench schedule book of a Bond that declares a call or put schedule
/// (ADR-025, increment 8b): the five-year 5% semiannual bullet of <see cref="BondExerciseNoticeWatchTests"/>,
/// issued 2026-01-15, read at 2026-12-01. The declared schedule is the authority for the bond's call
/// and put rows and for their share of the next lifecycle date: they come from the outcome the
/// obligation timeline uses, the scalar <c>call.firstCallDate</c> is not read, and a schedule that
/// cannot be resolved shows its blocker in the summary texts and no call or put row.
/// </summary>
[Trait("Category", "Unit")]
public sealed class SecurityMasterWorkbenchDeclaredExerciseScheduleBookTests
{
    private static readonly DateTimeOffset AsOf = new(2026, 12, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Maturity = new(2031, 1, 15);
    private static readonly IBusinessDayCalendarSource Calendars =
        new StaticBusinessDayCalendarSource([BusinessDayCalendars.WeekendsOnly]);
    private static readonly string[] OptionEventTypes = ["Call", "CallNoticeDeadline", "Put", "PutNoticeDeadline"];

    [Fact]
    public async Task DeclaredCall_WithAScalarCallDate_ShowsTheDeclaredRowsOnce_AndNoScalarRow()
    {
        // The fixture also carries the scalar callDate 2027-01-15: the same exercise must not appear twice.
        var snapshot = await ReadAsync(BondExerciseNoticeWatchTests.Bond([EuropeanCall(30, 60, "Calendar")]));

        var book = snapshot.ScheduleBook!;
        OptionEvents(book).Should().Equal(
            ("CallNoticeDeadline", new DateOnly(2026, 12, 16)),
            ("Call", new DateOnly(2027, 1, 15)));

        var exercise = book.Events.Single(static row => row.EventType == "Call");
        exercise.EventId.Should().Be("call-20270115");
        exercise.PayDate.Should().Be(new DateOnly(2027, 1, 15));
        exercise.ExpectedAmount.Should().BeNull("a redemption price is a percent of par, not a cash amount");
        exercise.PostingStatus.Should().Be("Projected");
        exercise.SourceReason.Should().Be("Issuer may redeem at 101 percent of par on this declared call date.");
        exercise.SourceSystem.Should().Be("golden-edm");
        exercise.SourceRecordId.Should().Be("edm-book-row");
        exercise.IsDerivedFromEconomicTerms.Should().BeFalse("the row is read from the declared schedule on the stored row");
        exercise.IsCurrentProjection.Should().BeTrue();
        exercise.Currency.Should().Be("USD");

        var deadline = book.Events.Single(static row => row.EventType == "CallNoticeDeadline");
        deadline.EventId.Should().Be("callnoticedeadline-20261216-20270115");
        deadline.PayDate.Should().BeNull("nothing is paid on a notice deadline");
        deadline.PostingStatus.Should().Be("Projected");
        deadline.SourceReason.Should().Be(
            "Last day to give notice for the 2027-01-15 declared call date. The notice window opens on 2026-11-16.");
        deadline.IsDerivedFromEconomicTerms.Should().BeFalse();

        book.NextLifecycleDate.Should().Be(new DateOnly(2026, 12, 16), "an open notice deadline is the next lifecycle date");
        snapshot.ScheduleSummary!.NextLifecycleDate.Should().Be(new DateOnly(2026, 12, 16));
        book.Summary.Should().Be("Cash-flow schedule support is available. Next lifecycle date 2026-12-16.");
        snapshot.ScheduleSummary.Summary.Should().Be(book.Summary);
        book.SourceSummary.Should().Be("Schedules follow golden-edm record edm-book-row as of 2026-01-15 00:00 UTC.");
        book.ProvenanceHistory.Select(static row => row.ProvenanceId).Should().Contain(
            ["event-call-20270115", "event-callnoticedeadline-20261216-20270115"]);
    }

    [Fact]
    public async Task ScalarCallDateThatDisagreesWithTheDeclaredSchedule_IsBlocked_AndTheScalarIsNotShown()
    {
        // The scalar (2026-12-05) precedes every declared date; a scalar read would make it "next".
        var security = BondExerciseNoticeWatchTests.Bond([EuropeanCall(null, null, null)], extra: new() { ["callDate"] = "2026-12-05" });
        (await EconomicFirstCallDateAsync(security)).Should().Be("2026-12-05", "the economic document still carries the scalar");

        var snapshot = await ReadAsync(security);

        AssertBlocked(snapshot, "ExerciseScheduleRefused", "the shared resolver refuses a scalar that contradicts the declared schedule");
    }

    [Fact]
    public async Task DeclaredPut_WithoutAScalarDate_ShowsAPutAndItsNoticeDeadline()
    {
        var snapshot = await ReadAsync(BondExerciseNoticeWatchTests.Bond(puts: [Put()]));

        var book = snapshot.ScheduleBook!;
        OptionEvents(book).Should().Equal(
            ("PutNoticeDeadline", new DateOnly(2026, 12, 31)),
            ("Put", new DateOnly(2027, 1, 15)));
        book.Events.Single(static row => row.EventType == "Put").EventId.Should().Be("put-20270115");
        book.Events.Single(static row => row.EventType == "Put").SourceReason
            .Should().Be("Holder may put at 100 percent of par on this declared put date.");
        book.Events.Single(static row => row.EventType == "PutNoticeDeadline").EventId.Should().Be("putnoticedeadline-20261231-20270115");
        book.Events.Single(static row => row.EventType == "PutNoticeDeadline").SourceReason
            .Should().Be("Last day to give notice for the 2027-01-15 declared put date. The notice window opens on 2026-12-01.");
        book.NextLifecycleDate.Should().Be(new DateOnly(2026, 12, 31));
    }

    [Fact]
    public async Task CallAndPutOnTheSameDate_AreSeparateRows()
    {
        var snapshot = await ReadAsync(BondExerciseNoticeWatchTests.Bond([EuropeanCall(30, 60, "Calendar")], [Put()]));

        OptionEvents(snapshot.ScheduleBook!).Should().Equal(
            ("CallNoticeDeadline", new DateOnly(2026, 12, 16)),
            ("PutNoticeDeadline", new DateOnly(2026, 12, 31)),
            ("Call", new DateOnly(2027, 1, 15)),
            ("Put", new DateOnly(2027, 1, 15)));
        snapshot.ScheduleBook!.Events.Select(static row => row.EventId).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task BermudanCall_ShowsEveryRemainingExerciseDate_OnTheWatchDates()
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

        var snapshot = await ReadAsync(security);

        var watch = BondExerciseNoticeWatch.EvaluateToMaturity(security, Calendars, DateOnly.FromDateTime(AsOf.UtcDateTime));
        watch.Windows.Should().HaveCountGreaterThan(2, "the grid runs past the notice watch's 366-day horizon");
        var book = snapshot.ScheduleBook!;
        book.Events.Where(static row => row.EventType == "Call").Select(static row => row.EffectiveDate)
            .Should().Equal(watch.Windows.Select(static window => window.ExerciseDate));
        book.Events.Where(static row => row.EventType == "CallNoticeDeadline").Select(static row => row.EffectiveDate)
            .Should().Equal(watch.Windows.Select(static window => window.LatestNoticeDate!.Value));
        book.Events.Select(static row => row.EventId).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task EntryWithoutNoticeBounds_ShowsTheExerciseOnly()
    {
        var snapshot = await ReadAsync(BondExerciseNoticeWatchTests.Bond([EuropeanCall(null, null, null)]));

        OptionEvents(snapshot.ScheduleBook!).Should().Equal(("Call", new DateOnly(2027, 1, 15)));
        snapshot.ScheduleBook!.NextLifecycleDate.Should().Be(new DateOnly(2027, 1, 15));
    }

    [Fact]
    public async Task BusinessDayNotice_UsesTheCalendarSourceTheServiceWasGiven()
    {
        var snapshot = await ReadAsync(BondExerciseNoticeWatchTests.Bond([EuropeanCall(10, null, "Business")]));

        // Ten weekday-only business days before Friday 2027-01-15 is Friday 2027-01-01.
        OptionEvents(snapshot.ScheduleBook!).Should().Equal(
            ("CallNoticeDeadline", new DateOnly(2027, 1, 1)),
            ("Call", new DateOnly(2027, 1, 15)));
        snapshot.ScheduleBook!.Events.Single(static row => row.EventType == "CallNoticeDeadline").SourceReason
            .Should().Be("Last day to give notice for the 2027-01-15 declared call date.");
    }

    [Fact]
    public async Task TwoExercisesSharingANoticeDeadline_KeepSeparateDeadlineRows()
    {
        // 30 days before 2027-01-15 and 40 days before 2027-01-25 are both 2026-12-16.
        var later = EuropeanCall(40, null, "Calendar");
        later["callDate"] = "2027-01-25";

        var snapshot = await ReadAsync(BondExerciseNoticeWatchTests.Bond([EuropeanCall(30, null, "Calendar"), later]));

        var book = snapshot.ScheduleBook!;
        book.Events.Where(static row => row.EventType == "CallNoticeDeadline").Select(static row => row.EventId)
            .Should().BeEquivalentTo("callnoticedeadline-20261216-20270115", "callnoticedeadline-20261216-20270125");
        book.Events.Select(static row => row.EventId).Should().OnlyHaveUniqueItems();
        book.ProvenanceHistory.Select(static row => row.ProvenanceId).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task TwoEntriesAdjustedOntoOneExerciseDate_KeepDistinctRowIdentities()
    {
        // Saturday 2027-01-16 and Sunday 2027-01-17 both roll Following to Monday 2027-01-18, with the
        // same notice bounds: type, adjusted exercise date and deadline are all equal.
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

        var book = (await ReadAsync(security)).ScheduleBook!;
        var again = (await ReadAsync(security)).ScheduleBook!;

        book.Events.Where(static row => OptionEventTypes.Contains(row.EventType)).Select(static row => row.EventId).Should().Equal(
            "callnoticedeadline-20261219-20270118",
            "callnoticedeadline-20261219-20270118-2",
            "call-20270118",
            "call-20270118-2");
        book.Events.Where(static row => row.EventType == "Call").Select(static row => row.SourceReason).Should().Equal(
            "Issuer may redeem at 101 percent of par on this declared call date.",
            "Issuer may redeem at 100.5 percent of par on this declared call date.");
        book.ProvenanceHistory.Select(static row => row.ProvenanceId).Should().OnlyHaveUniqueItems();
        again.Events.Select(static row => row.EventId).Should().Equal(book.Events.Select(static row => row.EventId), "identities are deterministic across reads");
    }

    [Fact]
    public async Task ElapsedNoticeDeadline_IsAReferenceRow_AndIsNeverTheNextLifecycleDate()
    {
        var snapshot = await ReadAsync(BondExerciseNoticeWatchTests.Bond([EuropeanCall(30, 60, "Calendar")]),
            asOf: new DateTimeOffset(2026, 12, 20, 0, 0, 0, TimeSpan.Zero));

        var book = snapshot.ScheduleBook!;
        var deadline = book.Events.Single(static row => row.EventType == "CallNoticeDeadline");
        deadline.EffectiveDate.Should().Be(new DateOnly(2026, 12, 16));
        deadline.PostingStatus.Should().Be("Reference", "a past scalar call date is a Reference row too");
        deadline.IsCurrentProjection.Should().BeTrue();
        deadline.SourceReason.Should().Be(
            "Last day to give notice for the 2027-01-15 declared call date. The notice window opens on 2026-11-16. "
            + BondExerciseNoticeWatch.ElapsedDiagnostic);
        book.Events.Single(static row => row.EventType == "Call").PostingStatus.Should().Be("Projected");

        book.NextLifecycleDate.Should().Be(new DateOnly(2027, 1, 15), "an elapsed deadline cannot be the next lifecycle date");
        snapshot.ScheduleSummary!.NextLifecycleDate.Should().Be(new DateOnly(2027, 1, 15));
        book.Summary.Should().Be(
            "Cash-flow schedule support is available. Next lifecycle date 2027-01-15. " + BondExerciseNoticeWatch.ElapsedDiagnostic);
        book.SourceSummary.Should().Be("Schedules follow golden-edm record edm-book-row as of 2026-01-15 00:00 UTC.",
            "only a blocker is added to the source summary");
    }

    [Fact]
    public async Task NoticeDeadlineOnTheEvaluationDate_IsStillOpen_AndIsTheNextLifecycleDate()
    {
        var snapshot = await ReadAsync(BondExerciseNoticeWatchTests.Bond([EuropeanCall(30, 60, "Calendar")]),
            asOf: new DateTimeOffset(2026, 12, 16, 18, 30, 0, TimeSpan.Zero));

        var book = snapshot.ScheduleBook!;
        book.Events.Single(static row => row.EventType == "CallNoticeDeadline").PostingStatus.Should().Be("Projected");
        book.NextLifecycleDate.Should().Be(new DateOnly(2026, 12, 16));
        book.Summary.Should().NotContain(BondExerciseNoticeWatch.ElapsedDiagnostic);
    }

    public static TheoryData<string, Dictionary<string, object?>?, bool, string> UnresolvableSchedules() => new()
    {
        // Business-day notice with no calendar source on the host.
        { "no calendar source", null, false, "DeclaredScheduleRequired" },
        // A scalar mandatory put beside a declared call is refused by the shared resolver.
        { "mandatory put scalar", new() { ["mandatoryPutDate"] = "2027-03-15" }, true, "ExerciseScheduleRefused" },
        { "pre-refund scalar", new() { ["preRefundDate"] = "2027-03-15" }, true, "ExerciseScheduleRefused" },
        // Structures the resolver refuses.
        { "floating coupon", new() { ["couponType"] = "Floating", ["floatingIndex"] = "SOFR" }, true, "UnsupportedCouponStructure" },
        { "factored principal", new() { ["currentFactor"] = 0.9m }, true, "UnsupportedPrincipalStructure" }
    };

    [Theory]
    [MemberData(nameof(UnresolvableSchedules))]
    public async Task UnresolvableDeclaredSchedule_ShowsTheBlocker_NoCallOrPutRow_AndNoScalarFallback(
        string because, Dictionary<string, object?>? extra, bool withCalendars, string code)
    {
        var security = BondExerciseNoticeWatchTests.Bond([EuropeanCall(10, null, "Business")], extra: extra);

        var snapshot = await ReadAsync(security, calendars: withCalendars ? Calendars : null);

        AssertBlocked(snapshot, code, because);
    }

    [Fact]
    public async Task MissingConventions_AreBlocked_NotReadFromTheScalar()
    {
        var security = WithoutConventions(BondExerciseNoticeWatchTests.Bond([EuropeanCall(30, 60, "Calendar")]));
        (await EconomicFirstCallDateAsync(security)).Should().Be("2027-01-15", "the scalar the blocked book must not show");

        var snapshot = await ReadAsync(security);

        AssertBlocked(snapshot, "DeclaredScheduleRequired", "legacy defaults are not inferred").Should().Be(
            "Exercise notice windows require reviewed explicit schedule conventions; legacy defaults are not inferred.");
    }

    [Theory]
    [InlineData("American")]
    [InlineData("Continuous")]
    public async Task UnsupportedExerciseStyle_IsBlocked_NotShown(string style)
    {
        var call = EuropeanCall(30, 60, "Calendar");
        call["exerciseStyle"] = style;
        call["callEndDate"] = "2029-01-15";

        var snapshot = await ReadAsync(BondExerciseNoticeWatchTests.Bond([call]));

        AssertBlocked(snapshot, "ExerciseScheduleRefused", style);
    }

    [Fact]
    public async Task MakeWholeCall_UnderAReviewedConventionsBlock_IsBlocked_NotShown()
    {
        // The recovered MakeWhole shape, completed with the conventions the economic family never carried.
        var snapshot = await ReadAsync(WithConventions(Recovered(MakeWhole, null, null), new
        {
            accrualAdjusted = false,
            couponRateUnit = "Percent",
            couponCashBasis = "Level",
            paymentCalendars = new[] { "WEEKENDS" }
        }));

        AssertBlocked(snapshot, "ExerciseScheduleRefused", "make-whole redemption is not a supported exercise");
    }

    [Theory]
    [InlineData("[42]")]
    [InlineData("[{\"callDate\":\"not-a-date\",\"callPrice\":101,\"exerciseStyle\":\"European\"}]")]
    [InlineData("[{\"callDate\":\"2027-01-15\",\"callPrice\":1e400,\"exerciseStyle\":\"European\"}]")]
    public async Task MalformedDeclaredSchedule_NeverFailsTheSnapshot_AndNeverFallsBackToTheScalar(string callScheduleJson)
    {
        var security = BondExerciseNoticeWatchTests.Bond(extra: new()
        {
            [SecurityVersionedTerms.CallScheduleKey] = JsonSerializer.Deserialize<JsonElement>(callScheduleJson),
            ["callDate"] = "2027-01-15",
            ["isCallable"] = true
        });

        var snapshot = await ReadAsync(security);

        AssertBlocked(snapshot, null, callScheduleJson);
    }

    private const string EuropeanPriced = "{\"callDt\":\"2027-01-15\",\"callPx\":101.0,\"isParCall\":false,\"callType\":\"European\"}";
    private const string NoStyleParCall = "{\"callDt\":\"2027-01-15\",\"callPx\":null,\"isParCall\":true,\"callType\":null}";
    private const string MakeWhole = "{\"callDt\":\"2027-01-15\",\"callPx\":null,\"isParCall\":false,\"callType\":\"MakeWhole\"}";
    private const string American = "{\"callDt\":\"2027-01-15\",\"callPx\":102.5,\"isParCall\":false,\"callType\":\"American\"}";
    private const string RecoveredPut = "{\"putDt\":\"2027-01-15\",\"putPx\":99.0}";
    private const string AccrualBoth = "{\"businessDayConvention\":\"Following\",\"holidayCalendar\":\"WEEKENDS\"}";
    private const string ConventionsRequired = "Exercise notice windows require reviewed explicit schedule conventions; legacy defaults are not inferred.";
    private const string AccrualAdjustedRequired = "Declared businessDayConvention 'Following' needs accrualAdjusted stated: true adjusts accrual and payment dates, false adjusts payment dates only.";

    public static TheoryData<string, string?, string?, string?, string> RecoveredShapes() => new()
    {
        // No Accrual module: the recovered block is null, so no conventions are declared.
        { "European, priced, no notice, no conventions", EuropeanPriced, null, null, ConventionsRequired },
        { "no exercise style (par call), no conventions", NoStyleParCall, null, null, ConventionsRequired },
        { "MakeWhole, no conventions", MakeWhole, null, null, ConventionsRequired },
        { "American, no conventions", American, null, null, ConventionsRequired },
        { "put only, no conventions", null, RecoveredPut, null, ConventionsRequired },
        // An Accrual module recovers a partial block: it never carries accrualAdjusted or a coupon unit.
        { "European, priced, partial conventions", EuropeanPriced, null, AccrualBoth, AccrualAdjustedRequired },
        { "no exercise style, partial conventions", NoStyleParCall, null, AccrualBoth, AccrualAdjustedRequired },
        { "MakeWhole, partial conventions", MakeWhole, null, AccrualBoth, AccrualAdjustedRequired },
        { "American, partial conventions", American, null, AccrualBoth, AccrualAdjustedRequired },
        { "call and put, partial conventions", EuropeanPriced, RecoveredPut, AccrualBoth, AccrualAdjustedRequired }
    };

    [Theory]
    [MemberData(nameof(RecoveredShapes))]
    public async Task RecoveredRow_IsADeclaringRow_BlockedTodayWithItsReason_AndNoScalarCallRow(
        string because, string? call, string? put, string? accrual, string reason)
    {
        var security = Recovered(call, put, accrual);
        AssetObligationProjectionService.DeclaresExerciseSchedule(security).Should().BeTrue(because);
        if (call is not null)
            (await EconomicFirstCallDateAsync(security)).Should().Be("2027-01-15",
                "the conversion recovers the scalar from firstCallDate; the blocked book must not show it");

        var snapshot = await ReadAsync(security);

        AssertBlocked(snapshot, "DeclaredScheduleRequired", because).Should().Be(reason, because);
    }

    [Theory]
    [InlineData("[null]")]
    [InlineData("[null, null]")]
    [InlineData("[{}, null]")]
    public async Task AllUnsetSchedule_DeclaresNothing_SoTheScalarCallRowStays(string callScheduleJson)
    {
        var security = BondExerciseNoticeWatchTests.Bond(extra: new()
        {
            [SecurityVersionedTerms.CallScheduleKey] = JsonSerializer.Deserialize<JsonElement>(callScheduleJson),
            ["callDate"] = "2027-07-15",
            ["isCallable"] = true
        });
        var policy = Substitute.For<IBondCalculationPolicy>();
        policy.GetBlockerAsync(Arg.Any<SecurityDetailDto>(), Arg.Any<CancellationToken>()).Returns("Reviewed cutover blocks this bond.");

        var snapshot = await ReadAsync(security, policy);

        var book = snapshot.ScheduleBook!;
        OptionEvents(book).Should().Equal(("Call", new DateOnly(2027, 7, 15)));
        var call = book.Events.Single(static row => row.EventType == "Call");
        call.IsDerivedFromEconomicTerms.Should().BeTrue("it is the scalar row");
        call.SourceReason.Should().BeNull();
        book.NextLifecycleDate.Should().Be(new DateOnly(2027, 7, 15));
        book.Summary.Should().Be("Cash-flow schedule support is available. Next lifecycle date 2027-07-15.");
        book.SourceSummary.Should().Be("Schedules follow golden-edm record edm-book-row as of 2026-01-15 00:00 UTC.");
        await policy.DidNotReceiveWithAnyArgs().GetBlockerAsync(default!, default);
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

        var snapshot = await ReadAsync(security);

        OptionEvents(snapshot.ScheduleBook!).Should().Equal(
            ("CallNoticeDeadline", new DateOnly(2026, 12, 16)),
            ("Call", new DateOnly(2027, 1, 15)));
    }

    [Theory]
    [InlineData(1, null)]
    [InlineData(-30, -1)]
    [InlineData(-30, 0)]
    public async Task TermsNotEffectiveAtTheSnapshotTime_AreBlocked(int effectiveFromOffsetDays, int? effectiveToOffsetDays)
    {
        var security = BondExerciseNoticeWatchTests.Bond([EuropeanCall(30, 60, "Calendar")]) with
        {
            EffectiveFrom = AsOf.AddDays(effectiveFromOffsetDays),
            EffectiveTo = effectiveToOffsetDays is { } offset ? AsOf.AddDays(offset) : null
        };

        var snapshot = await ReadAsync(security);

        AssertBlocked(snapshot, "TermsNotEffective", "the notice-watch read service refuses terms that are not effective at the evaluation date");
    }

    [Theory]
    [InlineData(0, null)]
    [InlineData(-30, 1)]
    public async Task TermsEffectiveAtTheSnapshotTime_Resolve(int effectiveFromOffsetDays, int? effectiveToOffsetDays)
    {
        var security = BondExerciseNoticeWatchTests.Bond([EuropeanCall(30, 60, "Calendar")]) with
        {
            EffectiveFrom = AsOf.AddDays(effectiveFromOffsetDays),
            EffectiveTo = effectiveToOffsetDays is { } offset ? AsOf.AddDays(offset) : null
        };

        var snapshot = await ReadAsync(security);

        OptionEvents(snapshot.ScheduleBook!).Should().HaveCount(2);
    }

    [Fact]
    public async Task CutoverPolicyBlock_BlocksTheDeclaredRows_WithThePolicyReason()
    {
        var policy = Substitute.For<IBondCalculationPolicy>();
        policy.GetBlockerAsync(Arg.Any<SecurityDetailDto>(), Arg.Any<CancellationToken>()).Returns("Reviewed cutover blocks this bond.");

        var snapshot = await ReadAsync(BondExerciseNoticeWatchTests.Bond([EuropeanCall(30, 60, "Calendar")]), policy);

        AssertBlocked(snapshot, "CalculationPolicyBlocked", "the retained cutover policy gates every bond calculation surface")
            .Should().Be("Reviewed cutover blocks this bond.");
    }

    [Fact]
    public async Task CutoverPolicyThatPermits_LeavesTheDeclaredRows()
    {
        var policy = Substitute.For<IBondCalculationPolicy>();
        policy.GetBlockerAsync(Arg.Any<SecurityDetailDto>(), Arg.Any<CancellationToken>()).Returns((string?)null);

        var snapshot = await ReadAsync(BondExerciseNoticeWatchTests.Bond([EuropeanCall(30, 60, "Calendar")]), policy);

        OptionEvents(snapshot.ScheduleBook!).Should().HaveCount(2);
        await policy.Received(1).GetBlockerAsync(Arg.Is<SecurityDetailDto>(row => row.AssetClass == "Bond"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CutoverPolicyThatRejectsAnOwnedSchedule_IsABlocker_NotAFailedSnapshot()
    {
        var policy = Substitute.For<IBondCalculationPolicy>();
        policy.GetBlockerAsync(Arg.Any<SecurityDetailDto>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(MalformedOwnedSchedule());

        var snapshot = await ReadAsync(BondExerciseNoticeWatchTests.Bond([EuropeanCall(30, 60, "Calendar")]), policy);

        AssertBlocked(snapshot, "InvalidEconomicSchedule", "the notice-watch read service reports the same exception as a blocker");
    }

    [Fact]
    public async Task PolicyBlockedAndNotEffective_ReportsTheCodeOfTheNoticeWatchReadServiceAndTheTimeline()
    {
        // The read service checks effectiveness before the policy; the schedule book gates in the same order.
        var security = BondExerciseNoticeWatchTests.Bond([EuropeanCall(30, 60, "Calendar")]) with { EffectiveFrom = AsOf.AddDays(1) };
        var policy = Substitute.For<IBondCalculationPolicy>();
        policy.GetBlockerAsync(Arg.Any<SecurityDetailDto>(), Arg.Any<CancellationToken>()).Returns("Reviewed cutover blocks this bond.");
        var query = Substitute.For<ISecurityMasterQueryService>();
        query.GetByIdAsync(security.SecurityId, Arg.Any<CancellationToken>()).Returns(security);

        var watch = await new BondExerciseNoticeReadService(query, Calendars, policy).GetAsync(security.SecurityId, AsOf, 90);
        var timeline = await TimelineAsync(security, policy);
        var snapshot = await ReadAsync(security, policy);

        var code = watch!.Blockers.Should().ContainSingle().Which.Code;
        code.Should().Be("TermsNotEffective");
        timeline.ExtensionPayload!.Value.GetProperty("declaredExercise").GetProperty("blockers")[0].GetProperty("code").GetString()
            .Should().Be(code);
        AssertBlocked(snapshot, code, "the schedule book gates in the read service's order");
    }

    [Fact]
    public async Task TermsNotEffective_DoesNotAskThePolicy()
    {
        var security = BondExerciseNoticeWatchTests.Bond([EuropeanCall(30, 60, "Calendar")]) with { EffectiveFrom = AsOf.AddDays(1) };
        var policy = Substitute.For<IBondCalculationPolicy>();
        policy.GetBlockerAsync(Arg.Any<SecurityDetailDto>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(MalformedOwnedSchedule());

        var snapshot = await ReadAsync(security, policy);

        AssertBlocked(snapshot, "TermsNotEffective", "the read service returns before it asks the policy");
        await policy.DidNotReceiveWithAnyArgs().GetBlockerAsync(default!, default);
    }

    [Fact]
    public async Task UndeclaredBond_DoesNotAskThePolicy_AndKeepsItsScalarCallRow()
    {
        var security = BondExerciseNoticeWatchTests.Bond(extra: new() { ["callDate"] = "2027-07-15", ["isCallable"] = true });
        var policy = Substitute.For<IBondCalculationPolicy>();
        policy.GetBlockerAsync(Arg.Any<SecurityDetailDto>(), Arg.Any<CancellationToken>()).Returns("Reviewed cutover blocks this bond.");

        var snapshot = await ReadAsync(security, policy);

        OptionEvents(snapshot.ScheduleBook!).Should().Equal(("Call", new DateOnly(2027, 7, 15)));
        snapshot.ScheduleBook!.Summary.Should().NotContain("Declared call/put schedule");
        await policy.DidNotReceiveWithAnyArgs().GetBlockerAsync(default!, default);
    }

    public static TheoryData<string, SecurityDetailDto> ConsistencyCases()
    {
        var bermudan = new Dictionary<string, object?>
        {
            ["callDate"] = "2027-01-15",
            ["callEndDate"] = "2029-01-15",
            ["callPrice"] = 100m,
            ["exerciseStyle"] = "Bermudan",
            ["priceBasis"] = "PercentOfPar",
            ["minimumNoticeDays"] = 30,
            ["noticeDayType"] = "Calendar"
        };
        var later = EuropeanCall(40, null, "Calendar");
        later["callDate"] = "2027-01-25";
        return new()
        {
            { "European call", BondExerciseNoticeWatchTests.Bond([EuropeanCall(30, 60, "Calendar")]) },
            { "call without notice", BondExerciseNoticeWatchTests.Bond([EuropeanCall(null, null, null)]) },
            { "call and put", BondExerciseNoticeWatchTests.Bond([EuropeanCall(30, 60, "Calendar")], [Put()]) },
            { "Bermudan call and put", BondExerciseNoticeWatchTests.Bond([bermudan], [Put()]) },
            { "shared deadline", BondExerciseNoticeWatchTests.Bond([EuropeanCall(30, null, "Calendar"), later]) },
            { "blocked: no conventions", WithoutConventions(BondExerciseNoticeWatchTests.Bond([EuropeanCall(30, 60, "Calendar")])) },
            { "blocked: floating", BondExerciseNoticeWatchTests.Bond([EuropeanCall(30, 60, "Calendar")], extra: new() { ["couponType"] = "Floating", ["floatingIndex"] = "SOFR" }) },
            { "recovered", Recovered(EuropeanPriced, RecoveredPut, AccrualBoth) }
        };
    }

    [Theory]
    [MemberData(nameof(ConsistencyCases))]
    public async Task ScheduleBookAndObligationTimeline_ListTheSameExerciseAndNoticeDates_ForTheSameRecord(
        string because, SecurityDetailDto security)
    {
        var timeline = await TimelineAsync(security, policy: null);
        var book = (await ReadAsync(security)).ScheduleBook!;

        var timelineRows = timeline.Events
            .Where(static row => TimelineKindToEventType.ContainsKey(row.EventKind))
            .Select(static row => (TimelineKindToEventType[row.EventKind], row.EffectiveDate));
        book.Events.Where(static row => OptionEventTypes.Contains(row.EventType)).Select(static row => (row.EventType, row.EffectiveDate))
            .Should().BeEquivalentTo(timelineRows, because);
        timeline.Events.Should().NotContain(static row => row.EventKind == "CallPutReview", because);

        var timelineBlockers = timeline.ExtensionPayload!.Value.GetProperty("declaredExercise").GetProperty("blockers")
            .EnumerateArray().Select(static blocker => blocker.GetProperty("code").GetString()).ToArray();
        foreach (var warning in timeline.Warnings.Where(static warning => warning.StartsWith("Declared call/put schedule is blocked", StringComparison.Ordinal)))
            book.Summary.Should().Contain(warning, because);
        book.Summary.Contains("Declared call/put schedule is blocked", StringComparison.Ordinal).Should().Be(timelineBlockers.Length > 0, because);
    }

    [Fact]
    public async Task InstrumentPassport_CountsTheDeclaredRows_AndCarriesTheBlockedSummary()
    {
        var resolved = await ReadAsync(BondExerciseNoticeWatchTests.Bond([EuropeanCall(30, 60, "Calendar")]));
        var blocked = await ReadAsync(WithoutConventions(BondExerciseNoticeWatchTests.Bond([EuropeanCall(30, 60, "Calendar")])));

        Section(resolved, "terms-obligations").EvidenceCount.Should().Be(resolved.ScheduleBook!.Events.Count);
        Section(resolved, "terms-obligations").Summary.Should().Be("3 projected obligation event(s) retained with source provenance.");
        Section(blocked, "terms-obligations").Summary.Should().Be("1 projected obligation event(s) retained with source provenance.");
        Section(blocked, "cash-flow-readiness").Summary.Should().Be(blocked.ScheduleSummary!.Summary)
            .And.Contain("Declared call/put schedule is blocked (DeclaredScheduleRequired)");

        static InstrumentPassportReferenceDataWorkbenchSectionDto Section(SecurityMasterTrustSnapshotDto snapshot, string id)
            => snapshot.InstrumentPassport!.ReferenceDataWorkbench!.Sections.Single(section => section.SectionId == id);
    }

    [Fact]
    public async Task SharedResolver_AsksThePolicyOnlyForADeclaringRecordWithEffectiveTerms()
    {
        var declaring = BondExerciseNoticeWatchTests.Bond([EuropeanCall(30, 60, "Calendar")]);
        var undeclared = BondExerciseNoticeWatchTests.Bond();
        var policy = Substitute.For<IBondCalculationPolicy>();
        policy.GetBlockerAsync(Arg.Any<SecurityDetailDto>(), Arg.Any<CancellationToken>()).Returns((string?)null);

        (await AssetObligationProjectionService.ResolveDeclaredExerciseAsync(undeclared, AsOf, Calendars, policy)).Should().BeNull();
        await policy.DidNotReceiveWithAnyArgs().GetBlockerAsync(default!, default);

        var resolved = await AssetObligationProjectionService.ResolveDeclaredExerciseAsync(declaring, AsOf, Calendars, policy);
        resolved.Should().BeEquivalentTo(AssetObligationProjectionService.ResolveDeclaredExercise(declaring, AsOf, Calendars),
            "the asynchronous entry is the synchronous one with the policy asked");
        resolved!.Windows.Should().ContainSingle();
        AssetObligationProjectionService.DeclaredExerciseRows(resolved).Select(static row => (row.EventKind, row.IsNoticeDeadline, row.Date, row.Occurrence))
            .Should().Equal(
                ("IssuerCallExercise", false, new DateOnly(2027, 1, 15), 1),
                ("IssuerCallNoticeDeadline", true, new DateOnly(2026, 12, 16), 1));

        using var cancelled = new CancellationTokenSource();
        policy.GetBlockerAsync(Arg.Any<SecurityDetailDto>(), cancelled.Token).ThrowsAsync(new OperationCanceledException());
        await cancelled.CancelAsync();
        var act = () => AssetObligationProjectionService.ResolveDeclaredExerciseAsync(declaring, AsOf, Calendars, policy, cancelled.Token);
        await act.Should().ThrowAsync<OperationCanceledException>("cancellation is not converted to a blocker");
    }

    private static SecurityScheduleValidationException MalformedOwnedSchedule()
        => new([new SecurityScheduleDiagnostic("INVALID_AMOUNT", "principalSchedule[0].amount", "The owned principal schedule is malformed.")]);

    private static readonly Dictionary<string, string> TimelineKindToEventType = new(StringComparer.Ordinal)
    {
        [AssetObligationProjectionService.IssuerCallExerciseEventKind] = SecurityMasterWorkbenchQueryService.CallEventType,
        [AssetObligationProjectionService.IssuerCallNoticeDeadlineEventKind] = SecurityMasterWorkbenchQueryService.CallNoticeDeadlineEventType,
        [AssetObligationProjectionService.HolderPutExerciseEventKind] = SecurityMasterWorkbenchQueryService.PutEventType,
        [AssetObligationProjectionService.HolderPutNoticeDeadlineEventKind] = SecurityMasterWorkbenchQueryService.PutNoticeDeadlineEventType
    };

    /// <summary>Asserts the blocked outcome on every text that reports it, and returns the blocker's reason.</summary>
    private static string AssertBlocked(SecurityMasterTrustSnapshotDto snapshot, string? code, string because)
    {
        var book = snapshot.ScheduleBook!;
        var summary = snapshot.ScheduleSummary!;
        OptionEvents(book).Should().BeEmpty(because);
        book.ProvenanceHistory.Should().NotContain(row => row.EventType != null && OptionEventTypes.Contains(row.EventType), because);
        book.Events.Should().ContainSingle(static row => row.EventType == "Maturity", because);

        const string Prefix = "Declared call/put schedule is blocked (";
        const string Suffix = " No call or put event is shown and the scalar call date is not used.";
        const string BaseSummary = "Cash-flow schedule support is available. Next lifecycle date 2031-01-15. ";
        const string BaseSource = "Schedules follow golden-edm record edm-book-row as of 2026-01-15 00:00 UTC. ";
        book.Summary.Should().StartWith(BaseSummary + Prefix, because).And.EndWith(Suffix, because);
        var warning = book.Summary[BaseSummary.Length..];
        var actualCode = warning[Prefix.Length..warning.IndexOf("): ", StringComparison.Ordinal)];
        if (code is not null)
            actualCode.Should().Be(code, because);
        var reason = warning[(Prefix.Length + actualCode.Length + "): ".Length)..^Suffix.Length];
        reason.Should().NotBeNullOrWhiteSpace(because);

        summary.Summary.Should().Be(book.Summary, because);
        book.SourceSummary.Should().Be(BaseSource + warning, because);
        summary.SourceSummary.Should().Be(book.SourceSummary, because);
        book.NextLifecycleDate.Should().Be(Maturity, "a blocked declaring bond offers no call or put date, scalar or declared");
        summary.NextLifecycleDate.Should().Be(Maturity, because);
        return reason;
    }

    private static (string EventType, DateOnly Date)[] OptionEvents(SecurityMasterScheduleBookDto book)
        => book.Events
            .Where(static row => row.EventType.Contains("Call", StringComparison.Ordinal) || row.EventType.Contains("Put", StringComparison.Ordinal))
            .Select(static row => (row.EventType, row.EffectiveDate))
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

    /// <summary>A Bond row as the real ADR-027 conversion recovers it from an economic terms document.</summary>
    private static SecurityDetailDto Recovered(string? call, string? put, string? accrual)
    {
        var callModule = "\"isCallable\":" + (call is null ? "false" : "true")
            + (call is null ? string.Empty : ",\"firstCallDate\":\"2027-01-15\"")
            + ",\"makeWholeSpreadBps\":50"
            + ",\"callSchedule\":[" + (call ?? string.Empty) + "]"
            + ",\"isPuttable\":" + (put is null ? "false" : "true")
            + ",\"putSchedule\":[" + (put ?? string.Empty) + "]";
        var economic = "{\"termsFormat\":\"economic\",\"schemaVersion\":2,"
            + "\"maturity\":{\"maturityDate\":\"2031-01-15\",\"issueDate\":\"2026-01-15\"},"
            + "\"coupon\":{\"couponType\":\"Fixed\",\"couponRate\":5,\"dayCount\":\"30/360\",\"paymentFrequency\":\"SemiAnnual\"},"
            + (accrual is null ? string.Empty : "\"accrual\":" + accrual + ",")
            + "\"call\":{" + callModule + "}}";
        var flat = SecurityEconomicTermsV2ToAssetSpecificTermsUpcaster
            .Convert(JsonSerializer.Deserialize<JsonElement>(economic), "Bond").Payload;
        return new(Guid.Parse("8b0b1c2d-0000-4000-8000-0000000000f1"), "Bond", SecurityStatusDto.Active, "Recovered bond", "USD",
            JsonSerializer.SerializeToElement(new { }), flat, [], [], 3, new DateTimeOffset(2026, 1, 15, 0, 0, 0, TimeSpan.Zero), null);
    }

    private static async Task<string?> EconomicFirstCallDateAsync(SecurityDetailDto security)
    {
        var economic = await new StoredRowSecurityMasterQueryService(ProjectionOf(security)).GetEconomicDefinitionByIdAsync(security.SecurityId);
        return economic!.EconomicTerms.GetProperty("call").GetProperty("firstCallDate").GetString();
    }

    private static async Task<SecurityMasterTrustSnapshotDto> ReadAsync(SecurityDetailDto security,
        IBondCalculationPolicy? policy = null, DateTimeOffset? asOf = null, bool economic = true)
        => await ReadAsync(security, Calendars, policy, asOf, economic);

    private static async Task<SecurityMasterTrustSnapshotDto> ReadAsync(SecurityDetailDto security,
        IBusinessDayCalendarSource? calendars, IBondCalculationPolicy? policy = null, DateTimeOffset? asOf = null, bool economic = true)
    {
        var query = new StoredRowSecurityMasterQueryService(ProjectionOf(security), withEconomicDefinition: economic);
        var snapshot = await CreateService(query, calendars, policy, asOf ?? AsOf).GetTrustSnapshotAsync(security.SecurityId, fundProfileId: null);
        snapshot.Should().NotBeNull();
        snapshot!.ScheduleSummary.Should().NotBeNull();
        snapshot.ScheduleBook.Should().NotBeNull();
        return snapshot;
    }

    private static async Task<AssetTermsObligationsTimelineDto> TimelineAsync(SecurityDetailDto security, IBondCalculationPolicy? policy)
    {
        var detail = await new AssetObligationProjectionService(policy, new FixedTimeProvider(AsOf), Calendars)
            .ProjectFromSecurityMasterAsync(security, new BondCalculationContext(AsOf),
                new StructuredCashFlowProjectionDto(security.SecurityId, StructuredCashFlowSourceKind.CalculatedBullet,
                    StructuredCashFlowScenario.Base, AsOf, []));
        return detail.TermsObligationsTimeline!;
    }
}
