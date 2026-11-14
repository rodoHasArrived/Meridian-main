using FluentAssertions;
using Meridian.Infrastructure.Adapters.Core;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Meridian.Tests.Backfill;

/// <summary>
/// Unit tests for the RateLimiter class.
/// Tests sliding window rate limiting, minimum delay enforcement, and status tracking.
/// </summary>
public class RateLimiterTests : IDisposable
{
    private RateLimiter? _rateLimiter;

    [Fact]
    public async Task WaitForSlotAsync_ExactlyAtWindowBoundaryExpiresTheOldRequest()
    {
        var clock = new FakeTimeProvider();
        _rateLimiter = new RateLimiter(1, TimeSpan.FromMinutes(1), timeProvider: clock);
        await _rateLimiter.WaitForSlotAsync();
        clock.Advance(TimeSpan.FromMinutes(1));

        // Confirm expiry before admission so a regression fails without spinning a test thread.
        _rateLimiter.GetStatus().RequestsInWindow.Should().Be(0);
        await _rateLimiter.WaitForSlotAsync().WaitAsync(TimeSpan.FromSeconds(5));

        _rateLimiter.GetStatus().RequestsInWindow.Should().Be(1);
    }

    [Fact]
    public async Task UpdateLimits_AppliesNewCapacityWithoutDiscardingRecentRequests()
    {
        var clock = new FakeTimeProvider();
        _rateLimiter = new RateLimiter(1, TimeSpan.FromMinutes(1), timeProvider: clock);
        await _rateLimiter.WaitForSlotAsync();

        _rateLimiter.UpdateLimits(2, TimeSpan.FromMinutes(1));
        await _rateLimiter.WaitForSlotAsync();

        _rateLimiter.GetStatus().RequestsInWindow.Should().Be(2);
        _rateLimiter.GetStatus().MaxRequests.Should().Be(2);
    }

    public void Dispose()
    {
        _rateLimiter?.Dispose();
    }

