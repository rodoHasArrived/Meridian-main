using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.Catalog;
using Meridian.Storage;
using Meridian.Storage.Interfaces;
using Meridian.Storage.Services;
using Xunit;

namespace Meridian.Tests.Storage;

/// <summary>
/// Tests for the StorageCatalogService.
/// </summary>
public sealed class StorageCatalogServiceTests : IDisposable
{
    private readonly string _testDirectory;
    private readonly StorageCatalogService _service;

    public StorageCatalogServiceTests()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), "CatalogTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDirectory);

        var options = new StorageOptions
        {
            RootPath = _testDirectory,
            NamingConvention = FileNamingConvention.BySymbol,
            DatePartition = DatePartition.Daily
        };

        _service = new StorageCatalogService(_testDirectory, options);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDirectory))
        {
            Directory.Delete(_testDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task InitializeAsync_CreatesNewCatalog_WhenNoExistingCatalog()
    {
        // Act
        await _service.InitializeAsync();

        // Assert
        var catalog = _service.GetCatalog();
        catalog.Should().NotBeNull();
        catalog.CatalogVersion.Should().Be("1.0.0");
        catalog.Configuration.Should().NotBeNull();
        catalog.Configuration!.NamingConvention.Should().Be("BySymbol");

        // Verify manifest file was created
        var manifestPath = Path.Combine(_testDirectory, "_catalog", "manifest.json");
        File.Exists(manifestPath).Should().BeTrue();
    }

    [Fact]
    public async Task InitializeAsync_LoadsExistingCatalog_WhenCatalogExists()
    {
        // Arrange
        var catalogDir = Path.Combine(_testDirectory, "_catalog");
        Directory.CreateDirectory(catalogDir);

        var existingCatalog = new StorageCatalog
        {
            CatalogId = "test-catalog-123",
            CatalogVersion = "1.0.0",
            Statistics = new CatalogStatistics { TotalFiles = 42 }
        };

        await File.WriteAllTextAsync(
            Path.Combine(catalogDir, "manifest.json"),
            JsonSerializer.Serialize(existingCatalog));

        // Act
        await _service.InitializeAsync();

        // Assert
        var catalog = _service.GetCatalog();
        catalog.CatalogId.Should().Be("test-catalog-123");
        catalog.Statistics.TotalFiles.Should().Be(42);
    }

    [Fact]
    public async Task UpdateFileEntryAsync_AddsFileToIndex()
    {
        // Arrange
        await _service.InitializeAsync();

        var entry = new IndexedFileEntry
        {
            FileName = "2024-01-15.jsonl",
            RelativePath = "AAPL/Trade/2024-01-15.jsonl",
            Symbol = "AAPL",
            EventType = "Trade",
            Date = new DateTime(2024, 1, 15),
            EventCount = 1000,
            SizeBytes = 50000,
            ChecksumSha256 = "abc123"
        };

        // Act
        await _service.UpdateFileEntryAsync(entry);

        // Assert
        var files = _service.GetFilesForSymbol("AAPL").ToList();
        files.Should().HaveCount(1);
        files[0].EventCount.Should().Be(1000);
    }

    [Fact]
    public async Task GetFilesForSymbol_ReturnsCorrectFiles()
    {
        // Arrange
        await _service.InitializeAsync();

        await _service.UpdateFileEntryAsync(new IndexedFileEntry
        {
            RelativePath = "AAPL/Trade/2024-01-15.jsonl",
            Symbol = "AAPL",
            EventType = "Trade"
        });

        await _service.UpdateFileEntryAsync(new IndexedFileEntry
        {
            RelativePath = "MSFT/Trade/2024-01-15.jsonl",
            Symbol = "MSFT",
            EventType = "Trade"
        });

        await _service.UpdateFileEntryAsync(new IndexedFileEntry
        {
            RelativePath = "AAPL/Quote/2024-01-15.jsonl",
            Symbol = "AAPL",
            EventType = "Quote"
        });

        // Act
        var aaplFiles = _service.GetFilesForSymbol("AAPL").ToList();
        var msftFiles = _service.GetFilesForSymbol("MSFT").ToList();

        // Assert
        aaplFiles.Should().HaveCount(2);
        msftFiles.Should().HaveCount(1);
    }

    [Fact]
    public async Task GetFilesForDateRange_ReturnsCorrectFiles()
    {
        // Arrange
        await _service.InitializeAsync();

        await _service.UpdateFileEntryAsync(new IndexedFileEntry
        {
            RelativePath = "AAPL/Trade/2024-01-15.jsonl",
            Symbol = "AAPL",
            Date = new DateTime(2024, 1, 15)
        });

        await _service.UpdateFileEntryAsync(new IndexedFileEntry
        {
            RelativePath = "AAPL/Trade/2024-01-20.jsonl",
            Symbol = "AAPL",
            Date = new DateTime(2024, 1, 20)
        });

        await _service.UpdateFileEntryAsync(new IndexedFileEntry
        {
            RelativePath = "AAPL/Trade/2024-02-01.jsonl",
            Symbol = "AAPL",
            Date = new DateTime(2024, 2, 1)
        });

        // Act
        var januaryFiles = _service.GetFilesForDateRange(
            new DateTime(2024, 1, 1),
            new DateTime(2024, 1, 31)).ToList();

        // Assert
        januaryFiles.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetFilesForEventType_ReturnsCorrectFiles()
    {
        // Arrange
        await _service.InitializeAsync();

        await _service.UpdateFileEntryAsync(new IndexedFileEntry
        {
            RelativePath = "AAPL/Trade/2024-01-15.jsonl",
            EventType = "Trade"
        });

        await _service.UpdateFileEntryAsync(new IndexedFileEntry
        {
            RelativePath = "AAPL/Quote/2024-01-15.jsonl",
            EventType = "Quote"
        });

        // Act
        var tradeFiles = _service.GetFilesForEventType("Trade").ToList();
        var quoteFiles = _service.GetFilesForEventType("Quote").ToList();

        // Assert
        tradeFiles.Should().HaveCount(1);
        quoteFiles.Should().HaveCount(1);
    }

    [Fact]
    public async Task SearchFiles_WithMultipleCriteria_ReturnsFilteredResults()
    {
        // Arrange
        await _service.InitializeAsync();

        await _service.UpdateFileEntryAsync(new IndexedFileEntry
        {
            RelativePath = "AAPL/Trade/2024-01-15.jsonl",
            Symbol = "AAPL",
            EventType = "Trade",
            Date = new DateTime(2024, 1, 15),
            SizeBytes = 10000
        });

        await _service.UpdateFileEntryAsync(new IndexedFileEntry
        {
            RelativePath = "AAPL/Trade/2024-01-20.jsonl",
            Symbol = "AAPL",
            EventType = "Trade",
            Date = new DateTime(2024, 1, 20),
            SizeBytes = 100000
        });

        await _service.UpdateFileEntryAsync(new IndexedFileEntry
        {
            RelativePath = "MSFT/Trade/2024-01-15.jsonl",
            Symbol = "MSFT",
            EventType = "Trade",
            Date = new DateTime(2024, 1, 15),
            SizeBytes = 20000
        });

        // Act
        var criteria = new CatalogSearchCriteria
        {
            Symbols = new[] { "AAPL" },
            EventTypes = new[] { "Trade" },
            MinSizeBytes = 50000
        };

        var results = _service.SearchFiles(criteria).ToList();

        // Assert
        results.Should().HaveCount(1);
        results[0].SizeBytes.Should().Be(100000);
    }

    [Fact]
    public async Task RemoveFileEntryAsync_RemovesFileFromIndex()
    {
        // Arrange
        await _service.InitializeAsync();

        await _service.UpdateFileEntryAsync(new IndexedFileEntry
        {
            RelativePath = "AAPL/Trade/2024-01-15.jsonl",
            Symbol = "AAPL"
        });

        _service.GetFilesForSymbol("AAPL").Should().HaveCount(1);

        // Act
        await _service.RemoveFileEntryAsync("AAPL/Trade/2024-01-15.jsonl");

        // Assert
        _service.GetFilesForSymbol("AAPL").Should().BeEmpty();
    }

    [Fact]
    public async Task GetStatistics_ReturnsCorrectStatistics()
    {
        // Arrange
        await _service.InitializeAsync();

        await _service.UpdateFileEntryAsync(new IndexedFileEntry
        {
            RelativePath = "AAPL/Trade/2024-01-15.jsonl",
            Symbol = "AAPL",
            EventCount = 1000,
            SizeBytes = 50000
        });

        await _service.UpdateFileEntryAsync(new IndexedFileEntry
        {
            RelativePath = "MSFT/Trade/2024-01-15.jsonl",
            Symbol = "MSFT",
            EventCount = 2000,
            SizeBytes = 100000
        });

        // Act
        var stats = _service.GetStatistics();

        // Assert
        stats.TotalFiles.Should().Be(2);
    }

    [Fact]
    public async Task ScanDirectoryAsync_CreatesDirectoryIndex()
    {
        // Arrange
        await _service.InitializeAsync();

        // Create test data files
        var dataDir = Path.Combine(_testDirectory, "AAPL", "Trade");
        Directory.CreateDirectory(dataDir);
        await File.WriteAllTextAsync(
            Path.Combine(dataDir, "2024-01-15.jsonl"),
            "{\"Symbol\":\"AAPL\",\"Price\":150.0}\n{\"Symbol\":\"AAPL\",\"Price\":151.0}");

        // Act
        var result = await _service.ScanDirectoryAsync("AAPL/Trade");

        // Assert
        result.Success.Should().BeTrue();
        result.FilesScanned.Should().Be(1);
        result.Index.Should().NotBeNull();
        result.Index!.Files.Should().HaveCount(1);

        // Verify _index.json was created
        var indexPath = Path.Combine(dataDir, "_index.json");
        File.Exists(indexPath).Should().BeTrue();
    }

    [Fact]
    public async Task SaveCatalogAsync_PersistsCatalogToDisk()
    {
        // Arrange
        await _service.InitializeAsync();
        await _service.UpdateFileEntryAsync(new IndexedFileEntry
        {
            RelativePath = "AAPL/Trade/2024-01-15.jsonl",
            Symbol = "AAPL",
            EventCount = 1000
        });

        // Act
        await _service.SaveCatalogAsync();

        // Assert
        var manifestPath = Path.Combine(_testDirectory, "_catalog", "manifest.json");
        var json = await File.ReadAllTextAsync(manifestPath);
        var savedCatalog = JsonSerializer.Deserialize<StorageCatalog>(json);

        savedCatalog.Should().NotBeNull();
        savedCatalog!.Symbols.Should().ContainKey("AAPL");
    }
}
