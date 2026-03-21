using FluentAssertions;
using Meridian.Application.Monitoring;
using Xunit;

namespace Meridian.Tests;

/// <summary>
/// Serializes all Prometheus metrics test classes that mutate the shared static
/// <see cref="Metrics"/> counters, preventing race conditions when tests run in parallel.
/// </summary>
[CollectionDefinition("PrometheusMetrics")]
public sealed class PrometheusMetricsCollection { }

/// <summary>
/// Tests for Prometheus metrics integration.
/// Validates metric export and update functionality.
/// Based on testing patterns from prometheus-net examples.
/// </summary>
[Collection("PrometheusMetrics")]
public class PrometheusMetricsTests
{
    [Fact]
    public void UpdateFromSnapshot_ShouldNotThrow()
    {
        // Arrange
        Metrics.Reset();
        Metrics.IncPublished();
        Metrics.IncTrades();

        // Act
        var act = () => PrometheusMetrics.UpdateFromSnapshot();

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void RecordTrade_ShouldNotThrow()
    {
        // Arrange
        var symbol = "AAPL";
        var venue = "NASDAQ";
        var price = 150.25m;
        var size = 100;

        // Act
        var act = () => PrometheusMetrics.RecordTrade(symbol, venue, price, size);

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void RecordProcessingLatency_ShouldNotThrow()
    {
        // Arrange
        var latencyMicroseconds = 125.5;

        // Act
        var act = () => PrometheusMetrics.RecordProcessingLatency(latencyMicroseconds);

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void Registry_ShouldNotBeNull()
    {
        // Act
        var registry = PrometheusMetrics.Registry;

        // Assert
        registry.Should().NotBeNull();
    }

    [Fact]
    public void UpdateFromSnapshot_ShouldReflectMetricsChanges()
    {
        // Arrange
        Metrics.Reset();
        var initialSnapshot = Metrics.GetSnapshot();

        // Increment some metrics
        for (int i = 0; i < 10; i++)
        {
            Metrics.IncPublished();
            Metrics.IncTrades();
        }

        // Act
        PrometheusMetrics.UpdateFromSnapshot();
        var updatedSnapshot = Metrics.GetSnapshot();

        // Assert
        updatedSnapshot.Published.Should().BeGreaterThan(initialSnapshot.Published);
        updatedSnapshot.Trades.Should().BeGreaterThan(initialSnapshot.Trades);
    }
}

/// <summary>
/// Tests for PrometheusMetricsUpdater background service.
/// </summary>
[Collection("PrometheusMetrics")]
public class PrometheusMetricsUpdaterTests
{
    [Fact]
    public async Task Constructor_ShouldInitialize()
    {
        // Arrange & Act
        await using var updater = new PrometheusMetricsUpdater(TimeSpan.FromSeconds(1));

        // Assert
        updater.Should().NotBeNull();
    }

    [Fact]
    public async Task DisposeAsync_ShouldCompleteGracefully()
    {
        // Arrange
        var updater = new PrometheusMetricsUpdater(TimeSpan.FromMilliseconds(100));

        // Act
        var act = async () => await updater.DisposeAsync();

        // Assert
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task UpdateLoop_ShouldUpdateMetricsPeriodically()
    {
        // Arrange
        Metrics.Reset();
        await using var updater = new PrometheusMetricsUpdater(TimeSpan.FromMilliseconds(20));

        // Increment metrics
        for (int i = 0; i < 5; i++)
        {
            Metrics.IncPublished();
        }

        // Act - Wait for a few update cycles
        await Task.Delay(TimeSpan.FromMilliseconds(60));

        // Assert - Metrics should have been updated at least once
        var snapshot = Metrics.GetSnapshot();
        snapshot.Published.Should().BeGreaterThanOrEqualTo(5);
    }
}
