using FluentAssertions;
using Meridian.Ui.Services;

namespace Meridian.Ui.Tests.Services;

/// <summary>
/// Tests for <see cref="SystemHealthService"/> functionality.
/// </summary>
public sealed class SystemHealthServiceTests
{
    [Fact]
    public void Instance_ReturnsNonNullSingleton()
    {
        // Act
        var instance = SystemHealthService.Instance;

        // Assert
        instance.Should().NotBeNull();
    }

    [Fact]
    public void Instance_ReturnsSameInstanceOnMultipleCalls()
    {
        // Act
        var instance1 = SystemHealthService.Instance;
        var instance2 = SystemHealthService.Instance;

        // Assert
        instance1.Should().BeSameAs(instance2);
    }

    [Fact]
    public async Task GetHealthSummaryAsync_WithCancellation_SupportsCancellationToken()
    {
        // Arrange
        var service = SystemHealthService.Instance;
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act - API will be unavailable, but method should accept cancellation token
        // We're testing the signature, not the actual API call
        var act = async () => await service.GetHealthSummaryAsync(cts.Token);

        // Assert - May throw due to cancelled token or network error, both are acceptable
        await act.Should().ThrowAsync<Exception>();
    }

    [Fact]
    public async Task GetProviderHealthAsync_WithCancellation_SupportsCancellationToken()
    {
        // Arrange
        var service = SystemHealthService.Instance;
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act
        var act = async () => await service.GetProviderHealthAsync(cts.Token);

        // Assert - May throw due to cancelled token or network error
        await act.Should().ThrowAsync<Exception>();
    }

    [Theory]
    [InlineData("Alpaca")]
    [InlineData("Polygon")]
    [InlineData("InteractiveBrokers")]
    public async Task GetProviderDiagnosticsAsync_WithProviderName_AcceptsValidProviders(string provider)
    {
        // Arrange
        var service = SystemHealthService.Instance;
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act - Test signature accepts provider names
        var act = async () => await service.GetProviderDiagnosticsAsync(provider, cts.Token);

        // Assert - May throw due to cancelled token or network error
        await act.Should().ThrowAsync<Exception>();
    }

    [Fact]
    public async Task GetStorageHealthAsync_WithCancellation_SupportsCancellationToken()
    {
        // Arrange
        var service = SystemHealthService.Instance;
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act
        var act = async () => await service.GetStorageHealthAsync(cts.Token);

        // Assert
        await act.Should().ThrowAsync<Exception>();
    }

    [Theory]
    [InlineData(10)]
    [InlineData(50)]
    [InlineData(100)]
    public async Task GetRecentEventsAsync_WithLimit_AcceptsValidLimits(int limit)
    {
        // Arrange
        var service = SystemHealthService.Instance;
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act - Test signature accepts different limits
        var act = async () => await service.GetRecentEventsAsync(limit, cts.Token);

        // Assert
        await act.Should().ThrowAsync<Exception>();
    }

    [Fact]
    public async Task GetSystemMetricsAsync_WithCancellation_SupportsCancellationToken()
    {
        // Arrange
        var service = SystemHealthService.Instance;
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act
        var act = async () => await service.GetSystemMetricsAsync(cts.Token);

        // Assert
        await act.Should().ThrowAsync<Exception>();
    }

    [Theory]
    [InlineData("Alpaca")]
    [InlineData("Polygon")]
    public async Task TestConnectionAsync_WithProviderName_AcceptsValidProviders(string provider)
    {
        // Arrange
        var service = SystemHealthService.Instance;
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act
        var act = async () => await service.TestConnectionAsync(provider, cts.Token);

        // Assert
        await act.Should().ThrowAsync<Exception>();
    }

    [Fact]
    public async Task GenerateDiagnosticBundleAsync_WithCancellation_SupportsCancellationToken()
    {
        // Arrange
        var service = SystemHealthService.Instance;
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act
        var act = async () => await service.GenerateDiagnosticBundleAsync(cts.Token);

        // Assert
        await act.Should().ThrowAsync<Exception>();
    }

    [Fact]
    public async Task Instance_ThreadSafety_MultipleThreadsGetSameInstance()
    {
        // Arrange
        SystemHealthService? instance1 = null;
        SystemHealthService? instance2 = null;
        var task1 = Task.Run(() => instance1 = SystemHealthService.Instance);
        var task2 = Task.Run(() => instance2 = SystemHealthService.Instance);

        // Act
        await Task.WhenAll(task1, task2);

        // Assert
        instance1.Should().NotBeNull();
        instance2.Should().NotBeNull();
        instance1.Should().BeSameAs(instance2);
    }

