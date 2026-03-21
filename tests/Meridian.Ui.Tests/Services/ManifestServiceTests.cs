using FluentAssertions;
using Meridian.Contracts.Manifest;
using Meridian.Contracts.Session;
using Meridian.Ui.Services;

namespace Meridian.Ui.Tests.Services;

/// <summary>
/// Tests for <see cref="ManifestService"/> and its associated manifest models.
/// </summary>
public sealed class ManifestServiceTests
{
    // ── Singleton ────────────────────────────────────────────────────

    [Fact]
    public void Instance_ShouldReturnNonNull()
    {
        var instance = ManifestService.Instance;
        instance.Should().NotBeNull();
    }

    [Fact]
    public void Instance_ShouldReturnSameSingleton()
    {
        var a = ManifestService.Instance;
        var b = ManifestService.Instance;
        a.Should().BeSameAs(b);
    }

    // ── DataManifest model ──────────────────────────────────────────

    [Fact]
    public void DataManifest_DefaultValues_ShouldBeCorrect()
    {
        var manifest = new DataManifest();

        manifest.ManifestVersion.Should().Be("1.0");
        manifest.SessionId.Should().BeNull();
        manifest.SessionName.Should().BeNull();
        manifest.DateRange.Should().BeNull();
        manifest.Symbols.Should().NotBeNull().And.BeEmpty();
        manifest.TotalFiles.Should().Be(0);
        manifest.TotalEvents.Should().Be(0);
        manifest.TotalBytesRaw.Should().Be(0);
        manifest.TotalBytesCompressed.Should().Be(0);
        manifest.Files.Should().NotBeNull().And.BeEmpty();
        manifest.Schemas.Should().BeNull();
        manifest.QualityMetrics.Should().BeNull();
        manifest.VerificationStatus.Should().Be(VerificationStatusValues.Pending);
        manifest.LastVerifiedAt.Should().BeNull();
    }

    [Fact]
    public void DataManifest_GeneratedAt_ShouldBeInitializedToUtcNow()
    {
        var before = DateTime.UtcNow;
        var manifest = new DataManifest();
        var after = DateTime.UtcNow;

        manifest.GeneratedAt.Should().BeOnOrAfter(before).And.BeOnOrBefore(after);
    }

    // ── ManifestFileEntry model ─────────────────────────────────────

    [Fact]
    public void ManifestFileEntry_DefaultValues_ShouldBeCorrect()
    {
        var entry = new ManifestFileEntry();

        entry.Path.Should().BeEmpty();
        entry.RelativePath.Should().BeEmpty();
        entry.Symbol.Should().BeNull();
        entry.EventType.Should().BeNull();
        entry.Date.Should().BeNull();
        entry.ChecksumSha256.Should().BeEmpty();
        entry.SizeBytes.Should().Be(0);
        entry.CompressedSizeBytes.Should().BeNull();
        entry.EventCount.Should().Be(0);
        entry.FirstTimestamp.Should().BeNull();
        entry.LastTimestamp.Should().BeNull();
        entry.SchemaVersion.Should().BeNull();
        entry.IsCompressed.Should().BeFalse();
        entry.CompressionType.Should().BeNull();
        entry.VerificationStatus.Should().Be(VerificationStatusValues.Pending);
        entry.LastVerifiedAt.Should().BeNull();
    }

    [Fact]
    public void ManifestFileEntry_CanSetAllProperties()
    {
        var now = DateTime.UtcNow;
        var entry = new ManifestFileEntry
        {
            Path = "/data/SPY/Trade/2024-01-15.jsonl.gz",
            RelativePath = "SPY/Trade/2024-01-15.jsonl.gz",
            Symbol = "SPY",
            EventType = "Trade",
            Date = now,
            ChecksumSha256 = "abc123",
            SizeBytes = 5000,
            CompressedSizeBytes = 1200,
            EventCount = 100,
            IsCompressed = true,
            CompressionType = "gzip"
        };

        entry.Symbol.Should().Be("SPY");
        entry.EventType.Should().Be("Trade");
        entry.SizeBytes.Should().Be(5000);
        entry.CompressedSizeBytes.Should().Be(1200);
        entry.EventCount.Should().Be(100);
        entry.IsCompressed.Should().BeTrue();
    }

