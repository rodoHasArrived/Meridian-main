using FluentAssertions;
using Meridian.Execution.Sdk;
using Xunit;

namespace Meridian.Tests.Execution;

/// <summary>
/// Guards the central order-placement gate shared by HTTP endpoints and the OMS.
/// The failure mode under guard: a permissive regression that lets an order route to a
/// live broker before every go-live phase, evidence gate, and signoff artifact has passed.
/// Every rejection branch is pinned so a removed check surfaces as a failing test.
/// </summary>
public sealed class BrokerageOrderPlacementGateTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(
        Path.GetTempPath(), "meridian-tests", $"placement-gate-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("paper")]
    [InlineData("PAPER")]
    public void Evaluate_NoConfigurationWithPaperGateway_Allows(string? selectedGatewayId)
    {
        var decision = BrokerageOrderPlacementGate.Evaluate(configuration: null, selectedGatewayId);

        decision.IsAllowed.Should().BeTrue(
            "paper trading must stay available before any brokerage configuration exists");
        decision.RejectReason.Should().BeNull();
    }

    [Fact]
    public void Evaluate_NoConfigurationWithLiveGateway_Rejects()
    {
        var decision = BrokerageOrderPlacementGate.Evaluate(configuration: null, selectedGatewayId: "alpaca");

        decision.IsAllowed.Should().BeFalse(
            "a live broker must never receive orders without an explicit brokerage configuration");
        decision.RejectReason.Should().Contain("alpaca").And.Contain("configuration is required");
    }

    [Fact]
    public void Evaluate_DefaultConfiguration_RejectsBecausePaperFlowIsNotEnabled()
    {
        // A freshly-constructed configuration (Gateway = "paper", no broker flows) must be
        // deny-by-default: even paper order flow requires an explicit flag.
        var decision = BrokerageOrderPlacementGate.Evaluate(new BrokerageConfiguration());

        decision.IsAllowed.Should().BeFalse();
        decision.RejectReason.Should().Contain("Paper order flow is disabled");
    }

    [Fact]
    public void Evaluate_PaperGatewayWithPaperFlowEnabled_Allows()
    {
        var configuration = new BrokerageConfiguration
        {
            Gateway = "paper",
            BrokerFlows = { ["paper"] = new BrokerFlowFlags { PaperOrderFlowEnabled = true } }
        };

        var decision = BrokerageOrderPlacementGate.Evaluate(configuration, "paper");

        decision.IsAllowed.Should().BeTrue();
    }

    [Fact]
    public void Evaluate_SelectedGatewayDiffersFromConfiguredGateway_Rejects()
    {
        var configuration = CreateLiveReadyConfiguration(gateway: "alpaca");

        var decision = BrokerageOrderPlacementGate.Evaluate(configuration, selectedGatewayId: "ib");

        decision.IsAllowed.Should().BeFalse(
            "orders must not route through a broker other than the configured one");
        decision.RejectReason.Should().Contain("'alpaca'").And.Contain("'ib'");
    }

    [Fact]
    public void Evaluate_SelectedGatewayMatchesAfterNormalization_Allows()
    {
        var configuration = CreateLiveReadyConfiguration(gateway: "alpaca");

        var decision = BrokerageOrderPlacementGate.Evaluate(configuration, selectedGatewayId: "  ALPACA  ");

        decision.IsAllowed.Should().BeTrue("gateway ids are compared case-insensitively after trimming");
    }

    [Fact]
    public void Evaluate_BlankConfiguredGatewayFallsBackToSelectedGateway_UsesSelectedBrokerFlow()
    {
        var configuration = CreateLiveReadyConfiguration(gateway: "alpaca");
        configuration.Gateway = "   ";

        var decision = BrokerageOrderPlacementGate.Evaluate(configuration, selectedGatewayId: "alpaca");

        decision.IsAllowed.Should().BeTrue(
            "when the configuration has no gateway the active broker selection drives the flow lookup");
    }

    [Fact]
    public void Evaluate_LiveGatewayWithoutBrokerFlowEntry_RejectsProductionRouting()
    {
        var configuration = CreateLiveReadyConfiguration(gateway: "alpaca");
        configuration.BrokerFlows.Clear();

        var decision = BrokerageOrderPlacementGate.Evaluate(configuration, "alpaca");

        decision.IsAllowed.Should().BeFalse("a broker with no flow flags must default to deny");
        decision.RejectReason.Should().Contain("Production order routing is disabled");
    }

    [Fact]
    public void Evaluate_LiveExecutionNotEnabled_Rejects()
    {
        var configuration = CreateLiveReadyConfiguration(gateway: "alpaca");
        configuration.LiveExecutionEnabled = false;

        var decision = BrokerageOrderPlacementGate.Evaluate(configuration, "alpaca");

        decision.IsAllowed.Should().BeFalse("the live-execution safety switch is the final manual guard");
        decision.RejectReason.Should().Contain("Live execution must be explicitly enabled");
    }

    [Theory]
    [InlineData(nameof(BrokerageConfiguration.ReadOnlyPhaseEnabled), "read-only phase is disabled")]
    [InlineData(nameof(BrokerageConfiguration.PaperTradingPhaseEnabled), "paper-trading phase is disabled")]
    [InlineData(nameof(BrokerageConfiguration.ProductionRoutingPhaseEnabled), "production routing is disabled")]
    public void Evaluate_AnyRolloutPhaseDisabled_Rejects(string phaseProperty, string expectedReasonFragment)
    {
        var configuration = CreateLiveReadyConfiguration(gateway: "alpaca");
        typeof(BrokerageConfiguration).GetProperty(phaseProperty)!.SetValue(configuration, false);

        var decision = BrokerageOrderPlacementGate.Evaluate(configuration, "alpaca");

        decision.IsAllowed.Should().BeFalse(
            "production routing requires every rollout phase to be explicitly enabled");
        decision.RejectReason.Should().Contain(expectedReasonFragment);
    }

    [Theory]
    [InlineData(nameof(BrokerageConfiguration.ReadOnlyVerificationPassed), "read-only verification must pass")]
    [InlineData(nameof(BrokerageConfiguration.PaperLifecycleTestsPassed), "paper-trading lifecycle tests must pass")]
    [InlineData(nameof(BrokerageConfiguration.ReplayEvidencePassed), "replay evidence must pass")]
    public void Evaluate_AnyEvidenceGateFailing_Rejects(string evidenceProperty, string expectedReasonFragment)
    {
        var configuration = CreateLiveReadyConfiguration(gateway: "alpaca");
        typeof(BrokerageConfiguration).GetProperty(evidenceProperty)!.SetValue(configuration, false);

        var decision = BrokerageOrderPlacementGate.Evaluate(configuration, "alpaca");

        decision.IsAllowed.Should().BeFalse("go-live evidence gates must all pass before live routing");
        decision.RejectReason.Should().Contain(expectedReasonFragment);
    }

    [Fact]
    public void Evaluate_ValidationArtifactMissing_Rejects()
    {
        var configuration = CreateLiveReadyConfiguration(gateway: "alpaca");
        configuration.ValidationGates = new BrokerValidationGateOptions
        {
            RequireValidationArtifactsForOrderPlacement = true,
            ValidationArtifactPath = Path.Combine(_tempRoot, "missing-validation.json"),
            SignoffArtifactPath = Path.Combine(_tempRoot, "missing-signoff.json")
        };

        var decision = BrokerageOrderPlacementGate.Evaluate(configuration, "alpaca");

        decision.IsAllowed.Should().BeFalse();
        decision.RejectReason.Should().Contain("validation artifact is present");
    }

    [Fact]
    public void Evaluate_SignoffArtifactMissing_Rejects()
    {
        var configuration = CreateLiveReadyConfiguration(gateway: "alpaca");
        configuration.ValidationGates = new BrokerValidationGateOptions
        {
            RequireValidationArtifactsForOrderPlacement = true,
            ValidationArtifactPath = WriteArtifact("validation.json"),
            SignoffArtifactPath = Path.Combine(_tempRoot, "missing-signoff.json")
        };

        var decision = BrokerageOrderPlacementGate.Evaluate(configuration, "alpaca");

        decision.IsAllowed.Should().BeFalse(
            "an operator signoff is required even when validation evidence exists");
        decision.RejectReason.Should().Contain("signoff artifact is present");
    }

    [Fact]
    public void Evaluate_AllPhasesEvidenceAndArtifactsPresent_Allows()
    {
        var configuration = CreateLiveReadyConfiguration(gateway: "alpaca");
        configuration.ValidationGates = new BrokerValidationGateOptions
        {
            RequireValidationArtifactsForOrderPlacement = true,
            ValidationArtifactPath = WriteArtifact("validation.json"),
            SignoffArtifactPath = WriteArtifact("signoff.json")
        };

        var decision = BrokerageOrderPlacementGate.Evaluate(configuration, "alpaca");

        decision.IsAllowed.Should().BeTrue();
        decision.RejectReason.Should().BeNull();
    }

    /// <summary>
    /// Builds a configuration that passes every live-routing gate except the artifact
    /// checks, which are disabled so individual tests can flip exactly one gate off.
    /// </summary>
    private static BrokerageConfiguration CreateLiveReadyConfiguration(string gateway) => new()
    {
        Gateway = gateway,
        LiveExecutionEnabled = true,
        ProductionRoutingPhaseEnabled = true,
        ReadOnlyVerificationPassed = true,
        PaperLifecycleTestsPassed = true,
        ReplayEvidencePassed = true,
        BrokerFlows =
        {
            [gateway] = new BrokerFlowFlags { ProductionOrderRoutingEnabled = true }
        },
        ValidationGates = new BrokerValidationGateOptions
        {
            RequireValidationArtifactsForOrderPlacement = false
        }
    };

    private string WriteArtifact(string fileName)
    {
        Directory.CreateDirectory(_tempRoot);
        var path = Path.Combine(_tempRoot, fileName);
        File.WriteAllText(path, "{}");
        return path;
    }
}
