using System.Net;
using System.Net.Http;
using Meridian.Core.Exceptions;
using Meridian.Infrastructure.Adapters.Core;
using Xunit;

namespace Meridian.Tests.Infrastructure.Adapters;

/// <summary>
/// Unit tests for Retry-After header parsing in BackfillWorkerService.
/// Part of improvement #17.
/// </summary>
public sealed class BackfillRetryAfterTests
{
    [Fact]
    public void IsRateLimited_MessageOnlySignal_IsNotClassifiedAsRateLimit()
    {
        var ex = new InvalidOperationException("provider said rate limit 429 but supplied no typed status");

        Assert.False(BackfillWorkerService.IsRateLimited(ex));
    }

    [Fact]
    public void IsRateLimited_TypedAndHttpStatusSignals_AreClassified()
    {
        Assert.True(BackfillWorkerService.IsRateLimited(
            new RateLimitException("quota exhausted", provider: "test")));
        Assert.True(BackfillWorkerService.IsRateLimited(
            new HttpRequestException("too many requests", null, HttpStatusCode.TooManyRequests)));
        Assert.True(BackfillWorkerService.IsRateLimited(new AggregateException(
            new InvalidOperationException("first provider failed"),
            new HttpRequestException("second provider throttled", null, HttpStatusCode.TooManyRequests))));
    }

    [Fact]
    public void TryExtractRetryAfter_NoRetryAfterInMessage_ReturnsNull()
    {
        var ex = new Exception("HTTP 429 Too Many Requests");
        var result = BackfillWorkerService.TryExtractRetryAfter(ex);
        Assert.Null(result);
    }

    [Fact]
    public void TryExtractRetryAfter_DeltaSeconds_ReturnsTimeSpan()
    {
        var ex = new Exception("Rate limited. Retry-After: 120");
        var result = BackfillWorkerService.TryExtractRetryAfter(ex);
        Assert.NotNull(result);
        Assert.Equal(120, result.Value.TotalSeconds, 1);
    }

    [Fact]
    public void TryExtractRetryAfter_LargeValue_CappedAt5Minutes()
    {
        var ex = new Exception("Rate limited. Retry-After: 600");
        var result = BackfillWorkerService.TryExtractRetryAfter(ex);
        Assert.NotNull(result);
        Assert.Equal(300, result.Value.TotalSeconds, 1); // Capped at 300s (5 min)
    }

    [Fact]
    public void TryExtractRetryAfter_NestedHttpException_Extracts()
    {
        var inner = new HttpRequestException("HTTP 429. Retry-After: 30");
        var outer = new Exception("Backfill request failed", inner);
        var result = BackfillWorkerService.TryExtractRetryAfter(outer);
        Assert.NotNull(result);
        Assert.Equal(30, result.Value.TotalSeconds, 1);
    }

    [Fact]
    public void TryExtractRetryAfter_CaseInsensitive()
    {
        var ex = new Exception("retry-after: 45");
        var result = BackfillWorkerService.TryExtractRetryAfter(ex);
        Assert.NotNull(result);
        Assert.Equal(45, result.Value.TotalSeconds, 1);
    }

    [Fact]
    public void TryExtractRetryAfter_ZeroOrNegative_ReturnsNull()
    {
        var ex = new Exception("Retry-After: 0");
        var result = BackfillWorkerService.TryExtractRetryAfter(ex);
        Assert.Null(result);
    }

    [Fact]
    public void TryExtractRetryAfter_NonNumericNonDate_ReturnsNull()
    {
        var ex = new Exception("Retry-After: invalid");
        var result = BackfillWorkerService.TryExtractRetryAfter(ex);
        Assert.Null(result);
    }

    [Fact]
    public void TryExtractRetryAfter_FromExceptionDataKey_Extracts()
    {
        var ex = new Exception("HTTP 429 Too Many Requests");
        ex.Data["Retry-After"] = "75";

        var result = BackfillWorkerService.TryExtractRetryAfter(ex);

        Assert.NotNull(result);
        Assert.Equal(75, result.Value.TotalSeconds, 1);
    }

    [Fact]
    public void TryExtractRetryAfter_FromExceptionDataResponse_Extracts()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(42));

        var ex = new Exception("HTTP 429 Too Many Requests");
        ex.Data["Response"] = response;

        var result = BackfillWorkerService.TryExtractRetryAfter(ex);

        Assert.NotNull(result);
        Assert.Equal(42, result.Value.TotalSeconds, 1);
    }

    [Fact]
    public void TryExtractRetryAfter_HttpRequestExceptionStatusCode429_WithoutMessageHeader_ReturnsNull()
    {
        var ex = new HttpRequestException("Too many requests", null, HttpStatusCode.TooManyRequests);

        var result = BackfillWorkerService.TryExtractRetryAfter(ex);

        Assert.Null(result);
    }
}
