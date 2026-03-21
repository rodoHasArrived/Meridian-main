using System.Net.WebSockets;
using FluentAssertions;
using Meridian.Infrastructure.Resilience;
using Polly;
using Polly.Timeout;
using Xunit;

namespace Meridian.Tests;

/// <summary>
/// Tests for WebSocket resilience policies.
/// Validates retry, circuit breaker, and timeout behaviors.
/// Testing patterns inspired by Polly's test suite and production best practices.
/// </summary>
public class WebSocketResiliencePolicyTests
{
    [Fact]
    public void CreateConnectionPipeline_ShouldReturnValidPipeline()
    {
        // Act
        var pipeline = WebSocketResiliencePolicy.CreateConnectionPipeline(
            maxRetries: 3,
            baseDelay: TimeSpan.FromMilliseconds(100),
            maxDelay: TimeSpan.FromSeconds(5));

        // Assert
        pipeline.Should().NotBeNull();
    }

    [Fact]
    public void CreateCircuitBreakerPipeline_ShouldReturnValidPipeline()
    {
        // Act
        var pipeline = WebSocketResiliencePolicy.CreateCircuitBreakerPipeline(
            failureThreshold: 5,
            breakDuration: TimeSpan.FromSeconds(10));

        // Assert
        pipeline.Should().NotBeNull();
    }

    [Fact]
    public void CreateTimeoutPipeline_ShouldReturnValidPipeline()
    {
        // Act
        var pipeline = WebSocketResiliencePolicy.CreateTimeoutPipeline(
            timeout: TimeSpan.FromSeconds(5));

        // Assert
        pipeline.Should().NotBeNull();
    }

    [Fact]
    public void CreateComprehensivePipeline_ShouldCombineAllPolicies()
    {
        // Act
        var pipeline = WebSocketResiliencePolicy.CreateComprehensivePipeline(
            maxRetries: 3,
            retryBaseDelay: TimeSpan.FromMilliseconds(100),
            circuitBreakerFailureThreshold: 5,
            circuitBreakerDuration: TimeSpan.FromSeconds(10),
            operationTimeout: TimeSpan.FromSeconds(5));

        // Assert
        pipeline.Should().NotBeNull();
    }

    [Fact]
    public async Task ConnectionPipeline_ShouldRetryOnWebSocketException()
    {
        // Arrange
        var pipeline = WebSocketResiliencePolicy.CreateConnectionPipeline(
            maxRetries: 2,
            baseDelay: TimeSpan.FromMilliseconds(10),
            maxDelay: TimeSpan.FromMilliseconds(100));

        var attemptCount = 0;

        // Act & Assert
        await Assert.ThrowsAsync<WebSocketException>(async () =>
        {
            await pipeline.ExecuteAsync(async ct =>
            {
                attemptCount++;
                await Task.CompletedTask;
                throw new WebSocketException("Test exception");
            });
        });

        // Should have tried initial attempt + 2 retries = 3 total attempts
        attemptCount.Should().Be(3);
    }

    [Fact]
    public async Task TimeoutPipeline_ShouldThrowOnTimeout()
    {
        // Arrange
        var pipeline = WebSocketResiliencePolicy.CreateTimeoutPipeline(
            timeout: TimeSpan.FromMilliseconds(100));

        // Act & Assert - Polly throws TimeoutRejectedException
        await Assert.ThrowsAsync<TimeoutRejectedException>(async () =>
        {
            await pipeline.ExecuteAsync(async ct =>
            {
                await Task.Delay(TimeSpan.FromSeconds(1), ct);
            });
        });
    }

    [Fact]
    public async Task TimeoutPipeline_ShouldSucceedBeforeTimeout()
    {
        // Arrange
        var pipeline = WebSocketResiliencePolicy.CreateTimeoutPipeline(
            timeout: TimeSpan.FromSeconds(1));

        var executed = false;

        // Act
        await pipeline.ExecuteAsync(async ct =>
        {
            await Task.Delay(TimeSpan.FromMilliseconds(10), ct);
            executed = true;
        });

        // Assert
        executed.Should().BeTrue();
    }
}

/// <summary>
/// Tests for WebSocketHeartbeat functionality.
/// </summary>
public class WebSocketHeartbeatTests : IAsyncLifetime
{
    private ClientWebSocket? _ws;

    public Task InitializeAsync()
    {
        _ws = new ClientWebSocket();
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        if (_ws != null)
        {
            _ws.Dispose();
            await Task.CompletedTask;
        }
    }

    [Fact]
    public async Task Constructor_ShouldInitializeWithDefaults()
    {
        // Arrange & Act
        await using var heartbeat = new WebSocketHeartbeat(_ws!);

        // Assert
        heartbeat.Should().NotBeNull();
    }

    [Fact]
    public async Task Constructor_ShouldAcceptCustomIntervals()
    {
        // Arrange & Act
        await using var heartbeat = new WebSocketHeartbeat(
            _ws!,
            pingInterval: TimeSpan.FromSeconds(10),
            pongTimeout: TimeSpan.FromSeconds(5));

        // Assert
        heartbeat.Should().NotBeNull();
    }

    [Fact]
    public async Task RecordPongReceived_ShouldNotThrow()
    {
        // Arrange
        await using var heartbeat = new WebSocketHeartbeat(_ws!);

        // Act & Assert
        var act = () => heartbeat.RecordPongReceived();
        act.Should().NotThrow();
    }

    [Fact]
    public async Task ConnectionLost_EventShouldFireWhenRaised()
    {
        // Arrange
        await using var heartbeat = new WebSocketHeartbeat(_ws!);
        var eventFired = false;

        heartbeat.ConnectionLost += async () =>
        {
            eventFired = true;
            await Task.CompletedTask;
        };

        // Act - raise the event via the internal helper exposed for testing
        await heartbeat.RaiseConnectionLostAsync();

        // Assert
        eventFired.Should().BeTrue();
    }
}
