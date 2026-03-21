using FluentAssertions;
using Meridian.Application.Monitoring;
using Xunit;

namespace Meridian.Tests.Monitoring;

public sealed class ErrorRingBufferTests
{
    [Fact]
    public void Record_ShouldStoreError()
    {
        // Arrange
        var buffer = new ErrorRingBuffer(10);

        // Act
        buffer.Record(new ErrorEntry(
            Id: "ERR-001",
            Timestamp: DateTimeOffset.UtcNow,
            Level: ErrorLevel.Error,
            Source: "TestSource",
            Message: "Test error message",
            ExceptionType: null,
            StackTrace: null,
            Context: "TestContext",
            Symbol: "AAPL",
            Provider: "TestProvider"
        ));

        // Assert
        buffer.Count.Should().Be(1);
    }

    [Fact]
    public void GetRecent_ShouldReturnErrorsInReverseOrder()
    {
        // Arrange
        var buffer = new ErrorRingBuffer(10);
        buffer.Record(new ErrorEntry("ERR-001", DateTimeOffset.UtcNow.AddSeconds(-2), ErrorLevel.Error, "Src", "First", null, null, null, null, null));
        buffer.Record(new ErrorEntry("ERR-002", DateTimeOffset.UtcNow.AddSeconds(-1), ErrorLevel.Error, "Src", "Second", null, null, null, null, null));
        buffer.Record(new ErrorEntry("ERR-003", DateTimeOffset.UtcNow, ErrorLevel.Error, "Src", "Third", null, null, null, null, null));

        // Act
        var recent = buffer.GetRecent(3);

        // Assert
        recent.Should().HaveCount(3);
        recent[0].Id.Should().Be("ERR-003"); // Most recent first
        recent[1].Id.Should().Be("ERR-002");
        recent[2].Id.Should().Be("ERR-001");
    }

    [Fact]
    public void GetRecent_WithLimit_ShouldRespectLimit()
    {
        // Arrange
        var buffer = new ErrorRingBuffer(10);
        for (int i = 0; i < 10; i++)
        {
            buffer.Record(new ErrorEntry($"ERR-{i:D3}", DateTimeOffset.UtcNow, ErrorLevel.Error, "Src", $"Error {i}", null, null, null, null, null));
        }

        // Act
        var recent = buffer.GetRecent(3);

        // Assert
        recent.Should().HaveCount(3);
    }

    [Fact]
    public void Record_WhenBufferFull_ShouldOverwriteOldest()
    {
        // Arrange
        var buffer = new ErrorRingBuffer(3);
        buffer.Record(new ErrorEntry("ERR-001", DateTimeOffset.UtcNow.AddSeconds(-3), ErrorLevel.Error, "Src", "First", null, null, null, null, null));
        buffer.Record(new ErrorEntry("ERR-002", DateTimeOffset.UtcNow.AddSeconds(-2), ErrorLevel.Error, "Src", "Second", null, null, null, null, null));
        buffer.Record(new ErrorEntry("ERR-003", DateTimeOffset.UtcNow.AddSeconds(-1), ErrorLevel.Error, "Src", "Third", null, null, null, null, null));

        // Act - add fourth, should overwrite first
        buffer.Record(new ErrorEntry("ERR-004", DateTimeOffset.UtcNow, ErrorLevel.Error, "Src", "Fourth", null, null, null, null, null));

        // Assert
        buffer.Count.Should().Be(3);
        var recent = buffer.GetRecent(3);
        recent.Select(e => e.Id).Should().NotContain("ERR-001");
        recent.Select(e => e.Id).Should().Contain("ERR-004");
    }

    [Fact]
    public void RecordException_ShouldCreateErrorEntry()
    {
        // Arrange
        var buffer = new ErrorRingBuffer(10);
        var exception = new InvalidOperationException("Test exception");

        // Act
        buffer.Record(exception, context: "TestContext", symbol: "AAPL", provider: "TestProvider");

        // Assert
        var recent = buffer.GetRecent(1);
        recent.Should().HaveCount(1);
        recent[0].Message.Should().Be("Test exception");
        recent[0].ExceptionType.Should().Be("InvalidOperationException");
        recent[0].Level.Should().Be(ErrorLevel.Error);
    }

