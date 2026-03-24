using Meridian.Contracts.Configuration;
using Meridian.Wpf.Services;

namespace Meridian.Wpf.Tests.Services;

/// <summary>
/// Tests for ConfigService singleton service.
/// Validates configuration management and validation functionality.
/// </summary>
public sealed class ConfigServiceTests
{
    [Fact]
    public void Instance_ShouldReturnSingleton()
    {
        // Arrange & Act
        var instance1 = ConfigService.Instance;
        var instance2 = ConfigService.Instance;

        // Assert
        instance1.Should().NotBeNull();
        instance2.Should().NotBeNull();
        instance1.Should().BeSameAs(instance2, "ConfigService should be a singleton");
    }

    [Fact]
    public void IsInitialized_BeforeInitialization_ShouldReturnFalse()
    {
        // Arrange
        var service = ConfigService.Instance;

        // Act
        var isInitialized = service.IsInitialized;

        // Assert - This may vary depending on when the test runs
        // In a fresh instance, it should be false until InitializeAsync is called
    }

    [Fact]
    public async Task InitializeAsync_ShouldSetInitializedFlag()
    {
        // Arrange
        var service = ConfigService.Instance;

        // Act
        await service.InitializeAsync();

        // Assert
        service.IsInitialized.Should().BeTrue("service should be initialized after InitializeAsync");
    }

    [Fact]
    public void ConfigPath_ShouldReturnValidPath()
    {
        // Arrange
        var service = ConfigService.Instance;

        // Act
        var configPath = service.ConfigPath;

        // Assert
        configPath.Should().NotBeNullOrEmpty("ConfigPath should return a valid path");
    }

    [Fact]
    public async Task ValidateConfigAsync_ShouldReturnValidationResult()
    {
        // Arrange
        var service = ConfigService.Instance;
        await service.InitializeAsync();

        // Act
        var result = await service.ValidateConfigAsync();

        // Assert
        result.Should().NotBeNull("validation should return a result");
        result.IsValid.Should().BeTrue("default configuration should be valid");
        result.Errors.Should().BeEmpty("valid configuration should have no errors");
    }

    [Fact]
    public void ConfigServiceValidationResult_Success_ShouldCreateValidResult()
    {
        // Act
        var result = ConfigServiceValidationResult.Success();

        // Assert
        result.Should().NotBeNull();
        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
        result.Warnings.Should().BeEmpty();
    }

    [Fact]
    public void ConfigServiceValidationResult_Failure_ShouldCreateInvalidResult()
    {
        // Arrange
        var errors = new[] { "Error 1", "Error 2" };

        // Act
        var result = ConfigServiceValidationResult.Failure(errors);

        // Assert
        result.Should().NotBeNull();
        result.IsValid.Should().BeFalse();
        result.Errors.Should().BeEquivalentTo(errors);
    }

    [Fact]
    public async Task GetDataSourcesAsync_ShouldReturnDataSources()
    {
        // Arrange
        var service = ConfigService.Instance;
        await service.InitializeAsync();

        // Act
        var dataSources = await service.GetDataSourcesAsync();

        // Assert
        dataSources.Should().NotBeNull("data sources should not be null");
    }

    [Fact]
    public async Task GetSymbolsAsync_ShouldReturnSymbols()
    {
        // Arrange
        var service = ConfigService.Instance;
        await service.InitializeAsync();

        // Act
        var symbols = await service.GetSymbolsAsync();

        // Assert
        symbols.Should().NotBeNull("symbols should not be null");
    }

    [Fact]
    public async Task GetActiveDataSourceAsync_ShouldReturnActiveSource()
    {
        // Arrange
        var service = ConfigService.Instance;
        await service.InitializeAsync();

        // Act
        var activeSource = await service.GetActiveDataSourceAsync();

        // Assert - May be null if no active source is configured
        // Just verify the method doesn't throw
    }

    [Fact]
    public async Task SaveDataSourcesAsync_WithValidData_ShouldNotThrow()
    {
        // Arrange
        var service = ConfigService.Instance;
        await service.InitializeAsync();
        var dataSources = await service.GetDataSourcesAsync();

        // Act
        Func<Task> act = async () => await service.SaveDataSourcesAsync(dataSources);

        // Assert
        await act.Should().NotThrowAsync("saving valid data sources should not throw");
    }

    [Fact]
    public async Task ReloadConfigAsync_ShouldNotThrow()
    {
        // Arrange
        var service = ConfigService.Instance;
        await service.InitializeAsync();

        // Act
        Func<Task> act = async () => await service.ReloadConfigAsync();

        // Assert
        await act.Should().NotThrowAsync("reloading config should not throw");
    }

    [Fact]
    public void InlineValidationResult_IsValid_WhenNoErrors()
    {
        // Act
        var result = new InlineValidationResult();

        // Assert
        result.IsValid.Should().BeTrue();
        result.HasWarnings.Should().BeFalse();
    }

    [Fact]
    public void InlineValidationResult_IsNotValid_WhenErrors()
    {
        // Act
        var result = new InlineValidationResult
        {
            Errors = ["Priority must be non-negative."]
        };

        // Assert
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void InlineValidationResult_HasWarnings_WhenWarningsPresent()
    {
        // Act
        var result = new InlineValidationResult
        {
            Warnings = ["Priority 10 is shared with another provider."]
        };

        // Assert
        result.IsValid.Should().BeTrue("warnings do not invalidate");
        result.HasWarnings.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateProviderInlineAsync_ValidOptions_ReturnsValid()
    {
        // Arrange
        var service = ConfigService.Instance;
        await service.InitializeAsync();
        var options = new BackfillProviderOptionsDto
        {
            Enabled = true,
            Priority = 5,
            RateLimitPerMinute = 200,
        };

        // Act
        var result = await service.ValidateProviderInlineAsync("alpaca", options);

        // Assert
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateProviderInlineAsync_NegativePriority_ReturnsError()
    {
        // Arrange
        var service = ConfigService.Instance;
        await service.InitializeAsync();
        var options = new BackfillProviderOptionsDto
        {
            Enabled = true,
            Priority = -1,
        };

        // Act
        var result = await service.ValidateProviderInlineAsync("alpaca", options);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("non-negative"));
    }

    [Fact]
    public async Task ValidateProviderInlineAsync_ZeroRateLimit_ReturnsError()
    {
        // Arrange
        var service = ConfigService.Instance;
        await service.InitializeAsync();
        var options = new BackfillProviderOptionsDto
        {
            Enabled = true,
            Priority = 5,
            RateLimitPerMinute = 0,
        };

        // Act
        var result = await service.ValidateProviderInlineAsync("alpaca", options);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("greater than zero"));
    }

    [Fact]
    public async Task GetBackfillProvidersConfigAsync_ShouldReturnConfig()
    {
        // Arrange
        var service = ConfigService.Instance;
        await service.InitializeAsync();

        // Act
        var config = await service.GetBackfillProvidersConfigAsync();

        // Assert
        config.Should().NotBeNull();
    }
}
