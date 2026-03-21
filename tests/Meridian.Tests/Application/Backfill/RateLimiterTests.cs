using FluentAssertions;
using Meridian.Infrastructure.Adapters.Core;
using Xunit;

namespace Meridian.Tests.Backfill;

/// <summary>
/// Unit tests for the RateLimiter class.
/// Tests sliding window rate limiting, minimum delay enforcement, and status tracking.
/// </summary>
public class RateLimiterTests : IDisposable
{
    private RateLimiter? _rateLimiter;

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
        // Arrange - Use longer window for CI stability
        _rateLimiter = new RateLimiter(maxRequestsPerWindow: 2, window: TimeSpan.FromMilliseconds(100));

        // Act - Make 2 requests (at limit)
        await _rateLimiter.WaitForSlotAsync();
        await _rateLimiter.WaitForSlotAsync();

        var sw = System.Diagnostics.Stopwatch.StartNew();
        await _rateLimiter.WaitForSlotAsync(); // Should wait for window
        sw.Stop();

        // Assert - Should have waited a meaningful amount (wide tolerance for slow CI)
        sw.ElapsedMilliseconds.Should().BeGreaterThan(30);
    }

    #endregion

    #region Minimum Delay Tests

    [Fact]
    public async Task WaitForSlotAsync_WithMinDelay_EnforcesDelayBetweenRequests()
    {
        // Arrange - Use longer delay for CI stability
        _rateLimiter = new RateLimiter(
            maxRequestsPerWindow: 100,
            window: TimeSpan.FromSeconds(60),
            minDelayBetweenRequests: TimeSpan.FromMilliseconds(80));

        // Act - Make first request
        await _rateLimiter.WaitForSlotAsync();

        var sw = System.Diagnostics.Stopwatch.StartNew();
        await _rateLimiter.WaitForSlotAsync(); // Should wait for min delay
        sw.Stop();

        // Assert - Wider tolerance for slow CI runners
        sw.ElapsedMilliseconds.Should().BeGreaterThanOrEqualTo(50);
    }

    [Fact]
    public async Task WaitForSlotAsync_AfterDelayElapsed_DoesNotWait()
    {
        // Arrange
        _rateLimiter = new RateLimiter(
            maxRequestsPerWindow: 100,
            window: TimeSpan.FromSeconds(60),
            minDelayBetweenRequests: TimeSpan.FromMilliseconds(50));

        // Act
        await _rateLimiter.WaitForSlotAsync();
        await Task.Delay(60); // Wait well beyond min delay for CI stability

        var sw = System.Diagnostics.Stopwatch.StartNew();
        await _rateLimiter.WaitForSlotAsync();
        sw.Stop();

        // Assert - Should return quickly (wider tolerance for CI)
        sw.ElapsedMilliseconds.Should().BeLessThan(100);
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
        // Arrange - Use longer window for CI stability
        _rateLimiter = new RateLimiter(maxRequestsPerWindow: 2, window: TimeSpan.FromMilliseconds(100));

        // Act - Record 2 requests (at limit)
        _rateLimiter.RecordRequest();
        _rateLimiter.RecordRequest();

        var sw = System.Diagnostics.Stopwatch.StartNew();
        await _rateLimiter.WaitForSlotAsync(); // Should wait
        sw.Stop();

        // Assert - Wider tolerance for slow CI runners
        sw.ElapsedMilliseconds.Should().BeGreaterThan(30);
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
        // Arrange - Short window for testing
        _rateLimiter = new RateLimiter(maxRequestsPerWindow: 10, window: TimeSpan.FromMilliseconds(50));

        // Act
        await _rateLimiter.WaitForSlotAsync();
        await _rateLimiter.WaitForSlotAsync();

        // Wait for window to expire
        await Task.Delay(60);

        var (requestsInWindow, _, _) = _rateLimiter.GetStatus();

        // Assert
        requestsInWindow.Should().Be(0);
    }

    #endregion

    #region Cancellation Tests

    [Fact]
    public async Task WaitForSlotAsync_WithCancellation_ThrowsOperationCanceledException()
    {
        // Arrange
        _rateLimiter = new RateLimiter(maxRequestsPerWindow: 1, window: TimeSpan.FromSeconds(60));
        await _rateLimiter.WaitForSlotAsync(); // Use up the slot

        using var cts = new CancellationTokenSource();
        cts.CancelAfter(50);

        // Act & Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => _rateLimiter.WaitForSlotAsync(cts.Token));
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
        // Arrange - Use a much shorter window (100ms) to make test fast
        // With limit of 3 and 6 concurrent requests, the second batch must wait
        _rateLimiter = new RateLimiter(maxRequestsPerWindow: 3, window: TimeSpan.FromMilliseconds(100));

        // Act - Launch 6 concurrent requests (2x limit)
        var tasks = Enumerable.Range(0, 6)
            .Select(_ => _rateLimiter.WaitForSlotAsync())
            .ToList();

        var waitTimes = await Task.WhenAll(tasks);

        // Assert - Some tasks should have waited (rate limited), others should not
        // First 3 requests should complete immediately (or near-zero wait)
        var immediateRequests = waitTimes.Count(t => t.TotalMilliseconds < 50);
        var delayedRequests = waitTimes.Count(t => t.TotalMilliseconds >= 50);

        immediateRequests.Should().BeGreaterThanOrEqualTo(3, "at least 3 requests should proceed immediately");
        delayedRequests.Should().BeGreaterThanOrEqualTo(3, "at least 3 requests should be delayed by rate limiting");

        // At least some requests should have waited a non-trivial duration (>= window/2)
        waitTimes.Should().Contain(t => t.TotalMilliseconds >= 50, "some requests should wait for rate limit window");
    }

    [Fact]
    public async Task WaitForSlotAsync_ConcurrentRequestsWithShortWindow_EnforcesLimit()
    {
        // Arrange
        _rateLimiter = new RateLimiter(maxRequestsPerWindow: 3, window: TimeSpan.FromMilliseconds(100));

        var sw = System.Diagnostics.Stopwatch.StartNew();

        // Act - Launch 6 concurrent requests (2x limit)
        var tasks = Enumerable.Range(0, 6).Select(_ => _rateLimiter.WaitForSlotAsync()).ToList();
        await Task.WhenAll(tasks);

        sw.Stop();

        // Assert - Should have taken at least one window period
        sw.ElapsedMilliseconds.Should().BeGreaterThan(50);
    }

    #endregion

    #region Sliding Window Behavior Tests

    [Fact]
    public async Task SlidingWindow_OldRequestsExpire_NewRequestsAllowed()
    {
        // Arrange
        _rateLimiter = new RateLimiter(maxRequestsPerWindow: 2, window: TimeSpan.FromMilliseconds(50));

        // Act - Use up the limit
        await _rateLimiter.WaitForSlotAsync();
        await _rateLimiter.WaitForSlotAsync();

        // Wait for window to expire
        await Task.Delay(60);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        await _rateLimiter.WaitForSlotAsync();
        sw.Stop();

        // Assert - Should not need to wait much since old requests expired
        sw.ElapsedMilliseconds.Should().BeLessThan(50);
    }

    #endregion
}

/// <summary>
/// Unit tests for the RateLimiterRegistry class.
/// </summary>
public class RateLimiterRegistryTests : IDisposable
{
    private RateLimiterRegistry? _registry;

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