    [Fact]
    public void RecordWarning_ShouldCreateWarningEntry()
    {
        // Arrange
        var buffer = new ErrorRingBuffer(10);

        // Act
        buffer.RecordWarning("TestSource", "Warning message", symbol: "AAPL");

        // Assert
        var recent = buffer.GetRecent(1);
        recent.Should().HaveCount(1);
        recent[0].Level.Should().Be(ErrorLevel.Warning);
        recent[0].Message.Should().Be("Warning message");
    }

    [Fact]
    public void RecordCritical_ShouldCreateCriticalEntry()
    {
        // Arrange
        var buffer = new ErrorRingBuffer(10);

        // Act
        buffer.RecordCritical("TestSource", "Critical error", symbol: "AAPL");

        // Assert
        var recent = buffer.GetRecent(1);
        recent.Should().HaveCount(1);
        recent[0].Level.Should().Be(ErrorLevel.Critical);
    }

    [Fact]
    public void GetByLevel_ShouldFilterByLevel()
    {
        // Arrange
        var buffer = new ErrorRingBuffer(10);
        buffer.RecordWarning("Src", "Warning 1");
        buffer.Record(new ErrorEntry("ERR-001", DateTimeOffset.UtcNow, ErrorLevel.Error, "Src", "Error 1", null, null, null, null, null));
        buffer.RecordWarning("Src", "Warning 2");
        buffer.RecordCritical("Src", "Critical 1");

        // Act
        var errors = buffer.GetByLevel(ErrorLevel.Error, 10);

        // Assert
        errors.Should().HaveCount(2); // Error and Critical are >= Error
        errors.Should().Contain(e => e.Level == ErrorLevel.Error);
        errors.Should().Contain(e => e.Level == ErrorLevel.Critical);
    }

    [Fact]
    public void GetBySymbol_ShouldFilterBySymbol()
    {
        // Arrange
        var buffer = new ErrorRingBuffer(10);
        buffer.Record(new ErrorEntry("ERR-001", DateTimeOffset.UtcNow, ErrorLevel.Error, "Src", "AAPL error", null, null, null, "AAPL", null));
        buffer.Record(new ErrorEntry("ERR-002", DateTimeOffset.UtcNow, ErrorLevel.Error, "Src", "MSFT error", null, null, null, "MSFT", null));
        buffer.Record(new ErrorEntry("ERR-003", DateTimeOffset.UtcNow, ErrorLevel.Error, "Src", "AAPL error 2", null, null, null, "AAPL", null));

        // Act
        var aaplErrors = buffer.GetBySymbol("AAPL", 10);

        // Assert
        aaplErrors.Should().HaveCount(2);
        aaplErrors.Should().OnlyContain(e => e.Symbol == "AAPL");
    }

    [Fact]
    public void GetStats_ShouldReturnCorrectCounts()
    {
        // Arrange
        var buffer = new ErrorRingBuffer(10);
        buffer.RecordWarning("Src", "Warning 1");
        buffer.RecordWarning("Src", "Warning 2");
        buffer.Record(new ErrorEntry("ERR-001", DateTimeOffset.UtcNow, ErrorLevel.Error, "Src", "Error", null, null, null, null, null));
        buffer.RecordCritical("Src", "Critical");

        // Act
        var stats = buffer.GetStats();

        // Assert
        stats.TotalErrors.Should().Be(4);
        stats.WarningCount.Should().Be(2);
        stats.ErrorCount.Should().Be(1);
        stats.CriticalCount.Should().Be(1);
    }

    [Fact]
    public void Clear_ShouldRemoveAllErrors()
    {
        // Arrange
        var buffer = new ErrorRingBuffer(10);
        for (int i = 0; i < 5; i++)
        {
            buffer.Record(new ErrorEntry($"ERR-{i}", DateTimeOffset.UtcNow, ErrorLevel.Error, "Src", $"Error {i}", null, null, null, null, null));
        }

        // Act
        buffer.Clear();

        // Assert
        buffer.Count.Should().Be(0);
        buffer.GetRecent(10).Should().BeEmpty();
    }

    [Fact]
    public void Capacity_ShouldReturnConfiguredCapacity()
    {
        // Arrange
        var buffer = new ErrorRingBuffer(50);

        // Act & Assert
        buffer.Capacity.Should().Be(50);
    }

    [Fact]
    public void Constructor_WithInvalidCapacity_ShouldThrow()
    {
        // Act & Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => new ErrorRingBuffer(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ErrorRingBuffer(-1));
    }
}
