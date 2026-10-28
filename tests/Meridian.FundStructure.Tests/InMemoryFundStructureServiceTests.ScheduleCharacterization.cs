using System.Globalization;
using System.Text.Json;
using Meridian.Contracts.FundStructure;
using Meridian.Entities.FundStructure;
using Xunit;

namespace Meridian.FundStructure.Tests;

/// <summary>
/// Characterization pins for the fund-structure cash-flow view's Security Master rule projection. The
/// SCH-DEF-007 pin was recorded against base 7d072638 before the schedule kernel (ADR-025) took over
/// its frequency stepping; the spelling, 256-date guard and range-end pins were added when that
/// stepping moved onto the kernel's tenor step and hold on both sides of that move. The pins drive the
/// public <c>GetCashFlowViewAsync</c> seam with the shared hybrid fixture of this partial class.
/// </summary>
/// <remarks>
/// Tests tagged <c>Category=KnownDefect</c> assert TODAY'S defective behavior from the registry in
/// <c>tests/fixtures/schedules/KNOWN-DEFECTS.md</c> and name the TARGET behavior in a comment. When a
/// fix lands the pin fails; the fixing PR flips the assertion and updates the registry in the same
/// change. The trait sits on the method, not on this partial class, so the shared class's other
/// tests stay untagged.
/// </remarks>
public sealed partial class InMemoryFundStructureServiceTests
{
    [Fact]
    [Trait("Category", "KnownDefect")]
    public async Task ScheduleCharacterization_MonthlyRuleFromMonthEnd_CurrentBehavior_ProjectedDatesDrift_SCH_DEF_007()
    {
        // SCH-DEF-007. TARGET: the projected dates stay anchored on the month end:
        // 2026-01-31, 02-28, 03-31, 04-30, 05-31, 06-30.
        var bondSecurityId = Guid.Parse("5c4ed007-0000-4000-8000-000000000007");
        var securityMasterQueryService = new FakeSecurityMasterQueryService(
            economicDefinitions:
            [
                CreateEconomicDefinition(
                    bondSecurityId,
                    displayName: "Month-End Monthly Note",
                    assetClass: "FixedIncome",
                    typeName: "CorporateBond",
                    currency: "USD",
                    economicTerms: new
                    {
                        maturity = new
                        {
                            issueDate = new DateOnly(2025, 12, 31),
                            maturityDate = new DateOnly(2030, 12, 31)
                        },
                        payment = new
                        {
                            paymentFrequency = "Monthly",
                            paymentCurrency = "USD"
                        }
                    })
            ]);
        var fixture = await CreateHybridFixtureAsync(securityMasterQueryService: securityMasterQueryService);
        await fixture.StructureService.AssignNodeAsync(new AssignFundStructureNodeRequest(
            Guid.Parse("5c4ed007-0000-4000-8000-0000000000a1"),
            fixture.SleeveAccountId,
            "SecurityMasterInstrument",
            JsonSerializer.Serialize(new
            {
                securityId = bondSecurityId,
                incomeAmount = 500m,
                firstProjectedDate = new DateOnly(2026, 01, 31)
            }),
            new DateTimeOffset(2026, 01, 01, 0, 0, 0, TimeSpan.Zero),
            "test",
            IsPrimary: true));

        // The hybrid fixture's structure takes effect on 2026-01-01 and the projection window opens
        // the day after the as-of, so the window is [2026-01-02, 2026-07-01); no projected date falls
        // on 2026-01-01, so this is the [2026-01-01, 2026-07-01) window of the registry entry.
        var view = await fixture.StructureService.GetCashFlowViewAsync(new GovernanceCashFlowQuery(
            GovernanceCashFlowScopeKindDto.Account,
            AccountId: fixture.SleeveAccountId,
            AsOf: new DateTimeOffset(2026, 01, 01, 12, 0, 0, TimeSpan.Zero),
            HistoricalDays: 1,
            ForecastDays: 180,
            BucketDays: 30));

        Assert.NotNull(view);
        var projectedDates = view!.ProjectedEntries
            .Where(entry => entry.SourceKind == "SecurityMasterRule"
                && entry.EventKind == "Coupon"
                && entry.SecurityId == bondSecurityId)
            .Select(static entry => DateOnly.FromDateTime(entry.EventDate.UtcDateTime))
            .OrderBy(static date => date)
            .ToArray();

        // CURRENT defective behavior: each date is the previous one plus a month, so the month-end
        // anchor drifts to the 28th after February.
        Assert.Equal(
            new[]
            {
                new DateOnly(2026, 01, 31),
                new DateOnly(2026, 02, 28),
                new DateOnly(2026, 03, 28),
                new DateOnly(2026, 04, 28),
                new DateOnly(2026, 05, 28),
                new DateOnly(2026, 06, 28)
            },
            projectedDates);
    }

    [Theory]
    [InlineData("BiMonthly", "2026-01-15")]
    [InlineData("AtMaturity", "2026-01-15")]
    [InlineData("semi annual", "2026-01-15")]
    [InlineData("3M", "2026-01-15")]
    [InlineData(" Semi-Annually ", "2026-01-15,2026-07-15,2027-01-15")]
    [InlineData("HALF-YEARLY", "2026-01-15,2026-07-15,2027-01-15")]
    public async Task ScheduleCharacterization_RuleFrequencySpellings_StepOnlyWhenRecognised(string paymentFrequency, string expectedDates)
    {
        // Only the forecast's own spellings step, trimmed and in any case. Any other spelling projects
        // the first date alone, including Security Master frequencies the kernel could map (BiMonthly to
        // two months, AtMaturity to a term), so the recognised set must not widen.
        var dates = await ProjectRuleCouponDatesAsync(paymentFrequency, new DateOnly(2026, 01, 15), forecastDays: 400);

        Assert.Equal(expectedDates.Split(',').Select(ParseScheduleDate), dates);
    }

