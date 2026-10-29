using System.Net;
using FluentAssertions;
using Meridian.Application.Services;

namespace Meridian.Tests.Application.Services;

public sealed class DailySummaryWebhookDeliveryTests
{
    [Fact]
    public async Task Summary_AllWebhooksDisabled_ReportsFailureWithoutSendingOrSuccessEvent()
    {
        var requests = 0;
        var successes = 0;
        var failures = 0;
        await using var webhook = Create((_, _) =>
        {
            Interlocked.Increment(ref requests);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }, enabled: false);
        webhook.OnSummarySent += _ => successes++;
        webhook.OnSummaryFailed += _ => failures++;

        var result = await webhook.SendSummaryAsync();

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Be("No enabled webhooks configured");
        result.DeliveryResults.Should().BeEmpty();
        requests.Should().Be(0);
        successes.Should().Be(0);
        failures.Should().Be(1);
    }

    [Fact]
    public async Task Summary_CallerCancellationDuringDeliveryPropagatesWithoutFailureEvent()
    {
        using var cts = new CancellationTokenSource();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var failures = 0;
        await using var webhook = Create(async (_, ct) =>
        {
            entered.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            throw new InvalidOperationException("Unreachable");
        });
        webhook.OnSummaryFailed += _ => failures++;
        var sending = webhook.SendSummaryAsync(cts.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sending);
        failures.Should().Be(0);
    }

    [Fact]
    public async Task Summary_HttpFailureStillReturnsDeliveryFailure()
    {
        await using var webhook = Create((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadGateway)));
        var result = await webhook.SendSummaryAsync();
        result.Success.Should().BeFalse();
        result.DeliveryResults.Should().ContainSingle().Which.Success.Should().BeFalse();
    }

    private static DailySummaryWebhook Create(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send,
        bool enabled = true)
        => new(new DailySummaryWebhookConfig
        {
            EnableScheduledSummary = false,
            RetryCount = 0,
            Webhooks = [new WebhookConfig { Url = "https://webhook.invalid", Enabled = enabled }]
        }, null, null, new HttpClient(new Handler(send)));

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => send(request, cancellationToken);
    }
}