    #region Constructor Validation Tests

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-100)]
    public void Constructor_WithInvalidMaxRequests_ThrowsArgumentOutOfRangeException(int maxRequests)
    {
        // Act & Assert
        var ex = Assert.Throws<ArgumentOutOfRangeException>(
            () => new RateLimiter(maxRequests, TimeSpan.FromSeconds(60)));

        ex.ParamName.Should().Be("maxRequestsPerWindow");
    }

    [Fact]
    public void Constructor_WithZeroWindow_ThrowsArgumentOutOfRangeException()
    {
        // Act & Assert
        var ex = Assert.Throws<ArgumentOutOfRangeException>(
            () => new RateLimiter(10, TimeSpan.Zero));

        ex.ParamName.Should().Be("window");
    }

    [Fact]
    public void Constructor_WithNegativeWindow_ThrowsArgumentOutOfRangeException()
    {
        // Act & Assert
        var ex = Assert.Throws<ArgumentOutOfRangeException>(
            () => new RateLimiter(10, TimeSpan.FromSeconds(-1)));

        ex.ParamName.Should().Be("window");
    }

    [Fact]
    public void Constructor_WithValidParameters_CreatesInstance()
    {
        // Act
        _rateLimiter = new RateLimiter(maxRequestsPerWindow: 1, window: TimeSpan.FromMilliseconds(1));

        // Assert
        _rateLimiter.Should().NotBeNull();
        var (requestsInWindow, maxRequests, _) = _rateLimiter.GetStatus();
        requestsInWindow.Should().Be(0);
        maxRequests.Should().Be(1);
    }

    #endregion

    #region Basic Rate Limiting Tests

    [Fact]
    public async Task WaitForSlotAsync_WithinLimit_ReturnsImmediately()
    {
        // Arrange
        _rateLimiter = new RateLimiter(maxRequestsPerWindow: 10, window: TimeSpan.FromSeconds(60));

        // Act
        var waitTime = await _rateLimiter.WaitForSlotAsync();

        // Assert
        waitTime.Should().BeLessThan(TimeSpan.FromMilliseconds(100));
    }

    [Fact]
    public async Task WaitForSlotAsync_MultipleCallsWithinLimit_AllReturnQuickly()
    {
        // Arrange
        _rateLimiter = new RateLimiter(maxRequestsPerWindow: 5, window: TimeSpan.FromSeconds(60));
        var waitTimes = new List<TimeSpan>();

        // Act
        for (int i = 0; i < 5; i++)
        {
            waitTimes.Add(await _rateLimiter.WaitForSlotAsync());
        }

        // Assert
        waitTimes.Should().AllSatisfy(t => t.Should().BeLessThan(TimeSpan.FromMilliseconds(100)));
    }

    [Fact]
    public async Task WaitForSlotAsync_ExceedsLimit_WaitsForWindowToExpire()
    {
        var clock = new FakeTimeProvider();
        _rateLimiter = new RateLimiter(2, TimeSpan.FromMinutes(1), timeProvider: clock);
        await _rateLimiter.WaitForSlotAsync();
        await _rateLimiter.WaitForSlotAsync();
        var pending = _rateLimiter.WaitForSlotAsync();
        pending.IsCompleted.Should().BeFalse();
        clock.Advance(TimeSpan.FromMinutes(1));
        (await pending.WaitAsync(TimeSpan.FromSeconds(5))).Should().Be(TimeSpan.FromMinutes(1));
    }

    #endregion

    #region Minimum Delay Tests

    [Fact]
    public async Task WaitForSlotAsync_WithMinDelay_EnforcesDelayBetweenRequests()
    {
        var clock = new FakeTimeProvider();
        _rateLimiter = new RateLimiter(100, TimeSpan.FromSeconds(60), TimeSpan.FromMilliseconds(80), timeProvider: clock);
        await _rateLimiter.WaitForSlotAsync();
        var pending = _rateLimiter.WaitForSlotAsync();
        pending.IsCompleted.Should().BeFalse();
        clock.Advance(TimeSpan.FromMilliseconds(80));
        (await pending.WaitAsync(TimeSpan.FromSeconds(5))).Should().Be(TimeSpan.FromMilliseconds(80));
    }

    [Fact]
    public async Task WaitForSlotAsync_AfterDelayElapsed_DoesNotWait()
    {
        var clock = new FakeTimeProvider();
        _rateLimiter = new RateLimiter(100, TimeSpan.FromSeconds(60), TimeSpan.FromMilliseconds(50), timeProvider: clock);
        await _rateLimiter.WaitForSlotAsync();
        clock.Advance(TimeSpan.FromMilliseconds(60));
        (await _rateLimiter.WaitForSlotAsync()).Should().Be(TimeSpan.Zero);
    }

    #endregion

    #region RecordRequest Tests

    [Fact]
    public void RecordRequest_TracksRequestInWindow()
    {
        // Arrange
        _rateLimiter = new RateLimiter(maxRequestsPerWindow: 10, window: TimeSpan.FromSeconds(60));

        // Act
        _rateLimiter.RecordRequest();
        _rateLimiter.RecordRequest();
        _rateLimiter.RecordRequest();

        var (requestsInWindow, maxRequests, _) = _rateLimiter.GetStatus();

        // Assert
        requestsInWindow.Should().Be(3);
        maxRequests.Should().Be(10);
    }

    [Fact]
    public async Task RecordRequest_CountsTowardLimit()
    {
        var clock = new FakeTimeProvider();
        _rateLimiter = new RateLimiter(2, TimeSpan.FromMinutes(1), timeProvider: clock);
        _rateLimiter.RecordRequest();
        _rateLimiter.RecordRequest();
        var pending = _rateLimiter.WaitForSlotAsync();
        pending.IsCompleted.Should().BeFalse();
        clock.Advance(TimeSpan.FromMinutes(1));
        (await pending.WaitAsync(TimeSpan.FromSeconds(5))).Should().Be(TimeSpan.FromMinutes(1));
    }

    #endregion

    #region GetStatus Tests

    [Fact]
    public void GetStatus_WithNoRequests_ReturnsZeroCount()
    {
        // Arrange
        _rateLimiter = new RateLimiter(maxRequestsPerWindow: 10, window: TimeSpan.FromSeconds(60));

        // Act
        var (requestsInWindow, maxRequests, windowRemaining) = _rateLimiter.GetStatus();

        // Assert
        requestsInWindow.Should().Be(0);
        maxRequests.Should().Be(10);
        windowRemaining.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public async Task GetStatus_AfterRequests_ShowsCorrectCount()
    {
        // Arrange
        _rateLimiter = new RateLimiter(maxRequestsPerWindow: 10, window: TimeSpan.FromSeconds(60));

        // Act
        await _rateLimiter.WaitForSlotAsync();
        await _rateLimiter.WaitForSlotAsync();
        await _rateLimiter.WaitForSlotAsync();

        var (requestsInWindow, maxRequests, windowRemaining) = _rateLimiter.GetStatus();

        // Assert
        requestsInWindow.Should().Be(3);
        maxRequests.Should().Be(10);
        windowRemaining.Should().BeGreaterThan(TimeSpan.Zero);
    }

    [Fact]
    public async Task GetStatus_AfterWindowExpires_CleansUpOldRequests()
    {
        var clock = new FakeTimeProvider();
        _rateLimiter = new RateLimiter(10, TimeSpan.FromMilliseconds(50), timeProvider: clock);
        await _rateLimiter.WaitForSlotAsync();
        await _rateLimiter.WaitForSlotAsync();
        clock.Advance(TimeSpan.FromMilliseconds(60));
        _rateLimiter.GetStatus().RequestsInWindow.Should().Be(0);
    }

    #endregion

    #region Cancellation Tests

    [Fact]
    public async Task WaitForSlotAsync_WithCancellation_ThrowsOperationCanceledException()
    {
        _rateLimiter = new RateLimiter(1, TimeSpan.FromHours(1));
        await _rateLimiter.WaitForSlotAsync();
        using var cancellation = new CancellationTokenSource();
        var pending = _rateLimiter.WaitForSlotAsync(cancellation.Token);
        pending.IsCompleted.Should().BeFalse();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
    }

    [Fact]
    public async Task WaitForSlotAsync_WithPreCancelledToken_ThrowsImmediately()
    {
        // Arrange
        _rateLimiter = new RateLimiter(maxRequestsPerWindow: 10, window: TimeSpan.FromSeconds(60));
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => _rateLimiter.WaitForSlotAsync(cts.Token));
    }

    #endregion

    #region Disposal Tests

    [Fact]
    public async Task WaitForSlotAsync_AfterDisposal_ThrowsObjectDisposedException()
    {
        // Arrange
        _rateLimiter = new RateLimiter(maxRequestsPerWindow: 10, window: TimeSpan.FromSeconds(60));
        _rateLimiter.Dispose();

        // Act & Assert
        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => _rateLimiter.WaitForSlotAsync());
    }

    [Fact]
    public void RecordRequest_AfterDisposal_ThrowsObjectDisposedException()
    {
        // Arrange
        _rateLimiter = new RateLimiter(maxRequestsPerWindow: 10, window: TimeSpan.FromSeconds(60));
        _rateLimiter.Dispose();

        // Act & Assert
        Assert.Throws<ObjectDisposedException>(() => _rateLimiter.RecordRequest());
    }

    [Fact]
    public void Dispose_CalledMultipleTimes_DoesNotThrow()
    {
        // Arrange
        _rateLimiter = new RateLimiter(maxRequestsPerWindow: 10, window: TimeSpan.FromSeconds(60));

        // Act & Assert - Should not throw
        _rateLimiter.Dispose();
        _rateLimiter.Dispose();
        _rateLimiter.Dispose();
    }

    #endregion

    #region Concurrency Tests

    [Fact]
    public async Task WaitForSlotAsync_ConcurrentRequests_RespectLimit()
    {
        var clock = new FakeTimeProvider();
        _rateLimiter = new RateLimiter(3, TimeSpan.FromMinutes(1), timeProvider: clock);
        var requests = Enumerable.Range(0, 6).Select(_ => _rateLimiter.WaitForSlotAsync()).ToArray();
        requests.Count(task => task.IsCompletedSuccessfully).Should().Be(3);
        clock.Advance(TimeSpan.FromMinutes(1));
        var waits = await Task.WhenAll(requests).WaitAsync(TimeSpan.FromSeconds(5));
        waits.Count(value => value == TimeSpan.Zero).Should().Be(3);
        waits.Count(value => value == TimeSpan.FromMinutes(1)).Should().Be(3);
    }

    [Fact]
    public async Task WaitForSlotAsync_ConcurrentRequestsWithShortWindow_EnforcesLimit()
    {
        var clock = new FakeTimeProvider();
        _rateLimiter = new RateLimiter(3, TimeSpan.FromMilliseconds(100), timeProvider: clock);
        var requests = Enumerable.Range(0, 6).Select(_ => _rateLimiter.WaitForSlotAsync()).ToArray();
        requests.Count(task => task.IsCompletedSuccessfully).Should().Be(3);
        clock.Advance(TimeSpan.FromMilliseconds(100));
        var waits = await Task.WhenAll(requests).WaitAsync(TimeSpan.FromSeconds(5));
        waits.Max().Should().Be(TimeSpan.FromMilliseconds(100));
        _rateLimiter.GetStatus().RequestsInWindow.Should().Be(3);
    }

    #endregion

    #region Sliding Window Behavior Tests

    [Fact]
    public async Task SlidingWindow_OldRequestsExpire_NewRequestsAllowed()
    {
        var clock = new FakeTimeProvider();
        _rateLimiter = new RateLimiter(2, TimeSpan.FromMilliseconds(50), timeProvider: clock);
        await _rateLimiter.WaitForSlotAsync();
        await _rateLimiter.WaitForSlotAsync();
        clock.Advance(TimeSpan.FromMilliseconds(60));
        (await _rateLimiter.WaitForSlotAsync()).Should().Be(TimeSpan.Zero);
    }

    #endregion
}

