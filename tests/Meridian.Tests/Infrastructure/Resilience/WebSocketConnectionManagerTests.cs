using FluentAssertions;
using Meridian.Infrastructure.Resilience;
using System.Net;
using System.Net.WebSockets;
using System.Security.Authentication;
using Xunit;

namespace Meridian.Tests.Infrastructure.Resilience;

/// <summary>
/// Unit tests for WebSocketConnectionManager lifecycle and resource management.
/// </summary>
public class WebSocketConnectionManagerTests
{
    [Fact]
    public void Constructor_WithValidArgs_CreatesInstance()
    {
        var manager = new WebSocketConnectionManager(
            providerName: "test-provider");

        manager.Should().NotBeNull();
        manager.IsConnected.Should().BeFalse();
    }

    [Fact]
    public void StartReceiveLoop_WithoutConnect_ThrowsInvalidOperationException()
    {
        var manager = new WebSocketConnectionManager(
            providerName: "test-provider");

        var act = () => manager.StartReceiveLoop(msg => Task.CompletedTask);
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*not connected*");
    }

    [Fact]
    public async Task DisposeAsync_WithoutConnect_DoesNotThrow()
    {
        var manager = new WebSocketConnectionManager(
            providerName: "test-provider");

        // Should not throw even if never connected
        await manager.DisposeAsync();
    }

    [Fact]
    public void GetDiagnosticsSnapshot_BeforeConnect_ExposesSafeInitialProviderState()
    {
        var manager = new WebSocketConnectionManager(
            providerName: "test-provider");

        var snapshot = manager.GetDiagnosticsSnapshot();

        snapshot.ProviderName.Should().Be("test-provider");
        snapshot.LifecycleState.Should().Be(ProviderConnectionLifecycleState.Configured);
        snapshot.IsConnected.Should().BeFalse();
        snapshot.IsReconnecting.Should().BeFalse();
        snapshot.LastHeartbeatReceivedAt.Should().BeNull();
        snapshot.LastFailureKind.Should().BeNull();
        snapshot.LastError.Should().BeNull();
    }

    [Fact]
    public void RecordPongReceived_UpdatesHeartbeatDiagnosticsWithoutRequiringMessagePayload()
    {
        var manager = new WebSocketConnectionManager(
            providerName: "test-provider");

        manager.RecordPongReceived();

        var snapshot = manager.GetDiagnosticsSnapshot();
        snapshot.LastHeartbeatReceivedAt.Should().NotBeNull();
        snapshot.LastMessageReceivedAt.Should().BeNull();
    }

    [Theory]
    [MemberData(nameof(FailureClassificationCases))]
    public void ProviderFailureClassifier_ClassifiesRetrySafety(Exception exception, ProviderFailureKind expectedKind, bool expectedRetryable)
    {
        var kind = ProviderFailureClassifier.Classify(exception);

        kind.Should().Be(expectedKind);
        ProviderFailureClassifier.IsRetryable(kind).Should().Be(expectedRetryable);
    }

    public static IEnumerable<object[]> FailureClassificationCases()
    {
        yield return new object[]
        {
            new WebSocketException("socket closed"),
            ProviderFailureKind.TransientNetworkFailure,
            true
        };
        yield return new object[]
        {
            new HttpRequestException("rate limited", null, HttpStatusCode.TooManyRequests),
            ProviderFailureKind.ProviderRateLimit,
            true
        };
        yield return new object[]
        {
            new HttpRequestException("provider unavailable", null, HttpStatusCode.ServiceUnavailable),
            ProviderFailureKind.ProviderOutage,
            true
        };
        yield return new object[]
        {
            new HttpRequestException("unauthorized", null, HttpStatusCode.Unauthorized),
            ProviderFailureKind.AuthenticationOrAuthorizationFailure,
            false
        };
        yield return new object[]
        {
            new AuthenticationException("authentication failed"),
            ProviderFailureKind.AuthenticationOrAuthorizationFailure,
            false
        };
        yield return new object[]
        {
            new InvalidOperationException("Alpaca API key is not configured."),
            ProviderFailureKind.LocalConfigurationError,
            false
        };
        yield return new object[]
        {
            new System.Text.Json.JsonException("malformed provider response"),
            ProviderFailureKind.MalformedProviderResponse,
            false
        };
    }
}
