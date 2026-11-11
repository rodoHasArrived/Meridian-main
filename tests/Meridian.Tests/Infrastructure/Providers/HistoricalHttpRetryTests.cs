using System.Net;
using System.Net.Http.Headers;
using FluentAssertions;
using Meridian.Contracts.Domain.Models;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Infrastructure.Http;
using Meridian.Infrastructure.Resilience;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Tests.Infrastructure.Providers;

public sealed class HistoricalHttpRetryTests
{
    [Theory]
    [InlineData("standard")]
    [InlineData("custom")]
    [InlineData("tracked")]
    public async Task FactoryAndProviderTogether_SendFourAttemptsAndChargeEveryAttempt(string policy)
    {
        var clock = new RequestBudgetTimeProvider();
        var contents = new List<ObservedContent>();
        using var handler = new AttemptHandler(_ =>
        {
            var content = new ObservedContent();
            contents.Add(content);
            return Response(HttpStatusCode.ServiceUnavailable, TimeSpan.FromSeconds(1), content);
        });
        var services = new ServiceCollection();
        var builder = services.AddHttpClient("retry-test").ConfigurePrimaryHttpMessageHandler(() => handler);
        switch (policy)
        {
            case "standard": builder.AddSharedResiliencePolicy(); break;
            case "custom": builder.AddCustomResiliencePolicy(); break;
            case "tracked": builder.AddSharedResiliencePolicyTracked("retry-test", (_, _, _) => { }); break;
        }
        using var serviceProvider = services.BuildServiceProvider();
        using var provider = new TestHistoricalProvider(
            serviceProvider.GetRequiredService<IHttpClientFactory>().CreateClient("retry-test"), clock);

        var pending = provider.SendAsync();
        handler.Attempts.Should().Be(1);
        for (var expected = 2; expected <= 4; expected++)
        {
            await RequestBudgetTimeProvider.WaitUntilAsync(() => clock.HasTimerDueWithin(TimeSpan.FromSeconds(1)));
            clock.Advance(TimeSpan.FromSeconds(1));
            await RequestBudgetTimeProvider.WaitUntilAsync(() => handler.Attempts >= expected);
        }
        using var response = await pending.WaitAsync(TimeSpan.FromSeconds(5));

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        handler.Attempts.Should().Be(4, "three retries must not multiply with the named-client policy");
        provider.GetRateLimitInfo().RequestsMade.Should().Be(4);
        provider.GetRateLimitInfo().RetryAfter.Should().Be(TimeSpan.FromSeconds(1),
            "the exhausted terminal attempt must still publish its cooldown");
        contents.Take(3).Should().OnlyContain(content => content.WasDisposed,
            "a retried response must release its content before the next attempt");
        contents[3].WasDisposed.Should().BeFalse("the terminal response belongs to the caller");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RetryAfterOn503_DoesNotSendBeforeDeltaOrHttpDateCooldown(bool useDate)
    {
        var clock = new RequestBudgetTimeProvider();
        using var handler = new AttemptHandler(attempt =>
        {
            if (attempt != 1) return Response(HttpStatusCode.OK);
            var response = Response(HttpStatusCode.ServiceUnavailable);
            response.Headers.RetryAfter = useDate
                ? new RetryConditionHeaderValue(clock.GetUtcNow().AddSeconds(45))
                : new RetryConditionHeaderValue(TimeSpan.FromSeconds(45));
            return response;
        });
        using var provider = new TestHistoricalProvider(new HttpClient(handler), clock);

        var pending = provider.SendAsync();
        await RequestBudgetTimeProvider.WaitUntilAsync(() => clock.HasTimerDueWithin(TimeSpan.FromSeconds(45)));
        clock.Advance(TimeSpan.FromSeconds(44));
        handler.Attempts.Should().Be(1);
        provider.GetRateLimitInfo().RetryAfter.Should().Be(TimeSpan.FromSeconds(1));
        clock.Advance(TimeSpan.FromSeconds(1));
        using var response = await pending.WaitAsync(TimeSpan.FromSeconds(5));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        handler.Attempts.Should().Be(2);
        provider.GetRateLimitInfo().RequestsMade.Should().Be(2);
    }

    [Fact]
    public async Task Terminal429_BlocksNextCallUntilCooldownAndCancelledWaitConsumesNoSlot()
    {
        var clock = new RequestBudgetTimeProvider();
        using var handler = new AttemptHandler(attempt => attempt == 1
            ? Response(HttpStatusCode.TooManyRequests, TimeSpan.FromSeconds(45))
            : Response(HttpStatusCode.OK));
        using var provider = new TestHistoricalProvider(new HttpClient(handler), clock);
        using var first = await provider.SendAsync();
        first.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        handler.Attempts.Should().Be(1, "throttling is returned to provider orchestration immediately");

        using var cancelled = new CancellationTokenSource();
        var abandoned = provider.SendAsync(cancelled.Token);
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => abandoned);
        provider.GetRateLimitInfo().RequestsMade.Should().Be(1);

        var pending = provider.SendAsync();
        await RequestBudgetTimeProvider.WaitUntilAsync(() => clock.HasTimerDueWithin(TimeSpan.FromSeconds(45)));
        clock.Advance(TimeSpan.FromSeconds(44));
        handler.Attempts.Should().Be(1);
        clock.Advance(TimeSpan.FromSeconds(1));
        using var next = await pending.WaitAsync(TimeSpan.FromSeconds(5));
        next.StatusCode.Should().Be(HttpStatusCode.OK);
        provider.GetRateLimitInfo().RequestsMade.Should().Be(2);
    }

