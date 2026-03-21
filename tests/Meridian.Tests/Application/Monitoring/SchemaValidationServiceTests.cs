using FluentAssertions;
using Meridian.Application.Monitoring;
using Meridian.Contracts.Domain.Enums;
using Meridian.Contracts.Domain.Models;
using Meridian.Domain.Events;
using Xunit;
using ContractPayload = Meridian.Contracts.Domain.Events.MarketEventPayload;

namespace Meridian.Tests.Monitoring;

public sealed class SchemaValidationServiceTests : IAsyncDisposable
{
    private readonly string _tempDir;
    private readonly SchemaValidationService _service;

    public SchemaValidationServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"schema-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        _service = new SchemaValidationService(
            new SchemaValidationOptions
            {
                EnableVersionTracking = true,
                MaxFilesToCheck = 10
            },
            _tempDir);
    }

    [Fact]
    public void CurrentSchemaVersion_ShouldMatchEventSchemaValidator()
    {
        // Assert
        SchemaValidationService.CurrentSchemaVersion.Should().Be(EventSchemaValidator.CurrentSchemaVersion);
    }

    [Fact]
    public void CurrentSemanticVersion_ShouldFollowPattern()
    {
        // Assert
        SchemaValidationService.CurrentSemanticVersion.Should().Be($"{EventSchemaValidator.CurrentSchemaVersion}.0.0");
    }

    [Fact]
    public void Validate_WithValidEvent_ShouldNotThrow()
    {
        // Arrange
        var evt = MarketEvent.Heartbeat(DateTimeOffset.UtcNow);

        // Act
        var act = () => _service.Validate(evt);

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void Validate_WithInvalidTimestamp_ShouldThrow()
    {
        // Arrange
        var evt = new MarketEvent(
            default, // Invalid timestamp
            "AAPL",
            MarketEventType.Heartbeat,
            new ContractPayload.HeartbeatPayload());

        // Act
        var act = () => _service.Validate(evt);

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*timestamp*");
    }

    [Fact]
    public void Validate_WithEmptySymbol_ShouldThrow()
    {
        // Arrange
        var evt = new MarketEvent(
            DateTimeOffset.UtcNow,
            "", // Invalid symbol
            MarketEventType.Heartbeat,
            new ContractPayload.HeartbeatPayload());

        // Act
        var act = () => _service.Validate(evt);

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*symbol*");
    }

    [Fact]
    public void Validate_WithUnknownEventType_ShouldThrow()
    {
        // Arrange
        var evt = new MarketEvent(
            DateTimeOffset.UtcNow,
            "AAPL",
            MarketEventType.Unknown, // Invalid type
            new ContractPayload.HeartbeatPayload());

        // Act
        var act = () => _service.Validate(evt);

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*type*");
    }

    [Fact]
    public void ValidateSafe_WithValidEvent_ShouldReturnSuccess()
    {
        // Arrange
        var evt = MarketEvent.Heartbeat(DateTimeOffset.UtcNow);

        // Act
        var result = _service.ValidateSafe(evt);

        // Assert
        result.IsValid.Should().BeTrue();
        result.Error.Should().BeNull();
    }

    [Fact]
    public void ValidateSafe_WithInvalidEvent_ShouldReturnFailure()
    {
        // Arrange
        var evt = new MarketEvent(
            default,
            "AAPL",
            MarketEventType.Heartbeat,
            new ContractPayload.HeartbeatPayload());

        // Act
        var result = _service.ValidateSafe(evt);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Error.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task PerformStartupCheckAsync_WithEmptyDirectory_ShouldPass()
    {
        // Act
        var result = await _service.PerformStartupCheckAsync();

        // Assert
        result.Success.Should().BeTrue();
        result.HasIncompatibilities.Should().BeFalse();
    }

    [Fact]
    public async Task PerformStartupCheckAsync_WithValidJsonlFile_ShouldPass()
    {
        // Arrange
        var testFile = Path.Combine(_tempDir, "test.jsonl");
        var validEvent = $"{{\"Timestamp\":\"{DateTimeOffset.UtcNow:O}\",\"Symbol\":\"AAPL\",\"Type\":1,\"SchemaVersion\":{EventSchemaValidator.CurrentSchemaVersion}}}";
        await File.WriteAllTextAsync(testFile, validEvent);

        // Act
        var result = await _service.PerformStartupCheckAsync();

        // Assert
        result.Success.Should().BeTrue();
        result.HasIncompatibilities.Should().BeFalse();
    }

    [Fact]
    public async Task PerformStartupCheckAsync_WithIncompatibleSchemaVersion_ShouldFail()
    {
        // Arrange
        var testFile = Path.Combine(_tempDir, "test.jsonl");
        var invalidEvent = "{\"Timestamp\":\"2025-01-01T00:00:00Z\",\"Symbol\":\"AAPL\",\"Type\":1,\"SchemaVersion\":999}";
        await File.WriteAllTextAsync(testFile, invalidEvent);

        // Act
        var result = await _service.PerformStartupCheckAsync();

        // Assert
        result.Success.Should().BeFalse();
        result.HasIncompatibilities.Should().BeTrue();
        result.IncompatibleFileCount.Should().Be(1);
        result.Incompatibilities[0].DetectedVersion.Should().Be(999);
        result.Incompatibilities[0].ExpectedVersion.Should().Be(EventSchemaValidator.CurrentSchemaVersion);
    }

    [Fact]
    public async Task PerformStartupCheckAsync_WithMissingSchemaVersion_ShouldFail()
    {
        // Arrange
        var testFile = Path.Combine(_tempDir, "test.jsonl");
        var eventWithoutVersion = "{\"Timestamp\":\"2025-01-01T00:00:00Z\",\"Symbol\":\"AAPL\",\"Type\":1}";
        await File.WriteAllTextAsync(testFile, eventWithoutVersion);

        // Act
        var result = await _service.PerformStartupCheckAsync();

        // Assert
        result.Success.Should().BeFalse();
        result.HasIncompatibilities.Should().BeTrue();
        result.Incompatibilities[0].DetectedVersion.Should().Be(0); // Legacy version
    }

    [Fact]
    public async Task PerformStartupCheckAsync_SecondCallSkipsIfAlreadyCompleted()
    {
        // Arrange
        await _service.PerformStartupCheckAsync();

        // Add an incompatible file after first check
        var testFile = Path.Combine(_tempDir, "late.jsonl");
        var invalidEvent = "{\"Timestamp\":\"2025-01-01T00:00:00Z\",\"Symbol\":\"AAPL\",\"Type\":1,\"SchemaVersion\":999}";
        await File.WriteAllTextAsync(testFile, invalidEvent);

        // Act - second call should skip
        var result = await _service.PerformStartupCheckAsync();

        // Assert - should pass because check was skipped
        result.Success.Should().BeTrue();
        result.Message.Should().Contain("Already completed");
    }

    [Fact]
    public void GetVersionManager_WhenEnabled_ShouldReturnInstance()
    {
        // Act
        var versionManager = _service.GetVersionManager();

        // Assert
        versionManager.Should().NotBeNull();
    }

    [Fact]
    public async Task GetVersionManager_WhenDisabled_ShouldReturnNull()
    {
        // Arrange
        await using var serviceWithoutTracking = new SchemaValidationService(
            new SchemaValidationOptions { EnableVersionTracking = false });

        // Act
        var versionManager = serviceWithoutTracking.GetVersionManager();

        // Assert
        versionManager.Should().BeNull();
    }

    [Fact]
    public void SchemaCheckResult_Success_ShouldHaveCorrectProperties()
    {
        // Act
        var result = SchemaCheckResult.Success();

        // Assert
        result.IsValid.Should().BeTrue();
        result.Error.Should().BeNull();
    }

    [Fact]
    public void SchemaCheckResult_Failed_ShouldHaveCorrectProperties()
    {
        // Act
        var result = SchemaCheckResult.Failed("Test error");

        // Assert
        result.IsValid.Should().BeFalse();
        result.Error.Should().Be("Test error");
    }

    [Fact]
    public void SchemaIncompatibility_Compatible_ShouldHaveCorrectProperties()
    {
        // Act
        var result = SchemaIncompatibility.Compatible("/test/file.jsonl");

        // Assert
        result.FilePath.Should().Be("/test/file.jsonl");
        result.IsCompatible.Should().BeTrue();
        result.DetectedVersion.Should().Be(EventSchemaValidator.CurrentSchemaVersion);
        result.ExpectedVersion.Should().Be(EventSchemaValidator.CurrentSchemaVersion);
        result.CanMigrate.Should().BeFalse();
    }

    [Fact]
    public void StartupSchemaCheckResult_WithIncompatibilities_ShouldCalculateCount()
    {
        // Arrange
        var incompatibilities = new[]
        {
            new SchemaIncompatibility("/file1.jsonl", 0, 1, false, true),
            new SchemaIncompatibility("/file2.jsonl", 0, 1, false, true),
            SchemaIncompatibility.Compatible("/file3.jsonl")
        };

        // Act
        var result = new StartupSchemaCheckResult(false, "Test", incompatibilities);

        // Assert
        result.HasIncompatibilities.Should().BeTrue();
        result.IncompatibleFileCount.Should().Be(2);
    }

    public async ValueTask DisposeAsync()
    {
        await _service.DisposeAsync();

        if (Directory.Exists(_tempDir))
        {
            try
            {
                Directory.Delete(_tempDir, recursive: true);
            }
            catch
            {
                // Ignore cleanup errors in tests
            }
        }
    }
}
