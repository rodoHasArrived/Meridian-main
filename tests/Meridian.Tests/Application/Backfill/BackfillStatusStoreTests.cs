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
    public async Task WriteAsync_RoundTripsSkippedSymbolsAndValidationSignals()
    {
        var store = new BackfillStatusStore(_testRoot);
        var from = new DateOnly(2024, 1, 1);
        var to = new DateOnly(2024, 1, 31);
        var result = new BackfillResult(
            Success: false,
            Provider: "polygon",
            Symbols: ["SPY", "AAPL"],
            From: from,
            To: to,
            BarsWritten: 2,
            StartedUtc: DateTimeOffset.UtcNow.AddMinutes(-2),
            CompletedUtc: DateTimeOffset.UtcNow,
            Error: "rate limited",
            SkippedSymbols: ["SPY"],
            SymbolValidationSignals:
            [
                SymbolValidationSignal.PassSkipped("SPY", checkpointBarsWritten: 10, coveredThrough: new DateOnly(2024, 1, 15)),
                SymbolValidationSignal.Fail("AAPL", from, to, "429 Too Many Requests")
            ]);

        await store.WriteAsync(result);

        var readBack = store.TryRead();

        readBack.Should().NotBeNull();
        readBack!.SkippedSymbols.Should().Equal("SPY");
        readBack.SymbolValidationSignals.Should().HaveCount(2);
        readBack.SymbolValidationSignals.Should().Contain(signal =>
            signal.Symbol == "SPY"
            && signal.Status == "Pass"
            && signal.CheckpointBarsWritten == 10);
        readBack.SymbolValidationSignals.Should().Contain(signal =>
            signal.Symbol == "AAPL"
            && signal.Status == "Fail"
            && signal.Reason == "429 Too Many Requests");
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

    // -----------------------------------------------------------------------
    // Per-symbol checkpoint tests
    // -----------------------------------------------------------------------

    [Fact]
    public void TryReadSymbolCheckpoints_WhenNoFile_ReturnsNull()
    {
        var store = new BackfillStatusStore(_testRoot);

        var result = store.TryReadSymbolCheckpoints();

        result.Should().BeNull();
    }

    [Fact]
    public async Task WriteSymbolCheckpointAsync_ThenTryRead_RoundTrips()
    {
        var store = new BackfillStatusStore(_testRoot);
        var date = new DateOnly(2024, 6, 30);

        await store.WriteSymbolCheckpointAsync("SPY", date);
        var checkpoints = store.TryReadSymbolCheckpoints();

        checkpoints.Should().NotBeNull();
        checkpoints!.Should().ContainKey("SPY");
        checkpoints["SPY"].Should().Be(date);
    }

    [Fact]
    public async Task WriteSymbolCheckpointAsync_MultipleSymbols_AllPersisted()
    {
        var store = new BackfillStatusStore(_testRoot);

        await store.WriteSymbolCheckpointAsync("SPY", new DateOnly(2024, 6, 30));
        await store.WriteSymbolCheckpointAsync("AAPL", new DateOnly(2024, 3, 31));
        await store.WriteSymbolCheckpointAsync("MSFT", new DateOnly(2024, 12, 31));

        var checkpoints = store.TryReadSymbolCheckpoints();

        checkpoints.Should().NotBeNull();
        checkpoints!.Should().ContainKey("SPY");
        checkpoints.Should().ContainKey("AAPL");
        checkpoints.Should().ContainKey("MSFT");
        checkpoints["SPY"].Should().Be(new DateOnly(2024, 6, 30));
        checkpoints["AAPL"].Should().Be(new DateOnly(2024, 3, 31));
        checkpoints["MSFT"].Should().Be(new DateOnly(2024, 12, 31));
    }

    [Fact]
    public async Task WriteSymbolCheckpointAsync_UpdatesOnlyIfNewer()
    {
        var store = new BackfillStatusStore(_testRoot);
        var later = new DateOnly(2024, 12, 31);
        var earlier = new DateOnly(2024, 3, 31);

        // Write later date first, then try to overwrite with earlier — should keep later
        await store.WriteSymbolCheckpointAsync("SPY", later);
        await store.WriteSymbolCheckpointAsync("SPY", earlier);

        var checkpoints = store.TryReadSymbolCheckpoints();

        checkpoints!["SPY"].Should().Be(later);
    }

    [Fact]
    public async Task WriteSymbolCheckpointAsync_OlderDate_DoesNotOverwriteBarCountSidecar()
    {
        var store = new BackfillStatusStore(_testRoot);
        var later = new DateOnly(2024, 12, 31);
        var earlier = new DateOnly(2024, 6, 30);

        await store.WriteSymbolCheckpointAsync("SPY", later, barsWritten: 25);
        await store.WriteSymbolCheckpointAsync("SPY", earlier, barsWritten: 5);

        var checkpoints = store.TryReadSymbolCheckpoints();
        var barCounts = store.TryReadSymbolBarCounts();

        checkpoints.Should().NotBeNull();
        barCounts.Should().NotBeNull();
        checkpoints!["SPY"].Should().Be(later);
        barCounts!["SPY"].Should().Be(25,
            "older overlapping windows must not regress the sidecar count once a later checkpoint is recorded");
    }

    [Fact]
    public async Task WriteSymbolCheckpointAsync_UpdatesWhenNewer()
    {
        var store = new BackfillStatusStore(_testRoot);
        var first = new DateOnly(2024, 6, 30);
        var extended = new DateOnly(2024, 12, 31);

        await store.WriteSymbolCheckpointAsync("SPY", first);
        await store.WriteSymbolCheckpointAsync("SPY", extended);

        var checkpoints = store.TryReadSymbolCheckpoints();

        checkpoints!["SPY"].Should().Be(extended);
    }

    [Fact]
    public async Task WriteSymbolCheckpointAsync_IsCaseInsensitive()
    {
        var store = new BackfillStatusStore(_testRoot);
        var date = new DateOnly(2024, 6, 30);

        await store.WriteSymbolCheckpointAsync("spy", date);
        var checkpoints = store.TryReadSymbolCheckpoints();

        checkpoints.Should().NotBeNull();
        // Should be readable with different casing
        checkpoints!.Should().ContainKey("SPY");
    }

    [Fact]
    public async Task ClearSymbolCheckpointsAsync_RemovesAllCheckpoints()
    {
        var store = new BackfillStatusStore(_testRoot);

        await store.WriteSymbolCheckpointAsync("SPY", new DateOnly(2024, 6, 30));
        await store.WriteSymbolCheckpointAsync("AAPL", new DateOnly(2024, 3, 31));

        await store.ClearSymbolCheckpointsAsync();

        var checkpoints = store.TryReadSymbolCheckpoints();
        // After clearing the file contains "{}" — an empty dict, not null
        checkpoints.Should().NotBeNull();
        checkpoints!.Should().BeEmpty();
    }

    [Fact]
    public async Task ClearSymbolCheckpointsAsync_WhenNoFile_DoesNotThrow()
    {
        var store = new BackfillStatusStore(_testRoot);

        // Should not throw even when no checkpoints file exists
        var act = async () => await store.ClearSymbolCheckpointsAsync();
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task WriteSymbolCheckpointAsync_IndependentOfAggregateResult()
    {
        var store = new BackfillStatusStore(_testRoot);

        // Writing symbol checkpoints should not affect aggregate result and vice-versa
        await store.WriteSymbolCheckpointAsync("SPY", new DateOnly(2024, 6, 30));
        await store.WriteAsync(CreateTestResult());

        var aggregate = store.TryRead();
        var checkpoints = store.TryReadSymbolCheckpoints();

        aggregate.Should().NotBeNull();
        checkpoints.Should().NotBeNull();
        checkpoints!.Should().ContainKey("SPY");
    }
}
