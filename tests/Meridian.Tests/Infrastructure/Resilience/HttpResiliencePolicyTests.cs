using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using FluentAssertions;
using Meridian.Contracts.Domain.Models;
using Meridian.Core.Exceptions;
using Meridian.Infrastructure.Adapters.Alpaca;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Infrastructure.Adapters.Finnhub;
using Meridian.Infrastructure.Adapters.Fred;
using Meridian.Infrastructure.Adapters.Tiingo;
using Meridian.Infrastructure.Resilience;
using Polly.CircuitBreaker;
using Polly.Timeout;
using Xunit;

namespace Meridian.Tests.Infrastructure.Resilience;

public sealed class HttpResiliencePolicyTests
{
    [Fact]
    public async Task ComprehensivePipeline_StalledSend_RetriesWithAFreshAttemptTimeout()
    {
        using var handler = new ScriptedHandler(async (attempt, ct) =>
        {
            if (attempt == 1)
                await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        using var client = new HttpClient(handler);
        var pipeline = HttpResiliencePolicy.CreateComprehensivePipeline(
            maxRetries: 1, retryBaseDelay: TimeSpan.Zero, requestTimeout: TimeSpan.FromMilliseconds(50));
        using var watchdog = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        using var response = await pipeline.ExecuteAsync(async ct => await client.GetAsync("https://provider.invalid/history", ct), watchdog.Token)
            .AsTask().WaitAsync(TimeSpan.FromSeconds(5));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        handler.Attempts.Should().Be(2);
    }

    [Fact]
    public async Task ComprehensivePipeline_ExhaustedTimeouts_AreCountedByCircuitBreaker()
    {
        using var handler = new ScriptedHandler(async (_, ct) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        using var client = new HttpClient(handler);
        var pipeline = HttpResiliencePolicy.CreateComprehensivePipeline(
            maxRetries: 1, retryBaseDelay: TimeSpan.Zero,
            circuitBreakerFailureThreshold: 2, requestTimeout: TimeSpan.FromMilliseconds(50));
        using var watchdog = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var send = () => pipeline.ExecuteAsync(async ct => await client.GetAsync("https://provider.invalid/history", ct), watchdog.Token).AsTask();

        await send.Should().ThrowAsync<TimeoutRejectedException>();
        await send.Should().ThrowAsync<TimeoutRejectedException>();
        await send.Should().ThrowAsync<BrokenCircuitException>();
        handler.Attempts.Should().Be(4);
    }

    [Fact]
    public async Task ComprehensivePipeline_CallerCancellation_IsNeitherRetriedNorCountedAsProviderFailure()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var handler = new ScriptedHandler(async (attempt, ct) =>
        {
            if (attempt <= 2)
            {
                entered.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            }
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        using var client = new HttpClient(handler);
        var pipeline = HttpResiliencePolicy.CreateComprehensivePipeline(
            maxRetries: 1, retryBaseDelay: TimeSpan.Zero,
            circuitBreakerFailureThreshold: 2, requestTimeout: TimeSpan.FromSeconds(5));

        for (var index = 0; index < 2; index++)
        {
            using var cancellation = new CancellationTokenSource();
            entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var operation = pipeline.ExecuteAsync(async ct => await client.GetAsync("https://provider.invalid/history", ct), cancellation.Token).AsTask();
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation);
        }

        using var response = await pipeline.ExecuteAsync(async ct => await client.GetAsync("https://provider.invalid/history", ct));
        response.IsSuccessStatusCode.Should().BeTrue();
        handler.Attempts.Should().Be(3);
    }

    [Fact]
    public async Task ComprehensivePipeline_RateLimitRetry_RespectsRetryAfterAndDisposesDiscardedResponse()
    {
        var content = new TrackedContent();
        var elapsed = Stopwatch.StartNew();
        var attemptTimes = new List<TimeSpan>();
        using var handler = new ScriptedHandler((attempt, _) =>
        {
            attemptTimes.Add(elapsed.Elapsed);
            var response = new HttpResponseMessage(attempt == 1 ? HttpStatusCode.TooManyRequests : HttpStatusCode.OK);
            if (attempt == 1)
            {
                response.Content = content;
                response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromMilliseconds(100));
            }
            return Task.FromResult(response);
        });
        using var client = new HttpClient(handler);
        var pipeline = HttpResiliencePolicy.CreateComprehensivePipeline(maxRetries: 1, retryBaseDelay: TimeSpan.Zero);

        using var response = await pipeline.ExecuteAsync(async ct => await client.GetAsync("https://provider.invalid/history", ct));

        response.IsSuccessStatusCode.Should().BeTrue();
        handler.Attempts.Should().Be(2);
        (attemptTimes[1] - attemptTimes[0]).Should().BeGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(90));
        content.IsDisposed.Should().BeTrue();
    }

    [Fact]
    public async Task HistoricalProvider_TransientRetries_CountAndPaceEveryPhysicalRequest()
    {
        var discarded = new List<TrackedContent>();
        var attemptTimes = new List<TimeSpan>();
        var elapsed = Stopwatch.StartNew();
        using var handler = new ScriptedHandler((attempt, _) =>
        {
            attemptTimes.Add(elapsed.Elapsed);
            var content = new TrackedContent();
            if (attempt < 3)
                discarded.Add(content);
            return Task.FromResult(new HttpResponseMessage(attempt < 3 ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK)
            { Content = content });
        });
        using var client = new HttpClient(handler);
        using var provider = new TestHistoricalProvider(client);

        using var response = await provider.SendAsync().WaitAsync(TimeSpan.FromSeconds(20));

        handler.Attempts.Should().Be(3);
        provider.GetRateLimitInfo().RequestsMade.Should().Be(3);
        provider.GetRateLimitDiagnosticsSnapshot().RequestsInWindow.Should().Be(3);
        discarded.Should().OnlyContain(content => content.IsDisposed);
        for (var index = 1; index < attemptTimes.Count; index++)
            (attemptTimes[index] - attemptTimes[index - 1]).Should().BeGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(65));
        await response.Content.ReadAsStringAsync(); // The final response remains owned by its caller.
    }

