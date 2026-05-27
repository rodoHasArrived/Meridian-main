using FluentAssertions;
using Meridian.Application.Reconciliation;

namespace Meridian.Tests.Reconciliation;

public sealed class ReconciliationContractsTests
{
    [Fact]
    public void IsSupported_KnownContractAtV1_ReturnsTrue()
    {
        var isSupported = ReconciliationContractCatalog.IsSupported(
            ReconciliationContractCatalog.BreakWorkflowContract,
            ReconciliationContractVersion.V1);

        isSupported.Should().BeTrue();
    }

    [Fact]
    public void IsSupported_UnknownContract_ReturnsFalse()
    {
        var isSupported = ReconciliationContractCatalog.IsSupported(
            "meridian.reconciliation.unknown",
            ReconciliationContractVersion.V1);

        isSupported.Should().BeFalse();
    }

    [Fact]
    public void FeatureFlagPolicy_EnablesByClientTeamOrCounterparty()
    {
        var policy = new ReconciliationFeatureFlagPolicy();
        policy.EnableTeam("Ops-West");

        var target = new ReconciliationRolloutTarget("Client-A", "Ops-West", "Broker-X");

        policy.IsEnabled(target).Should().BeTrue();
    }

    [Theory]
    [InlineData(1, 2)]
    [InlineData(2, 4)]
    [InlineData(3, 8)]
    public void ResilienceOptions_GetRetryDelay_UsesExponentialBackoff(int attempt, int expectedSeconds)
    {
        var options = new ReconciliationOrchestrationResilienceOptions(MaxRetries: 5, RetryBaseDelay: TimeSpan.FromSeconds(2));

        var delay = options.GetRetryDelay(attempt);

        delay.Should().Be(TimeSpan.FromSeconds(expectedSeconds));
    }
}
