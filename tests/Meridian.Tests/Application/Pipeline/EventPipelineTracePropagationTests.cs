using System.Diagnostics;
using FluentAssertions;
using Meridian.Application.Pipeline;
using Meridian.Contracts.Domain.Enums;
using Meridian.Contracts.Domain.Models;
using Meridian.Domain.Events;
using Meridian.Storage.Interfaces;
using Xunit;

namespace Meridian.Tests.Pipeline;

public sealed class EventPipelineTracePropagationTests
{
    [Fact]
    public async Task TryPublish_PreservesParentTraceIntoStorageAppend()
    {
        using var listener = CreateListener();
        using var parentActivity = new Activity("provider.receive");
        parentActivity.Start();

        await using var sink = new TraceCapturingSink();
        await using var pipeline = new EventPipeline(sink, capacity: 32, enablePeriodicFlush: false);

        pipeline.TryPublish(CreateTradeEvent("SPY"));

        await sink.WaitForEventsAsync(1);

        sink.TraceIds.Should().ContainSingle();
        sink.TraceIds[0].Should().Be(parentActivity.TraceId.ToString());
        sink.ParentSpanIds.Should().ContainSingle();
        sink.ParentSpanIds[0].Should().NotBeNullOrWhiteSpace();
        sink.ParentSpanIds[0].Should().NotBe(parentActivity.SpanId.ToString());
        sink.OperationNames.Should().ContainSingle(name => name == "StoreMarketEvent.TraceCapturingSink");
        sink.EventTraceIds.Should().ContainSingle(parentActivity.TraceId.ToString());
        sink.EventParentSpanIds.Should().ContainSingle(parentActivity.SpanId.ToString());
    }

    [Fact]
    public async Task PublishAsync_WithoutParentContext_CreatesStorageSpan()
    {
        using var listener = CreateListener();
        await using var sink = new TraceCapturingSink();
        await using var pipeline = new EventPipeline(sink, capacity: 32, enablePeriodicFlush: false);

        await pipeline.PublishAsync(CreateTradeEvent("AAPL"));
        await sink.WaitForEventsAsync(1);

        sink.TraceIds.Should().ContainSingle(id => !string.IsNullOrWhiteSpace(id));
        sink.OperationNames.Should().ContainSingle(name => name == "StoreMarketEvent.TraceCapturingSink");
        sink.EventTraceIds.Should().ContainSingle(id => string.IsNullOrWhiteSpace(id));
        sink.EventParentSpanIds.Should().ContainSingle(id => string.IsNullOrWhiteSpace(id));
    }

    [Fact]
    public async Task PublishAsync_WithoutParent_DoesNotInheritPipelineConstructionActivity()
    {
        using var listener = CreateListener();
        using var startup = new Activity("host.startup").Start();
        await using var sink = new TraceCapturingSink();
        await using var pipeline = new EventPipeline(sink, capacity: 32, enablePeriodicFlush: false);
        startup.Stop();

        await pipeline.PublishAsync(CreateTradeEvent("UNPARENTED"));
        await sink.WaitForEventsAsync(1);

        sink.TraceIds.Should().ContainSingle(id => !string.IsNullOrWhiteSpace(id));
        sink.TraceIds[0].Should().NotBe(startup.TraceId.ToString());
        sink.EventTraceIds.Should().ContainSingle(id => string.IsNullOrWhiteSpace(id));
    }

    [Fact]
    public async Task PublishAsync_MixedBatch_KeepsIndependentAndAbsentProducerContexts()
    {
        using var listener = CreateListener();
        var releaseFirstAppend = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var sink = new TraceCapturingSink(releaseFirstAppend.Task);
        await using var pipeline = new EventPipeline(sink, capacity: 32, enablePeriodicFlush: false);

        await pipeline.PublishAsync(CreateTradeEvent("BLOCKER"));
        await sink.WaitForEventsAsync(1);

        using var first = new Activity("provider.first").Start();
        await pipeline.PublishAsync(CreateTradeEvent("FIRST"));
        first.Stop();

        using var second = new Activity("provider.second").Start();
        await pipeline.PublishAsync(CreateTradeEvent("SECOND"));
        second.Stop();
        await pipeline.PublishAsync(CreateTradeEvent("UNPARENTED"));
        releaseFirstAppend.TrySetResult(true);
        await sink.WaitForEventsAsync(4);

        sink.TraceIds[1].Should().Be(first.TraceId.ToString());
        sink.TraceIds[2].Should().Be(second.TraceId.ToString());
        sink.TraceIds[3].Should().NotBeNullOrWhiteSpace()
            .And.NotBe(first.TraceId.ToString())
            .And.NotBe(second.TraceId.ToString());
        sink.EventParentSpanIds[1].Should().Be(first.SpanId.ToString());
        sink.EventParentSpanIds[2].Should().Be(second.SpanId.ToString());
        sink.EventTraceIds[3].Should().BeNullOrEmpty();
    }

    private static ActivityListener CreateListener()
    {
        var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "Meridian",
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            SampleUsingParentId = static (ref ActivityCreationOptions<string> _) => ActivitySamplingResult.AllData
        };

        ActivitySource.AddActivityListener(listener);
        return listener;
    }

    private static MarketEvent CreateTradeEvent(string symbol)
    {
        var now = DateTimeOffset.UtcNow;
        var trade = new Trade(
            Timestamp: now,
            Symbol: symbol,
            Price: 100.50m,
            Size: 100,
            Aggressor: AggressorSide.Buy,
            SequenceNumber: 1,
            Venue: "XNYS");

        return MarketEvent.Trade(now, symbol, trade, seq: 1, source: "test-provider");
    }

    private sealed class TraceCapturingSink : IStorageSink
    {
        private readonly List<string?> _traceIds = new();
        private readonly List<string?> _parentSpanIds = new();
        private readonly List<string?> _operationNames = new();
        private readonly List<string?> _eventTraceIds = new();
        private readonly List<string?> _eventParentSpanIds = new();
        private readonly SemaphoreSlim _received = new(0);
        private readonly Task? _firstAppendGate;

        public TraceCapturingSink(Task? firstAppendGate = null)
        {
            _firstAppendGate = firstAppendGate;
        }

        public IReadOnlyList<string?> TraceIds => _traceIds;

        public IReadOnlyList<string?> ParentSpanIds => _parentSpanIds;

        public IReadOnlyList<string?> OperationNames => _operationNames;

        public IReadOnlyList<string?> EventTraceIds => _eventTraceIds;

        public IReadOnlyList<string?> EventParentSpanIds => _eventParentSpanIds;

        public async ValueTask AppendAsync(MarketEvent evt, CancellationToken ct = default)
        {
            _traceIds.Add(Activity.Current?.TraceId.ToString());
            _parentSpanIds.Add(Activity.Current?.ParentSpanId.ToString());
            _operationNames.Add(Activity.Current?.OperationName);
            _eventTraceIds.Add(evt.TraceId);
            _eventParentSpanIds.Add(evt.ParentSpanId);
            _received.Release();
            if (_traceIds.Count == 1 && _firstAppendGate is not null)
                await _firstAppendGate.WaitAsync(ct);
        }

        public Task FlushAsync(CancellationToken ct = default) => Task.CompletedTask;

        public ValueTask DisposeAsync()
        {
            _received.Dispose();
            return ValueTask.CompletedTask;
        }

        public async Task WaitForEventsAsync(int expectedCount)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            while (_traceIds.Count < expectedCount)
            {
                await _received.WaitAsync(timeout.Token);
            }
        }
    }
}
