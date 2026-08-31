using FluentAssertions;
using Meridian.Infrastructure.DataSources;
using Meridian.Ui.Shared.Endpoints;
using Xunit;

namespace Meridian.Tests.Ui;

public sealed class ProviderEndpointProjectionTests
{
    [Theory]
    [InlineData(false, "Connected", true, "disabled")]
    [InlineData(true, "Reconnecting", false, "reconnecting")]
    [InlineData(true, "Degraded", false, "degraded")]
    [InlineData(true, "Connected", true, "connected")]
    [InlineData(true, "Disconnected", false, "disconnected")]
    public void ResolveConnectionState_ProjectsOperatorStateWithoutEquatingEnabledWithConnected(
        bool isEnabled,
        string lifecycleState,
        bool isConnected,
        string expected)
    {
        ProviderExtendedEndpoints.ResolveConnectionState(isEnabled, lifecycleState, isConnected)
            .Should().Be(expected);
    }

    [Theory]
    [InlineData(false, true, false)]
    [InlineData(true, true, true)]
    [InlineData(true, false, false)]
    public void ResolveIsConnected_DisabledProvidersCannotAppearConnected(
        bool isEnabled,
        bool runtimeIsConnected,
        bool expected)
    {
        ProviderExtendedEndpoints.ResolveIsConnected(isEnabled, runtimeIsConnected)
            .Should().Be(expected);
    }

    [Fact]
    public void ResolveIsConnected_EnabledProviderWithoutDiagnostics_RemainsUnknown()
    {
        ProviderExtendedEndpoints.ResolveIsConnected(isEnabled: true, runtimeIsConnected: null)
            .Should().BeNull();
    }

    [Fact]
    public void CreateRegistrationReportDto_SanitizesAndBoundsPublicFailureText()
    {
        var report = new ProviderRegistrationReport(
            new DateTimeOffset(2026, 7, 13, 12, 0, 0, TimeSpan.Zero),
            DiscoveredSourceCount: 2,
            ModuleCandidateCount: 3,
            ModuleActivationAttemptCount: 3,
            ModuleRegistrationAttemptCount: 2,
            RegisteredModuleCount: 1,
            SkippedModuleCount: 0,
            Array.AsReadOnly(new[]
            {
                new DataSourceDiscoveryFailure(
                    "register",
                    "https://operator:password@example.test/provider",
                    "secret-module",
                    nameof(InvalidOperationException),
                    $"authorization=Bearer abc123 apiKey=top-secret {new string('x', 700)}")
            }));

        var dto = ProviderEndpoints.CreateRegistrationReportDto(report);

        dto.IsHealthy.Should().BeFalse();
        dto.FailedModuleCount.Should().Be(1);
        dto.Failures.Should().ContainSingle();
        var failure = dto.Failures[0];
        failure.Subject.Should().Contain("[REDACTED]");
        failure.ErrorMessage.Should().Contain("[REDACTED]");
        failure.ErrorMessage.Should().NotContain("abc123").And.NotContain("top-secret");
        failure.ErrorMessage.Length.Should().BeLessThanOrEqualTo(512);
    }
}
