using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.FixedIncome;
using Meridian.Contracts.SecurityMaster;
using Meridian.Instruments.FixedIncome;
using NSubstitute;

namespace Meridian.Tests.FixedIncome;

public sealed partial class BondWorkflowServiceTests
{
    private static readonly DateTimeOffset InflationEconomicCutoff = new(2026, 1, 15, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset InflationKnowledgeCutoff = new(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task FutureRealizedInflationCannotReplaceTheEconomicCutoffForecast()
    {
        var fixture = InflationCutoffFixture();
        var result = (await fixture.Service.CalculateAsync(fixture.Request))!;

        result.IsBlocked.Should().BeFalse(string.Join("; ", result.Issues.Select(issue => issue.Reason)));
        result.ModelVersion.Should().Be(BondWorkflowModelVersions.Current);
        result.SettlementIndexRatio.Should().Be(1m);
        result.SettlementIndexIsObserved.Should().BeTrue();
        result.CashFlows.Sum(row => row.Principal).Should().Be(110m);
        result.CashFlows.Sum(row => row.Interest).Should().Be(5.5m);
        result.PresentValue.Should().BeApproximately(115.5m, .000000000001m);
        result.CashFlows.Should().OnlyContain(row => row.IsForecast && row.IndexRatio == 1.1m);
        result.Inputs.Should().Contain(input => input.Kind == "Curve:Inflation:CPI");
        result.Inputs.Where(input => input.Kind == "Observation").Should().ContainSingle()
            .Which.Key.Should().Be("CPI:2026-01-01");
        result.Inputs.Should().NotContain(input => input.ContentSha256 == new string('f', 64));
        result.Request!.ModelVersion.Should().Be(BondWorkflowModelVersions.Current);
    }

    [Fact]
    public async Task FutureActualInflationDoesNotSubstituteForAMissingForecastCurve()
    {
        var fixture = InflationCutoffFixture(includeInflationCurve: false);
        var result = (await fixture.Service.CalculateAsync(fixture.Request))!;

        result.IsBlocked.Should().BeTrue();
        result.Issues.Should().Contain(issue => issue.Reason.Contains("InflationCurveMissing"));
        result.CashFlows.Should().BeEmpty();
        result.PresentValue.Should().BeNull();
        result.Inputs.Should().NotContain(input => input.ContentSha256 == new string('f', 64));
    }

    [Fact]
    public async Task MonthlyInterpolationCombinesPastObservedAndFutureForecastLevels()
    {
        var fixture = InflationCutoffFixture(interpolate: true);
        var result = (await fixture.Service.CalculateAsync(fixture.Request))!;

        result.IsBlocked.Should().BeFalse();
        // January 15 uses January's observed100 plus14/31 of the forecast110 step,
        // rather than the later realized February130 known at the September cutoff.
        result.SettlementIndexRatio.Should().BeApproximately((100m + 10m * 14m / 31m) / 100m, .000000000001m);
        result.SettlementIndexIsObserved.Should().BeFalse();
        result.CashFlows.Sum(row => row.Principal).Should().Be(110m);
        result.CashFlows.Should().OnlyContain(row => row.IsForecast);
        result.Inputs.Where(input => input.Kind == "Observation").Should().ContainSingle()
            .Which.Key.Should().Be("CPI:2026-01-01");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(15)]
    public async Task InflationOnOrBeforeEconomicCutoffStillUsesRevisionsEligibleAtKnownAt(int economicDay)
    {
        var fixture = InflationCutoffFixture(reviseJanuary: true);
        var context = fixture.Request.Context with
        {
            AsOf = new(2026, 1, economicDay, 0, 0, 0, TimeSpan.Zero),
            SettlementDate = new(2026, 1, economicDay)
        };
        var revised = (await fixture.Service.CalculateAsync(fixture.Request with { Context = context }))!;
        var initial = (await fixture.Service.CalculateAsync(fixture.Request with
        { Context = context with { KnownAt = new(2026, 2, 1, 0, 0, 0, TimeSpan.Zero) } }))!;

        revised.IsBlocked.Should().BeFalse();
        initial.IsBlocked.Should().BeFalse();
        revised.SettlementIndexRatio.Should().Be(1.02m);
        initial.SettlementIndexRatio.Should().Be(1m);
        revised.Inputs.Should().Contain(input => input.Kind == "Observation" && input.ContentSha256 == new string('b', 64));
        initial.Inputs.Should().NotContain(input => input.ContentSha256 == new string('b', 64));
    }

    [Fact]
    public async Task PastInflationStillRequiresObservedEvidenceInsteadOfAForecast()
    {
        var fixture = InflationCutoffFixture(omitJanuary: true);
        var result = (await fixture.Service.CalculateAsync(fixture.Request))!;

        result.IsBlocked.Should().BeTrue();
        result.Issues.Should().Contain(issue => issue.Reason.Contains("InflationObservationMissing"));
        result.CashFlows.Should().BeEmpty();
    }

    [Theory]
    [InlineData(BondWorkflowModelVersions.Legacy)]
    [InlineData(BondWorkflowModelVersions.Previous)]
    public async Task HistoricalInflationVersionsPreserveFutureObservedArithmeticAndExactReplay(string model)
    {
        var fixture = InflationCutoffFixture();
        var retained = (await fixture.Service.CalculateAsync(fixture.Request with { ModelVersion = model }))!;

        retained.IsBlocked.Should().BeFalse();
        retained.CashFlows.Sum(row => row.Principal).Should().Be(150m);
        retained.CashFlows.Sum(row => row.Interest).Should().Be(7.5m);
        retained.PresentValue.Should().BeApproximately(157.5m, .000000000001m);
        retained.CashFlows.Should().OnlyContain(row => !row.IsForecast);
        retained.Inputs.Should().Contain(input => input.Kind == "Observation" && input.Key == "CPI:2026-07-01"
            && input.ContentSha256 == new string('f', 64));
        var json = JsonSerializer.Serialize(retained);
        JsonSerializer.Serialize(await fixture.Service.ReplayAsync(JsonSerializer.Deserialize<BondWorkflowResult>(json)!))
            .Should().Be(json);
    }

    [Theory]
    [InlineData(BondWorkflowModelVersions.Previous)]
    [InlineData(BondWorkflowModelVersions.Current)]
    public async Task FrozenIncomeInflationRetainsOnlyTheEligiblePolicyDateLevel(string model)
    {
        var fixture = InflationCutoffFixture();
        var result = (await fixture.Service.CalculateIncomeScheduleAsync(fixture.Request with { ModelVersion = model }, new(2026, 1, 15)))!;

        result.IsBlocked.Should().BeFalse();
        result.ModelVersion.Should().Be(model);
        result.CashFlows.Sum(row => row.Principal).Should().Be(100m);
        result.CashFlows.Sum(row => row.Interest).Should().Be(5m);
        result.CashFlows.Should().OnlyContain(row => row.IsForecast);
        result.Inputs.Where(input => input.Kind == "Observation").Should().ContainSingle()
            .Which.Key.Should().Be("CPI:2026-01-01");
    }

    private static (BondWorkflowService Service, BondWorkflowRequest Request) InflationCutoffFixture(
        bool includeInflationCurve = true, bool interpolate = false, bool reviseJanuary = false, bool omitJanuary = false)
    {
        var bond = WithExerciseTerms(Bond(), terms =>
        {
            terms["assetClassTermsVersion"] = 5;
            terms["issueDate"] = "2025-07-01";
            terms["maturity"] = "2026-07-01";
            terms["couponType"] = "InflationLinked";
            terms["inflationIndex"] = "CPI";
            terms["inflationBaseIndexValue"] = 100m;
            terms["bondWorkflow"] = new { lagMonths = 0, interpolateMonthly = interpolate };
        }) with
        { EffectiveFrom = new(2025, 7, 1, 0, 0, 0, TimeSpan.Zero) };
        var rows = new List<BondWorkflowRateObservation>();
        if (!omitJanuary)
            rows.Add(new("CPI", new(2026, 1, 1), 100m, "CPI source", AsOf.AddDays(1), AsOf.AddDays(2), new string('a', 64)));
        if (reviseJanuary)
            rows.Add(new("CPI", new(2026, 1, 1), 102m, "CPI source", AsOf.AddMonths(7), AsOf.AddMonths(7).AddDays(1), new string('b', 64)));
        foreach (var (date, level) in new[] { (new DateOnly(2026, 2, 1), 130m), (new DateOnly(2026, 7, 1), 150m), (new DateOnly(2026, 8, 1), 160m) })
            rows.Add(new("CPI", date, level, "CPI source", AsOf.AddMonths(7), AsOf.AddMonths(7).AddDays(1), new string('f', 64)));
        var observations = Substitute.For<IBondWorkflowTypedObservationSource>();
        observations.GetAsync("CPI", BondMarketObservationKind.InflationIndex, Arg.Any<DateOnly>(), Arg.Any<DateOnly>(),
            Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(rows);
        var curves = Substitute.For<ISecurityCurveStore>();
        var definitions = new List<SecurityCurveDefinitionDto>();
        var versions = new Dictionary<Guid, SecurityCurveVersionDto>();
        foreach (var kind in includeInflationCurve ? new[] { SecurityCurveKind.Discount, SecurityCurveKind.Inflation } : new[] { SecurityCurveKind.Discount })
        {
            var definition = new SecurityCurveDefinitionDto(Guid.NewGuid(), "CUTOFF-" + kind, kind, "USD",
                kind == SecurityCurveKind.Inflation ? "CPI" : null, null, "ACT/365", SecurityCurveInterpolation.Linear,
                "synthetic", null, "reviewer", AsOf);
            var level = kind == SecurityCurveKind.Inflation ? 110m : 1m;
            SecurityCurvePointDto[] points = [new(new(2026, 1, 1), level), new(new(2026, 8, 1), level)];
            definitions.Add(definition);
            versions.Add(definition.CurveId, new(Guid.NewGuid(), definition.CurveId, definition.CurveKey, 1,
                new(2026, 1, 1), AsOf, AsOf, "synthetic", "reviewer", points,
                SecurityCurveRules.ContentSha256(new(2026, 1, 1), points)));
        }
        curves.ListDefinitionsAsync(Arg.Any<SecurityCurveKind?>(), "USD", Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(call => definitions.Where(definition => definition.Kind == call.ArgAt<SecurityCurveKind?>(0)
                && definition.IndexName == call.ArgAt<string?>(2)).ToArray());
        curves.GetLatestVersionAsOfAsync(Arg.Any<Guid>(), Arg.Any<DateOnly>(), Arg.Any<DateTimeOffset?>(), Arg.Any<CancellationToken>())
            .Returns(call => versions.GetValueOrDefault(call.ArgAt<Guid>(0)));
        curves.GetDefinitionAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(call => definitions.SingleOrDefault(definition => definition.CurveId == call.ArgAt<Guid>(0)));
        curves.GetVersionByIdAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(call => versions.GetValueOrDefault(call.ArgAt<Guid>(0)) is { } version && version.CurveVersionId == call.ArgAt<Guid>(1) ? version : null);
        return (Service(bond, curves, observations: observations), new(Id,
            new(InflationEconomicCutoff, new(2026, 1, 15), InflationKnowledgeCutoff)));
    }
}
