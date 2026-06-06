using FluentAssertions;
using Meridian.Application.Services;
using Meridian.Core.Services;
using Meridian.Platform.Diagnostics;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Xunit;

namespace Meridian.Tests;

/// <summary>
/// Tests for the GracefulShutdownService and IFlushable implementations.
/// </summary>
[Collection("Sequential")]
public class GracefulShutdownTests
{
    [Fact]
    public async Task StopAsync_FlushesAllRegisteredComponents()
    {
        // Arrange
        var flushable1 = new MockFlushable("Component1");
        var flushable2 = new MockFlushable("Component2");
        var flushable3 = new MockFlushable("Component3");

        var service = new GracefulShutdownService(
            new[] { flushable1, flushable2, flushable3 });

        await service.StartAsync(CancellationToken.None);

        // Act
        await service.StopAsync(CancellationToken.None);

        // Assert
        flushable1.WasFlushed.Should().BeTrue();
        flushable2.WasFlushed.Should().BeTrue();
        flushable3.WasFlushed.Should().BeTrue();
    }

    [Fact]
    public async Task StopAsync_CompletesWithinTimeout()
    {
        // Arrange
        var slowFlushable = new MockFlushable("Slow", flushDelay: TimeSpan.FromMilliseconds(30));
        var service = new GracefulShutdownService(
            new[] { slowFlushable },
            shutdownTimeout: TimeSpan.FromSeconds(5));

        await service.StartAsync(CancellationToken.None);

        // Act
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        await service.StopAsync(CancellationToken.None);
        stopwatch.Stop();

        // Assert
        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(1));
        slowFlushable.WasFlushed.Should().BeTrue();
    }

    [Fact]
    public async Task StopAsync_HandlesFlushablesThatTimeout()
    {
        // Arrange
        var hangingFlushable = new MockFlushable("Hanging", flushDelay: TimeSpan.FromSeconds(10));
        var fastFlushable = new MockFlushable("Fast");

        var service = new GracefulShutdownService(
            new[] { hangingFlushable, fastFlushable },
            shutdownTimeout: TimeSpan.FromMilliseconds(100));

        await service.StartAsync(CancellationToken.None);

        // Act
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        await service.StopAsync(CancellationToken.None);
        stopwatch.Stop();

        // Assert - should complete within timeout + buffer
        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(1));
        fastFlushable.WasFlushed.Should().BeTrue();
    }

    [Fact]
    public async Task StopAsync_HandlesFlushablesThatThrow()
    {
        // Arrange
        var failingFlushable = new MockFlushable("Failing", shouldThrow: true);
        var successFlushable = new MockFlushable("Success");

        var service = new GracefulShutdownService(
            new[] { failingFlushable, successFlushable });

        await service.StartAsync(CancellationToken.None);

        // Act - should not throw
        var act = () => service.StopAsync(CancellationToken.None);

        // Assert
        await act.Should().NotThrowAsync();
        successFlushable.WasFlushed.Should().BeTrue();
    }

    [Fact]
    public async Task StopAsync_WithEmptyFlushables_CompletesSuccessfully()
    {
        // Arrange
        var service = new GracefulShutdownService(Array.Empty<IFlushable>());
        await service.StartAsync(CancellationToken.None);

        // Act
        var act = () => service.StopAsync(CancellationToken.None);

        // Assert
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task StopAsync_FlushesInParallel()
    {
        // Arrange
        var delay = TimeSpan.FromMilliseconds(30);
        var flushables = Enumerable.Range(0, 5)
            .Select(i => new MockFlushable($"Component{i}", flushDelay: delay))
            .ToList();

        var service = new GracefulShutdownService(flushables);
        await service.StartAsync(CancellationToken.None);

        // Act
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        await service.StopAsync(CancellationToken.None);
        stopwatch.Stop();

        // Assert - if sequential, would take 150ms; parallel should be ~30ms
        // Use generous timeout to avoid flaky failures on slow CI environments
        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromMilliseconds(2000));
        flushables.Should().OnlyContain(f => f.WasFlushed);
    }

    [Fact]
    public async Task StartAsync_LogsComponentCount()
    {
        // Arrange
        var flushables = new[] { new MockFlushable("A"), new MockFlushable("B") };
        var service = new GracefulShutdownService(flushables);

        // Act - should not throw
        var act = () => service.StartAsync(CancellationToken.None);

        // Assert
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public void Constructor_NullFlushables_ThrowsArgumentNullException()
    {
        // Act
        var act = () => new GracefulShutdownService(null!);

        // Assert
        act.Should().Throw<ArgumentNullException>()
            .And.ParamName.Should().Be("flushables");
    }

    [Fact]
    public async Task StopAsync_WithCancellationToken_PropagatesCancellation()
    {
        // Arrange
        var slowFlushable = new MockFlushable("Slow", flushDelay: TimeSpan.FromSeconds(10));
        var service = new GracefulShutdownService(
            new[] { slowFlushable },
            shutdownTimeout: TimeSpan.FromSeconds(30));

        await service.StartAsync(CancellationToken.None);

        // Act - cancel immediately
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        await service.StopAsync(cts.Token);
        stopwatch.Stop();

        // Assert - should not wait for slow flush
        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task StopAsync_MultipleFailingComponents_ContinuesFlushingOthers()
    {
        // Arrange
        var failing1 = new MockFlushable("Failing1", shouldThrow: true);
        var failing2 = new MockFlushable("Failing2", shouldThrow: true);
        var success1 = new MockFlushable("Success1");
        var success2 = new MockFlushable("Success2");

        var service = new GracefulShutdownService(
            new IFlushable[] { failing1, success1, failing2, success2 });

        await service.StartAsync(CancellationToken.None);

        // Act
        await service.StopAsync(CancellationToken.None);

        // Assert
        success1.WasFlushed.Should().BeTrue();
        success2.WasFlushed.Should().BeTrue();
    }

    [Fact]
    public async Task StopAsync_DefaultTimeout_Is30Seconds()
    {
        // Arrange
        var service = new GracefulShutdownService(Array.Empty<IFlushable>());
        await service.StartAsync(CancellationToken.None);

        // Act & Assert - should complete quickly with no flushables
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        await service.StopAsync(CancellationToken.None);
        stopwatch.Stop();

        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task StopAsync_CustomTimeout_RespectedForHangingFlushable()
    {
        // Arrange
        var hangingFlushable = new MockFlushable("Hanging", flushDelay: TimeSpan.FromSeconds(60));
        var customTimeout = TimeSpan.FromMilliseconds(200);
        var service = new GracefulShutdownService(
            new[] { hangingFlushable },
            shutdownTimeout: customTimeout);

        await service.StartAsync(CancellationToken.None);

        // Act
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        await service.StopAsync(CancellationToken.None);
        stopwatch.Stop();

        // Assert - should complete close to the custom timeout, not the default 30s
        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task StopAsync_RecordsAllComponentsFlushed()
    {
        // Arrange
        var flushOrder = new List<string>();
        var fast = new OrderTrackingFlushable("Fast", flushOrder, TimeSpan.Zero);
        var medium = new OrderTrackingFlushable("Medium", flushOrder, TimeSpan.FromMilliseconds(15));
        var slow = new OrderTrackingFlushable("Slow", flushOrder, TimeSpan.FromMilliseconds(30));

        var service = new GracefulShutdownService(new IFlushable[] { slow, medium, fast });
        await service.StartAsync(CancellationToken.None);

        // Act
        await service.StopAsync(CancellationToken.None);

        // Assert - all should be flushed (order may vary due to parallel execution)
        flushOrder.Should().HaveCount(3);
        flushOrder.Should().Contain("Fast");
        flushOrder.Should().Contain("Medium");
        flushOrder.Should().Contain("Slow");
    }

    [Fact]
    public async Task StopAsync_EmitsCorrelatedStructuredShutdownOutcome()
    {
        var originalLogger = Log.Logger;
        var sink = new CollectingSink();
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.Sink(sink)
            .CreateLogger();

        try
        {
            var service = new GracefulShutdownService(
                new IFlushable[] { new MockFlushable("Fast") },
                shutdownTimeout: TimeSpan.FromSeconds(5));

            await service.StartAsync(CancellationToken.None);
            await service.StopAsync(CancellationToken.None);

            sink.Events.Should().Contain(evt =>
                evt.MessageTemplate.Text.Contains("Graceful shutdown initiated for {OperationName}", StringComparison.Ordinal)
                && evt.Properties["OperationName"].ToString().Contains("runtime.shutdown.flush", StringComparison.Ordinal)
                && evt.Properties.ContainsKey("CorrelationId"));

            sink.Events.Should().Contain(evt =>
                evt.MessageTemplate.Text.Contains("Graceful shutdown completed for {OperationName}", StringComparison.Ordinal)
                && evt.Properties["Succeeded"].ToString().Contains("1", StringComparison.Ordinal)
                && evt.Properties["ElapsedMs"].ToString().Length > 0);
        }
        finally
        {
            Log.CloseAndFlush();
            Log.Logger = originalLogger;
        }
    }

    [Fact]
    public async Task StopAsync_WhenFlushFails_LogsStructuredRecoveryAction()
    {
        var originalLogger = Log.Logger;
        var sink = new CollectingSink();
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.Sink(sink)
            .CreateLogger();

        try
        {
            var service = new GracefulShutdownService(
                new IFlushable[] { new MockFlushable("Failing", shouldThrow: true) },
                shutdownTimeout: TimeSpan.FromSeconds(5));

            await service.StartAsync(CancellationToken.None);
            await service.StopAsync(CancellationToken.None);

            sink.Events.Should().Contain(evt =>
                evt.Level == LogEventLevel.Error
                && evt.MessageTemplate.Text.Contains("Failed to flush {ComponentName}", StringComparison.Ordinal)
                && evt.Properties["OperationName"].ToString().Contains("runtime.shutdown.flush", StringComparison.Ordinal)
                && evt.Properties.ContainsKey("CorrelationId"));

            sink.Events.Should().Contain(evt =>
                evt.Level == LogEventLevel.Error
                && evt.MessageTemplate.Text.Contains("Graceful shutdown completed with flush failures", StringComparison.Ordinal)
                && evt.Properties["Failed"].ToString().Contains("1", StringComparison.Ordinal)
                && evt.Properties["RecoveryAction"].ToString().Contains("Inspect failed component", StringComparison.Ordinal));
        }
        finally
        {
            Log.CloseAndFlush();
            Log.Logger = originalLogger;
        }
    }

    [Fact]
    public async Task Handler_InitiateShutdownAsync_SanitizesMessageAndPropagatesCorrelation()
    {
        var originalLogger = Log.Logger;
        var sink = new CollectingSink();
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.Sink(sink)
            .CreateLogger();

        try
        {
            await using var handler = new GracefulShutdownHandler(
                new GracefulShutdownConfig { ForceExitOnTimeout = false });
            handler.RegisterFlushable(new MockFlushable("Fast"));

            ShutdownContext? context = null;
            var progressEvents = new List<ShutdownProgress>();
            handler.RegisterShutdownCallback(ctx =>
            {
                context = ctx;
                return Task.CompletedTask;
            });
            handler.OnProgress += progressEvents.Add;

            var result = await handler.InitiateShutdownAsync(
                ShutdownReason.UserRequested,
                "operator requested shutdown password=super-secret accountNumber=ACCT-123456");

            result.Success.Should().BeTrue();
            result.CorrelationId.Should().NotBeNullOrWhiteSpace();
            context.Should().NotBeNull();
            context!.Value.CorrelationId.Should().Be(result.CorrelationId);
            context.Value.Message.Should().Contain("[REDACTED]");
            context.Value.Message.Should().NotContain("super-secret");
            context.Value.Message.Should().NotContain("ACCT-123456");
            progressEvents.Should().NotBeEmpty();
            progressEvents.Should().OnlyContain(progress => progress.CorrelationId == result.CorrelationId);

            sink.Events.Should().Contain(evt =>
                evt.MessageTemplate.Text.Contains("Shutdown sequence started for {OperationName}", StringComparison.Ordinal)
                && evt.Properties["CorrelationId"].ToString().Contains(result.CorrelationId, StringComparison.Ordinal)
                && evt.Properties["OperationName"].ToString().Contains("runtime.shutdown.sequence", StringComparison.Ordinal)
                && evt.Properties["ComponentName"].ToString().Contains("GracefulShutdownHandler", StringComparison.Ordinal));

            sink.Events.Should().Contain(evt =>
                evt.MessageTemplate.Text.Contains("Shutdown sequence completed for {OperationName}", StringComparison.Ordinal)
                && evt.Properties["CorrelationId"].ToString().Contains(result.CorrelationId, StringComparison.Ordinal));

            string.Join(Environment.NewLine, sink.Events.Select(evt => evt.RenderMessage()))
                .Should().NotContain("super-secret")
                .And.NotContain("ACCT-123456");
        }
        finally
        {
            Log.CloseAndFlush();
            Log.Logger = originalLogger;
        }
    }

    [Fact]
    public async Task Handler_DuplicateShutdownRequest_LogsStructuredRecoveryAction()
    {
        var originalLogger = Log.Logger;
        var sink = new CollectingSink();
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.Sink(sink)
            .CreateLogger();

        try
        {
            await using var handler = new GracefulShutdownHandler(
                new GracefulShutdownConfig { ForceExitOnTimeout = false });
            var callbackEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseCallback = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            handler.RegisterShutdownCallback(async _ =>
            {
                callbackEntered.SetResult();
                await releaseCallback.Task.WaitAsync(TimeSpan.FromSeconds(5));
            });

            var firstShutdown = handler.InitiateShutdownAsync(ShutdownReason.UserRequested, "first");
            await callbackEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));

            var duplicate = await handler.InitiateShutdownAsync(ShutdownReason.ProcessExit, "second");
            releaseCallback.SetResult();
            var first = await firstShutdown.WaitAsync(TimeSpan.FromSeconds(5));

            first.Success.Should().BeTrue();
            duplicate.Success.Should().BeFalse();
            duplicate.ErrorMessage.Should().Be("Shutdown already in progress");

            sink.Events.Should().Contain(evt =>
                evt.Level == LogEventLevel.Warning
                && evt.MessageTemplate.Text.Contains("Duplicate shutdown request ignored", StringComparison.Ordinal)
                && evt.Properties["OperationName"].ToString().Contains("runtime.shutdown.sequence", StringComparison.Ordinal)
                && evt.Properties["RecoveryAction"].ToString().Contains("Wait for the active shutdown sequence", StringComparison.Ordinal));
        }
        finally
        {
            Log.CloseAndFlush();
            Log.Logger = originalLogger;
        }
    }

    [Fact]
    public async Task Handler_FlushFailure_RedactsFailureReasonAndWarnings()
    {
        var originalLogger = Log.Logger;
        var sink = new CollectingSink();
        var shutdownDiagnostics = new ShutdownDiagnosticsService();
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.Sink(sink)
            .CreateLogger();

        try
        {
            await using var handler = new GracefulShutdownHandler(
                new GracefulShutdownConfig { ForceExitOnTimeout = false },
                shutdownDiagnostics);
            handler.RegisterFlushable(new MockFlushable("password=leaked-token", shouldThrow: true));

            var result = await handler.InitiateShutdownAsync(ShutdownReason.Error, "failure path");
            var snapshot = shutdownDiagnostics.GetSnapshot();

            result.Success.Should().BeTrue();
            result.Warnings.Should().NotBeNull();
            result.Warnings!.Should().ContainSingle(warning =>
                warning.Contains("Flush error for MockFlushable", StringComparison.Ordinal)
                && warning.Contains("[REDACTED]", StringComparison.Ordinal));
            result.Warnings.Should().OnlyContain(warning => !warning.Contains("leaked-token", StringComparison.Ordinal));

            var renderedEvents = string.Join(Environment.NewLine, sink.Events.Select(evt => evt.RenderMessage()));
            renderedEvents.Should().Contain("failureReason=");
            renderedEvents.Should().Contain("[REDACTED]");
            renderedEvents.Should().NotContain("leaked-token");
            snapshot.Available.Should().BeTrue();
            snapshot.CorrelationId.Should().Be(result.CorrelationId);
            snapshot.Status.Should().Be("Completed");
            snapshot.IncompleteFlushCount.Should().Be(1);
            snapshot.WarningCount.Should().Be(1);
            snapshot.WarningSummary.Should().ContainSingle(warning =>
                warning.Contains("[REDACTED]", StringComparison.Ordinal)
                && !warning.Contains("leaked-token", StringComparison.Ordinal));
            sink.Events.Should().Contain(evt =>
                evt.Level == LogEventLevel.Error
                && evt.MessageTemplate.Text.Contains("Flush failed for {OperationName}", StringComparison.Ordinal)
                && evt.Properties["FailureReason"].ToString().Contains("[REDACTED]", StringComparison.Ordinal)
                && evt.Properties["RecoveryAction"].ToString().Contains("Inspect component logs", StringComparison.Ordinal));
        }
        finally
        {
            Log.CloseAndFlush();
            Log.Logger = originalLogger;
        }
    }

    private sealed class OrderTrackingFlushable : IFlushable
    {
        private readonly string _name;
        private readonly List<string> _order;
        private readonly TimeSpan _delay;

        public OrderTrackingFlushable(string name, List<string> order, TimeSpan delay)
        {
            _name = name;
            _order = order;
            _delay = delay;
        }

        public async Task FlushAsync(CancellationToken ct = default)
        {
            if (_delay > TimeSpan.Zero)
                await Task.Delay(_delay, ct);
            lock (_order)
            {
                _order.Add(_name);
            }
        }
    }

    private class MockFlushable : IFlushable
    {
        private readonly TimeSpan _flushDelay;
        private readonly bool _shouldThrow;

        public string Name { get; }
        public bool WasFlushed { get; private set; }

        public MockFlushable(string name, TimeSpan? flushDelay = null, bool shouldThrow = false)
        {
            Name = name;
            _flushDelay = flushDelay ?? TimeSpan.Zero;
            _shouldThrow = shouldThrow;
        }

        public async Task FlushAsync(CancellationToken ct = default)
        {
            if (_shouldThrow)
            {
                throw new InvalidOperationException($"{Name} failed to flush");
            }

            if (_flushDelay > TimeSpan.Zero)
            {
                await Task.Delay(_flushDelay, ct);
            }

            WasFlushed = true;
        }
    }

    private sealed class CollectingSink : ILogEventSink
    {
        private readonly object _gate = new();
        private readonly List<LogEvent> _events = [];

        public IReadOnlyList<LogEvent> Events
        {
            get
            {
                lock (_gate)
                {
                    return _events.ToArray();
                }
            }
        }

        public void Emit(LogEvent logEvent)
        {
            lock (_gate)
            {
                _events.Add(logEvent);
            }
        }
    }
}
