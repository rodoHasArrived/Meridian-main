using System.IO.Compression;
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

    [Fact]
    public async Task ExportAsync_WhenBoundedSourceCannotBeDated_PreservesPreviousExtract()
    {
        var sourceDirectory = Path.Combine(_root, "AAPL");
        Directory.CreateDirectory(sourceDirectory);
        await File.WriteAllTextAsync(Path.Combine(sourceDirectory, "trades.jsonl"), "{\"symbol\":\"AAPL\"}");
        var previousExport = Path.Combine(_root, "_etl", "exports", "retry", "normalized", "previous.jsonl");
        Directory.CreateDirectory(Path.GetDirectoryName(previousExport)!);
        await File.WriteAllTextAsync(previousExport, "retained prior extract");
        var service = new EtlExportService(_root, Array.Empty<ISftpFilePublisher>());

        var export = () => service.ExportAsync(new IngestionJob { JobId = "retry" }, new EtlJobDefinition
        {
            JobId = "retry",
            FlowDirection = EtlFlowDirection.Export,
            PartnerSchemaId = "partner.trades.csv.v1",
            LogicalSourceName = "test",
            Source = new EtlSourceDefinition { Kind = EtlSourceKind.Local, Location = sourceDirectory },
            Destination = new EtlDestinationDefinition { Kind = EtlDestinationKind.StorageCatalog },
            PublishNormalizedExtract = true,
            FromDateUtc = new DateTime(2026, 1, 2)
        });

        await export.Should().ThrowAsync<InvalidDataException>();
        (await File.ReadAllTextAsync(previousExport)).Should().Be("retained prior extract");
        Directory.GetDirectories(Path.GetDirectoryName(Path.GetDirectoryName(previousExport)!)!, "normalized.staging-*").Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExportAsync_NormalizedDateRange_FiltersMixedDatesAndUndatedFileNames(bool compressed)
    {
        var directory = Path.Combine(_root, "AAPL", "Trade");
        Directory.CreateDirectory(directory);
        var suffix = compressed ? ".jsonl.gz" : ".jsonl";
        var lines = new[]
        {
            "{\"timestamp\":\"2026-01-01T23:59:59Z\",\"symbol\":\"AAPL\"}",
            "{\"timestamp\":\"2026-01-02T00:00:00Z\",\"symbol\":\"AAPL\"}",
            "{\"timestamp\":\"2026-01-02T23:59:59.999Z\",\"symbol\":\"AAPL\"}",
            "{\"timestamp\":\"2026-01-03T00:00:00Z\",\"symbol\":\"AAPL\"}",
            // Selection uses the UTC date, including events with explicit offsets.
            "{\"timestamp\":\"2026-01-03T01:00:00+02:00\",\"symbol\":\"AAPL\"}"
        };
        foreach (var name in new[] { "mixed", "2026-01-01" })
        {
            await using var file = File.Create(Path.Combine(directory, name + suffix));
            await using var stream = compressed ? new GZipStream(file, CompressionLevel.Fastest) : (Stream)file;
            await using var writer = new StreamWriter(stream);
            await writer.WriteAsync(string.Join('\n', name == "mixed" ? lines : lines.Take(1)));
        }

        var service = new EtlExportService(_root, Array.Empty<ISftpFilePublisher>());
        var previousExport = Path.Combine(_root, "_etl", "exports", "date-range", "normalized", "AAPL", "stale.jsonl");
        Directory.CreateDirectory(Path.GetDirectoryName(previousExport)!);
        await File.WriteAllTextAsync(previousExport, lines[0]);
        var localDestination = Path.Combine(_root, "_etl", "local-destination");
        var staleLocalFile = Path.Combine(localDestination, "normalized", "stale.jsonl");
        Directory.CreateDirectory(Path.GetDirectoryName(staleLocalFile)!);
        await File.WriteAllTextAsync(staleLocalFile, lines[0]);
        var result = await service.ExportAsync(new IngestionJob { JobId = "date-range" }, new EtlJobDefinition
        {
            JobId = "date-range",
            FlowDirection = EtlFlowDirection.Export,
            PartnerSchemaId = "partner.trades.csv.v1",
            LogicalSourceName = "test",
            Source = new EtlSourceDefinition { Kind = EtlSourceKind.Local, Location = directory },
            Destination = new EtlDestinationDefinition { Kind = EtlDestinationKind.Local, Location = localDestination, OverwriteIfExists = true },
            Symbols = ["AAPL"],
            EventTypes = ["Trade"],
            PublishNormalizedExtract = true,
            FromDateUtc = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc),
            ToDateUtc = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc)
        });

        result.Success.Should().BeTrue();
        File.Exists(staleLocalFile).Should().BeFalse();
        Directory.GetFiles(Path.Combine(localDestination, "normalized"), "*", SearchOption.AllDirectories).Should().ContainSingle();
        var exported = Directory.GetFiles(result.ArtifactPaths.Single(), "*", SearchOption.AllDirectories).Should().ContainSingle().Subject;
        exported.Should().EndWith("mixed" + suffix);
        await using var exportedFile = File.OpenRead(exported);
        await using var exportedStream = compressed ? new GZipStream(exportedFile, CompressionMode.Decompress) : (Stream)exportedFile;
        using var reader = new StreamReader(exportedStream);
        (await reader.ReadToEndAsync()).Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.TrimEnd('\r')).Should().Equal(lines[1], lines[2], lines[4]);
    }
}
