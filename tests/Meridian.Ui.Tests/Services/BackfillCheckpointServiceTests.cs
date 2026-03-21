using FluentAssertions;
using Meridian.Ui.Services;

namespace Meridian.Ui.Tests.Services;

/// <summary>
/// Tests for <see cref="BackfillCheckpointService"/> — singleton access,
/// checkpoint model properties, default initialization, and progress tracking.
/// </summary>
public sealed class BackfillCheckpointServiceTests
{
    // ── Singleton ────────────────────────────────────────────────────

    [Fact]
    public void Instance_ShouldReturnNonNull()
    {
        // Act
        var instance = BackfillCheckpointService.Instance;

        // Assert
        instance.Should().NotBeNull();
    }

    [Fact]
    public void Instance_ShouldReturnSameInstanceOnMultipleCalls()
    {
        // Act
        var instance1 = BackfillCheckpointService.Instance;
        var instance2 = BackfillCheckpointService.Instance;

        // Assert
        instance1.Should().BeSameAs(instance2);
    }

    // ── BackfillCheckpoint model defaults ────────────────────────────

    [Fact]
    public void BackfillCheckpoint_ShouldHaveDefaultValues()
    {
        // Act
        var checkpoint = new BackfillCheckpoint();

        // Assert
        checkpoint.JobId.Should().BeEmpty();
        checkpoint.Provider.Should().BeEmpty();
        checkpoint.FromDate.Should().Be(default(DateTime));
        checkpoint.ToDate.Should().Be(default(DateTime));
        checkpoint.CreatedAt.Should().Be(default(DateTime));
        checkpoint.CompletedAt.Should().BeNull();
        checkpoint.Status.Should().Be(CheckpointStatus.InProgress);
        checkpoint.ErrorMessage.Should().BeNull();
        checkpoint.TotalBarsDownloaded.Should().Be(0);
        checkpoint.SymbolCheckpoints.Should().NotBeNull();
        checkpoint.SymbolCheckpoints.Should().BeEmpty();
    }

    [Fact]
    public void BackfillCheckpoint_CanStoreJobProperties()
    {
        // Arrange
        var now = DateTime.UtcNow;

        // Act
        var checkpoint = new BackfillCheckpoint
        {
            JobId = "job-123",
            Provider = "alpaca",
            FromDate = new DateTime(2024, 1, 1),
            ToDate = new DateTime(2024, 12, 31),
            CreatedAt = now,
            Status = CheckpointStatus.InProgress,
            TotalBarsDownloaded = 500
        };

        // Assert
        checkpoint.JobId.Should().Be("job-123");
        checkpoint.Provider.Should().Be("alpaca");
        checkpoint.FromDate.Should().Be(new DateTime(2024, 1, 1));
        checkpoint.ToDate.Should().Be(new DateTime(2024, 12, 31));
        checkpoint.CreatedAt.Should().Be(now);
        checkpoint.Status.Should().Be(CheckpointStatus.InProgress);
        checkpoint.TotalBarsDownloaded.Should().Be(500);
    }

    [Fact]
    public void BackfillCheckpoint_CompletedAt_CanBeSet()
    {
        // Arrange
        var completedTime = DateTime.UtcNow;

        // Act
        var checkpoint = new BackfillCheckpoint
        {
            CompletedAt = completedTime,
            Status = CheckpointStatus.Completed
        };

        // Assert
        checkpoint.CompletedAt.Should().Be(completedTime);
        checkpoint.Status.Should().Be(CheckpointStatus.Completed);
    }

    [Fact]
    public void BackfillCheckpoint_ErrorMessage_CanBeSet()
    {
        // Act
        var checkpoint = new BackfillCheckpoint
        {
            Status = CheckpointStatus.Failed,
            ErrorMessage = "Connection timeout"
        };

        // Assert
        checkpoint.Status.Should().Be(CheckpointStatus.Failed);
        checkpoint.ErrorMessage.Should().Be("Connection timeout");
    }

    // ── SymbolCheckpoints collection ─────────────────────────────────

    [Fact]
    public void BackfillCheckpoint_SymbolCheckpoints_CanAddMultipleSymbols()
    {
        // Arrange
        var checkpoint = new BackfillCheckpoint();

        // Act
        checkpoint.SymbolCheckpoints.Add(new SymbolCheckpoint { Symbol = "SPY" });
        checkpoint.SymbolCheckpoints.Add(new SymbolCheckpoint { Symbol = "AAPL" });
        checkpoint.SymbolCheckpoints.Add(new SymbolCheckpoint { Symbol = "MSFT" });

        // Assert
        checkpoint.SymbolCheckpoints.Should().HaveCount(3);
    }

    // ── Computed properties ──────────────────────────────────────────

