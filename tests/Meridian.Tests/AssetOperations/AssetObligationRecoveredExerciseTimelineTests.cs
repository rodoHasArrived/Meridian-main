using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.Calendars;
using Meridian.Contracts.FixedIncome;
using Meridian.Contracts.SecurityMaster;
using Meridian.Instruments.AssetOperations;
using NSubstitute;

namespace Meridian.Tests.AssetOperations;

/// <summary>
/// The obligation timeline of Bond rows whose call and put arrays were recovered from an economic
/// terms document (ADR-027 stage 1, increments 1 and 2). Each row is built by the real conversion,
/// <see cref="SecurityEconomicTermsV2ToAssetSpecificTermsUpcaster.Convert(JsonElement, string?)"/>, so the
/// shapes are the ones stored rows take: stamp 3, <c>isCallable</c>, a scalar <c>callDate</c> from the
/// first call date, entries with a null exercise style unless the call type named one, null notice
/// members, and a <c>scheduleConventions</c> block that is null or holds only what the Accrual module
/// carried. Such rows are a declaring population: the scalar review is not used for them.
/// </summary>
[Trait("Category", "Unit")]
public sealed class AssetObligationRecoveredExerciseTimelineTests
{
    private static readonly DateTimeOffset AsOf = new(2026, 12, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly IBusinessDayCalendarSource Calendars =
        new StaticBusinessDayCalendarSource([BusinessDayCalendars.WeekendsOnly]);
    private const string EuropeanPriced = "{\"callDt\":\"2027-01-15\",\"callPx\":101.0,\"isParCall\":false,\"callType\":\"European\"}";
    private const string NoStyleParCall = "{\"callDt\":\"2027-01-15\",\"callPx\":null,\"isParCall\":true,\"callType\":null}";
    private const string MakeWhole = "{\"callDt\":\"2027-01-15\",\"callPx\":null,\"isParCall\":false,\"callType\":\"MakeWhole\"}";
    private const string American = "{\"callDt\":\"2027-01-15\",\"callPx\":102.5,\"isParCall\":false,\"callType\":\"American\"}";
    private const string Put = "{\"putDt\":\"2027-01-15\",\"putPx\":99.0}";
    private const string AccrualBoth = "{\"businessDayConvention\":\"Following\",\"holidayCalendar\":\"WEEKENDS\"}";
    private const string AccrualCalendarOnly = "{\"holidayCalendar\":\"WEEKENDS\"}";
    private const string ConventionsRequired = "Exercise notice windows require reviewed explicit schedule conventions; legacy defaults are not inferred.";
    private const string AccrualAdjustedRequired = "Declared businessDayConvention 'Following' needs accrualAdjusted stated: true adjusts accrual and payment dates, false adjusts payment dates only.";

    public static TheoryData<string, string?, string?, string?, string> RecoveredShapes() => new()
    {
        // No Accrual module: the recovered block is null, so no conventions are declared.
        { "European, priced, no notice, no conventions", EuropeanPriced, null, null, ConventionsRequired },
        { "no exercise style (par call), no conventions", NoStyleParCall, null, null, ConventionsRequired },
        { "MakeWhole, no conventions", MakeWhole, null, null, ConventionsRequired },
        { "American, no conventions", American, null, null, ConventionsRequired },
        { "put only, no conventions", null, Put, null, ConventionsRequired },
        // An Accrual module recovers a partial block: it never carries accrualAdjusted or a coupon unit.
        { "European, priced, partial conventions", EuropeanPriced, null, AccrualBoth, AccrualAdjustedRequired },
        { "no exercise style, partial conventions", NoStyleParCall, null, AccrualBoth, AccrualAdjustedRequired },
        { "MakeWhole, partial conventions", MakeWhole, null, AccrualBoth, AccrualAdjustedRequired },
        { "American, partial conventions", American, null, AccrualBoth, AccrualAdjustedRequired },
        { "call and put, partial conventions", EuropeanPriced, Put, AccrualBoth, AccrualAdjustedRequired }
    };

    [Theory]
    [MemberData(nameof(RecoveredShapes))]
    public async Task RecoveredRow_IsADeclaringRow_BlockedTodayWithAWarning_AndNoScalarFallback(
        string because, string? call, string? put, string? accrual, string reason)
    {
        var security = Recovered(call, put, accrual);
        AssetObligationProjectionService.DeclaresExerciseSchedule(security).Should().BeTrue(because);
        if (call is not null)
            security.AssetSpecificTerms.GetProperty("callDate").GetString().Should().Be("2027-01-15",
                "the conversion recovers the scalar from firstCallDate; it must not be shown as a second review");

        var detail = await new AssetObligationProjectionService(calendars: Calendars).ProjectFromSecurityMasterAsync(
            security, new BondCalculationContext(AsOf),
            new StructuredCashFlowProjectionDto(security.SecurityId, StructuredCashFlowSourceKind.CalculatedBullet,
                StructuredCashFlowScenario.Base, AsOf, []));

        var timeline = detail.TermsObligationsTimeline!;
        timeline.EngineVersion.Should().EndWith("+asset-terms-obligations-timeline-declared-v1", because);
        timeline.Events.Should().NotContain(row => row.EventKind.Contains("Call", StringComparison.Ordinal)
            || row.EventKind.Contains("Put", StringComparison.Ordinal), because);
        var outcome = timeline.ExtensionPayload!.Value.GetProperty("declaredExercise");
        outcome.GetProperty("status").GetString().Should().Be("Blocked", because);
        var blocker = outcome.GetProperty("blockers").EnumerateArray().Should().ContainSingle().Subject;
        blocker.GetProperty("code").GetString().Should().Be("DeclaredScheduleRequired", because);
        blocker.GetProperty("reason").GetString().Should().Be(reason, because);
        var warning = $"Declared call/put schedule is blocked (DeclaredScheduleRequired): {reason} No call or put event is shown and the scalar call date is not used.";
        timeline.Warnings.Should().Contain(warning, because);
        detail.Readiness.Warnings.Should().Contain(warning, because);
    }

    [Fact]
    public void RecoveredRow_WithACalendarOnlyBlock_IsBlockedToo()
    {
        var outcome = new AssetObligationProjectionService(calendars: Calendars)
            .ResolveDeclaredExercise(Recovered(EuropeanPriced, null, AccrualCalendarOnly), new BondCalculationContext(AsOf));

        outcome!.Windows.Should().BeEmpty();
        outcome.Blockers.Should().ContainSingle().Which.Code.Should().BeOneOf("DeclaredScheduleRequired", "CouponUnitRequired");
    }

    [Fact]
    public async Task RecoveredRow_ThroughTheReadService_KeepsItsLegacyCashFlows_AndBlocksOnlyTheExerciseEvents()
    {
        // A null block declares no conventions, so the read service projects the legacy bond loop.
        var security = Recovered(EuropeanPriced, null, null);
        var query = Substitute.For<ISecurityMasterQueryService>();
        query.GetByIdAsync(security.SecurityId, Arg.Any<CancellationToken>()).Returns(security);

        var detail = await new AssetOperationsReadService(securityMasterQueryService: query, calendars: Calendars)
            .GetOperationsAsOfAsync(security.SecurityId, new BondCalculationContext(AsOf));

        detail!.CashFlowProjectionRuns.Should().ContainSingle().Which.Status.Should().Be("Completed");
        detail.ProjectedCashFlows.Should().NotBeEmpty();
        var timeline = detail.TermsObligationsTimeline!;
        timeline.EngineVersion.Should().Be("asset-obligation-projection-v1+asset-terms-obligations-timeline-declared-v1");
        timeline.Events.Should().NotContain(row => row.EventKind == "CallPutReview");
        timeline.ExtensionPayload!.Value.GetProperty("declaredExercise").GetProperty("status").GetString().Should().Be("Blocked");
    }

    [Fact]
    public void RecoveredRow_CompletedByAReviewedBlock_ResolvesExerciseOnlyEvents()
    {
        // What a recovered European entry yields once an operator declares the conventions the
        // economic family never carried: the exercise date and, with no notice members, no deadline.
        var recovered = Recovered(EuropeanPriced, null, null);
        var terms = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(recovered.AssetSpecificTerms)!;
        terms[SecurityVersionedTerms.ScheduleConventionsKey] = JsonSerializer.SerializeToElement(new
        {
            accrualAdjusted = false,
            couponRateUnit = "Percent",
            couponCashBasis = "Level",
            paymentCalendars = new[] { "WEEKENDS" }
        });
        var security = recovered with { AssetSpecificTerms = JsonSerializer.SerializeToElement(terms) };

        var outcome = new AssetObligationProjectionService(calendars: Calendars)
            .ResolveDeclaredExercise(security, new BondCalculationContext(AsOf));

        outcome!.Blockers.Should().BeEmpty();
        outcome.Windows.Should().ContainSingle().Which.Should().Be(new BondExerciseNoticeWindow(
            "Call", new DateOnly(2027, 1, 15), 101.0m, null, null, BondExerciseNoticeStatus.NotStated, null));
    }

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
        return new(Guid.Parse("8a0b1c2d-0000-4000-8000-0000000000f1"), "Bond", SecurityStatusDto.Active, "Recovered bond", "USD",
            JsonSerializer.SerializeToElement(new { }), flat, [], [], 3, new DateTimeOffset(2026, 1, 15, 0, 0, 0, TimeSpan.Zero), null);
    }
}
