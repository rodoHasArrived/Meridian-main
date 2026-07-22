using FluentAssertions;
using Meridian.Contracts.Operations;
using Meridian.Storage;
using Meridian.Storage.Services;
using Meridian.Tests.Infrastructure;
using System.Text.Json;
using Xunit;

namespace Meridian.Tests.Storage;

public sealed class QualityTrendStoreTests : TempDirectoryTestBase
{
    [Fact]
    public async Task AppendAndReadAsync_RoundTripsVerifiedQualityEvidence()
    {
        var sut = new FileQualityTrendStore(new StorageOptions { RootPath = TestDataRoot });
        var at = DateTimeOffset.Parse("2026-07-19T12:00:00Z");
        var hash = new string('a', 64);
        var dimensions = new Dictionary<string, double> { ["Completeness"] = 1.0 };
        var resultHash = QualityTrendResultHash.Compute(
            "quality-evaluation-1",
            "data-quality.v1",
            hash,
            "AAPL",
            new DateOnly(2026, 7, 19),
            "provider-a",
            at,
            0.98,
            dimensions);
        var outcome = new VerifiedOperationOutcome(
            "quality-evaluation-1",
            "data-quality-evaluation",
            OperationTerminalState.Succeeded,
            at,
            at.AddSeconds(1),
            1,
            "quality-evaluation-1",
            hash,
            [new OperationPostcondition("evaluated", "Quality evaluated.", OperationPostconditionState.Satisfied, true, ["input", "result"])],
            [
                new OperationEvidenceReference("input", "input-file", "Retained input.", "file:///quality.jsonl", hash, at),
                new OperationEvidenceReference("result", "quality-result", "Retained result hash.", ContentHashSha256: resultHash, CapturedAtUtc: at)
            ],
            [],
            [],
            []);
        var point = new QualityTrendPoint(
            "AAPL",
            new DateOnly(2026, 7, 19),
            "provider-a",
            at,
            0.98,
            dimensions)
        {
            EvaluationId = outcome.OperationId,
            InputHashSha256 = hash,
            ResultHashSha256 = resultHash,
            RulesetVersion = "data-quality.v1",
            Outcome = outcome
        };

        await sut.AppendAsync(point);

        var restored = await sut.GetPointsAsync("aapl", at.AddMinutes(-1), at.AddMinutes(1));
        restored.Should().ContainSingle();
        restored[0].Should().BeEquivalentTo(point);
        VerifiedOperationOutcomeValidator.Validate(restored[0].Outcome!).Should().BeEmpty();
    }