    [Fact]
    public void BackfillCheckpoint_CompletedCount_ShouldCountCompletedSymbols()
    {
        // Arrange
        var checkpoint = new BackfillCheckpoint
        {
            SymbolCheckpoints = new List<SymbolCheckpoint>
            {
                new() { Symbol = "SPY", Status = SymbolCheckpointStatus.Completed },
                new() { Symbol = "AAPL", Status = SymbolCheckpointStatus.Completed },
                new() { Symbol = "MSFT", Status = SymbolCheckpointStatus.Pending }
            }
        };

        // Act & Assert
        checkpoint.CompletedCount.Should().Be(2);
    }

    [Fact]
    public void BackfillCheckpoint_FailedCount_ShouldCountFailedSymbols()
    {
        // Arrange
        var checkpoint = new BackfillCheckpoint
        {
            SymbolCheckpoints = new List<SymbolCheckpoint>
            {
                new() { Symbol = "SPY", Status = SymbolCheckpointStatus.Completed },
                new() { Symbol = "AAPL", Status = SymbolCheckpointStatus.Failed },
                new() { Symbol = "TSLA", Status = SymbolCheckpointStatus.Failed }
            }
        };

        // Act & Assert
        checkpoint.FailedCount.Should().Be(2);
    }

    [Fact]
    public void BackfillCheckpoint_PendingCount_ShouldCountPendingAndFailedSymbols()
    {
        // Arrange
        var checkpoint = new BackfillCheckpoint
        {
            SymbolCheckpoints = new List<SymbolCheckpoint>
            {
                new() { Symbol = "SPY", Status = SymbolCheckpointStatus.Completed },
                new() { Symbol = "AAPL", Status = SymbolCheckpointStatus.Pending },
                new() { Symbol = "MSFT", Status = SymbolCheckpointStatus.Failed },
                new() { Symbol = "TSLA", Status = SymbolCheckpointStatus.Skipped }
            }
        };

        // Act & Assert
        checkpoint.PendingCount.Should().Be(2);
    }

    [Fact]
    public void BackfillCheckpoint_ProgressPercent_ShouldCalculateCorrectly()
    {
        // Arrange
        var checkpoint = new BackfillCheckpoint
        {
            SymbolCheckpoints = new List<SymbolCheckpoint>
            {
                new() { Symbol = "SPY", Status = SymbolCheckpointStatus.Completed },
                new() { Symbol = "AAPL", Status = SymbolCheckpointStatus.Completed },
                new() { Symbol = "MSFT", Status = SymbolCheckpointStatus.Pending },
                new() { Symbol = "TSLA", Status = SymbolCheckpointStatus.Pending }
            }
        };

        // Act & Assert
        checkpoint.ProgressPercent.Should().Be(50.0);
    }

    [Fact]
    public void BackfillCheckpoint_ProgressPercent_WithEmptySymbols_ShouldReturnZero()
    {
        // Arrange
        var checkpoint = new BackfillCheckpoint();

        // Act & Assert
        checkpoint.ProgressPercent.Should().Be(0);
    }

    [Fact]
    public void BackfillCheckpoint_ProgressPercent_AllCompleted_ShouldReturnHundred()
    {
        // Arrange
        var checkpoint = new BackfillCheckpoint
        {
            SymbolCheckpoints = new List<SymbolCheckpoint>
            {
                new() { Symbol = "SPY", Status = SymbolCheckpointStatus.Completed },
                new() { Symbol = "AAPL", Status = SymbolCheckpointStatus.Completed }
            }
        };

        // Act & Assert
        checkpoint.ProgressPercent.Should().Be(100.0);
    }

    // ── SymbolCheckpoint model defaults ──────────────────────────────

    [Fact]
    public void SymbolCheckpoint_ShouldHaveDefaultValues()
    {
        // Act
        var symbolCp = new SymbolCheckpoint();

        // Assert
        symbolCp.Symbol.Should().BeEmpty();
        symbolCp.Status.Should().Be(SymbolCheckpointStatus.Pending);
        symbolCp.BarsDownloaded.Should().Be(0);
        symbolCp.LastProcessedDate.Should().BeNull();
        symbolCp.ErrorMessage.Should().BeNull();
        symbolCp.RetryCount.Should().Be(0);
        symbolCp.LastUpdated.Should().BeNull();
        symbolCp.CompletedAt.Should().BeNull();
    }

    [Fact]
    public void SymbolCheckpoint_CanStoreAllProperties()
    {
        // Arrange
        var now = DateTime.UtcNow;

        // Act
        var symbolCp = new SymbolCheckpoint
        {
            Symbol = "SPY",
            Status = SymbolCheckpointStatus.Downloading,
            BarsDownloaded = 252,
            LastProcessedDate = "2024-06-15",
            ErrorMessage = null,
            RetryCount = 1,
            LastUpdated = now,
            CompletedAt = null
        };

        // Assert
        symbolCp.Symbol.Should().Be("SPY");
        symbolCp.Status.Should().Be(SymbolCheckpointStatus.Downloading);
        symbolCp.BarsDownloaded.Should().Be(252);
        symbolCp.LastProcessedDate.Should().Be("2024-06-15");
        symbolCp.RetryCount.Should().Be(1);
        symbolCp.LastUpdated.Should().Be(now);
    }

