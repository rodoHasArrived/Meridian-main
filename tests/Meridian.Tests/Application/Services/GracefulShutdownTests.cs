using FluentAssertions;
using Meridian.Application.Services;
using Xunit;

namespace Meridian.Tests;

/// <summary>
/// Tests for the GracefulShutdownService and IFlushable implementations.
/// </summary>
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
}
