using System.IO.Compression;
using System.Text.Json;
using System.Threading;
using FluentAssertions;
using Meridian.Application.Serialization;
using Meridian.Contracts.Domain.Enums;
using Meridian.Contracts.Domain.Models;
using Meridian.Domain.Events;
using Meridian.Tools;
using Xunit;

namespace Meridian.Tests.Storage;

public sealed class DataValidatorTests : IDisposable
{
    private readonly string _testRoot;
    private readonly DataValidator _validator;

    public DataValidatorTests()
    {
        _testRoot = Path.Combine(Path.GetTempPath(), $"mdc_validator_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testRoot);
        _validator = new DataValidator();
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
            catch (IOException) when (attempt < 4)
            {
                Thread.Sleep(10);
            }
            catch (UnauthorizedAccessException) when (attempt < 4)
            {
                Thread.Sleep(10);
            }
        }
    }

    [Fact]
    public async Task ValidateFileAsync_WithValidEvents_ReturnsValid()
    {
        // Arrange
        var filePath = Path.Combine(_testRoot, "valid_trades.jsonl");
        var events = CreateTradeEvents("SPY", 5);
        await WriteJsonlFileAsync(filePath, events);

        // Act
        var result = await _validator.ValidateFileAsync(filePath);

        // Assert
        result.IsValid.Should().BeTrue();
        result.ValidEvents.Should().Be(5);
        result.InvalidEvents.Should().Be(0);
        result.ParseErrors.Should().Be(0);
        result.Errors.Should().BeEmpty();
        result.FirstTimestamp.Should().NotBeNull();
        result.LastTimestamp.Should().NotBeNull();
    }

    [Fact]
    public async Task ValidateFileAsync_WithMissingFile_ReturnsInvalid()
    {
        // Arrange
        var filePath = Path.Combine(_testRoot, "nonexistent.jsonl");

        // Act
        var result = await _validator.ValidateFileAsync(filePath);

        // Assert
        result.IsValid.Should().BeFalse();
        result.TotalLines.Should().Be(0);
        result.Errors.Should().ContainSingle().Which.Should().Contain("File not found");
    }

    [Fact]
    public async Task ValidateFileAsync_WithInvalidJson_CountsParseErrors()
    {
        // Arrange
        var filePath = Path.Combine(_testRoot, "bad_json.jsonl");
        await File.WriteAllLinesAsync(filePath, new[]
        {
            "this is not json",
            "{invalid json too",
            CreateValidEventJson("SPY", DateTimeOffset.UtcNow)
        });

        // Act
        var result = await _validator.ValidateFileAsync(filePath);

        // Assert
        result.IsValid.Should().BeFalse();
        result.ParseErrors.Should().Be(2);
        result.ValidEvents.Should().Be(1);
    }

    [Fact]
    public async Task ValidateFileAsync_WithMissingTypeField_CountsInvalidEvents()
    {
        // Arrange
        var filePath = Path.Combine(_testRoot, "missing_type.jsonl");
        var json = JsonSerializer.Serialize(new
        {
            symbol = "SPY",
            timestamp = DateTimeOffset.UtcNow
        });
        await File.WriteAllLinesAsync(filePath, new[] { json });

        // Act
        var result = await _validator.ValidateFileAsync(filePath);

        // Assert
        result.IsValid.Should().BeFalse();
        result.InvalidEvents.Should().Be(1);
        result.Errors.Should().ContainSingle().Which.Should().Contain("Missing 'Type' field");
    }

    [Fact]
    public async Task ValidateFileAsync_WithMissingSymbolField_CountsInvalidEvents()
    {
        // Arrange
        var filePath = Path.Combine(_testRoot, "missing_symbol.jsonl");
        var json = JsonSerializer.Serialize(new
        {
            type = "Trade",
            timestamp = DateTimeOffset.UtcNow
        });
        await File.WriteAllLinesAsync(filePath, new[] { json });

        // Act
        var result = await _validator.ValidateFileAsync(filePath);

        // Assert
        result.IsValid.Should().BeFalse();
        result.InvalidEvents.Should().Be(1);
        result.Errors.Should().ContainSingle().Which.Should().Contain("Missing 'Symbol' field");
    }