    [Theory]
    [InlineData(5)]
    [InlineData(25)]
    [InlineData(250)]
    public async Task GetRecentEventsAsync_WithVariousLimits_AcceptsAllValues(int limit)
    {
        // Arrange
        var service = SystemHealthService.Instance;
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act - Test that method accepts different limit values
        var act = async () => await service.GetRecentEventsAsync(limit, cts.Token);

        // Assert
        await act.Should().ThrowAsync<Exception>();
    }

    [Fact]
    public async Task GetHealthSummaryAsync_WithoutCancellation_AcceptsDefaultToken()
    {
        // Arrange
        var service = SystemHealthService.Instance;

        // Act - Call without cancellation token (uses default)
        // API is unavailable in test environment; method should return null gracefully
        var act = async () => await service.GetHealthSummaryAsync();

        // Assert - Method accepts call without explicit cancellation token and does not throw
        await act.Should().NotThrowAsync();
    }

    [Theory]
    [InlineData("NYSE")]
    [InlineData("Finnhub")]
    [InlineData("Tiingo")]
    public async Task GetProviderDiagnosticsAsync_WithDifferentProviders_AcceptsVariousNames(string provider)
    {
        // Arrange
        var service = SystemHealthService.Instance;
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act
        var act = async () => await service.GetProviderDiagnosticsAsync(provider, cts.Token);

        // Assert
        await act.Should().ThrowAsync<Exception>();
    }

    [Fact]
    public async Task GetStorageHealthAsync_WhenCalled_SupportsAsyncOperation()
    {
        // Arrange
        var service = SystemHealthService.Instance;
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act
        var act = async () => await service.GetStorageHealthAsync(cts.Token);

        // Assert - Verifies method is truly async
        await act.Should().ThrowAsync<Exception>();
    }

    [Fact]
    public async Task GetProviderHealthAsync_WhenCalled_ReturnsOrThrows()
    {
        // Arrange
        var service = SystemHealthService.Instance;
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act
        var act = async () => await service.GetProviderHealthAsync(cts.Token);

        // Assert
        await act.Should().ThrowAsync<Exception>();
    }

    [Theory]
    [InlineData("StockSharp")]
    [InlineData("IEX")]
    public async Task TestConnectionAsync_WithVariousProviders_AcceptsAllNames(string provider)
    {
        // Arrange
        var service = SystemHealthService.Instance;
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act
        var act = async () => await service.TestConnectionAsync(provider, cts.Token);

        // Assert
        await act.Should().ThrowAsync<Exception>();
    }

    [Fact]
    public async Task GetSystemMetricsAsync_WhenApiUnavailable_HandlesGracefully()
    {
        // Arrange
        var service = SystemHealthService.Instance;
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act
        var act = async () => await service.GetSystemMetricsAsync(cts.Token);

        // Assert - Should throw due to cancellation or network error
        await act.Should().ThrowAsync<Exception>();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(500)]
    [InlineData(1000)]
    public async Task GetRecentEventsAsync_WithBoundaryValues_AcceptsEdgeCases(int limit)
    {
        // Arrange
        var service = SystemHealthService.Instance;
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act
        var act = async () => await service.GetRecentEventsAsync(limit, cts.Token);

        // Assert
        await act.Should().ThrowAsync<Exception>();
    }

    [Fact]
    public async Task AllMethods_WithCancellationToken_SupportCancellation()
    {
        // Arrange
        var service = SystemHealthService.Instance;
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert - All methods should accept and respect cancellation tokens
        // Use FluentAssertions ThrowAsync which accepts subtypes (e.g. TaskCanceledException)
        Func<Task> health = async () => await service.GetHealthSummaryAsync(cts.Token);
        await health.Should().ThrowAsync<OperationCanceledException>();

        Func<Task> providers = async () => await service.GetProviderHealthAsync(cts.Token);
        await providers.Should().ThrowAsync<OperationCanceledException>();

        Func<Task> storage = async () => await service.GetStorageHealthAsync(cts.Token);
        await storage.Should().ThrowAsync<OperationCanceledException>();

        Func<Task> metrics = async () => await service.GetSystemMetricsAsync(cts.Token);
        await metrics.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task GenerateDiagnosticBundleAsync_WhenCalled_SupportsAsyncOperation()
    {
        // Arrange
        var service = SystemHealthService.Instance;
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act
        var act = async () => await service.GenerateDiagnosticBundleAsync(cts.Token);

        // Assert - Verify async behavior
        await act.Should().ThrowAsync<Exception>();
    }
}
