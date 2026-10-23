using System.Text.Json;
using Meridian.Contracts.FundStructure;
using Xunit;

namespace Meridian.FundStructure.Tests;

/// <summary>
/// Characterization pin for the fund-structure cash-flow view's Security Master rule projection,
/// recorded against base 7d072638 before the schedule kernel (ADR-025) takes over its frequency
/// stepping. The pin drives the public <c>GetCashFlowViewAsync</c> seam with the shared hybrid
/// fixture of this partial class.
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
}