    [Fact]
    public async Task ValidateFileAsync_WithMissingTimestampField_CountsInvalidEvents()
    {
        // Arrange
        var filePath = Path.Combine(_testRoot, "missing_timestamp.jsonl");
        var json = JsonSerializer.Serialize(new
        {
            type = "Trade",
            symbol = "SPY"
        });
        await File.WriteAllLinesAsync(filePath, new[] { json });

        // Act
        var result = await _validator.ValidateFileAsync(filePath);

        // Assert
        result.IsValid.Should().BeFalse();
        result.InvalidEvents.Should().Be(1);
        result.Errors.Should().ContainSingle().Which.Should().Contain("Missing 'Timestamp' field");
    }

    [Fact]
    public async Task ValidateFileAsync_WithInvalidTimestamp_CountsInvalidEvents()
    {
        // Arrange
        var filePath = Path.Combine(_testRoot, "bad_timestamp.jsonl");
        var json = JsonSerializer.Serialize(new
        {
            type = "Trade",
            symbol = "SPY",
            timestamp = "not-a-date"
        });
        await File.WriteAllLinesAsync(filePath, new[] { json });

        // Act
        var result = await _validator.ValidateFileAsync(filePath);

        // Assert
        result.IsValid.Should().BeFalse();
        result.InvalidEvents.Should().Be(1);
        result.Errors.Should().ContainSingle().Which.Should().Contain("Invalid timestamp format");
    }

    [Fact]
    public async Task ValidateFileAsync_WithUnknownEventType_CountsInvalidEvents()
    {
        // Arrange
        var filePath = Path.Combine(_testRoot, "unknown_type.jsonl");
        var json = JsonSerializer.Serialize(new
        {
            type = "NonExistentEventType",
            symbol = "SPY",
            timestamp = DateTimeOffset.UtcNow
        });
        await File.WriteAllLinesAsync(filePath, new[] { json });

        // Act
        var result = await _validator.ValidateFileAsync(filePath);

        // Assert
        result.IsValid.Should().BeFalse();
        result.InvalidEvents.Should().Be(1);
        result.Errors.Should().ContainSingle().Which.Should().Contain("Unknown event type");
    }

    [Fact]
    public async Task ValidateFileAsync_WithGaps_DetectsGaps()
    {
        // Arrange
        var filePath = Path.Combine(_testRoot, "gaps.jsonl");
        var baseTime = new DateTimeOffset(2025, 1, 15, 10, 0, 0, TimeSpan.Zero);
        var lines = new[]
        {
            CreateValidEventJson("SPY", baseTime),
            CreateValidEventJson("SPY", baseTime.AddMinutes(1)),
            CreateValidEventJson("SPY", baseTime.AddMinutes(10)), // 9 minute gap > 5 min threshold
        };
        await File.WriteAllLinesAsync(filePath, lines);

        // Act
        var result = await _validator.ValidateFileAsync(filePath);

        // Assert
        result.IsValid.Should().BeTrue();
        result.Gaps.Should().HaveCount(1);
        result.Gaps[0].Symbol.Should().Be("SPY");
        result.Gaps[0].Duration.Should().BeGreaterThan(TimeSpan.FromMinutes(5));
    }

    [Fact]
    public async Task ValidateFileAsync_WithNoGaps_ReturnsEmptyGapList()
    {
        // Arrange
        var filePath = Path.Combine(_testRoot, "no_gaps.jsonl");
        var baseTime = new DateTimeOffset(2025, 1, 15, 10, 0, 0, TimeSpan.Zero);
        var lines = new[]
        {
            CreateValidEventJson("SPY", baseTime),
            CreateValidEventJson("SPY", baseTime.AddMinutes(1)),
            CreateValidEventJson("SPY", baseTime.AddMinutes(2)),
        };
        await File.WriteAllLinesAsync(filePath, lines);

        // Act
        var result = await _validator.ValidateFileAsync(filePath);

        // Assert
        result.Gaps.Should().BeEmpty();
    }