    [Fact]
    public void SymbolCheckpoint_CanRecordCompletion()
    {
        // Arrange
        var completedAt = DateTime.UtcNow;

        // Act
        var symbolCp = new SymbolCheckpoint
        {
            Symbol = "AAPL",
            Status = SymbolCheckpointStatus.Completed,
            BarsDownloaded = 500,
            CompletedAt = completedAt
        };

        // Assert
        symbolCp.Status.Should().Be(SymbolCheckpointStatus.Completed);
        symbolCp.CompletedAt.Should().Be(completedAt);
        symbolCp.BarsDownloaded.Should().Be(500);
    }

    [Fact]
    public void SymbolCheckpoint_CanRecordFailure()
    {
        // Act
        var symbolCp = new SymbolCheckpoint
        {
            Symbol = "TSLA",
            Status = SymbolCheckpointStatus.Failed,
            ErrorMessage = "Rate limit exceeded",
            RetryCount = 3
        };

        // Assert
        symbolCp.Status.Should().Be(SymbolCheckpointStatus.Failed);
        symbolCp.ErrorMessage.Should().Be("Rate limit exceeded");
        symbolCp.RetryCount.Should().Be(3);
    }

    // ── CheckpointStatus enum ────────────────────────────────────────

    [Theory]
    [InlineData(CheckpointStatus.InProgress)]
    [InlineData(CheckpointStatus.Completed)]
    [InlineData(CheckpointStatus.PartiallyCompleted)]
    [InlineData(CheckpointStatus.Failed)]
    [InlineData(CheckpointStatus.Cancelled)]
    public void CheckpointStatus_AllValues_ShouldBeDefined(CheckpointStatus status)
    {
        // Act & Assert
        Enum.IsDefined(typeof(CheckpointStatus), status).Should().BeTrue();
    }

    // ── SymbolCheckpointStatus enum ──────────────────────────────────

    [Theory]
    [InlineData(SymbolCheckpointStatus.Pending)]
    [InlineData(SymbolCheckpointStatus.Downloading)]
    [InlineData(SymbolCheckpointStatus.Completed)]
    [InlineData(SymbolCheckpointStatus.Failed)]
    [InlineData(SymbolCheckpointStatus.Skipped)]
    public void SymbolCheckpointStatus_AllValues_ShouldBeDefined(SymbolCheckpointStatus status)
    {
        // Act & Assert
        Enum.IsDefined(typeof(SymbolCheckpointStatus), status).Should().BeTrue();
    }

    // ── Multiple symbol progress scenarios ───────────────────────────

    [Theory]
    [InlineData("SPY")]
    [InlineData("AAPL")]
    [InlineData("MSFT")]
    [InlineData("TSLA")]
    [InlineData("GOOGL")]
    public void SymbolCheckpoint_AcceptsDifferentSymbols(string symbol)
    {
        // Act
        var symbolCp = new SymbolCheckpoint { Symbol = symbol };

        // Assert
        symbolCp.Symbol.Should().Be(symbol);
    }

    [Fact]
    public void BackfillCheckpoint_SupportsLargeNumberOfSymbols()
    {
        // Arrange
        var checkpoint = new BackfillCheckpoint();

        // Act
        for (int i = 0; i < 100; i++)
        {
            checkpoint.SymbolCheckpoints.Add(new SymbolCheckpoint
            {
                Symbol = $"SYM{i}",
                Status = SymbolCheckpointStatus.Pending
            });
        }

        // Assert
        checkpoint.SymbolCheckpoints.Should().HaveCount(100);
        checkpoint.PendingCount.Should().Be(100);
        checkpoint.CompletedCount.Should().Be(0);
        checkpoint.ProgressPercent.Should().Be(0);
    }

    [Fact]
    public void BackfillCheckpoint_ProgressPercent_MixedStatuses_CalculatesCorrectly()
    {
        // Arrange — 3 completed out of 5 total
        var checkpoint = new BackfillCheckpoint
        {
            SymbolCheckpoints = new List<SymbolCheckpoint>
            {
                new() { Symbol = "SPY", Status = SymbolCheckpointStatus.Completed },
                new() { Symbol = "AAPL", Status = SymbolCheckpointStatus.Completed },
                new() { Symbol = "MSFT", Status = SymbolCheckpointStatus.Completed },
                new() { Symbol = "TSLA", Status = SymbolCheckpointStatus.Failed },
                new() { Symbol = "GOOGL", Status = SymbolCheckpointStatus.Downloading }
            }
        };

        // Act & Assert
        checkpoint.ProgressPercent.Should().Be(60.0);
        checkpoint.CompletedCount.Should().Be(3);
        checkpoint.FailedCount.Should().Be(1);
    }
}