    [Fact]
    public async Task HistoricalProvider_RateLimit_IsReportedImmediatelyWithRetryAfter()
    {
        using var handler = new ScriptedHandler((_, _) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(45));
            return Task.FromResult(response);
        });
        using var client = new HttpClient(handler);
        using var provider = new TestHistoricalProvider(client);

        var failure = await Assert.ThrowsAsync<RateLimitException>(() => provider.ReadAsync());

        failure.RetryAfter.Should().Be(TimeSpan.FromSeconds(45));
        handler.Attempts.Should().Be(1);
        provider.GetRateLimitInfo().IsLimited.Should().BeTrue();
    }

    [Fact]
    public async Task HistoricalProvider_CancelledAdmission_DoesNotSendOrSpendAnotherSlot()
    {
        using var handler = new ScriptedHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));
        using var client = new HttpClient(handler);
        using var provider = new TestHistoricalProvider(client);
        using var first = await provider.SendAsync();
        using var cancellation = new CancellationTokenSource();

        var operation = provider.SendAsync(cancellation.Token);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation);

        handler.Attempts.Should().Be(1);
        provider.GetRateLimitInfo().RequestsMade.Should().Be(1);
    }

    [Theory]
    [InlineData("alpaca")]
    [InlineData("finnhub")]
    [InlineData("tiingo")]
    [InlineData("fred")]
    public async Task HistoricalAdapter_Availability_UsesSharedRetryAndPhysicalRequestAccounting(string adapter)
    {
        using var handler = new ScriptedHandler((attempt, _) => Task.FromResult(
            new HttpResponseMessage(attempt == 1 ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK)));
        using var client = new HttpClient(handler);
        using BaseHistoricalDataProvider provider = adapter switch
        {
            "alpaca" => new AlpacaHistoricalDataProvider(keyId: "synthetic-key", secretKey: "synthetic-secret", httpClient: client),
            "finnhub" => new FinnhubHistoricalDataProvider(apiKey: "synthetic-key", httpClient: client),
            "tiingo" => new TiingoHistoricalDataProvider(apiToken: "synthetic-key", httpClient: client),
            _ => new FredHistoricalDataProvider(apiKey: "synthetic-key", httpClient: client)
        };

        var available = await provider.IsAvailableAsync().WaitAsync(TimeSpan.FromSeconds(10));

        available.Should().BeTrue();
        handler.Attempts.Should().Be(2);
        provider.GetRateLimitInfo().RequestsMade.Should().Be(2);
    }

    private sealed class TestHistoricalProvider(HttpClient client) : BaseHistoricalDataProvider(client)
    {
        public override string Name => "synthetic-http";
        public override string DisplayName => Name;
        public override string Description => "Synthetic HTTP resilience regression provider";
        protected override string HttpClientName => Name;
        public override TimeSpan RateLimitDelay => TimeSpan.FromMilliseconds(75);
        public Task<HttpResponseMessage> SendAsync(CancellationToken ct = default)
            => ExecuteGetAsync("https://provider.invalid/history", "TEST", "bars", ct);
        public Task<string?> ReadAsync(CancellationToken ct = default)
            => ExecuteGetAndReadAsync("https://provider.invalid/history", "TEST", "bars", ct);
        public override Task<IReadOnlyList<HistoricalBar>> GetDailyBarsAsync(string symbol, DateOnly? from, DateOnly? to, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<HistoricalBar>>([]);
    }

    private sealed class ScriptedHandler(Func<int, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        public int Attempts { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => send(++Attempts, cancellationToken);
    }

    private sealed class TrackedContent() : StringContent("synthetic response")
    {
        public bool IsDisposed { get; private set; }
        protected override void Dispose(bool disposing)
        {
            IsDisposed |= disposing;
            base.Dispose(disposing);
        }
    }
}
