using FluentAssertions;
using Meridian.Application.Config;
using Meridian.Infrastructure.Adapters.Core;
using Xunit;

namespace Meridian.Tests.Backfill;

/// <summary>
/// Unit tests for the BackfillWorkerService class.
/// Tests constructor validation for MaxConcurrentRequests bounds.
/// </summary>
public class BackfillWorkerServiceTests
{
    private readonly string _testDataRoot = Path.Combine(Path.GetTempPath(), "BackfillWorkerServiceTests");

    #region Constructor Validation Tests

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-100)]
    public void Constructor_WithZeroOrNegativeMaxConcurrentRequests_ThrowsArgumentOutOfRangeException(int maxConcurrentRequests)
    {
        // Arrange
        var config = new BackfillJobsConfig(MaxConcurrentRequests: maxConcurrentRequests);

        // Act & Assert
        // NOTE: Using null! because validation throws before dependencies are accessed
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() =>
            new BackfillWorkerService(
                null!,
                null!,
                null!,
                null!,
                config,
                _testDataRoot));

        ex.ParamName.Should().Be("config");
        ex.ActualValue.Should().Be(maxConcurrentRequests);
        ex.Message.Should().Contain("MaxConcurrentRequests");
        ex.Message.Should().Contain("1");
        ex.Message.Should().Contain("100");
    }

    [Theory]
    [InlineData(101)]
    [InlineData(200)]
    [InlineData(1000)]
    public void Constructor_WithExcessiveMaxConcurrentRequests_ThrowsArgumentOutOfRangeException(int maxConcurrentRequests)
    {
        // Arrange
        var config = new BackfillJobsConfig(MaxConcurrentRequests: maxConcurrentRequests);

        // Act & Assert
        // NOTE: Using null! because validation throws before dependencies are accessed
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() =>
            new BackfillWorkerService(
                null!,
                null!,
                null!,
                null!,
                config,
                _testDataRoot));

        ex.ParamName.Should().Be("config");
        ex.ActualValue.Should().Be(maxConcurrentRequests);
        ex.Message.Should().Contain("MaxConcurrentRequests");
        ex.Message.Should().Contain("1");
        ex.Message.Should().Contain("100");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(10)]
    [InlineData(50)]
    [InlineData(100)]
    public void Constructor_WithValidMaxConcurrentRequests_DoesNotThrowArgumentOutOfRangeException(int maxConcurrentRequests)
    {
        // Arrange
        var config = new BackfillJobsConfig(MaxConcurrentRequests: maxConcurrentRequests);

        // Act & Assert
        // NOTE: Using null! dependencies - we only verify that ArgumentOutOfRangeException is not thrown
        // The constructor may throw other exceptions (e.g., NullReferenceException) when accessing null dependencies
        var exception = Record.Exception(() =>
        {
            try
            {
                new BackfillWorkerService(
                    null!,
                    null!,
                    null!,
                    null!,
                    config,
                    _testDataRoot);
            }
            catch (ArgumentOutOfRangeException)
            {
                throw; // Re-throw to fail the test
            }
            catch
            {
                // Ignore other exceptions - we only care about validation
            }
        });

        exception.Should().BeNull("valid MaxConcurrentRequests should not throw ArgumentOutOfRangeException");
    }

    [Fact]
    public void Constructor_WithDefaultConfig_DoesNotThrowArgumentOutOfRangeException()
    {
        // Arrange - default config has MaxConcurrentRequests = 3
        var config = new BackfillJobsConfig();
        config.MaxConcurrentRequests.Should().Be(3, "default MaxConcurrentRequests should be 3");

        // Act & Assert
        var exception = Record.Exception(() =>
        {
            try
            {
                new BackfillWorkerService(
                    null!,
                    null!,
                    null!,
                    null!,
                    config,
                    _testDataRoot);
            }
            catch (ArgumentOutOfRangeException)
            {
                throw; // Re-throw to fail the test
            }
            catch
            {
                // Ignore other exceptions - we only care about validation
            }
        });

        exception.Should().BeNull("default config should have valid MaxConcurrentRequests");
    }

    #endregion
}
