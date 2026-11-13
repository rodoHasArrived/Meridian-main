using System.Net;
using System.Net.Http.Headers;
using Meridian.Infrastructure.Resilience;
using Meridian.Infrastructure.Utilities;
using Polly;
using Polly.CircuitBreaker;
using Polly.Timeout;
using Xunit;

namespace Meridian.Tests.Infrastructure.Resilience;

public sealed class HttpResiliencePolicyTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task QuotaResponse_IsReturnedOnceWithRetryAfterForProviderHandling(bool comprehensive)
    {
        var pipeline = CreatePipeline(comprehensive);
        using var quotaResponse = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        quotaResponse.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(42));
        var attempts = 0;

        var response = await pipeline.ExecuteAsync(_ =>
        {
            attempts++;
            return ValueTask.FromResult(quotaResponse);
        });
        var result = await new HttpResponseHandler("test").TryHandleResponseAsync(response, "AAPL", "bars");

        Assert.Same(quotaResponse, response);
        Assert.Equal(1, attempts);
        Assert.True(result.IsRateLimited);
        Assert.Equal(TimeSpan.FromSeconds(42), result.RetryAfter);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task QuotaResponses_DoNotOpenCircuit(bool comprehensive)
    {
        var pipeline = CreateBreakerPipeline(comprehensive);
        for (var i = 0; i < 3; i++)
        {
            using var response = await pipeline.ExecuteAsync(_ =>
                ValueTask.FromResult(new HttpResponseMessage(HttpStatusCode.TooManyRequests)));
            Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        }

        using var healthy = await pipeline.ExecuteAsync(_ =>
            ValueTask.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));
        Assert.Equal(HttpStatusCode.OK, healthy.StatusCode);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task QuotaExceptions_AreNotRetriedAndDoNotOpenCircuit(bool comprehensive)
    {
        var pipeline = CreateBreakerPipeline(comprehensive);
        var attempts = 0;
        for (var i = 0; i < 3; i++)
        {
            await Assert.ThrowsAsync<HttpRequestException>(async () => await pipeline.ExecuteAsync(_ =>
            {
                attempts++;
                return ValueTask.FromException<HttpResponseMessage>(
                    new HttpRequestException("quota exhausted", null, HttpStatusCode.TooManyRequests));
            }));
        }

        Assert.Equal(3, attempts);
        using var healthy = await pipeline.ExecuteAsync(_ =>
            ValueTask.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));
        Assert.Equal(HttpStatusCode.OK, healthy.StatusCode);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CallerCancellation_DoesNotOpenCircuit(bool comprehensive)
    {
        var pipeline = CreateBreakerPipeline(comprehensive);
        for (var i = 0; i < 3; i++)
        {
            using var cancellation = new CancellationTokenSource();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await pipeline.ExecuteAsync(ct =>
            {
                cancellation.Cancel();
                return ValueTask.FromException<HttpResponseMessage>(new TaskCanceledException("cancelled", null, ct));
            }, cancellation.Token));
        }

        using var healthy = await pipeline.ExecuteAsync(_ =>
            ValueTask.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));
        Assert.Equal(HttpStatusCode.OK, healthy.StatusCode);
    }

    [Fact]
    public async Task ComprehensivePipeline_TimesOutHungAttemptThenRetriesWithFreshBudget()
    {
        var pipeline = HttpResiliencePolicy.CreateComprehensivePipeline(
            maxRetries: 1,
            retryBaseDelay: TimeSpan.Zero,
            requestTimeout: TimeSpan.FromMilliseconds(100));
        var attempts = 0;

        using var response = await pipeline.ExecuteAsync(async ct =>
        {
            if (++attempts == 1)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            }

            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        Assert.Equal(2, attempts);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ComprehensivePipeline_HungAttemptsExhaustRetryBudget()
    {
        var pipeline = HttpResiliencePolicy.CreateComprehensivePipeline(
            maxRetries: 1,
            retryBaseDelay: TimeSpan.Zero,
            requestTimeout: TimeSpan.FromMilliseconds(100));
        var attempts = 0;

        await Assert.ThrowsAsync<TimeoutRejectedException>(async () => await pipeline.ExecuteAsync(async ct =>
        {
            attempts++;
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }));

        Assert.Equal(2, attempts);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ServerErrors_StillOpenCircuit(bool comprehensive)
    {
        var pipeline = CreateBreakerPipeline(comprehensive);
        for (var i = 0; i < 2; i++)
        {
            using var response = await pipeline.ExecuteAsync(_ =>
                ValueTask.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)));
        }

        await Assert.ThrowsAsync<BrokenCircuitException>(async () => await pipeline.ExecuteAsync(_ =>
            ValueTask.FromResult(new HttpResponseMessage(HttpStatusCode.OK))));
    }

    private static ResiliencePipeline<HttpResponseMessage> CreatePipeline(bool comprehensive) =>
        comprehensive
            ? HttpResiliencePolicy.CreateComprehensivePipeline(retryBaseDelay: TimeSpan.Zero)
            : HttpResiliencePolicy.CreateRetryPipeline(baseDelay: TimeSpan.Zero);

    private static ResiliencePipeline<HttpResponseMessage> CreateBreakerPipeline(bool comprehensive) =>
        comprehensive
            ? HttpResiliencePolicy.CreateComprehensivePipeline(
                maxRetries: 1,
                retryBaseDelay: TimeSpan.Zero,
                circuitBreakerFailureThreshold: 2)
            : HttpResiliencePolicy.CreateCircuitBreakerPipeline(failureThreshold: 2);
}
