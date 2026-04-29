using FluentAssertions;
using Meridian.Storage;
using Meridian.Storage.Services;
using Xunit;

namespace Meridian.Tests.Application.Monitoring;

public sealed class QualityTrendCalculationTests
{
    [Fact]
    public async Task GetTrendAsync_ImprovingFixture_ReportsImprovingDimensionsAndPositiveSlope()
    {
        var now = DateTimeOffset.UtcNow;
        var store = new InMemoryQualityTrendStore(new[]
        {
            CreatePoint("AAPL", now.AddDays(-5), 0.70, 0.68),
            CreatePoint("AAPL", now.AddDays(-4), 0.72, 0.69),
            CreatePoint("AAPL", now.AddDays(-2), 0.85, 0.86),
            CreatePoint("AAPL", now.AddDays(-1), 0.88, 0.89)
        });
        var sut = CreateService(store);

        var trend = await sut.GetTrendAsync("AAPL", TimeSpan.FromDays(3));

        trend.CurrentScore.Should().BeApproximately(0.88, 0.0001);
        trend.PreviousScore.Should().BeLessThan(trend.CurrentScore);
        trend.TrendDirection.Should().BePositive();
        trend.ImprovingDimensions.Should().Contain("Completeness");
        trend.DegradingDimensions.Should().BeEmpty();
        trend.ScoreHistory.Should().NotBeEmpty();
        trend.ScoreValues.Should().NotBeEmpty();
        trend.HasConfidence.Should().BeFalse();
    }

    [Fact]
    public async Task GetTrendAsync_FlatFixture_ReportsNoDimensionDriftAndNearZeroSlope()
    {
        var now = DateTimeOffset.UtcNow;
        var store = new InMemoryQualityTrendStore(new[]
        {
            CreatePoint("MSFT", now.AddDays(-5), 0.80, 0.80),
            CreatePoint("MSFT", now.AddDays(-4), 0.80, 0.80),
            CreatePoint("MSFT", now.AddDays(-2), 0.80, 0.80),
            CreatePoint("MSFT", now.AddDays(-1), 0.80, 0.80)
        });
        var sut = CreateService(store);

        var trend = await sut.GetTrendAsync("MSFT", TimeSpan.FromDays(3));

        trend.CurrentScore.Should().BeApproximately(0.80, 0.0001);
        trend.PreviousScore.Should().BeApproximately(0.80, 0.0001);
        trend.TrendDirection.Should().BeApproximately(0, 0.0001);
        trend.ImprovingDimensions.Should().BeEmpty();
        trend.DegradingDimensions.Should().BeEmpty();
        trend.IsSparseData.Should().BeTrue();
    }

    [Fact]
    public async Task GetTrendAsync_DegradingFixture_ReportsDegradingDimensionsAndNegativeSlope()
    {
        var now = DateTimeOffset.UtcNow;
        var store = new InMemoryQualityTrendStore(new[]
        {
            CreatePoint("TSLA", now.AddDays(-5), 0.92, 0.94),
            CreatePoint("TSLA", now.AddDays(-4), 0.90, 0.91),
            CreatePoint("TSLA", now.AddDays(-2), 0.76, 0.75),
            CreatePoint("TSLA", now.AddDays(-1), 0.72, 0.70)
        });
        var sut = CreateService(store);

        var trend = await sut.GetTrendAsync("TSLA", TimeSpan.FromDays(3));

        trend.CurrentScore.Should().BeApproximately(0.72, 0.0001);
        trend.PreviousScore.Should().BeGreaterThan(trend.CurrentScore);
        trend.TrendDirection.Should().BeNegative();
        trend.DegradingDimensions.Should().Contain("Completeness");
        trend.ImprovingDimensions.Should().BeEmpty();
        trend.ScoreHistory.Should().HaveCount(2);
        trend.ScoreValues.Should().HaveCount(2);
    }

    private static DataQualityService CreateService(IQualityTrendStore store)
        => new(
            new StorageOptions { RootPath = Path.Combine(Path.GetTempPath(), "meridian-quality-trend-tests") },
            trendStore: store);

    private static QualityTrendPoint CreatePoint(string symbol, DateTimeOffset scoredAt, double overall, double completeness)
        => new(
            Symbol: symbol,
            Date: DateOnly.FromDateTime(scoredAt.UtcDateTime.Date),
            Provider: "fixture-provider",
            ScoredAt: scoredAt,
            OverallScore: overall,
            DimensionScores: new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
            {
                ["Completeness"] = completeness,
                ["Accuracy"] = overall
            });

    private sealed class InMemoryQualityTrendStore : IQualityTrendStore
    {
        private readonly List<QualityTrendPoint> _points;

        public InMemoryQualityTrendStore(IEnumerable<QualityTrendPoint> points)
        {
            _points = points.ToList();
        }

        public Task AppendAsync(QualityTrendPoint point, CancellationToken ct = default)
        {
            _points.Add(point);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<QualityTrendPoint>> GetPointsAsync(string symbol, DateTimeOffset fromInclusive, DateTimeOffset toInclusive, CancellationToken ct = default)
        {
            IReadOnlyList<QualityTrendPoint> points = _points
                .Where(p => string.Equals(p.Symbol, symbol, StringComparison.OrdinalIgnoreCase))
                .Where(p => p.ScoredAt >= fromInclusive && p.ScoredAt <= toInclusive)
                .OrderBy(p => p.ScoredAt)
                .ToArray();

            return Task.FromResult(points);
        }
    }
}
