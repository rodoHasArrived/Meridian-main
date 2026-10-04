using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using FluentAssertions;
using Xunit;

namespace Meridian.Tests.DataIntegration.AccountingSystem;

public sealed class ExternalGlRateLimitTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Xero_RetryAfterWaits_AndResumesSameJournalOffsetWithoutDuplicatingEvidence(bool httpDate)
    {
        var store = new ExternalGlTestStore("xero");
        var attempts = 0;
        var elapsed = new Stopwatch();
        TimeSpan expectedDelay = default;
        using var handler = new ExternalGlTestHandler((request, body) =>
        {
            if (request.RequestUri!.Query.Contains("offset=7", StringComparison.Ordinal))
            {
                attempts++;
                if (attempts == 1)
                {
                    var response = Limited();
                    var now = DateTimeOffset.UtcNow;
                    var until = DateTimeOffset.FromUnixTimeSeconds(now.ToUnixTimeSeconds() + 2);
                    response.Headers.RetryAfter = httpDate ? new RetryConditionHeaderValue(until) : new RetryConditionHeaderValue(TimeSpan.FromSeconds(1));
                    expectedDelay = httpDate ? until - now : TimeSpan.FromSeconds(1);
                    elapsed.Start();
                    return response;
                }
                elapsed.Elapsed.Should().BeGreaterThanOrEqualTo(expectedDelay - TimeSpan.FromMilliseconds(100));
            }
            return ExternalGlTestData.Respond(request, body);
        });
        using var client = new HttpClient(handler);
        var import = await ExternalGlTestData.Provider("xero", store, client).ImportAsync(ExternalGlTestData.Request("xero"));
        import.JournalEntries.Should().ContainSingle();
        attempts.Should().Be(2);
        var journalReads = handler.Requests.Where(request => request.Uri.AbsolutePath.EndsWith("Journals", StringComparison.Ordinal)).ToArray();
        journalReads.Select(request => request.Uri.Query).Should().Equal("?offset=0&paymentsOnly=false", "?offset=7&paymentsOnly=false", "?offset=7&paymentsOnly=false");
        journalReads.Should().OnlyContain(request => request.Method == HttpMethod.Get && request.Authorization == "Bearer" && request.Tenant == ExternalGlTestStore.Tenant);
        store.Verifications.Should().ContainSingle().Which.Success.Should().BeTrue();
    }

    [Theory]
    [InlineData("exhausted", 4)]
    [InlineData("missing", 1)]
    [InlineData("invalid", 1)]
    [InlineData("daily", 1)]
    [InlineData("token", 1)]
    public async Task Xero_UnrecoverableRateLimitsFailClosed_AndTokenExchangeIsNotRetried(string fault, int expectedAttempts)
    {
        var store = new ExternalGlTestStore("xero");
        var attempts = 0;
        using var handler = new ExternalGlTestHandler((request, body) =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith(fault == "token" ? "token" : "Journals", StringComparison.Ordinal))
            {
                attempts++;
                var response = Limited();
                if (fault != "missing")
                    response.Headers.TryAddWithoutValidation("Retry-After", fault == "invalid" ? "invalid" : fault == "daily" ? "86400" : "0");
                return response;
            }
            return ExternalGlTestData.Respond(request, body);
        });
        using var client = new HttpClient(handler);
        var provider = ExternalGlTestData.Provider("xero", store, client);
        var error = await provider.Invoking(p => p.ImportAsync(ExternalGlTestData.Request("xero"))).Should().ThrowAsync<InvalidOperationException>();
        error.Which.ToString().Should().NotContain("secret-provider-error");
        attempts.Should().Be(expectedAttempts);
        handler.Requests.Should().NotContain(request => request.Uri.AbsolutePath.EndsWith("TrialBalance", StringComparison.Ordinal));
        store.Verifications.Should().ContainSingle().Which.Success.Should().BeFalse();
    }

    [Fact]
    public async Task Xero_RetryAfterDelayIsCancellable_WithoutRetryOrVerificationFailure()
    {
        var store = new ExternalGlTestStore("xero");
        using var cancellation = new CancellationTokenSource();
        var limited = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var handler = new ExternalGlTestHandler((request, body) =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("Journals", StringComparison.Ordinal))
            {
                var response = Limited();
                response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromMinutes(1));
                limited.SetResult();
                return response;
            }
            return ExternalGlTestData.Respond(request, body);
        });
        using var client = new HttpClient(handler);
        var task = ExternalGlTestData.Provider("xero", store, client).ImportAsync(ExternalGlTestData.Request("xero"), cancellation.Token);
        await limited.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        await ((Func<Task>)(async () => await task)).Should().ThrowAsync<OperationCanceledException>();
        handler.Requests.Should().ContainSingle(request => request.Uri.AbsolutePath.EndsWith("Journals", StringComparison.Ordinal));
        store.Verifications.Should().BeEmpty();
    }

    private static HttpResponseMessage Limited() => ExternalGlTestHandler.Raw("secret-provider-error", HttpStatusCode.TooManyRequests);
}
