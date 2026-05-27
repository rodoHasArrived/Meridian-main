using FluentAssertions;
using Meridian.Contracts.StrategyEngine;
using Meridian.Strategies.Services;
using Xunit;

namespace Meridian.Tests.Strategies;

public sealed class StrategyEngineValidationServiceTests
{
    [Fact]
    public void Validate_WithCoveredCallRequestAndDataAvailability_ProducesStableInputHashAndEvidence()
    {
        var service = CreateService();
        var request = CreateCoveredCallRequest(new Dictionary<string, string>
        {
            ["underlying"] = "SPY",
            ["daysToExpiration"] = "30",
            ["targetDelta"] = "0.30",
            ["contracts"] = "1",
            ["maxAssignmentRisk"] = "0.25"
        });
        var availability = CreateCoveredCallAvailability();

        var first = service.Validate(request, availability);
        var second = service.Validate(request with { Universe = ["spy"] }, availability);

        first.IsValid.Should().BeTrue();
        first.IsDegraded.Should().BeFalse();
        first.Findings.Should().BeEmpty();
        first.InputHash.Should().Be(second.InputHash);
        first.Evidence.EvidenceId.Should().StartWith("strategy-engine:");
        first.Evidence.DataSourceReferences.Should().Contain("fixture:options-chain:spy");
        first.Evidence.EvidenceRoute.Should().Be("/workstation/reporting/evidence");
    }

    [Fact]
    public void Validate_WithInvalidParameterAndMissingBlockingData_BlocksRunWithActionableFindings()
    {
        var service = CreateService();
        var request = CreateCoveredCallRequest(new Dictionary<string, string>
        {
            ["underlying"] = "SPY",
            ["daysToExpiration"] = "180",
            ["targetDelta"] = "not-a-number",
            ["contracts"] = "1",
            ["maxAssignmentRisk"] = "0.25"
        });

        var result = service.Validate(request, []);

        result.IsValid.Should().BeFalse();
        result.Findings.Should().Contain(finding => finding.Code == "parameter-max" && finding.Target == "parameters.daysToExpiration");
        result.Findings.Should().Contain(finding => finding.Code == "parameter-type" && finding.Target == "parameters.targetDelta");
        result.Findings.Should().Contain(finding =>
            finding.Code == "dependency-missing" &&
            finding.Severity == StrategyEngineValidationSeverity.Blocker &&
            finding.HandoffRoute == "/workstation/data");
    }

    [Fact]
    public void Validate_WithDegradedOpenLotDependency_AllowsRunButMarksEvidenceDegraded()
    {
        var service = CreateService();
        var availability = CreateCoveredCallAvailability()
            .Where(static item => item.DependencyId != "position-lots")
            .Append(new StrategyEngineDataAvailability(
                DependencyId: "position-lots",
                IsAvailable: false,
                SourceReference: null,
                DegradationReason: "Position lot import has not run."))
            .ToArray();

        var result = service.Validate(CreateCoveredCallRequest(), availability);

        result.IsValid.Should().BeTrue();
        result.IsDegraded.Should().BeTrue();
        result.Findings.Should().Contain(finding =>
            finding.Code == "dependency-missing" &&
            finding.Severity == StrategyEngineValidationSeverity.Warning &&
            finding.Message.Contains("Position lot import", StringComparison.Ordinal));
        result.Evidence.Warnings.Should().Contain(message => message.Contains("Position lot import", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_WithBlankMetadataAndNullCollections_ReturnsStructuredBlockersInsteadOfThrowing()
    {
        var service = CreateService();
        var request = new StrategyEngineRunRequest(
            StrategyId: " ",
            StrategyVersion: null!,
            Parameters: null!,
            Universe: null!,
            From: new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            To: new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero),
            DataSource: null!,
            Mode: StrategyEngineRunMode.Backtest);

        var act = () => service.Validate(request, []);

        var result = act.Should().NotThrow().Subject;
        result.IsValid.Should().BeFalse();
        result.Findings.Should().Contain(finding => finding.Code == "strategy-id-required");
        result.Findings.Should().Contain(finding => finding.Code == "strategy-version-required");
        result.Findings.Should().Contain(finding => finding.Code == "data-source-required");
        result.Findings.Should().Contain(finding => finding.Code == "universe-required");
    }

    private static StrategyEngineValidationService CreateService()
    {
        var designService = new StrategyDesignService();
        var registry = new StrategyEngineRegistry(designService);
        return new StrategyEngineValidationService(registry);
    }

    private static StrategyEngineRunRequest CreateCoveredCallRequest(IReadOnlyDictionary<string, string>? parameters = null)
        => new(
            StrategyId: "covered-call",
            StrategyVersion: "1",
            Parameters: parameters ?? new Dictionary<string, string>
            {
                ["underlying"] = "SPY",
                ["daysToExpiration"] = "30",
                ["targetDelta"] = "0.30",
                ["contracts"] = "1",
                ["maxAssignmentRisk"] = "0.25"
            },
            Universe: ["SPY"],
            From: new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            To: new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero),
            DataSource: "fixture",
            Mode: StrategyEngineRunMode.Backtest,
            CostModel: "fixed",
            SlippageModel: "midpoint");

    private static IReadOnlyList<StrategyEngineDataAvailability> CreateCoveredCallAvailability()
        =>
        [
            new(
                DependencyId: "underlying-bars",
                IsAvailable: true,
                AvailableFrom: new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero),
                AvailableTo: new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero),
                LastUpdatedAt: new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero),
                SourceReference: "fixture:bars:spy"),
            new(
                DependencyId: "options-chain",
                IsAvailable: true,
                AvailableFrom: new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero),
                AvailableTo: new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero),
                LastUpdatedAt: new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero),
                SourceReference: "fixture:options-chain:spy"),
            new(
                DependencyId: "position-lots",
                IsAvailable: true,
                SourceReference: "fixture:positions:spy")
        ];
}
