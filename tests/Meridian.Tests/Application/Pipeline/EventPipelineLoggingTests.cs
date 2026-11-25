using System.Collections.Concurrent;
using System.Threading.Channels;
using FluentAssertions;
using Meridian.Application.Pipeline;
using Meridian.Contracts.Domain.Enums;
using Meridian.Contracts.Domain.Models;
using Meridian.Domain.Events;
using Meridian.Storage.Interfaces;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Meridian.Tests.Application.Pipeline;

/// <summary>
/// The pipeline's backpressure warning reaches the logger it is given: with a stalled sink, a
/// queue past 80% of capacity writes the utilization warning an operator reads in the host log.
/// </summary>
[Trait("Category", "Unit")]
public sealed class EventPipelineLoggingTests
{
    [Fact]
    public async Task QueuePastEightyPercent_WithAStalledSink_LogsTheUtilizationWarning()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var sink = new Mock<IStorageSink>();
        sink.Setup(s => s.AppendAsync(It.IsAny<MarketEvent>(), It.IsAny<CancellationToken>()))
            .Returns(() => new ValueTask(release.Task));
        sink.Setup(s => s.FlushAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        var logger = new CapturingLogger<EventPipeline>();

        // The queue depth is sampled every 64th publish: with capacity 150 and one event held by
        // the stalled consumer, the 128th publish samples ~127 queued events, past the 80% mark (120).
        await using (var pipeline = new EventPipeline(
            sink.Object,
            capacity: 150,
            fullMode: BoundedChannelFullMode.Wait,
            batchSize: 1,
            enablePeriodicFlush: false,
            logger: logger,
            finalFlushTimeout: TimeSpan.FromSeconds(5)))
        {
            for (var i = 0; i < 128; i++)
            {
                pipeline.TryPublish(CreateTradeEvent(i)).Should().BeTrue();
            }

            logger.Entries.Should().Contain(entry =>
                entry.Level == LogLevel.Warning
                && entry.Message.StartsWith("Pipeline queue utilization at", StringComparison.Ordinal)
                && entry.Message.Contains("/150)", StringComparison.Ordinal));

            release.SetResult();
        }
    }

    private static MarketEvent CreateTradeEvent(int sequence)
    {
        var trade = new Trade(
            Timestamp: DateTimeOffset.UtcNow,
            Symbol: "SPY",
            Price: 100.50m,
            Size: 100,
            Aggressor: AggressorSide.Buy,
            SequenceNumber: sequence,
            Venue: "NYSE");

        return MarketEvent.Trade(DateTimeOffset.UtcNow, "SPY", trade, source: "TEST");
    }

    private sealed record CapturedEntry(LogLevel Level, string Message);

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        private readonly ConcurrentQueue<CapturedEntry> _entries = new();

        public IReadOnlyCollection<CapturedEntry> Entries => _entries;

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => _entries.Enqueue(new CapturedEntry(logLevel, formatter(state, exception)));
    }
}
