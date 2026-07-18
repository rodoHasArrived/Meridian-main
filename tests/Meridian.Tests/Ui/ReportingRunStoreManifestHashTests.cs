using System.Collections.Immutable;
using FluentAssertions;
using Meridian.Reporting;
using Meridian.Ui.Shared.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meridian.Tests.Ui;

public sealed class ReportingRunStoreManifestHashTests
{
    [Fact]
    public async Task SaveAsync_NonCertifiedFailedManifestWithDefaultArrays_PersistsAndRoundTrips()
    {
        using var fixture = new TempDirectory();
        var store = new FileReportingRunStore(
            new ReportingRunStoreOptions(fixture.Path),
            NullLogger<FileReportingRunStore>.Instance);

        // A non-certified run-failure manifest reaches the store before any of the grid/diff
        // collections are populated, and with no AuthoritativeSource its CertifiedDatasetRows is
        // default too. Every optional ImmutableArray member is therefore default (uninitialized).
        // Serializing a default ImmutableArray throws InvalidOperationException, and ValidateManifest
        // rejects a default CertifiedDatasetRows outright — either would previously abort the write
        // and mask the real failure. SaveAsync must normalize the whole manifest and persist it.
        var manifest = new ReportingOutputManifest(
            "run-default-arrays",
            "shadow-nav-daily-pack",
            new DateOnly(2026, 7, 15),
            ReportingRunStatus.Failed,
            ImmutableArray<ReportingSectionManifest>.Empty,
            ImmutableArray<string>.Empty,
            1,
            ReportingRunTrigger.AdHoc,
            FailureReason: "renderer threw");

        manifest.ReportWriterGrids.IsDefault.Should().BeTrue();
        manifest.RenderedReportWriterGrids.IsDefault.Should().BeTrue();
        manifest.ReportWriterGridDiffs.IsDefault.Should().BeTrue();
        manifest.CertifiedDatasetRows.IsDefault.Should().BeTrue();

        await store.SaveAsync(manifest, []);

        // ListRuns re-verifies the persisted manifest hash on read; a clean round-trip proves that
        // the stored snapshot serialized successfully and that save-side and load-side hashing agree
        // on the normalized manifest.
        var retained = store.ListRuns().Should()
            .ContainSingle(run => run.Manifest.RunId == "run-default-arrays").Subject;
        retained.Manifest.CertifiedDatasetRows.IsDefault.Should().BeFalse();
        retained.Manifest.ReportWriterGrids.IsDefault.Should().BeFalse();
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "Meridian.Tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
