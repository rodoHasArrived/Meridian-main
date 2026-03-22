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
        private readonly TaskCompletionSource<bool> _received = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public IReadOnlyList<string?> TraceIds => _traceIds;

        public IReadOnlyList<string?> ParentSpanIds => _parentSpanIds;

        public IReadOnlyList<string?> OperationNames => _operationNames;

        public ValueTask AppendAsync(MarketEvent evt, CancellationToken ct = default)
        {
            _traceIds.Add(Activity.Current?.TraceId.ToString());
            _parentSpanIds.Add(Activity.Current?.ParentSpanId.ToString());
            _operationNames.Add(Activity.Current?.OperationName);
            _received.TrySetResult(true);
            return ValueTask.CompletedTask;
        }

        public Task FlushAsync(CancellationToken ct = default) => Task.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public async Task WaitForEventsAsync(int expectedCount)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            while (_traceIds.Count < expectedCount)
            {
                await _received.Task.WaitAsync(timeout.Token);
            }
        }
    }
}
