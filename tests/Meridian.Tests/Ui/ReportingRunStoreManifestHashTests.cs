using System.Collections.Immutable;
using FluentAssertions;
using Meridian.Reporting;
using Meridian.Ui.Shared.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meridian.Tests.Ui;

public sealed class ReportingRunStoreManifestHashTests
{
    [Fact]
    public async Task SaveAsync_ManifestWithDefaultOptionalArrays_PersistsAndRoundTrips()
    {
        using var fixture = new TempDirectory();
        var store = new FileReportingRunStore(
            new ReportingRunStoreOptions(fixture.Path),
            NullLogger<FileReportingRunStore>.Instance);

        // A run-failure manifest reaches the store before the grid/diff collections are ever
        // populated, leaving those ImmutableArray members default (uninitialized). Serializing a
        // default ImmutableArray throws InvalidOperationException, which previously aborted the
        // durable write on the failure path. Only CertifiedDatasetRows is initialized here because
        // ValidateManifest rejects a default value for that member specifically.
        var manifest = new ReportingOutputManifest(
            "run-default-arrays",
            "shadow-nav-daily-pack",
            new DateOnly(2026, 7, 15),
            ReportingRunStatus.Failed,
            ImmutableArray<ReportingSectionManifest>.Empty,
            ImmutableArray<string>.Empty,
            1,
            ReportingRunTrigger.AdHoc,
            FailureReason: "renderer threw",
            CertifiedDatasetRows: ImmutableArray<IReadOnlyDictionary<string, string>>.Empty);

        manifest.ReportWriterGrids.IsDefault.Should().BeTrue();
        manifest.RenderedReportWriterGrids.IsDefault.Should().BeTrue();
        manifest.ReportWriterGridDiffs.IsDefault.Should().BeTrue();

        await store.SaveAsync(manifest, []);

        // ListRuns re-verifies the persisted manifest hash on read; a clean round-trip proves that
        // save-side and load-side hashing agree on the normalized manifest.
        store.ListRuns().Should().ContainSingle(run => run.Manifest.RunId == "run-default-arrays");
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