    // ── DateRangeInfo model ─────────────────────────────────────────

    [Fact]
    public void DateRangeInfo_DefaultValues_ShouldBeCorrect()
    {
        var range = new DateRangeInfo();

        range.Start.Should().Be(default(DateTime));
        range.End.Should().Be(default(DateTime));
        range.TradingDays.Should().Be(0);
    }

    // ── DataQualityMetrics model ────────────────────────────────────

    [Fact]
    public void DataQualityMetrics_DefaultValues_ShouldBeCorrect()
    {
        var metrics = new DataQualityMetrics();

        metrics.CompletenessScore.Should().Be(0);
        metrics.IntegrityScore.Should().Be(0);
        metrics.OverallScore.Should().Be(0);
        metrics.GapsDetected.Should().Be(0);
        metrics.SequenceErrors.Should().Be(0);
        metrics.DuplicatesFound.Should().Be(0);
        metrics.ExpectedEvents.Should().Be(0);
        metrics.ActualEvents.Should().Be(0);
        metrics.MissingTradingDays.Should().BeNull();
        metrics.OutliersDetected.Should().Be(0);
    }

    // ── CollectionSession model ─────────────────────────────────────

    [Fact]
    public void CollectionSession_DefaultValues_ShouldBeCorrect()
    {
        var session = new CollectionSession();

        session.Id.Should().NotBeNullOrEmpty("Id is auto-generated as a Guid");
        session.Name.Should().BeEmpty();
        session.Description.Should().BeNull();
        session.Status.Should().Be(SessionStatus.Pending);
        session.StartedAt.Should().BeNull();
        session.EndedAt.Should().BeNull();
        session.Symbols.Should().NotBeNull().And.BeEmpty();
        session.EventTypes.Should().NotBeNull().And.BeEmpty();
        session.Provider.Should().BeNull();
        session.Tags.Should().BeNull();
        session.Notes.Should().BeNull();
        session.Statistics.Should().BeNull();
        session.QualityScore.Should().Be(0);
        session.ManifestPath.Should().BeNull();
    }

    // ── ManifestVerificationResult model ────────────────────────────

    [Fact]
    public void ManifestVerificationResult_DefaultValues_ShouldBeCorrect()
    {
        var result = new ManifestVerificationResult();

        result.ManifestId.Should().BeEmpty();
        result.IsValid.Should().BeFalse();
        result.VerifiedFiles.Should().Be(0);
        result.FailedFiles.Should().Be(0);
        result.MissingFiles.Should().NotBeNull().And.BeEmpty();
        result.ChecksumMismatches.Should().NotBeNull().And.BeEmpty();
        result.Errors.Should().NotBeNull().And.BeEmpty();
    }

    // ── LoadManifestAsync with non-existent file ────────────────────

    [Fact]
    public async Task LoadManifestAsync_WithNonExistentPath_ShouldReturnNull()
    {
        var service = ManifestService.Instance;
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString(), "missing.json");

        var result = await service.LoadManifestAsync(path);

        result.Should().BeNull();
    }

    // ── Events ──────────────────────────────────────────────────────

    [Fact]
    public void ManifestGenerated_Event_ShouldBeAttachable()
    {
        var service = ManifestService.Instance;
        ManifestEventArgs? received = null;
        service.ManifestGenerated += (_, args) => received = args;

        // Just verifying the event can be subscribed without error.
        service.ManifestGenerated -= (_, args) => received = args;
        received.Should().BeNull("no manifest was generated during this test");
    }
}
