using System.IO.Compression;
using System.Text;
using FluentAssertions;
using Meridian.Application.Services;
using Xunit;

namespace Meridian.Tests.Application.Services;

public sealed class HistoricalDataQueryServiceTests : IDisposable
{
    private readonly string _root;
    private readonly HistoricalDataQueryService _service;

    public HistoricalDataQueryServiceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "meridian-historical-query-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _service = new HistoricalDataQueryService(_root);
    }

    [Fact]
    public async Task QueryAsync_ReadsCurrentCompressedHistoricalBarLayoutWithNumericEventType()
    {
        var symbol = "INDI";
        var path = Path.Combine(_root, symbol, "HistoricalBar", "2026-05-15.jsonl.gz");
        WriteGzipJsonl(path, BuildHistoricalBarJson(symbol, "2026-05-15"));

        var result = await _service.QueryAsync(new HistoricalDataQuery(
            Symbol: symbol,
            From: new DateOnly(2026, 5, 15),
            To: new DateOnly(2026, 5, 15)));

        result.Success.Should().BeTrue();
        result.TotalFiles.Should().Be(1);
        result.FilesProcessed.Should().Be(1);
        result.TotalRecords.Should().Be(1);
        result.Records[0].Symbol.Should().Be(symbol);
        result.Records[0].EventType.Should().Be("HistoricalBar");
    }

    [Fact]
    public void GetAvailableSymbols_ReturnsSymbolRootsAndTopLevelFlatFilesOnly()
    {
        WriteGzipJsonl(
            Path.Combine(_root, "INDI", "HistoricalBar", "2026-05-15.jsonl.gz"),
            BuildHistoricalBarJson("INDI", "2026-05-15"));
        WriteGzipJsonl(
            Path.Combine(_root, "workstation", "activity", "2026-05-15.jsonl.gz"),
            BuildHistoricalBarJson("WORKSTATION", "2026-05-15"));
        File.WriteAllText(
            Path.Combine(_root, "SPY_2026-05-15.jsonl"),
            BuildHistoricalBarJson("SPY", "2026-05-15"));

        var symbols = _service.GetAvailableSymbols();

        symbols.Should().Contain(["INDI", "SPY"]);
        symbols.Should().NotContain(["2026-05-15", "WORKSTATION", "workstation"]);
    }

    private static void WriteGzipJsonl(string path, string line)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var fs = File.Create(path);
        using var gz = new GZipStream(fs, CompressionLevel.Fastest);
        var bytes = Encoding.UTF8.GetBytes(line + "\n");
        gz.Write(bytes, 0, bytes.Length);
    }

    private static string BuildHistoricalBarJson(string symbol, string sessionDate)
    {
        return $$"""
            {"timestamp":"{{sessionDate}}T00:00:00+00:00","symbol":"{{symbol}}","type":8,"payload":{"kind":"historical_bar","symbol":"{{symbol}}","sessionDate":"{{sessionDate}}","open":4.19,"high":4.3,"low":4.08,"close":4.19,"volume":217691,"source":"alpaca","sequenceNumber":739750},"sequence":739750,"source":"alpaca","schemaVersion":1,"effectiveSymbol":"{{symbol}}"}
            """;
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch
        {
            // best effort
        }
    }
}
