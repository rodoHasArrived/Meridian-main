using System.Text.Json;
using Meridian.Application.Services;
using Meridian.Contracts.Catalog;

namespace Meridian.McpServer.Tests.Tools;

/// <summary>
/// Unit tests for <see cref="StorageTools"/>.
/// Uses a mocked <see cref="IStorageCatalogService"/> and a real
/// <see cref="HistoricalDataQueryService"/> against an empty temporary directory.
/// </summary>
public sealed class StorageToolsTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
    private readonly Mock<IStorageCatalogService> _catalog = new();
    private readonly ILogger<StorageTools> _log = NullLogger<StorageTools>.Instance;

    public StorageToolsTests()
    {
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private StorageTools CreateSut()
    {
        var query = new HistoricalDataQueryService(_tempDir);
        return new StorageTools(_catalog.Object, query, _log);
    }

    [Fact]
    public void GetStorageStats_ReturnsValidJson()
    {
        _catalog.Setup(c => c.GetStatistics()).Returns(new CatalogStatistics
        {
            TotalFiles = 42,
            UniqueSymbols = 3,
            TotalBytes = 1_024_000
        });

        var sut = CreateSut();
        var json = sut.GetStorageStats();

        json.Should().NotBeNullOrWhiteSpace();
        var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("totalFiles").GetInt32().Should().Be(42);
        doc.RootElement.GetProperty("uniqueSymbols").GetInt32().Should().Be(3);
    }

    [Fact]
    public void ListStoredSymbols_EmptyCatalog_ReturnsZeroCount()
    {
        _catalog.Setup(c => c.GetCatalog()).Returns(new StorageCatalog());

        var sut = CreateSut();
        var json = sut.ListStoredSymbols();

        json.Should().NotBeNullOrWhiteSpace();
        var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("count").GetInt32().Should().Be(0);
        doc.RootElement.GetProperty("symbols").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public void ListStoredSymbols_WithSymbols_ReturnsCorrectCount()
    {
        var catalog = new StorageCatalog();
        catalog.Symbols["SPY"] = new SymbolCatalogEntry
        {
            Symbol = "SPY",
            FileCount = 10,
            EventCount = 5000,
            EventTypes = ["bars"],
            Sources = ["stooq"],
            DateRange = new CatalogDateRange
            {
                Earliest = new DateTime(2024, 1, 2),
                Latest = new DateTime(2024, 12, 31)
            }
        };
        catalog.Symbols["AAPL"] = new SymbolCatalogEntry
        {
            Symbol = "AAPL",
            FileCount = 8,
            EventCount = 3000,
            EventTypes = ["bars"],
            Sources = ["yahoo"]
        };

        _catalog.Setup(c => c.GetCatalog()).Returns(catalog);

        var sut = CreateSut();
        var json = sut.ListStoredSymbols();

        var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("count").GetInt32().Should().Be(2);

        var symbols = doc.RootElement.GetProperty("symbols").EnumerateArray().ToArray();
        symbols.Should().HaveCount(2);
        // Symbols should be ordered alphabetically
        symbols[0].GetProperty("symbol").GetString().Should().Be("AAPL");
        symbols[1].GetProperty("symbol").GetString().Should().Be("SPY");
    }

    [Fact]
    public async Task QueryStoredData_MissingSymbol_ReturnsError()
    {
        var sut = CreateSut();
        var json = await sut.QueryStoredData("   ");

        var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("error").GetString().Should().Contain("Symbol is required");
    }

    [Fact]
    public async Task QueryStoredData_InvalidFromDate_ReturnsError()
    {
        var sut = CreateSut();
        var json = await sut.QueryStoredData("SPY", from: "not-a-date");

        var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("error").GetString().Should().Contain("Invalid 'from' date");
    }

    [Fact]
    public async Task QueryStoredData_InvalidToDate_ReturnsError()
    {
        var sut = CreateSut();
        var json = await sut.QueryStoredData("SPY", to: "99-99-99");

        var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("error").GetString().Should().Contain("Invalid 'to' date");
    }

    [Fact]
    public async Task QueryStoredData_EmptyDirectory_ReturnsEmptyResult()
    {
        var sut = CreateSut();
        var json = await sut.QueryStoredData("SPY", from: "2024-01-01", to: "2024-12-31");

        var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("symbol").GetString().Should().Be("SPY");
        doc.RootElement.GetProperty("count").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task QueryStoredData_LargeLimit_IsClampedWithoutError()
    {
        // Even if we request 999999 records, the clamp brings it to 500.
        // With an empty temp dir we get 0 records — we're confirming the
        // clamp doesn't throw and returns a valid JSON envelope.
        var sut = CreateSut();
        var json = await sut.QueryStoredData("SPY", limit: 999999);

        json.Should().NotBeNullOrWhiteSpace();
        var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("count").GetInt32().Should().Be(0);
    }
}
