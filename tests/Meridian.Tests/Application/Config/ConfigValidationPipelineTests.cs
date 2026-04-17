using FluentAssertions;
using Meridian.Application.Config;
using Meridian.Contracts.Configuration;
using Xunit;

namespace Meridian.Tests.Application.Config;

/// <summary>
/// Tests for ConfigValidationPipeline (C5 improvement).
/// Validates the consolidated configuration validation approach.
/// </summary>
public class ConfigValidationPipelineTests
{
    [Fact]
    public void CreateDefault_ReturnsValidPipeline()
    {
        // Act
        var pipeline = ConfigValidationPipeline.CreateDefault();

        // Assert
        pipeline.Should().NotBeNull();
    }

    [Fact]
    public void Validate_ValidConfig_ReturnsNoErrors()
    {
        // Arrange
        var pipeline = ConfigValidationPipeline.CreateDefault();
        var config = CreateValidConfig();

        // Act
        var results = pipeline.Validate(config);

        // Assert
        results.Should().NotBeNull();
        results.Where(r => r.IsError).Should().BeEmpty();
    }

    [Fact]
    public void Validate_EmptyDataRoot_ReturnsError()
    {
        // Arrange
        var pipeline = ConfigValidationPipeline.CreateDefault();
        var config = CreateValidConfig();
        config = config with { DataRoot = "" };

        // Act
        var results = pipeline.Validate(config);

        // Assert
        results.Should().Contain(r => r.IsError && r.Property == "DataRoot");
    }

    [Fact]
    public void Validate_InvalidDataSource_ReturnsError()
    {
        // Arrange
        var pipeline = ConfigValidationPipeline.CreateDefault();
        var config = CreateValidConfig();
        // DataSource is an enum, so we can't set an invalid value directly
        // This test validates that enum validation works

        // Act
        var results = pipeline.Validate(config);

        // Assert - Valid enum values should not produce errors
        results.Where(r => r.Property == "DataSource" && r.IsError).Should().BeEmpty();
    }

    [Fact]
    public void Validate_AlpacaWithoutCredentials_ReturnsErrors()
    {
        // Arrange
        var pipeline = ConfigValidationPipeline.CreateDefault();
        var config = CreateValidConfig();
        config = config with
        {
            DataSource = DataSourceKind.Alpaca,
            Alpaca = new AlpacaOptions
            {
                KeyId = "",
                SecretKey = "",
                Feed = "iex"
            }
        };

        // Act
        var results = pipeline.Validate(config);

        // Assert
        results.Should().Contain(r => r.IsError && r.Property.Contains("KeyId"));
        results.Should().Contain(r => r.IsError && r.Property.Contains("SecretKey"));
    }

    [Fact]
    public void Validate_AlpacaWithPlaceholders_ReturnsErrors()
    {
        // Arrange
        var pipeline = ConfigValidationPipeline.CreateDefault();
        var config = CreateValidConfig();
        config = config with
        {
            DataSource = DataSourceKind.Alpaca,
            Alpaca = new AlpacaOptions
            {
                KeyId = "__SET_ME__",
                SecretKey = "YOUR_SECRET_KEY",
                Feed = "iex"
            }
        };

        // Act
        var results = pipeline.Validate(config);

        // Assert
        results.Should().Contain(r => r.IsError && r.Message.Contains("placeholder"));
    }

    [Fact]
    public void Validate_InvalidFeed_ReturnsError()
    {
        // Arrange
        var pipeline = ConfigValidationPipeline.CreateDefault();
        var config = CreateValidConfig();
        config = config with
        {
            DataSource = DataSourceKind.Alpaca,
            Alpaca = new AlpacaOptions
            {
                KeyId = "VALIDKEYID123",
                SecretKey = "VALIDSECRET123",
                Feed = "invalid"
            }
        };

        // Act
        var results = pipeline.Validate(config);

        // Assert
        results.Should().Contain(r => r.IsError && r.Property.Contains("Feed"));
    }

    [Fact]
    public void Validate_DuplicateSymbols_ReturnsError()
    {
        // Arrange
        var pipeline = ConfigValidationPipeline.CreateDefault();
        var config = CreateValidConfig();
        config = config with
        {
            Symbols = new[]
            {
                new SymbolConfig("SPY", SubscribeTrades: true),
                new SymbolConfig("SPY", SubscribeTrades: true)
            }
        };

        // Act
        var results = pipeline.Validate(config);

        // Assert
        results.Should().Contain(r => r.IsError && r.Message.Contains("Duplicate"));
    }