    [Fact]
    public async Task GetPointsAsync_LegacyVerifiedRowsMigrateToChainedHistoryWithBackup()
    {
        var qualityDirectory = Path.Combine(TestDataRoot, "quality");
        Directory.CreateDirectory(qualityDirectory);
        var path = Path.Combine(qualityDirectory, "trend-points.jsonl");
        var at = DateTimeOffset.Parse("2026-07-19T12:00:00Z");
        var legacyLine = JsonSerializer.Serialize(
            CreatePoint(1, at),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        await File.WriteAllTextAsync(path, legacyLine + Environment.NewLine);
        var sut = new FileQualityTrendStore(new StorageOptions { RootPath = TestDataRoot });

        var restored = await sut.GetPointsAsync("AAPL", at.AddMinutes(-1), at.AddMinutes(1));

        restored.Should().ContainSingle();
        (await File.ReadAllTextAsync(path)).Should().Contain("\"schemaVersion\":2");
        File.Exists(path + ".head").Should().BeTrue();
        File.Exists(path + ".bak").Should().BeTrue();
    }

    [Fact]
    public async Task GetPointsAsync_MalformedHistory_FailsClosed()
    {
        var qualityDirectory = Path.Combine(TestDataRoot, "quality");
        Directory.CreateDirectory(qualityDirectory);
        await File.WriteAllTextAsync(Path.Combine(qualityDirectory, "trend-points.jsonl"), "{not-json}\n");
        var sut = new FileQualityTrendStore(new StorageOptions { RootPath = TestDataRoot });

        var read = () => sut.GetPointsAsync(
            "AAPL",
            DateTimeOffset.MinValue,
            DateTimeOffset.MaxValue);

        await read.Should().ThrowAsync<InvalidDataException>()
            .WithMessage("*line 1*cannot be trusted*");
    }

    [Fact]
    public async Task ConcurrentStoreInstances_DoNotLoseVerifiedPoints()
    {
        var options = new StorageOptions { RootPath = TestDataRoot };
        var first = new FileQualityTrendStore(options);
        var second = new FileQualityTrendStore(options);
        var at = DateTimeOffset.Parse("2026-07-19T12:00:00Z");

        await Task.WhenAll(Enumerable.Range(1, 20).Select(index =>
            (index % 2 == 0 ? first : second).AppendAsync(CreatePoint(index, at.AddSeconds(index)))));

        var restored = await first.GetPointsAsync("AAPL", at, at.AddMinutes(1));
        restored.Should().HaveCount(20);
        restored.Select(static point => point.EvaluationId).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task AppendAsync_ExactEvaluationReplayIsIdempotentButChangedEvidenceFailsClosed()
    {
        var sut = new FileQualityTrendStore(new StorageOptions { RootPath = TestDataRoot });
        var at = DateTimeOffset.Parse("2026-07-19T12:00:00Z");
        var point = CreatePoint(1, at);

        await sut.AppendAsync(point);
        await sut.AppendAsync(point);
        var changed = point with { Provider = "different-provider" };
        var appendChanged = () => sut.AppendAsync(changed);

        await appendChanged.Should().ThrowAsync<ArgumentException>();
        (await sut.GetPointsAsync("AAPL", at.AddMinutes(-1), at.AddMinutes(1)))
            .Should().ContainSingle();
    }

    [Fact]
    public async Task GetPointsAsync_TruncatedTailFailsChainHeadValidation()
    {
        var sut = new FileQualityTrendStore(new StorageOptions { RootPath = TestDataRoot });
        var at = DateTimeOffset.Parse("2026-07-19T12:00:00Z");
        await sut.AppendAsync(CreatePoint(1, at));
        await sut.AppendAsync(CreatePoint(2, at.AddSeconds(1)));
        var path = Path.Combine(TestDataRoot, "quality", "trend-points.jsonl");
        var lines = await File.ReadAllLinesAsync(path);
        await File.WriteAllLinesAsync(path, lines.Take(1));

        var read = () => sut.GetPointsAsync("AAPL", at.AddMinutes(-1), at.AddMinutes(1));

        await read.Should().ThrowAsync<InvalidDataException>()
            .WithMessage("*chain head does not match*");
    }

    [Fact]
    public async Task GetPointsAsync_ReorderedHistoryFailsPredecessorValidation()
    {
        var sut = new FileQualityTrendStore(new StorageOptions { RootPath = TestDataRoot });
        var at = DateTimeOffset.Parse("2026-07-19T12:00:00Z");
        await sut.AppendAsync(CreatePoint(1, at));
        await sut.AppendAsync(CreatePoint(2, at.AddSeconds(1)));
        var path = Path.Combine(TestDataRoot, "quality", "trend-points.jsonl");
        var lines = await File.ReadAllLinesAsync(path);
        await File.WriteAllLinesAsync(path, lines.Reverse());

        var read = () => sut.GetPointsAsync("AAPL", at.AddMinutes(-1), at.AddMinutes(1));

        await read.Should().ThrowAsync<InvalidDataException>()
            .WithMessage("*sequence or predecessor hash chain*");
    }

    [Fact]
    public async Task AppendAsync_UnverifiedPoint_FailsClosed()
    {
        var sut = new FileQualityTrendStore(new StorageOptions { RootPath = TestDataRoot });
        var at = DateTimeOffset.Parse("2026-07-19T12:00:00Z");
        var point = CreatePoint(1, at) with { Outcome = null };

        var append = () => sut.AppendAsync(point);

        await append.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*verified terminal outcome*");
    }

    [Fact]
    public async Task GetPointsAsync_SemanticallyTamperedHistory_FailsClosed()
    {
        var sut = new FileQualityTrendStore(new StorageOptions { RootPath = TestDataRoot });
        var at = DateTimeOffset.Parse("2026-07-19T12:00:00Z");
        await sut.AppendAsync(CreatePoint(1, at));
        var path = Path.Combine(TestDataRoot, "quality", "trend-points.jsonl");
        var retained = await File.ReadAllTextAsync(path);
        const string originalId = "quality-evaluation-1";
        var idOffset = retained.IndexOf(originalId, StringComparison.Ordinal);
        idOffset.Should().BeGreaterThanOrEqualTo(0);
        var tampered = retained[..idOffset] + "quality-evaluation-tampered" + retained[(idOffset + originalId.Length)..];
        await File.WriteAllTextAsync(path, tampered);

        var read = () => sut.GetPointsAsync("AAPL", at.AddMinutes(-1), at.AddMinutes(1));

        await read.Should().ThrowAsync<InvalidDataException>()
            .WithMessage("*violates the verified-evidence contract*");
    }

    [Fact]
    public async Task GetPointsAsync_InRangeScoreTamper_FailsResultHashValidation()
    {
        var sut = new FileQualityTrendStore(new StorageOptions { RootPath = TestDataRoot });
        var at = DateTimeOffset.Parse("2026-07-19T12:00:00Z");
        await sut.AppendAsync(CreatePoint(1, at));
        var path = Path.Combine(TestDataRoot, "quality", "trend-points.jsonl");
        var retained = await File.ReadAllTextAsync(path);
        var tampered = retained.Replace("\"overallScore\":0.98", "\"overallScore\":0.97", StringComparison.Ordinal);
        tampered.Should().NotBe(retained);
        await File.WriteAllTextAsync(path, tampered);

        var read = () => sut.GetPointsAsync("AAPL", at.AddMinutes(-1), at.AddMinutes(1));

        await read.Should().ThrowAsync<InvalidDataException>()
            .WithMessage("*violates the verified-evidence contract*");
    }

    [Fact]
    public async Task GetPointsAsync_RulesetTamper_FailsResultHashValidation()
    {
        var sut = new FileQualityTrendStore(new StorageOptions { RootPath = TestDataRoot });
        var at = DateTimeOffset.Parse("2026-07-19T12:00:00Z");
        await sut.AppendAsync(CreatePoint(1, at));
        var path = Path.Combine(TestDataRoot, "quality", "trend-points.jsonl");
        var retained = await File.ReadAllTextAsync(path);
        var tampered = retained.Replace(
            "\"rulesetVersion\":\"data-quality.v1\"",
            "\"rulesetVersion\":\"data-quality.v2\"",
            StringComparison.Ordinal);
        tampered.Should().NotBe(retained);
        await File.WriteAllTextAsync(path, tampered);

        var read = () => sut.GetPointsAsync("AAPL", at.AddMinutes(-1), at.AddMinutes(1));

        await read.Should().ThrowAsync<InvalidDataException>()
            .WithMessage("*violates the verified-evidence contract*");
    }

    [Fact]
    public void ResultHash_BindsEvaluationRulesetAndInputProvenance()
    {
        var at = DateTimeOffset.Parse("2026-07-19T12:00:00Z");
        var inputHash = new string('a', 64);
        var dimensions = new Dictionary<string, double> { ["Completeness"] = 1.0 };
        var baseline = QualityTrendResultHash.Compute(
            "quality-evaluation-1",
            "data-quality.v1",
            inputHash,
            "AAPL",
            new DateOnly(2026, 7, 19),
            "provider-a",
            at,
            0.98,
            dimensions);

        QualityTrendResultHash.Compute(
                "quality-evaluation-2", "data-quality.v1", inputHash, "AAPL",
                new DateOnly(2026, 7, 19), "provider-a", at, 0.98, dimensions)
            .Should().NotBe(baseline);
        QualityTrendResultHash.Compute(
                "quality-evaluation-1", "data-quality.v2", inputHash, "AAPL",
                new DateOnly(2026, 7, 19), "provider-a", at, 0.98, dimensions)
            .Should().NotBe(baseline);
        QualityTrendResultHash.Compute(
                "quality-evaluation-1", "data-quality.v1", new string('b', 64), "AAPL",
                new DateOnly(2026, 7, 19), "provider-a", at, 0.98, dimensions)
            .Should().NotBe(baseline);
    }

    private static QualityTrendPoint CreatePoint(int index, DateTimeOffset at)
    {
        var hash = new string('a', 64);
        var operationId = $"quality-evaluation-{index}";
        var dimensions = new Dictionary<string, double> { ["Completeness"] = 1.0 };
        var resultHash = QualityTrendResultHash.Compute(
            operationId,
            "data-quality.v1",
            hash,
            "AAPL",
            DateOnly.FromDateTime(at.UtcDateTime),
            "provider-a",
            at,
            0.98,
            dimensions);
        var outcome = new VerifiedOperationOutcome(
            operationId,
            "data-quality-evaluation",
            OperationTerminalState.Succeeded,
            at,
            at.AddMilliseconds(1),
            1,
            operationId,
            hash,
            [new OperationPostcondition("evaluated", "Quality evaluated.", OperationPostconditionState.Satisfied, true, ["input", "result"])],
            [
                new OperationEvidenceReference("input", "input-file", "Retained input.", "file:///quality.jsonl", hash, at),
                new OperationEvidenceReference("result", "quality-result", "Retained result hash.", ContentHashSha256: resultHash, CapturedAtUtc: at)
            ],
            [],
            [],
            []);
        return new QualityTrendPoint(
            "AAPL",
            DateOnly.FromDateTime(at.UtcDateTime),
            "provider-a",
            at,
            0.98,
            dimensions)
        {
            EvaluationId = operationId,
            InputHashSha256 = hash,
            ResultHashSha256 = resultHash,
            RulesetVersion = "data-quality.v1",
            Outcome = outcome
        };
    }
}
