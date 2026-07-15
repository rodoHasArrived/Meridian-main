using FluentAssertions;
using Meridian.Infrastructure.Resilience;
using System.Net;
using System.Net.WebSockets;
using System.Reflection;
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
    public async Task DisconnectAsync_WhenReceiveTaskIgnoresCancellation_HonorsShutdownCancellation()
    {
        var manager = new WebSocketConnectionManager(
            providerName: "test-provider");
        var receiveTaskRelease = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var connectionCts = new CancellationTokenSource();
        var receiveLoopCts = new CancellationTokenSource();
        Task? disconnectTask = null;

        SetPrivateField(manager, "_connectionCts", connectionCts);
        SetPrivateField(manager, "_receiveLoopCts", receiveLoopCts);
        SetPrivateField(manager, "_receiveTask", receiveTaskRelease.Task);

        try
        {
            using var shutdownCts = new CancellationTokenSource();
            disconnectTask = manager.DisconnectAsync(shutdownCts.Token);
            shutdownCts.Cancel();

            var completedTask = await Task.WhenAny(
                disconnectTask,
                Task.Delay(TimeSpan.FromSeconds(2)));

            completedTask.Should().BeSameAs(
                disconnectTask,
                "shutdown cancellation must stop waiting for a cancellation-ignoring receive handler");
            await FluentActions.Awaiting(() => disconnectTask!)
                .Should().ThrowAsync<OperationCanceledException>();

            GetPrivateField<Task>(manager, "_receiveTask").Should().BeNull();
            GetPrivateField<CancellationTokenSource>(manager, "_connectionCts").Should().BeNull();
            GetPrivateField<CancellationTokenSource>(manager, "_receiveLoopCts").Should().BeNull();

            FluentActions.Invoking(connectionCts.Cancel)
                .Should().Throw<ObjectDisposedException>();
            FluentActions.Invoking(receiveLoopCts.Cancel)
                .Should().Throw<ObjectDisposedException>();
        }
        finally
        {
            receiveTaskRelease.TrySetResult(true);
            if (disconnectTask != null)
            {
                try
                {
                    await disconnectTask;
                }
                catch (OperationCanceledException)
                {
                    // Expected when shutdown cancellation wins the receive-task wait.
                }
            }

            await manager.DisposeAsync();
        }
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

    private static void SetPrivateField<T>(WebSocketConnectionManager manager, string fieldName, T value)
    {
        var field = typeof(WebSocketConnectionManager).GetField(
            fieldName,
            BindingFlags.Instance | BindingFlags.NonPublic);

        field.Should().NotBeNull();
        field!.SetValue(manager, value);
    }

    private static T? GetPrivateField<T>(WebSocketConnectionManager manager, string fieldName)
        where T : class
    {
        var field = typeof(WebSocketConnectionManager).GetField(
            fieldName,
            BindingFlags.Instance | BindingFlags.NonPublic);

        field.Should().NotBeNull();
        return field!.GetValue(manager) as T;
    }
}