    [Fact]
    public async Task NetworkFailure_IsRetriedAndEveryTransportAttemptConsumesQuota()
    {
        var clock = new RequestBudgetTimeProvider();
        using var handler = new AttemptHandler(attempt => attempt == 1
            ? throw new HttpRequestException("synthetic connection reset")
            : Response(HttpStatusCode.OK));
        using var provider = new TestHistoricalProvider(new HttpClient(handler), clock);
        var pending = provider.SendAsync();
        await RequestBudgetTimeProvider.WaitUntilAsync(() => clock.HasTimerDueWithin(TimeSpan.FromSeconds(30)));
        clock.Advance(TimeSpan.FromSeconds(30));
        using var response = await pending.WaitAsync(TimeSpan.FromSeconds(5));
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        handler.Attempts.Should().Be(2);
        provider.GetRateLimitInfo().RequestsMade.Should().Be(2);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.NotFound)]
    public async Task PermanentHttpFailure_IsReturnedAfterOneChargedAttempt(HttpStatusCode status)
    {
        var clock = new RequestBudgetTimeProvider();
        using var handler = new AttemptHandler(_ => Response(status));
        using var provider = new TestHistoricalProvider(new HttpClient(handler), clock);
        using var response = await provider.SendAsync();
        response.StatusCode.Should().Be(status);
        handler.Attempts.Should().Be(1);
        provider.GetRateLimitInfo().RequestsMade.Should().Be(1);
    }

    [Fact]
    public void RetryAfterInPast_UsesZeroRatherThanInventingAnotherCooldown()
    {
        var clock = new RequestBudgetTimeProvider();
        using var response = Response(HttpStatusCode.TooManyRequests);
        response.Headers.RetryAfter = new RetryConditionHeaderValue(clock.GetUtcNow().AddMinutes(-1));
        HttpResiliencePolicy.ExtractRetryAfter(response, clock).Should().Be(TimeSpan.Zero);
    }

    private static HttpResponseMessage Response(HttpStatusCode status, TimeSpan? retryAfter = null, HttpContent? content = null)
    {
        var response = new HttpResponseMessage(status) { Content = content ?? new StringContent("test") };
        if (retryAfter is { } delay) response.Headers.RetryAfter = new RetryConditionHeaderValue(delay);
        return response;
    }

    private sealed class AttemptHandler(Func<int, HttpResponseMessage> respond) : HttpMessageHandler
    {
        private int _attempts;
        public int Attempts => Volatile.Read(ref _attempts);
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(Interlocked.Increment(ref _attempts)));
    }

    private sealed class ObservedContent() : StringContent("transient failure")
    {
        public bool WasDisposed { get; private set; }
        protected override void Dispose(bool disposing)
        {
            WasDisposed = true;
            base.Dispose(disposing);
        }
    }

    private sealed class TestHistoricalProvider(HttpClient client, TimeProvider clock)
        : BaseHistoricalDataProvider(client, timeProvider: clock)
    {
        public override string Name => "retry-test";
        public override string DisplayName => Name;
        public override string Description => Name;
        protected override string HttpClientName => "retry-test";
        public override int MaxRequestsPerWindow => 100;
        public Task<HttpResponseMessage> SendAsync(CancellationToken ct = default) =>
            ExecuteGetAsync("https://example.test/history", "TEST", "bars", ct);
        public override Task<IReadOnlyList<HistoricalBar>> GetDailyBarsAsync(
            string symbol, DateOnly? from, DateOnly? to, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }
}
