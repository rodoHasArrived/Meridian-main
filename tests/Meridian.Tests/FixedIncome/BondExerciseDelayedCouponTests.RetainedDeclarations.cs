using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Meridian.Contracts.FixedIncome;

namespace Meridian.Tests.FixedIncome;

// Exercise analytics read stored rows: a declaration stored before a schedule-terms gap rule (here
// E10d, a noticeDayType without counts) keeps the analytics it had, rather than being refused on read.
public sealed partial class BondExerciseDelayedCouponTests
{
    [Fact]
    public void ARetainedDeclarationThatPredatesAGapRule_StillResolvesItsExerciseScenario()
    {
        var stored = Bond("Put", "2026-04-15");
        var terms = JsonNode.Parse(stored.AssetSpecificTerms.GetRawText())!.AsObject();
        terms["putSchedule"]![0]!["noticeDayType"] = "Calendar";
        var security = stored with { AssetSpecificTerms = JsonSerializer.SerializeToElement(terms) };
        var (resolved, declared, schedule, flows) = ContractualInputs(security);

        BondExerciseEconomics.TryResolveScenarios(security, resolved, declared, schedule,
            DateOnly.FromDateTime(AsOf.UtcDateTime), 0.05m, BondYieldConvention.NominalPeriodic,
            2, flows, Calendars, out var scenarios, out var reason).Should().BeTrue(reason);

        scenarios.Should().ContainSingle();
    }
}
