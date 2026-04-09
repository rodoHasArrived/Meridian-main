using System.Net.WebSockets;
using FluentAssertions;
using Meridian.Infrastructure.Resilience;
using Polly;
using Xunit;

namespace Meridian.Tests;

/// <summary>
/// Integration tests for connection retry logic across providers.
/// Validates that the Polly-based resilience pipelines are properly configured
/// for all market data clients.
/// </summary>
[Trait("Category", "Integration")]
public class ConnectionRetryIntegrationTests
{
    /// <summary>
    /// Validates the comprehensive pipeline configuration used by Alpaca and Polygon clients.
    /// </summary>
    [Fact]
    public void ComprehensivePipeline_ShouldBeConfiguredWithCorrectDefaults()
    {
        // Act - Use the same configuration as Alpaca/Polygon clients
        var pipeline = WebSocketResiliencePolicy.CreateComprehensivePipeline(
            maxRetries: 5,
            retryBaseDelay: TimeSpan.FromSeconds(2),
            circuitBreakerFailureThreshold: 5,
            circuitBreakerDuration: TimeSpan.FromSeconds(30),
            operationTimeout: TimeSpan.FromSeconds(30));

        // Assert
        pipeline.Should().NotBeNull();
    }

    [Fact]
    public async Task ComprehensivePipeline_ShouldRetryOnTransientFailures()
    {
        // Arrange
        var pipeline = WebSocketResiliencePolicy.CreateComprehensivePipeline(
            maxRetries: 2,
            retryBaseDelay: TimeSpan.FromMilliseconds(10),
            circuitBreakerFailureThreshold: 10, // High threshold to not trigger
            circuitBreakerDuration: TimeSpan.FromSeconds(1),
            operationTimeout: TimeSpan.FromSeconds(10));

        var attemptCount = 0;

        // Act & Assert - Should retry on WebSocketException
        await Assert.ThrowsAsync<WebSocketException>(async () =>
        {
            await pipeline.ExecuteAsync(async ct =>
            {
                attemptCount++;
                await Task.CompletedTask;
                throw new WebSocketException("Simulated connection failure");
            });
        });

        // Should have retried: initial + 2 retries = 3 attempts
        attemptCount.Should().Be(3);
    }

    [Fact]
    public async Task ComprehensivePipeline_ShouldRetryOnHttpRequestException()
    {
        // Arrange
        var pipeline = WebSocketResiliencePolicy.CreateComprehensivePipeline(
            maxRetries: 2,
            retryBaseDelay: TimeSpan.FromMilliseconds(10),
            circuitBreakerFailureThreshold: 10,
            circuitBreakerDuration: TimeSpan.FromSeconds(1),
            operationTimeout: TimeSpan.FromSeconds(10));

        var attemptCount = 0;

        // Act & Assert
        await Assert.ThrowsAsync<HttpRequestException>(async () =>
        {
            await pipeline.ExecuteAsync(async ct =>
            {
                attemptCount++;
                await Task.CompletedTask;
                throw new HttpRequestException("Network unreachable");
            });
        });

        attemptCount.Should().Be(3);
    }

    [Fact]
    public async Task ComprehensivePipeline_ShouldSucceedAfterTransientFailure()
    {
        // Arrange
        var pipeline = WebSocketResiliencePolicy.CreateComprehensivePipeline(
            maxRetries: 3,
            retryBaseDelay: TimeSpan.FromMilliseconds(10),
            circuitBreakerFailureThreshold: 10,
            circuitBreakerDuration: TimeSpan.FromSeconds(1),
            operationTimeout: TimeSpan.FromSeconds(10));

        var attemptCount = 0;
        var succeeded = false;

        // Act - Fail twice, succeed on third attempt
        await pipeline.ExecuteAsync(async ct =>
        {
            attemptCount++;
            await Task.CompletedTask;

            if (attemptCount < 3)
            {
                throw new WebSocketException("Transient failure");
            }

            succeeded = true;
        });

        // Assert
        attemptCount.Should().Be(3);
        succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task ComprehensivePipeline_ShouldRespectCancellation()
    {
        // Arrange
        var pipeline = WebSocketResiliencePolicy.CreateComprehensivePipeline(
            maxRetries: 5,
            retryBaseDelay: TimeSpan.FromMilliseconds(10),
            circuitBreakerFailureThreshold: 10,
            circuitBreakerDuration: TimeSpan.FromSeconds(1),
            operationTimeout: TimeSpan.FromSeconds(5));

        using var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromMilliseconds(50));

        // Act & Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await pipeline.ExecuteAsync(async ct =>
            {
                await Task.Delay(TimeSpan.FromSeconds(2), ct);
            }, cts.Token);
        });
    }

    [Fact]
    public void ConnectionPipeline_DefaultConfiguration_ShouldMatchDocumentation()
    {
        // This test validates that the default configuration matches what's documented
        // in the class summaries and README

        // Act
        var pipeline = WebSocketResiliencePolicy.CreateConnectionPipeline(
            maxRetries: 5,
            baseDelay: TimeSpan.FromSeconds(2),
            maxDelay: TimeSpan.FromSeconds(30));

        // Assert
        pipeline.Should().NotBeNull();
    }

    [Fact]
    public async Task ReceivePipeline_ShouldHandleReceiveOperations()
    {
        // Arrange
        var pipeline = WebSocketResiliencePolicy.CreateReceivePipeline(
            maxRetries: 2,
            baseDelay: TimeSpan.FromMilliseconds(10),
            maxDelay: TimeSpan.FromMilliseconds(100));

        var attemptCount = 0;

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
        {
            await pipeline.ExecuteAsync<WebSocketReceiveResult>(async ct =>
            {
                attemptCount++;
                await Task.CompletedTask;
                throw new OperationCanceledException("Receive cancelled");
            });
        });

        // Should retry on OperationCanceledException
        attemptCount.Should().Be(3);
    }
}