    [Theory]
    [InlineData("2026-01-02", 256, "2026-09-14")]
    [InlineData("2025-04-22", 1, "2026-01-02")]
    [InlineData("2025-04-21", 0, null)]
    public async Task ScheduleCharacterization_DailyRule_StopsAfter256DatesCountedFromTheFirstProjectedDate(
        string firstProjectedDate, int expectedCount, string? expectedLastDate)
    {
        // The window is [2026-01-02, 2027-02-06), but the roll stops after 256 dates counted from the
        // first projected date, whether or not they fall in the window: from 2025-04-21 the 256th date is
        // 2026-01-01, the day before the window opens.
        var dates = await ProjectRuleCouponDatesAsync("Daily", ParseScheduleDate(firstProjectedDate), forecastDays: 400);

        Assert.Equal(expectedCount, dates.Length);
        if (expectedLastDate is not null)
        {
            Assert.Equal(ParseScheduleDate(expectedLastDate), dates[^1]);
        }
    }

    [Fact]
    [Trait("Category", "KnownDefect")]
    public async Task ScheduleCharacterization_RuleStepPastTheLastRepresentableDate_CurrentBehavior_Throws_SCH_DEF_011()
    {
        // SCH-DEF-011. TARGET: the roll stops at the end of the date range and the view returns the
        // dates inside the window; it never throws.
        // As of 9999-12-20 with 10 forecast days, the window is [9999-12-21, 9999-12-31). The view builds
        // there: a daily rule from 9999-12-30 steps to 9999-12-31, the window's end, and stops.
        var asOf = new DateTimeOffset(9999, 12, 20, 12, 0, 0, TimeSpan.Zero);
        Assert.Equal(
            new[] { new DateOnly(9999, 12, 30) },
            await ProjectRuleCouponDatesAsync("Daily", new DateOnly(9999, 12, 30), forecastDays: 10, asOf));

        // CURRENT defective behavior: a weekly rule from 9999-12-25, inside the window, steps to
        // 10000-01-01 before the window can stop it. The parameter name tells the schedule step apart
        // from query validation, which throws the same exception type for "query".
        var thrown = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => ProjectRuleCouponDatesAsync("Weekly", new DateOnly(9999, 12, 25), forecastDays: 10, asOf));
        Assert.NotEqual("query", thrown.ParamName);
    }

    private static DateOnly ParseScheduleDate(string value) => DateOnly.ParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static async Task<DateOnly[]> ProjectRuleCouponDatesAsync(
        string paymentFrequency,
        DateOnly firstProjectedDate,
        int forecastDays,
        DateTimeOffset? asOf = null)
    {
        var securityId = Guid.Parse("5c4ed011-0000-4000-8000-000000000011");
        var securityMasterQueryService = new FakeSecurityMasterQueryService(
            economicDefinitions:
            [
                CreateEconomicDefinition(
                    securityId,
                    displayName: "Rule Stepping Note",
                    assetClass: "FixedIncome",
                    typeName: "CorporateBond",
                    currency: "USD",
                    economicTerms: new
                    {
                        payment = new
                        {
                            paymentFrequency,
                            paymentCurrency = "USD"
                        }
                    })
            ]);
        var fixture = await CreateHybridFixtureAsync(securityMasterQueryService: securityMasterQueryService);
        await fixture.StructureService.AssignNodeAsync(new AssignFundStructureNodeRequest(
            Guid.Parse("5c4ed011-0000-4000-8000-0000000000a1"),
            fixture.SleeveAccountId,
            "SecurityMasterInstrument",
            JsonSerializer.Serialize(new
            {
                securityId,
                incomeAmount = 500m,
                firstProjectedDate
            }),
            new DateTimeOffset(2026, 01, 01, 0, 0, 0, TimeSpan.Zero),
            "test",
            IsPrimary: true));

        // The projection window opens the day after the as-of. A bucket is capped at
        // FundStructurePolicyService.MaxCashFlowBucketDays (365), so a window up to that long is one
        // bucket and no ladder bucket ends past it, which matters at the end of the date range.
        var view = await fixture.StructureService.GetCashFlowViewAsync(new GovernanceCashFlowQuery(
            GovernanceCashFlowScopeKindDto.Account,
            AccountId: fixture.SleeveAccountId,
            AsOf: asOf ?? new DateTimeOffset(2026, 01, 01, 12, 0, 0, TimeSpan.Zero),
            HistoricalDays: 1,
            ForecastDays: forecastDays,
            BucketDays: Math.Min(forecastDays, FundStructurePolicyService.MaxCashFlowBucketDays)));

        Assert.NotNull(view);
        return view!.ProjectedEntries
            .Where(entry => entry.SourceKind == "SecurityMasterRule"
                && entry.EventKind == "Coupon"
                && entry.SecurityId == securityId)
            .Select(static entry => DateOnly.FromDateTime(entry.EventDate.UtcDateTime))
            .OrderBy(static date => date)
            .ToArray();
    }
}
