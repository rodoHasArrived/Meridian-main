using FluentAssertions;
using Meridian.Application.Pipeline;
using Meridian.Contracts.Domain.Enums;
using Meridian.Contracts.Domain.Models;
using Meridian.Domain.Events;
using Meridian.Domain.Models;
using Meridian.Storage.Interfaces;
using Xunit;

namespace Meridian.Tests.Pipeline;

/// <summary>
/// Tests for the <see cref="IBackpressureSignal"/> implementation on <see cref="EventPipeline"/>,
/// verifying that producers can observe pipeline pressure and make throttling decisions.
/// </summary>
public sealed class BackpressureSignalTests : IAsyncLifetime
{
    private MockBpSink _sink = null!;
    private EventPipeline _pipeline = null!;

    public Task InitializeAsync()
    {
        _sink = new MockBpSink();
        _pipeline = new EventPipeline(
            _sink,
            capacity: 100,
            enablePeriodicFlush: false);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await _pipeline.DisposeAsync();
    }

    // ------------------------------------------------------------------ //
    //  IBackpressureSignal contract                                        //
    // ------------------------------------------------------------------ //

    [Fact]
    public void EventPipeline_ImplementsIBackpressureSignal()
    {
        _pipeline.Should().BeAssignableTo<IBackpressureSignal>(
            "EventPipeline must implement IBackpressureSignal so producers can observe pressure");
    }

    [Fact]
    public void BackpressureSignal_InitialUtilization_IsZeroOrLow()
    {
        IBackpressureSignal signal = _pipeline;
        signal.QueueUtilization.Should().BeInRange(0.0, 0.1,
            "an idle pipeline should have near-zero utilization");
    }

    [Fact]
    public void BackpressureSignal_InitialPressure_IsFalse()
    {
        IBackpressureSignal signal = _pipeline;
        signal.IsUnderPressure.Should().BeFalse(
            "a freshly created pipeline must not report pressure before any events are published");
    }

    [Fact]
    public void IsUnderPressure_Initially_IsFalse()
    {
        _pipeline.IsUnderPressure.Should().BeFalse();
    }

    [Fact]
    public void QueueUtilization_Fraction_IsBetweenZeroAndOne()
    {
        IBackpressureSignal signal = _pipeline;
        // Push some events to get a non-trivial utilization reading
        var ts = DateTimeOffset.UtcNow;
        var trade = new Trade(ts, "SPY", 520m, 100L, AggressorSide.Buy, 1L);
        for (var i = 0; i < 10; i++)
        {
            _pipeline.TryPublish(MarketEvent.Trade(ts, "SPY", trade, seq: i));
        }

        signal.QueueUtilization.Should().BeInRange(0.0, 1.0,
            "IBackpressureSignal.QueueUtilization must return a 0–1 fraction");
    }

    [Fact]
    public void PublicQueueUtilization_And_SignalQueueUtilization_AreConsistent()
    {
        // Public property returns 0–100; interface returns 0–1 fraction
        IBackpressureSignal signal = _pipeline;

        var ts = DateTimeOffset.UtcNow;
        var trade = new Trade(ts, "MSFT", 420m, 50L, AggressorSide.Sell, 1L);
        _pipeline.TryPublish(MarketEvent.Trade(ts, "MSFT", trade, seq: 1));

        var publicUtil = _pipeline.QueueUtilization; // 0–100
        var signalUtil = signal.QueueUtilization;    // 0–1

        (publicUtil / 100.0).Should().BeApproximately(signalUtil, precision: 1.0 / 100.0 + 1e-9,
            "IBackpressureSignal.QueueUtilization must be the public property divided by 100");
    }

    // ------------------------------------------------------------------ //
    //  Helpers                                                             //
    // ------------------------------------------------------------------ //

    private sealed class MockBpSink : IStorageSink
    {
        public ValueTask AppendAsync(MarketEvent evt, CancellationToken ct = default)
            => ValueTask.CompletedTask;

        public Task FlushAsync(CancellationToken ct = default) => Task.CompletedTask;
        public void Dispose() { }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