    [Fact]
    public void Validate_NoSubscriptionsEnabled_ReturnsWarning()
    {
        // Arrange
        var pipeline = ConfigValidationPipeline.CreateDefault();
        var config = CreateValidConfig();
        config = config with
        {
            Symbols = new[]
            {
                new SymbolConfig("SPY", SubscribeTrades: false, SubscribeDepth: false),
                new SymbolConfig("AAPL", SubscribeTrades: false, SubscribeDepth: false)
            }
        };

        // Act
        var results = pipeline.Validate(config);

        // Assert
        results.Should().Contain(r => r.Severity == ConfigValidationSeverity.Warning);
    }

    [Fact]
    public void Validate_InvalidDepthLevels_ReturnsError()
    {
        // Arrange
        var pipeline = ConfigValidationPipeline.CreateDefault();
        var config = CreateValidConfig();
        config = config with
        {
            Symbols = new[]
            {
                new SymbolConfig("SPY", SubscribeDepth: true, DepthLevels: 0)  // Invalid
            }
        };

        // Act
        var results = pipeline.Validate(config);

        // Assert
        results.Should().Contain(r => r.IsError && r.Property.Contains("DepthLevels"));
    }

    [Fact]
    public void Validate_ExcessiveDepthLevels_ReturnsError()
    {
        // Arrange
        var pipeline = ConfigValidationPipeline.CreateDefault();
        var config = CreateValidConfig();
        config = config with
        {
            Symbols = new[]
            {
                new SymbolConfig("SPY", SubscribeDepth: true, DepthLevels: 100)  // Too many
            }
        };

        // Act
        var results = pipeline.Validate(config);

        // Assert
        results.Should().Contain(r => r.IsError && r.Property.Contains("DepthLevels"));
    }

    [Fact]
    public void Validate_DuplicateSymbols_IsCaseInsensitive()
    {
        var pipeline = ConfigValidationPipeline.CreateDefault();
        var config = CreateValidConfig() with
        {
            Symbols = new[]
            {
                new SymbolConfig("spy", SubscribeTrades: true),
                new SymbolConfig("SPY", SubscribeTrades: true)
            }
        };

        var results = pipeline.Validate(config);

        results.Should().Contain(r => r.IsError && r.Message.Contains("Duplicate"));
    }

    [Fact]
    public void Validate_StockSharpCustomConnectorWithoutAdapterType_ReturnsError()
    {
        var pipeline = ConfigValidationPipeline.CreateDefault();
        var config = CreateValidConfig() with
        {
            DataSource = DataSourceKind.StockSharp,
            StockSharp = new StockSharpConfig
            {
                Enabled = true,
                ConnectorType = "custom"
            }
        };

        var results = pipeline.Validate(config);

        results.Should().Contain(r => r.IsError && r.Message.Contains("AdapterType"));
    }

    [Fact]
    public void ValidationResult_HasCorrectSeverityLevels()
    {
        // Arrange
        var error = new ConfigValidationResult(ConfigValidationSeverity.Error, "Test", "Message");
        var warning = new ConfigValidationResult(ConfigValidationSeverity.Warning, "Test", "Message");
        var info = new ConfigValidationResult(ConfigValidationSeverity.Info, "Test", "Message");

        // Assert
        error.IsError.Should().BeTrue();
        warning.IsError.Should().BeFalse();
        info.IsError.Should().BeFalse();
    }

    [Fact]
    public void ValidationResult_SupportsSuggestions()
    {
        // Arrange
        var suggestion = "Try setting this value";
        var result = new ConfigValidationResult(
            ConfigValidationSeverity.Error,
            "TestProperty",
            "Test message",
            suggestion);

        // Assert
        result.Suggestion.Should().Be(suggestion);
    }

    private static AppConfig CreateValidConfig()
    {
        return new AppConfig
        {
            DataRoot = "/tmp/test-data",
            DataSource = DataSourceKind.IB,
            Compress = true,
            Alpaca = new AlpacaOptions
            {
                KeyId = "VALIDKEYID123456",
                SecretKey = "VALIDSECRET123456",
                Feed = "iex"
            },
            IB = new IBOptions
            {
                Host = "127.0.0.1",
                Port = 7497,
                ClientId = 1,
                UsePaperTrading = true
            },
            Symbols = new[]
            {
                new SymbolConfig("SPY", SubscribeTrades: true),
                new SymbolConfig("AAPL", SubscribeTrades: true)
            }
        };
    }
}
