using FluentAssertions;
using Meridian.Contracts.Catalog;
using Meridian.Storage;
using Meridian.Storage.Interfaces;
using Meridian.Storage.Query;
using Xunit;

namespace Meridian.Tests.Storage;

public sealed class SqlStatementGuardTests
{
    [Theory]
    [InlineData("SELECT 1")]
    [InlineData("select symbol from meridian_files")]
    [InlineData("WITH x AS (SELECT 1 AS v) SELECT * FROM x")]
    [InlineData("FROM meridian_files SELECT symbol")]
    [InlineData("DESCRIBE meridian_files")]
    [InlineData("SHOW TABLES")]
    [InlineData("EXPLAIN SELECT 1")]
    [InlineData("SUMMARIZE meridian_files")]
    [InlineData("-- leading comment\nSELECT 1")]
    [InlineData("/* block comment */ SELECT 1")]
    [InlineData("SELECT 1;")]
    public void Validate_AllowsReadOnlyQueries(string sql)
    {
        SqlStatementGuard.Validate(sql, maxSqlLength: 20_000).Should().BeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("-- only a comment\n")]
    public void Validate_RejectsEmptyStatements(string? sql)
    {
        SqlStatementGuard.Validate(sql, maxSqlLength: 20_000).Should().NotBeNull();
    }

    [Theory]
    [InlineData("DROP TABLE meridian_files")]
    [InlineData("INSERT INTO meridian_files VALUES ('x')")]
    [InlineData("UPDATE meridian_files SET symbol = 'x'")]
    [InlineData("DELETE FROM meridian_files")]
    [InlineData("CREATE TABLE t (a INT)")]
    [InlineData("PRAGMA memory_limit='1GB'")]
    [InlineData("SET threads=64")]
    [InlineData("INSTALL httpfs")]
    [InlineData("ATTACH 'other.db'")]
    [InlineData("CALL pragma_version()")]
    public void Validate_RejectsWriteAndConfigStatements(string sql)
    {
        SqlStatementGuard.Validate(sql, maxSqlLength: 20_000).Should().NotBeNull();
    }

    [Theory]
    [InlineData("SELECT * FROM x; DROP TABLE x")]
    [InlineData("SELECT 1; SELECT 2")]
    public void Validate_RejectsMultipleStatements(string sql)
    {
        SqlStatementGuard.Validate(sql, maxSqlLength: 20_000).Should().Contain("single statement");
    }

    [Fact]
    public void Validate_RejectsBlockedKeywordsInsideQueries()
    {
        SqlStatementGuard.Validate("SELECT * FROM read_csv('x') COPY something", 20_000)
            .Should().Contain("COPY");
    }

    [Fact]
    public void Validate_RejectsOverlongStatements()
    {
        var sql = "SELECT " + new string('1', 100);

        SqlStatementGuard.Validate(sql, maxSqlLength: 50).Should().Contain("limit");
    }
}

public sealed class DuckDbQueryServiceTests
{
    private sealed class StubCatalogService : IStorageCatalogService
    {
        public List<IndexedFileEntry> Files { get; } = [];

        public StorageCatalog GetCatalog() => new();
        public Task InitializeAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task<CatalogRebuildResult> RebuildCatalogAsync(CatalogRebuildOptions? options = null, IProgress<CatalogRebuildProgress>? progress = null, CancellationToken ct = default) => Task.FromResult(new CatalogRebuildResult());
        public Task UpdateFileEntryAsync(IndexedFileEntry entry, CancellationToken ct = default) => Task.CompletedTask;
        public Task RemoveFileEntryAsync(string relativePath, CancellationToken ct = default) => Task.CompletedTask;
        public Task<DirectoryIndex?> GetDirectoryIndexAsync(string relativePath, CancellationToken ct = default) => Task.FromResult<DirectoryIndex?>(null);
        public Task UpdateDirectoryIndexAsync(DirectoryIndex index, CancellationToken ct = default) => Task.CompletedTask;
        public Task<DirectoryScanResult> ScanDirectoryAsync(string path, bool recursive = false, CancellationToken ct = default) => Task.FromResult(new DirectoryScanResult());
        public CatalogStatistics GetStatistics() => new();
        public Task<CatalogVerificationResult> VerifyIntegrityAsync(CatalogVerificationOptions? options = null, IProgress<CatalogVerificationProgress>? progress = null, CancellationToken ct = default) => Task.FromResult(new CatalogVerificationResult());
        public IEnumerable<IndexedFileEntry> GetFilesForSymbol(string symbol) => Files;
        public IEnumerable<IndexedFileEntry> GetFilesForDateRange(DateTime start, DateTime end) => Files;
        public IEnumerable<IndexedFileEntry> GetFilesForEventType(string eventType) => Files;
        public IEnumerable<IndexedFileEntry> SearchFiles(CatalogSearchCriteria criteria) => Files;
        public Task SaveCatalogAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task ExportCatalogAsync(string outputPath, CatalogExportFormat format = CatalogExportFormat.Json, CancellationToken ct = default) => Task.CompletedTask;
    }

