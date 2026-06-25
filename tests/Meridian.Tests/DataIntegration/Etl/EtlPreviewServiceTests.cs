using FluentAssertions;
using Meridian.Contracts.Etl;
using Meridian.DataIntegration.Etl;
using Meridian.Infrastructure.Etl;
using Meridian.Storage.Etl;

namespace Meridian.Tests.DataIntegration.Etl;

public sealed class EtlPreviewServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "meridian-etl-preview-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task PreviewAsync_ForBankStatementCsv_ReturnsSamplesAndIdempotencyHash()
    {
        Directory.CreateDirectory(_root);
        var input = Path.Combine(_root, "input");
        Directory.CreateDirectory(input);
        await File.WriteAllTextAsync(
            Path.Combine(input, "bank.csv"),
            "transaction_date,value_date,amount,currency,transaction_type,description,reference,closing_balance\n2026-06-05,2026-06-05,-2500.00,USD,ACH,Management fee,FEE-1,948750.25\n");

        var registry = new PartnerSchemaRegistry();
        var service = new EtlPreviewService(
            [new LocalFileSourceReader(new EtlStagingStore(_root))],
            new CsvPartnerFileParser(registry),
            registry);

        var result = await service.PreviewAsync(new EtlJobDefinition
        {
            JobId = "preview-bank",
            FlowDirection = EtlFlowDirection.Import,
            PartnerSchemaId = "bank.statement.csv.v1",
            LogicalSourceName = "bank",
            Source = new EtlSourceDefinition { Kind = EtlSourceKind.Local, Location = input, FilePattern = "*.csv" },
            Destination = new EtlDestinationDefinition { Kind = EtlDestinationKind.StorageCatalog }
        });

        result.Success.Should().BeTrue();
        result.Files.Should().ContainSingle();
        var file = result.Files[0];
        file.Disposition.Should().Be(EtlPreviewDisposition.Ready);
        file.RecordCount.Should().Be(1);
        file.SampleRows.Should().ContainSingle(row => row["reference"] == "FEE-1");
        file.FileHashSha256.Should().NotBeNullOrWhiteSpace();
        file.IdempotencyKey.Should().Contain("bank.statement.csv.v1");
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, true);
    }
}
