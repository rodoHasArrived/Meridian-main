using System.Text.Json;
using FluentAssertions;
using Meridian.Backtesting.Metrics;

namespace Meridian.Backtesting.Tests;

public sealed class UnavailableMetricSerializationTests
{
    [Fact]
    public void Metrics_MeasuredZeroAndNoLossProfitFactorRetainTheirMeaning()
    {
        var metrics = EmptyMetrics() with { WinRate = 0, ProfitFactor = double.PositiveInfinity };

        var document = JsonSerializer.SerializeToElement(metrics);

        document.GetProperty(nameof(BacktestMetrics.WinRate)).GetDouble().Should().Be(0);
        document.GetProperty(nameof(BacktestMetrics.ProfitFactor)).GetString().Should().Be("Infinity");
        var restored = document.Deserialize<BacktestMetrics>()!;
        restored.WinRate.Should().Be(0);
        restored.ProfitFactor.Should().Be(double.PositiveInfinity);
    }

    [Fact]
    public void Metrics_UnavailableOutcomesRoundTripAsNull()
    {
        var metrics = EmptyMetrics() with { WinRate = double.NaN, ProfitFactor = double.NaN };

        var document = JsonSerializer.SerializeToElement(metrics);

        document.GetProperty(nameof(BacktestMetrics.WinRate)).ValueKind.Should().Be(JsonValueKind.Null);
        document.GetProperty(nameof(BacktestMetrics.ProfitFactor)).ValueKind.Should().Be(JsonValueKind.Null);
        var restored = document.Deserialize<BacktestMetrics>()!;
        double.IsNaN(restored.WinRate).Should().BeTrue();
        double.IsNaN(restored.ProfitFactor).Should().BeTrue();
    }

    [Theory]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void Metrics_InfiniteWinRateIsRejectedRatherThanPromotedToAValidOutcome(double value)
    {
        var metrics = EmptyMetrics() with { WinRate = value };

        Action serialize = () => JsonSerializer.Serialize(metrics);

        serialize.Should().Throw<JsonException>();
    }

    [Theory]
    [InlineData("Infinity")]
    [InlineData("-Infinity")]
    public void Metrics_RetainedInfiniteWinRateIsRejected(string value)
    {
        var json = JsonSerializer.Serialize(EmptyMetrics() with { WinRate = 0 });
        json = json.Replace("\"WinRate\":0", $"\"WinRate\":\"{value}\"", StringComparison.Ordinal);

        Action deserialize = () => JsonSerializer.Deserialize<BacktestMetrics>(json);

        deserialize.Should().Throw<JsonException>();
    }

    private static BacktestMetrics EmptyMetrics() => BacktestMetricsEngine.Compute([], [], [],
        new BacktestRequest(new DateOnly(2024, 1, 1), new DateOnly(2024, 1, 2)));
}