/// <summary>
/// Unit tests for the RateLimiterRegistry class.
/// </summary>
public class RateLimiterRegistryTests : IDisposable
{
    private RateLimiterRegistry? _registry;

    [Fact]
    public void GetOrCreate_ChangedProviderLimitsUpdatesExistingLimiter()
    {
        _registry = new RateLimiterRegistry();
        var limiter = _registry.GetOrCreate("provider", 1, TimeSpan.FromMinutes(1));
        limiter.RecordRequest();

        var updated = _registry.GetOrCreate("provider", 20, TimeSpan.FromMinutes(2));

        updated.Should().BeSameAs(limiter);
        updated.GetStatus().MaxRequests.Should().Be(20);
        updated.GetStatus().RequestsInWindow.Should().Be(1);
        updated.GetStatus().WindowRemaining.Should().BeGreaterThan(TimeSpan.FromMinutes(1));
    }

    public void Dispose()
    {
        _registry?.Dispose();
    }

    [Fact]
    public void GetOrCreate_NewProvider_CreatesLimiter()
    {
        // Arrange
        _registry = new RateLimiterRegistry();

        // Act
        var limiter = _registry.GetOrCreate("yahoo", 100, TimeSpan.FromHours(1));

        // Assert
        limiter.Should().NotBeNull();
    }

