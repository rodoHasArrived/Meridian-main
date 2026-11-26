using System.Text;
using FluentAssertions;
using Meridian.Storage.Archival;
using Meridian.Tests.Infrastructure;
using Meridian.Ui.Shared.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meridian.Tests.Ui;

public sealed class ReportingPersistenceFailClosedTests
{
    [Fact]
    public void ReportingRunStore_CorruptSnapshot_BlocksInsteadOfReturningEmptyState()
    {
        using var fixture = CorruptSnapshotFixture.Create("reporting-runs.json");
        var store = new FileReportingRunStore(
            new ReportingRunStoreOptions(fixture.RootDirectory),
            NullLogger<FileReportingRunStore>.Instance);

        var act = () => store.ListRuns();

        act.Should().Throw<ReportingStateCorruptionException>()
            .Which.StatePath.Should().Be(fixture.SnapshotPath);
        File.ReadAllText(fixture.SnapshotPath).Should().Be(CorruptSnapshotFixture.CorruptJson);
    }

    [Fact]
    public void TemplateGovernanceStore_CorruptSnapshot_BlocksInsteadOfReturningEmptyState()
    {
        using var fixture = CorruptSnapshotFixture.Create("report-templates.json");
        var store = new FileReportTemplateGovernanceStore(
            new ReportTemplateGovernanceStoreOptions(fixture.SnapshotPath),
            NullLogger<FileReportTemplateGovernanceStore>.Instance);

        var act = () => store.Load();

        act.Should().Throw<ReportingStateCorruptionException>()
            .Which.StatePath.Should().Be(fixture.SnapshotPath);
    }

    [Fact]
    public void WorkflowStore_CorruptSnapshot_BlocksInsteadOfReturningEmptyState()
    {
        using var fixture = CorruptSnapshotFixture.Create("report-pack-workflows.json");
        var store = new FileReportPackWorkflowRecordStore(
            new ReportPackWorkflowRecordStoreOptions(fixture.SnapshotPath),
            NullLogger<FileReportPackWorkflowRecordStore>.Instance);

        var act = () => store.Load();

        act.Should().Throw<ReportingStateCorruptionException>()
            .Which.StatePath.Should().Be(fixture.SnapshotPath);
    }

    [Fact]
    public void DeliveryStore_CorruptSnapshot_BlocksInsteadOfReturningEmptyState()
    {
        using var fixture = CorruptSnapshotFixture.Create("report-pack-deliveries.json");
        var store = new FileReportPackDeliveryRecordStore(
            new ReportPackDeliveryStoreOptions(fixture.SnapshotPath),
            NullLogger<FileReportPackDeliveryRecordStore>.Instance);

        var act = () => store.Load();

        act.Should().Throw<ReportingStateCorruptionException>()
            .Which.StatePath.Should().Be(fixture.SnapshotPath);
    }

    [Fact]
    public void DeliveryStore_BlobDirectorySyncFails_WritesNoSnapshotAndRetrySucceeds()
    {
        var root = Path.Combine(Path.GetTempPath(), "meridian-delivery-dirsync", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var snapshotPath = Path.Combine(root, "report-pack-deliveries.json");
            var store = new FileReportPackDeliveryRecordStore(
                new ReportPackDeliveryStoreOptions(snapshotPath),
                NullLogger<FileReportPackDeliveryRecordStore>.Instance);
            var contents = new Dictionary<string, byte[]> { ["packs/pack-1/report.csv"] = Encoding.UTF8.GetBytes("a,b\n1,2\n") };
            var blobDirectory = $"{snapshotPath}.artifacts";

            using (DirectorySyncFaults.FailIn(blobDirectory))
            {
                var save = () => store.Save([], contents);
                save.Should().Throw<DirectorySyncException>();
                File.Exists(snapshotPath).Should().BeFalse("delivery metadata must not reference a blob that is not durable");

                // The retained blob is reused on retry, and that path must not skip the sync.
                save.Should().Throw<DirectorySyncException>();
                File.Exists(snapshotPath).Should().BeFalse();
            }

            store.Save([], contents);
            File.Exists(snapshotPath).Should().BeTrue();
            Directory.GetFiles(blobDirectory, "*.blob").Should().ContainSingle();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private sealed class CorruptSnapshotFixture : IDisposable
    {
        public const string CorruptJson = "{ this-is-not-json";

        private CorruptSnapshotFixture(string rootDirectory, string snapshotPath)
        {
            RootDirectory = rootDirectory;
            SnapshotPath = snapshotPath;
        }

        public string RootDirectory { get; }

        public string SnapshotPath { get; }

        public static CorruptSnapshotFixture Create(string fileName)
        {
            var root = Path.Combine(Path.GetTempPath(), $"meridian-reporting-corruption-{Guid.NewGuid():N}");
            Directory.CreateDirectory(root);
            var path = Path.Combine(root, fileName);
            File.WriteAllText(path, CorruptJson);
            return new CorruptSnapshotFixture(root, path);
        }

        public void Dispose()
        {
            if (Directory.Exists(RootDirectory))
            {
                Directory.Delete(RootDirectory, recursive: true);
            }
        }
    }
}