    [Fact]
    public async Task ValidateFileAsync_TracksTimestampRange()
    {
        // Arrange
        var filePath = Path.Combine(_testRoot, "timestamps.jsonl");
        var firstTs = new DateTimeOffset(2025, 1, 15, 9, 30, 0, TimeSpan.Zero);
        var lastTs = new DateTimeOffset(2025, 1, 15, 16, 0, 0, TimeSpan.Zero);
        var lines = new[]
        {
            CreateValidEventJson("SPY", firstTs),
            CreateValidEventJson("SPY", firstTs.AddHours(3)),
            CreateValidEventJson("SPY", lastTs),
        };
        await File.WriteAllLinesAsync(filePath, lines);

        // Act
        var result = await _validator.ValidateFileAsync(filePath);

        // Assert
        result.FirstTimestamp.Should().Be(firstTs);
        result.LastTimestamp.Should().Be(lastTs);
    }

    [Fact]
    public async Task ValidateFileAsync_WithEmptyLines_SkipsBlankLines()
    {
        // Arrange
        var filePath = Path.Combine(_testRoot, "empty_lines.jsonl");
        var lines = new[]
        {
            CreateValidEventJson("SPY", DateTimeOffset.UtcNow),
            "",
            "   ",
            CreateValidEventJson("SPY", DateTimeOffset.UtcNow.AddSeconds(1)),
        };
        await File.WriteAllLinesAsync(filePath, lines);

        // Act
        var result = await _validator.ValidateFileAsync(filePath);

        // Assert
        result.IsValid.Should().BeTrue();
        result.ValidEvents.Should().Be(2);
        result.TotalLines.Should().Be(4);
    }

    [Fact]
    public async Task ValidateFileAsync_WithGzipCompression_ReadsCompressedFile()
    {
        // Arrange
        var filePath = Path.Combine(_testRoot, "compressed.jsonl.gz");
        var lines = new[]
        {
            CreateValidEventJson("AAPL", DateTimeOffset.UtcNow),
            CreateValidEventJson("AAPL", DateTimeOffset.UtcNow.AddSeconds(1)),
        };

        await using (var fs = new FileStream(filePath, FileMode.Create))
        await using (var gz = new GZipStream(fs, CompressionMode.Compress))
        await using (var writer = new StreamWriter(gz))
        {
            foreach (var line in lines)
                await writer.WriteLineAsync(line);
        }

        // Act
        var result = await _validator.ValidateFileAsync(filePath);

        // Assert
        result.IsValid.Should().BeTrue();
        result.ValidEvents.Should().Be(2);
    }

    [Fact]
    public async Task ValidateFileAsync_WithMultipleSymbols_TracksGapsPerSymbol()
    {
        // Arrange
        var filePath = Path.Combine(_testRoot, "multi_symbol.jsonl");
        var baseTime = new DateTimeOffset(2025, 1, 15, 10, 0, 0, TimeSpan.Zero);
        var lines = new[]
        {
            CreateValidEventJson("SPY", baseTime),
            CreateValidEventJson("AAPL", baseTime),
            CreateValidEventJson("SPY", baseTime.AddMinutes(10)), // gap for SPY
            CreateValidEventJson("AAPL", baseTime.AddMinutes(2)), // no gap for AAPL
        };
        await File.WriteAllLinesAsync(filePath, lines);

        // Act
        var result = await _validator.ValidateFileAsync(filePath);

        // Assert
        result.Gaps.Should().HaveCount(1);
        result.Gaps[0].Symbol.Should().Be("SPY");
    }

