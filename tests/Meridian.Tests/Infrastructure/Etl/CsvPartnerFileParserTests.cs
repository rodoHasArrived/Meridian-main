using System.IO.Compression;
using FluentAssertions;
using Meridian.Contracts.Etl;
using Meridian.DataIntegration.Etl;
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

    [Fact]
    public async Task ParseAsync_ParsesExcelWorkbookRows()
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, "input.xlsx");
        CreateWorkbook(path);

        var parser = new CsvPartnerFileParser(new PartnerSchemaRegistry());
        var staged = new EtlStagedFile
        {
            OriginalPath = path,
            StagedPath = path,
            FileName = "input.xlsx",
            ChecksumSha256 = "xlsx123",
            SizeBytes = new FileInfo(path).Length
        };

        var rows = new List<PartnerRecordEnvelope>();
        await foreach (var row in parser.ParseAsync(staged, new EtlCheckpointToken { CurrentFileChecksum = "xlsx123", CurrentRecordIndex = 1 }))
        {
            rows.Add(row);
        }

        rows.Should().HaveCount(1);
        rows[0].PartnerSchemaId.Should().Be("partner.trades.csv.v1");
        rows[0].Fields["symbol"].Should().Be("MSFT");
        rows[0].Fields["price"].Should().Be("401.25");
        rows[0].RecordIndex.Should().Be(2);
    }

    private static void CreateWorkbook(string path)
    {
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        WriteEntry(archive, "xl/workbook.xml", """
            <?xml version="1.0" encoding="UTF-8"?>
            <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
              <sheets><sheet name="Sheet1" sheetId="1" r:id="rId1" /></sheets>
            </workbook>
            """);
        WriteEntry(archive, "xl/_rels/workbook.xml.rels", """
            <?xml version="1.0" encoding="UTF-8"?>
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml" />
            </Relationships>
            """);
        WriteEntry(archive, "xl/sharedStrings.xml", """
            <?xml version="1.0" encoding="UTF-8"?>
            <sst xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
              <si><t>timestamp</t></si><si><t>symbol</t></si><si><t>price</t></si><si><t>size</t></si><si><t>venue</t></si><si><t>sequence</t></si><si><t>aggressor</t></si>
              <si><t>2026-01-01T00:00:00Z</t></si><si><t>AAPL</t></si><si><t>XNAS</t></si><si><t>BUY</t></si>
              <si><t>2026-01-01T00:00:01Z</t></si><si><t>MSFT</t></si><si><t>SELL</t></si>
            </sst>
            """);
        WriteEntry(archive, "xl/worksheets/sheet1.xml", """
            <?xml version="1.0" encoding="UTF-8"?>
            <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
              <sheetData>
                <row r="1"><c r="A1" t="s"><v>0</v></c><c r="B1" t="s"><v>1</v></c><c r="C1" t="s"><v>2</v></c><c r="D1" t="s"><v>3</v></c><c r="E1" t="s"><v>4</v></c><c r="F1" t="s"><v>5</v></c><c r="G1" t="s"><v>6</v></c></row>
                <row r="2"><c r="A2" t="s"><v>7</v></c><c r="B2" t="s"><v>8</v></c><c r="C2"><v>100.5</v></c><c r="D2"><v>10</v></c><c r="E2" t="s"><v>9</v></c><c r="F2"><v>1</v></c><c r="G2" t="s"><v>10</v></c></row>
                <row r="3"><c r="A3" t="s"><v>11</v></c><c r="B3" t="s"><v>12</v></c><c r="C3"><v>401.25</v></c><c r="D3"><v>20</v></c><c r="E3" t="s"><v>9</v></c><c r="F3"><v>2</v></c><c r="G3" t="s"><v>13</v></c></row>
              </sheetData>
            </worksheet>
            """);
    }

    private static void WriteEntry(ZipArchive archive, string name, string content)
    {
        var entry = archive.CreateEntry(name);
        using var writer = new StreamWriter(entry.Open());
        writer.Write(content.Trim());
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, true);
    }
}
