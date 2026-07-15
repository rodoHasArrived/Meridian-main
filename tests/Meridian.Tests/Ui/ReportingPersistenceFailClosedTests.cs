using FluentAssertions;
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
