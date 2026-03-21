using System.Text.Json;
using FluentAssertions;
using Meridian.Application.Backfill;
using Xunit;

namespace Meridian.Tests.Application.Backfill;

public sealed class BackfillStatusStoreTests : IDisposable
{
    private readonly string _testRoot;

    public BackfillStatusStoreTests()
    {
        _testRoot = Path.Combine(Path.GetTempPath(), $"mdc_backfill_store_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testRoot);
    }

    public void Dispose()
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                if (Directory.Exists(_testRoot))
                    Directory.Delete(_testRoot, recursive: true);
                return;
            }
            catch (IOException) when (attempt < 4) { Thread.Sleep(10); }
            catch (UnauthorizedAccessException) when (attempt < 4) { Thread.Sleep(10); }
        }
    }

    [Fact]
    public async Task WriteAsync_CreatesStatusFile()
    {
        var store = new BackfillStatusStore(_testRoot);
        var result = CreateTestResult();

        await store.WriteAsync(result);

        var statusPath = Path.Combine(_testRoot, "_status", "backfill.json");
        File.Exists(statusPath).Should().BeTrue();
    }

    [Fact]
    public async Task WriteAsync_ThenTryRead_RoundTrips()
    {
        var store = new BackfillStatusStore(_testRoot);
        var result = CreateTestResult();

        await store.WriteAsync(result);
        var readBack = store.TryRead();

        readBack.Should().NotBeNull();
        readBack!.Success.Should().Be(result.Success);
        readBack.Provider.Should().Be(result.Provider);
        readBack.BarsWritten.Should().Be(result.BarsWritten);
        readBack.Symbols.Should().BeEquivalentTo(result.Symbols);
    }

    [Fact]
    public void TryRead_WhenNoFile_ReturnsNull()
    {
        var store = new BackfillStatusStore(_testRoot);

        var result = store.TryRead();

        result.Should().BeNull();
    }

    [Fact]
    public async Task TryRead_WithCorruptFile_ReturnsNull()
    {
        var store = new BackfillStatusStore(_testRoot);

        // Write a result first to create the directory structure
        await store.WriteAsync(CreateTestResult());

        // Corrupt the file
        var statusPath = Path.Combine(_testRoot, "_status", "backfill.json");
        await File.WriteAllTextAsync(statusPath, "not valid json {{{");

        var result = store.TryRead();

        result.Should().BeNull();
    }

    [Fact]
    public async Task WriteAsync_CreatesDirectoryStructure()
    {
        var deepPath = Path.Combine(_testRoot, "deep", "nested");
        var store = new BackfillStatusStore(deepPath);

        await store.WriteAsync(CreateTestResult());

        Directory.Exists(Path.Combine(deepPath, "_status")).Should().BeTrue();
    }

    [Fact]
    public async Task WriteAsync_OverwritesPreviousResult()
    {
        var store = new BackfillStatusStore(_testRoot);

        var first = new BackfillResult(true, "stooq", new[] { "SPY" }, null, null, 100,
            DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow);
        var second = new BackfillResult(false, "alpaca", new[] { "AAPL", "MSFT" }, null, null, 0,
            DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow, "API error");

        await store.WriteAsync(first);
        await store.WriteAsync(second);

        var result = store.TryRead();
        result.Should().NotBeNull();
        result!.Provider.Should().Be("alpaca");
        result.Success.Should().BeFalse();
        result.Error.Should().Be("API error");
    }

    [Fact]
    public void Constructor_WithEmptyDataRoot_DefaultsToData()
    {
        // This tests that the constructor doesn't throw with empty/whitespace
        var store = new BackfillStatusStore("");
        store.Should().NotBeNull();
    }

    [Fact]
    public async Task WriteAsync_ProducesValidJson()
    {
        var store = new BackfillStatusStore(_testRoot);
        await store.WriteAsync(CreateTestResult());

        var statusPath = Path.Combine(_testRoot, "_status", "backfill.json");
        var json = await File.ReadAllTextAsync(statusPath);

        // Should be valid JSON and indented
        var act = () => JsonDocument.Parse(json);
        act.Should().NotThrow();

        // Should contain expected properties in camelCase
        json.Should().Contain("\"success\"");
        json.Should().Contain("\"provider\"");
    }

    [Fact]
    public async Task WriteAsync_PreservesDateRange()
    {
        var store = new BackfillStatusStore(_testRoot);
        var from = new DateOnly(2024, 1, 1);
        var to = new DateOnly(2024, 12, 31);
        var result = new BackfillResult(true, "stooq", new[] { "SPY" }, from, to, 252,
            DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow);

        await store.WriteAsync(result);
        var readBack = store.TryRead();

        readBack.Should().NotBeNull();
        readBack!.From.Should().Be(from);
        readBack.To.Should().Be(to);
    }

    #region Helpers

    private static BackfillResult CreateTestResult()
    {
        return new BackfillResult(
            true,
            "stooq",
            new[] { "SPY", "AAPL" },
            new DateOnly(2024, 1, 1),
            new DateOnly(2024, 6, 30),
            500,
            DateTimeOffset.UtcNow.AddMinutes(-2),
            DateTimeOffset.UtcNow
        );
    }

    #endregion
}