    [Fact]
    public void GetOrCreate_SameProvider_ReturnsSameInstance()
    {
        // Arrange
        _registry = new RateLimiterRegistry();

        // Act
        var limiter1 = _registry.GetOrCreate("yahoo", 100, TimeSpan.FromHours(1));
        var limiter2 = _registry.GetOrCreate("yahoo", 100, TimeSpan.FromHours(1));

        // Assert
        limiter1.Should().BeSameAs(limiter2);
    }

    [Fact]
    public void GetOrCreate_DifferentProviders_ReturnsDifferentInstances()
    {
        // Arrange
        _registry = new RateLimiterRegistry();

        // Act
        var yahooLimiter = _registry.GetOrCreate("yahoo", 100, TimeSpan.FromHours(1));
        var stooqLimiter = _registry.GetOrCreate("stooq", 50, TimeSpan.FromMinutes(30));

        // Assert
        yahooLimiter.Should().NotBeSameAs(stooqLimiter);
    }

    [Fact]
    public async Task GetAllStatus_ReturnsStatusForAllProviders()
    {
        // Arrange
        _registry = new RateLimiterRegistry();
        var yahooLimiter = _registry.GetOrCreate("yahoo", 100, TimeSpan.FromHours(1));
        var stooqLimiter = _registry.GetOrCreate("stooq", 50, TimeSpan.FromMinutes(30));

        await yahooLimiter.WaitForSlotAsync();
        await stooqLimiter.WaitForSlotAsync();
        await stooqLimiter.WaitForSlotAsync();

        // Act
        var allStatus = _registry.GetAllStatus();

        // Assert
        allStatus.Should().HaveCount(2);
        allStatus["yahoo"].RequestsInWindow.Should().Be(1);
        allStatus["yahoo"].MaxRequests.Should().Be(100);
        allStatus["stooq"].RequestsInWindow.Should().Be(2);
        allStatus["stooq"].MaxRequests.Should().Be(50);
    }

    [Fact]
    public void Dispose_DisposesAllLimiters()
    {
        // Arrange
        _registry = new RateLimiterRegistry();
        var yahooLimiter = _registry.GetOrCreate("yahoo", 100, TimeSpan.FromHours(1));
        _registry.Dispose();

        // Act & Assert
        Assert.Throws<ObjectDisposedException>(() => yahooLimiter.RecordRequest());
    }

    [Fact]
    public void GetOrCreate_AfterDispose_ThrowsObjectDisposedException()
    {
        // Arrange
        _registry = new RateLimiterRegistry();
        _registry.Dispose();

        // Act & Assert
        Assert.Throws<ObjectDisposedException>(
            () => _registry.GetOrCreate("test", 10, TimeSpan.FromSeconds(60)));
    }
}
