using FluentAssertions;
using Meridian.Contracts.Etl;
using Meridian.Contracts.Pipeline;
using Meridian.DataIntegration.Etl;

namespace Meridian.Tests.DataIntegration.Etl;

public sealed class EtlExportServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "meridian-etl-export-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task ExportAsync_WhenNormalizedExtractRequested_CopiesMatchingStorageFiles()
    {
        var sourceDirectory = Path.Combine(_root, "AAPL");
        Directory.CreateDirectory(sourceDirectory);
        var sourcePath = Path.Combine(sourceDirectory, "AAPL_trade_20260101.jsonl");
        await File.WriteAllTextAsync(sourcePath, "{\"symbol\":\"AAPL\"}");

        var service = new EtlExportService(_root, Array.Empty<ISftpFilePublisher>());
        var result = await service.ExportAsync(
            new IngestionJob { JobId = "job-1" },
            new EtlJobDefinition
            {
                JobId = "job-1",
                FlowDirection = EtlFlowDirection.Export,
                PartnerSchemaId = "partner.trades.csv.v1",
                LogicalSourceName = "partner-a",
                Source = new EtlSourceDefinition { Kind = EtlSourceKind.Local, Location = sourceDirectory },
                Destination = new EtlDestinationDefinition { Kind = EtlDestinationKind.StorageCatalog },
                Symbols = ["AAPL"],
                PublishNormalizedExtract = true
            });

        result.Success.Should().BeTrue();
        var exportRoot = result.ArtifactPaths.Should().ContainSingle().Subject;
        var exportedPath = Path.Combine(exportRoot, "AAPL", "AAPL_trade_20260101.jsonl");
        File.Exists(exportedPath).Should().BeTrue();
        File.ReadAllText(exportedPath).Should().Be("{\"symbol\":\"AAPL\"}");
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
