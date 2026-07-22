using FluentAssertions;
using Meridian.Contracts.Operations;
using Meridian.Storage;
using Meridian.Storage.Services;
using System.Security.Cryptography;
using Xunit;

namespace Meridian.Tests.Application.Monitoring;

public sealed class QualityTrendCalculationTests
{
    [Fact]
    public async Task ScoreAsync_PersistsVerifiedTrendPoint()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"meridian-quality-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        var filePath = Path.Combine(tempRoot, "sample.jsonl");
        await File.WriteAllTextAsync(filePath, "{\"symbol\":\"AAPL\",\"timestamp\":\"2026-01-01T00:00:00Z\",\"price\":100}\n");

        try
        {
            var store = new InMemoryQualityTrendStore(Array.Empty<QualityTrendPoint>());
            var sut = new DataQualityService(new StorageOptions { RootPath = tempRoot }, trendStore: store);

            var score = await sut.ScoreAsync(filePath);

            store.AppendCount.Should().Be(1);
            store.Points.Should().ContainSingle();
            var point = store.Points.Single();
            point.InputHashSha256.Should().MatchRegex("^[0-9a-f]{64}$");
            point.ResultHashSha256.Should().MatchRegex("^[0-9a-f]{64}$");
            point.Outcome.Should().NotBeNull();
            point.Outcome!.State.Should().Be(OperationTerminalState.CompletedWithWarnings);
            point.Outcome.Postconditions.Should().OnlyContain(static condition =>
                condition.State == OperationPostconditionState.Satisfied);
            score.Outcome.Should().BeSameAs(point.Outcome);
            VerifiedOperationOutcomeValidator.Validate(score.Outcome!).Should().BeEmpty();
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task ScoreAsync_BindsOutcomeToImmutableSnapshotWhenSourceMutatesDuringPersistence()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"meridian-quality-mutation-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        var filePath = Path.Combine(tempRoot, "sample.jsonl");
        await File.WriteAllTextAsync(filePath, "{\"Sequence\":1}\n");
        try
        {
            var store = new MutatingQualityTrendStore(filePath, "{\"Sequence\":99}\n");
            var sut = new DataQualityService(new StorageOptions { RootPath = tempRoot }, trendStore: store);

            var score = await sut.ScoreAsync(filePath);

            store.Point.Should().NotBeNull();
            var point = store.Point!;
            var mutatedHash = Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(filePath)));
            point.InputHashSha256.Should().NotBe(mutatedHash);
            var inputEvidence = score.Outcome!.Evidence.Single(evidence => evidence.Kind == "input-file");
            var snapshotPath = new Uri(inputEvidence.Uri!).LocalPath;
            File.Exists(snapshotPath).Should().BeTrue();
            var snapshotHash = Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(snapshotPath)));
            snapshotHash.Should().Be(point.InputHashSha256);
            inputEvidence.ContentHashSha256.Should().Be(snapshotHash);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public async Task ScoreAsync_WhenTrendAppendFails_DoesNotPublishUnretainedCacheEntry()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"meridian-quality-append-failure-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        var filePath = Path.Combine(tempRoot, "sample.jsonl");
        await File.WriteAllTextAsync(filePath, "{\"Sequence\":1}\n");
        try
        {
            var sut = new DataQualityService(
                new StorageOptions { RootPath = tempRoot },
                trendStore: new ThrowingQualityTrendStore());

            var score = await sut.ScoreAsync(filePath);

            score.Outcome.Should().NotBeNull();
            score.Outcome!.State.Should().Be(OperationTerminalState.Failed);
            score.Outcome.Issues.Should().ContainSingle(issue =>
                issue.Code == "quality-evaluation-failed" &&
                issue.Message.Contains("trend append failed", StringComparison.Ordinal));
            VerifiedOperationOutcomeValidator.Validate(score.Outcome).Should().BeEmpty();
            var retainedReceipt = Path.Combine(
                tempRoot,
                "quality",
                "outcomes",
                $"{score.Outcome.OperationId}.json");
            File.Exists(retainedReceipt).Should().BeTrue();
            (await sut.GetHistoricalScoresAsync(Path.GetFullPath(filePath), TimeSpan.FromDays(1)))
                .Should().BeEmpty();
        }
        finally
        {
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public async Task GenerateReportAsync_TracksSuccessfulCorruptAndMissingInputsWithoutFalsePerfectAverage()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"meridian-quality-report-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        var corruptPath = Path.Combine(tempRoot, "corrupt.jsonl");
        var missingPath = Path.Combine(tempRoot, "missing.jsonl");
        await File.WriteAllTextAsync(corruptPath, "not-json\n");
        try
        {
            var sut = new DataQualityService(
                new StorageOptions { RootPath = tempRoot },
                trendStore: new InMemoryQualityTrendStore([]));

            var report = await sut.GenerateReportAsync(new QualityReportOptions([corruptPath, missingPath]));

            report.FilesAttempted.Should().Be(2);
            report.FilesSucceeded.Should().Be(1);
            report.FilesFailed.Should().Be(1);
            report.FilesAnalyzed.Should().Be(1);
            report.AverageScore.Should().BeLessThan(1.0);
            report.Issues.Should().ContainSingle(issue => issue.Path == missingPath);
            report.LowQualityFiles.Should().ContainSingle(score =>
                score.Path == Path.GetFullPath(corruptPath) &&
                score.Outcome!.State == OperationTerminalState.CompletedWithWarnings);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public async Task GenerateReportAsync_CancellationIsNotConvertedIntoAQualityIssue()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"meridian-quality-cancel-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        var filePath = Path.Combine(tempRoot, "sample.jsonl");
        await File.WriteAllTextAsync(filePath, "{\"Sequence\":1}\n");
        try
        {
            var sut = new DataQualityService(
                new StorageOptions { RootPath = tempRoot },
                trendStore: new InMemoryQualityTrendStore([]));
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            var report = () => sut.GenerateReportAsync(new QualityReportOptions([filePath]), cts.Token);

            await report.Should().ThrowAsync<OperationCanceledException>();
        }
        finally
        {
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, recursive: true);
        }
    }

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
        public int AppendCount { get; private set; }
        public IReadOnlyList<QualityTrendPoint> Points => _points;

        public InMemoryQualityTrendStore(IEnumerable<QualityTrendPoint> points)
        {
            _points = points.ToList();
        }

        public Task AppendAsync(QualityTrendPoint point, CancellationToken ct = default)
        {
            AppendCount++;
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

    private sealed class MutatingQualityTrendStore(string sourcePath, string replacement) : IQualityTrendStore
    {
        public QualityTrendPoint? Point { get; private set; }

        public async Task AppendAsync(QualityTrendPoint point, CancellationToken ct = default)
        {
            Point = point;
            await File.WriteAllTextAsync(sourcePath, replacement, ct);
        }

        public Task<IReadOnlyList<QualityTrendPoint>> GetPointsAsync(
            string symbol,
            DateTimeOffset fromInclusive,
            DateTimeOffset toInclusive,
            CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<QualityTrendPoint>>(Point is null ? [] : [Point]);
    }

    private sealed class ThrowingQualityTrendStore : IQualityTrendStore
    {
        public Task AppendAsync(QualityTrendPoint point, CancellationToken ct = default) =>
            Task.FromException(new IOException("trend append failed"));

        public Task<IReadOnlyList<QualityTrendPoint>> GetPointsAsync(
            string symbol,
            DateTimeOffset fromInclusive,
            DateTimeOffset toInclusive,
            CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<QualityTrendPoint>>([]);
    }
}
