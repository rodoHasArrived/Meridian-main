using FluentAssertions;
using Meridian.Application.Etl;
using Meridian.Contracts.Etl;
using Meridian.Infrastructure.Etl;

namespace Meridian.Tests.Infrastructure.Etl;

public sealed class CsvPartnerFileParserTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "meridian-etl-parser-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task ParseAsync_ParsesCsvRows_AndHonorsCheckpoint()
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, "input.csv");
        await File.WriteAllTextAsync(path, "timestamp,symbol,price,size,venue,sequence,aggressor\n2026-01-01T00:00:00Z,AAPL,100.5,10,XNAS,1,BUY\n2026-01-01T00:00:01Z,AAPL,100.6,11,XNAS,2,SELL\n");

        var parser = new CsvPartnerFileParser(new PartnerSchemaRegistry());
        var staged = new EtlStagedFile
        {
            OriginalPath = path,
            StagedPath = path,
            FileName = "input.csv",
            ChecksumSha256 = "abc123",
            SizeBytes = new FileInfo(path).Length
        };

        var rows = new List<PartnerRecordEnvelope>();
        await foreach (var row in parser.ParseAsync(staged, new EtlCheckpointToken { CurrentFileChecksum = "abc123", CurrentRecordIndex = 1 }))
        {
            rows.Add(row);
        }

        rows.Should().HaveCount(1);
        rows[0].Fields["symbol"].Should().Be("AAPL");
        rows[0].RecordIndex.Should().Be(2);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, true);
    }
}