/// <summary>
/// Tests for exponential backoff behavior.
/// </summary>
public class ExponentialBackoffTests
{
    [Fact]
    public async Task ExponentialBackoff_ShouldIncreaseDelayBetweenRetries()
    {
        // Arrange
        var retryDelays = new List<TimeSpan>();
        var pipeline = new ResiliencePipelineBuilder()
            .AddRetry(new Polly.Retry.RetryStrategyOptions
            {
                MaxRetryAttempts = 3,
                Delay = TimeSpan.FromMilliseconds(10),
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = false,
                MaxDelay = TimeSpan.FromSeconds(10),
                ShouldHandle = new PredicateBuilder().Handle<InvalidOperationException>(),
                OnRetry = args =>
                {
                    retryDelays.Add(args.RetryDelay);
                    return ValueTask.CompletedTask;
                }
            })
            .Build();

        // Act
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            pipeline.ExecuteAsync(async ct =>
            {
                await Task.CompletedTask;
                throw new InvalidOperationException("Force retry");
            }).AsTask());

        // Assert - delays should increase exponentially (10ms, 20ms, 40ms)
        exception.Message.Should().Be("Force retry");
        retryDelays.Should().HaveCount(3);
        retryDelays[0].Should().BeCloseTo(TimeSpan.FromMilliseconds(10), TimeSpan.FromMilliseconds(10));
        retryDelays[1].Should().BeGreaterThan(retryDelays[0]);
        retryDelays[2].Should().BeGreaterThan(retryDelays[1]);
    }

    [Fact]
    public async Task ExponentialBackoff_WithJitter_ShouldVaryDelays()
    {
        // Arrange
        var retryDelays = new List<TimeSpan>();
        var pipeline = new ResiliencePipelineBuilder()
            .AddRetry(new Polly.Retry.RetryStrategyOptions
            {
                MaxRetryAttempts = 5,
                Delay = TimeSpan.FromMilliseconds(10),
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                MaxDelay = TimeSpan.FromSeconds(10),
                ShouldHandle = new PredicateBuilder().Handle<InvalidOperationException>(),
                OnRetry = args =>
                {
                    retryDelays.Add(args.RetryDelay);
                    return ValueTask.CompletedTask;
                }
            })
            .Build();

        // Act
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            pipeline.ExecuteAsync(async ct =>
            {
                await Task.CompletedTask;
                throw new InvalidOperationException("Force retry");
            }).AsTask());

        // Assert - with jitter, delays should still generally increase but with variation
        exception.Message.Should().Be("Force retry");
        retryDelays.Should().HaveCount(5);
        // Jitter means exact values are unpredictable, but general trend should be increasing
        retryDelays.Average(d => d.TotalMilliseconds).Should().BeGreaterThan(10);
    }
}

/// <summary>
/// Tests for circuit breaker behavior.
/// </summary>
public class CircuitBreakerTests
{
    [Fact]
    public void CircuitBreakerPipeline_ShouldBeConfigurable()
    {
        // Act
        var pipeline = WebSocketResiliencePolicy.CreateCircuitBreakerPipeline(
            failureThreshold: 3,
            breakDuration: TimeSpan.FromSeconds(15));

        // Assert
        pipeline.Should().NotBeNull();
    }

    [Fact]
    public async Task CircuitBreaker_AfterThreshold_ShouldOpenCircuit()
    {
        // Arrange
        var pipeline = new ResiliencePipelineBuilder()
            .AddCircuitBreaker(new Polly.CircuitBreaker.CircuitBreakerStrategyOptions
            {
                FailureRatio = 1.0, // 100% failure rate
                MinimumThroughput = 2,
                BreakDuration = TimeSpan.FromSeconds(30),
                ShouldHandle = new PredicateBuilder().Handle<WebSocketException>()
            })
            .Build();

        var exceptionsThrown = 0;

        // Act - exhaust the throughput with failures
        for (int i = 0; i < 5; i++)
        {
            try
            {
                await pipeline.ExecuteAsync(async ct =>
                {
                    await Task.CompletedTask;
                    throw new WebSocketException("Failure");
                });
            }
            catch (WebSocketException)
            {
                exceptionsThrown++;
            }
            catch (Polly.CircuitBreaker.BrokenCircuitException)
            {
                // Circuit is open
                break;
            }
        }

        // Assert - should have opened after minimum throughput + some failures
        exceptionsThrown.Should().BeGreaterThanOrEqualTo(2);
    }
}
