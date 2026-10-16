using System.Diagnostics;
using FluentAssertions;
using Meridian.Infrastructure.Adapters.Core;
using Xunit;

namespace Meridian.Tests.Backfill;

public sealed class BackfillRequestTraceContextTests
{
    [Fact]
    public async Task QueueRetry_PreservesInitiatingContextIncludingSamplingAndTraceState()
    {
        using var tracker = new ProviderRateLimitTracker();
        using var queue = new BackfillRequestQueue(tracker);
        var request = new BackfillRequest { Symbol = "SPY", PreferredProviders = ["test"] };
        using var producer = new Activity("backfill.submit").SetIdFormat(ActivityIdFormat.W3C).Start();
        producer.ActivityTraceFlags = ActivityTraceFlags.Recorded;
        producer.TraceStateString = "vendor=value";
        var originalContext = producer.Context;
        await queue.EnqueueAsync(request);
        producer.Stop();

        using var worker = new Activity("worker.unrelated").Start();
        var firstAttempt = (await queue.TryDequeueAsync())!.Value;
        await queue.CompleteRequestAsync(request, firstAttempt.Token, success: false, error: "timeout");
        var retryAttempt = await queue.TryDequeueAsync();

        retryAttempt.Should().NotBeNull();
        retryAttempt!.Value.Request.Should().BeSameAs(request);
        request.ParentContext.Should().Be(originalContext);
        request.ParentContext.TraceId.Should().NotBe(worker.TraceId);
        request.RetryCount.Should().Be(1);
    }

    [Fact]
    public async Task QueueRetry_WithoutProducerContext_DoesNotAdoptWorkerContext()
    {
        using var tracker = new ProviderRateLimitTracker();
        using var queue = new BackfillRequestQueue(tracker);
        var request = new BackfillRequest { Symbol = "SPY", PreferredProviders = ["test"] };
        await queue.EnqueueAsync(request);

        using var worker = new Activity("worker.unrelated").Start();
        var firstAttempt = (await queue.TryDequeueAsync())!.Value;
        await queue.CompleteRequestAsync(request, firstAttempt.Token, success: false, error: "timeout");
        var retryAttempt = await queue.TryDequeueAsync();

        retryAttempt.Should().NotBeNull();
        request.ParentContext.Should().Be(default(ActivityContext));
    }
}