    [Fact]
    public async Task ValidateFileAsync_WithActualSerializedEvents_ValidatesCorrectly()
    {
        // Arrange - use the same serializer as the storage system
        var filePath = Path.Combine(_testRoot, "real_events.jsonl");
        var events = CreateTradeEvents("MSFT", 3);
        await WriteJsonlFileAsync(filePath, events);

        // Act
        var result = await _validator.ValidateFileAsync(filePath);

        // Assert - validator should handle camelCase properties from real serialization
        result.IsValid.Should().BeTrue();
        result.ValidEvents.Should().Be(3);
        result.InvalidEvents.Should().Be(0);
        result.ParseErrors.Should().Be(0);
    }

    [Fact]
    public async Task ValidateFileAsync_SupportsCancellation()
    {
        // Arrange
        var filePath = Path.Combine(_testRoot, "cancel_test.jsonl");
        var lines = Enumerable.Range(0, 100)
            .Select(i => CreateValidEventJson("SPY", DateTimeOffset.UtcNow.AddSeconds(i)));
        await File.WriteAllLinesAsync(filePath, lines);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => _validator.ValidateFileAsync(filePath, cts.Token));
    }

    [Fact]
    public async Task ValidateDirectoryAsync_FindsAllJsonlFiles()
    {
        // Arrange
        var subDir = Path.Combine(_testRoot, "sub");
        Directory.CreateDirectory(subDir);

        await File.WriteAllLinesAsync(
            Path.Combine(_testRoot, "file1.jsonl"),
            new[] { CreateValidEventJson("SPY", DateTimeOffset.UtcNow) });
        await File.WriteAllLinesAsync(
            Path.Combine(subDir, "file2.jsonl"),
            new[] { CreateValidEventJson("AAPL", DateTimeOffset.UtcNow) });

        // Act
        var results = await _validator.ValidateDirectoryAsync(_testRoot);

        // Assert
        results.Should().HaveCount(2);
        results.Should().OnlyContain(r => r.IsValid);
    }

    [Fact]
    public async Task ValidateDirectoryAsync_WithNonRecursive_SkipsSubdirectories()
    {
        // Arrange
        var subDir = Path.Combine(_testRoot, "sub");
        Directory.CreateDirectory(subDir);

        await File.WriteAllLinesAsync(
            Path.Combine(_testRoot, "file1.jsonl"),
            new[] { CreateValidEventJson("SPY", DateTimeOffset.UtcNow) });
        await File.WriteAllLinesAsync(
            Path.Combine(subDir, "file2.jsonl"),
            new[] { CreateValidEventJson("AAPL", DateTimeOffset.UtcNow) });

        // Act
        var results = await _validator.ValidateDirectoryAsync(_testRoot, recursive: false);

        // Assert
        results.Should().HaveCount(1);
    }

    [Fact]
    public async Task ValidateDirectoryAsync_WithNonexistentDirectory_ReturnsEmpty()
    {
        // Arrange
        var badPath = Path.Combine(_testRoot, "nonexistent");

        // Act
        var results = await _validator.ValidateDirectoryAsync(badPath);

        // Assert
        results.Should().BeEmpty();
    }

    [Fact]
    public async Task ValidateDirectoryAsync_FindsGzipFiles()
    {
        // Arrange
        var gzPath = Path.Combine(_testRoot, "data.jsonl.gz");
        await using (var fs = new FileStream(gzPath, FileMode.Create))
        await using (var gz = new GZipStream(fs, CompressionMode.Compress))
        await using (var writer = new StreamWriter(gz))
        {
            await writer.WriteLineAsync(CreateValidEventJson("SPY", DateTimeOffset.UtcNow));
        }

        // Act
        var results = await _validator.ValidateDirectoryAsync(_testRoot);

        // Assert
        results.Should().HaveCount(1);
        results[0].IsValid.Should().BeTrue();
    }

    [Fact]
    public void GenerateSummary_ComputesCorrectAggregates()
    {
        // Arrange
        var ts1 = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var ts2 = new DateTimeOffset(2025, 6, 15, 0, 0, 0, TimeSpan.Zero);

        var results = new[]
        {
            new DataValidator.ValidationResult(
                "file1.jsonl", true, 10, 10, 0, 0,
                new List<string>(), new List<DataValidator.GapInfo>(), ts1, ts1.AddHours(6)),
            new DataValidator.ValidationResult(
                "file2.jsonl", false, 20, 15, 3, 2,
                new List<string> { "err1", "err2", "err3", "err4", "err5" },
                new List<DataValidator.GapInfo>
                {
                    new("SPY", ts2, ts2.AddMinutes(10), TimeSpan.FromMinutes(10))
                },
                ts2, ts2.AddHours(8)),
        };

        // Act
        var summary = DataValidator.GenerateSummary(results);

        // Assert
        summary.TotalFiles.Should().Be(2);
        summary.ValidFiles.Should().Be(1);
        summary.InvalidFiles.Should().Be(1);
        summary.TotalEvents.Should().Be(25);
        summary.TotalErrors.Should().Be(5);
        summary.TotalGaps.Should().Be(1);
        summary.EarliestTimestamp.Should().Be(ts1);
        summary.LatestTimestamp.Should().Be(ts2.AddHours(8));
    }

    [Fact]
    public void GenerateSummary_WithEmptyResults_ReturnsZeroes()
    {
        // Act
        var summary = DataValidator.GenerateSummary(Array.Empty<DataValidator.ValidationResult>());

        // Assert
        summary.TotalFiles.Should().Be(0);
        summary.ValidFiles.Should().Be(0);
        summary.TotalEvents.Should().Be(0);
    }

    [Fact]
    public async Task ValidateFileAsync_WithMixedValidAndInvalidLines_ReportsCorrectCounts()
    {
        // Arrange
        var filePath = Path.Combine(_testRoot, "mixed.jsonl");
        var lines = new[]
        {
            CreateValidEventJson("SPY", DateTimeOffset.UtcNow),
            "not json",
            CreateValidEventJson("SPY", DateTimeOffset.UtcNow.AddSeconds(1)),
            JsonSerializer.Serialize(new { type = "Trade", symbol = "SPY" }), // missing timestamp
            CreateValidEventJson("AAPL", DateTimeOffset.UtcNow.AddSeconds(2)),
        };
        await File.WriteAllLinesAsync(filePath, lines);

        // Act
        var result = await _validator.ValidateFileAsync(filePath);

        // Assert
        result.IsValid.Should().BeFalse();
        result.ValidEvents.Should().Be(3);
        result.ParseErrors.Should().Be(1);
        result.InvalidEvents.Should().Be(1);
        result.TotalLines.Should().Be(5);
    }

    #region Helpers

    private static List<MarketEvent> CreateTradeEvents(string symbol, int count)
    {
        var events = new List<MarketEvent>();
        var baseTime = new DateTimeOffset(2025, 1, 15, 10, 0, 0, TimeSpan.Zero);

        for (int i = 0; i < count; i++)
        {
            var trade = new Trade(
                baseTime.AddSeconds(i),
                symbol,
                100m + i,
                100,
                AggressorSide.Buy,
                i);

            events.Add(MarketEvent.Trade(baseTime.AddSeconds(i), symbol, trade, i, "TEST"));
        }

        return events;
    }

    private static async Task WriteJsonlFileAsync(string filePath, IEnumerable<MarketEvent> events)
    {
        var lines = events.Select(e =>
            JsonSerializer.Serialize(e, MarketDataJsonContext.HighPerformanceOptions));
        await File.WriteAllLinesAsync(filePath, lines);
    }

    private static string CreateValidEventJson(string symbol, DateTimeOffset timestamp)
    {
        return JsonSerializer.Serialize(new
        {
            type = "Trade",
            symbol,
            timestamp,
            sequence = 1L,
            source = "TEST",
            schemaVersion = 1
        });
    }

    #endregion
}