    private static DuckDbQueryService MakeService(StubCatalogService? catalog = null) => new(
        catalog ?? new StubCatalogService(),
        new StorageOptions { RootPath = Path.GetTempPath() });

    [Fact]
    public async Task ExecuteAsync_RunsSimpleSelect()
    {
        var result = await MakeService().ExecuteAsync("SELECT 1 AS answer, 'x' AS label", new DataQueryOptions());

        result.Success.Should().BeTrue();
        result.Columns.Should().Equal("answer", "label");
        result.RowCount.Should().Be(1);
        result.Rows[0][0].Should().Be("1");
        result.Rows[0][1].Should().Be("x");
    }

    [Fact]
    public async Task ExecuteAsync_ExposesCatalogAsMeridianFilesView()
    {
        var catalog = new StubCatalogService();
        catalog.Files.Add(new IndexedFileEntry
        {
            FileName = "SPY-Trade.parquet",
            RelativePath = "parquet/SPY/SPY-Trade.parquet",
            Format = "parquet",
            Symbol = "SPY",
            EventType = "Trade",
            Source = "alpaca",
            Date = new DateTime(2026, 6, 15),
            SizeBytes = 1_024,
            EventCount = 5_000
        });
        catalog.Files.Add(new IndexedFileEntry
        {
            FileName = "AAPL-Quote.jsonl",
            RelativePath = "jsonl/AAPL/AAPL-Quote.jsonl",
            Format = "jsonl",
            Symbol = "AAPL",
            EventType = "Quote",
            SizeBytes = 2_048,
            EventCount = 9_000
        });

        var result = await MakeService(catalog).ExecuteAsync(
            "SELECT symbol, event_type, format, event_count FROM meridian_files ORDER BY symbol",
            new DataQueryOptions());

        result.Success.Should().BeTrue();
        result.RowCount.Should().Be(2);
        result.Rows[0].Should().Equal("AAPL", "Quote", "jsonl", "9000");
        result.Rows[1].Should().Equal("SPY", "Trade", "parquet", "5000");
    }

    [Fact]
    public async Task ExecuteAsync_TruncatesAtMaxRows()
    {
        var result = await MakeService().ExecuteAsync(
            "SELECT * FROM range(100)",
            new DataQueryOptions { MaxRows = 10 });

        result.Success.Should().BeTrue();
        result.RowCount.Should().Be(10);
        result.Truncated.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsGuardErrorAsFailedResult()
    {
        var result = await MakeService().ExecuteAsync("DROP TABLE meridian_files", new DataQueryOptions());

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("read-only");
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsSqlErrorsAsFailedResult()
    {
        var result = await MakeService().ExecuteAsync("SELECT * FROM does_not_exist", new DataQueryOptions());

        result.Success.Should().BeFalse();
        result.Error.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task ExecuteAsync_ProjectsNullsAsNullCells()
    {
        var result = await MakeService().ExecuteAsync("SELECT NULL AS empty", new DataQueryOptions());

        result.Success.Should().BeTrue();
        result.Rows[0][0].Should().BeNull();
    }

    [Fact]
    public async Task ExecuteAsync_EscapesQuotesInCatalogValues()
    {
        var catalog = new StubCatalogService();
        catalog.Files.Add(new IndexedFileEntry
        {
            FileName = "odd.parquet",
            RelativePath = "parquet/odd'name.parquet",
            Format = "parquet",
            Symbol = "O'HARE",
            EventType = "Trade",
            SizeBytes = 1,
            EventCount = 1
        });

        var result = await MakeService(catalog).ExecuteAsync(
            "SELECT symbol FROM meridian_files",
            new DataQueryOptions());

        result.Success.Should().BeTrue();
        result.Rows[0][0].Should().Be("O'HARE");
    }
}
