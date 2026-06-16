using FluentAssertions;
using Meridian.Contracts.Configuration;
using Meridian.Core.Config;
using Xunit;

namespace Meridian.Tests.Core.Config;

/// <summary>
/// Tests for ConfigValidationPipeline (C5 improvement).
/// Validates the consolidated configuration validation approach.
/// </summary>
public class ConfigValidationPipelineTests
{
    [Fact]
    public void CreateDefault_ReturnsValidPipeline()
    {
        var pipeline = ConfigValidationPipeline.CreateDefault();

        pipeline.Should().NotBeNull();
    }

    [Fact]
    public void Validate_ValidConfig_ReturnsNoErrors()
    {
        var pipeline = ConfigValidationPipeline.CreateDefault();
        var config = CreateValidConfig();

        var results = pipeline.Validate(config);

        results.Should().NotBeNull();
        results.Where(r => r.IsError).Should().BeEmpty();
    }

    [Fact]
    public void Validate_EmptyDataRoot_ReturnsError()
    {
        var pipeline = ConfigValidationPipeline.CreateDefault();
        var config = CreateValidConfig() with { DataRoot = "" };

        var results = pipeline.Validate(config);

        results.Should().Contain(r => r.IsError && r.Property == "DataRoot");
    }

    [Fact]
    public void Validate_InvalidDataSource_ReturnsError()
    {
        var pipeline = ConfigValidationPipeline.CreateDefault();
        var config = CreateValidConfig();

        var results = pipeline.Validate(config);

        results.Where(r => r.Property == "DataSource" && r.IsError).Should().BeEmpty();
    }

    [Fact]
    public void Validate_AlpacaWithoutCredentials_ReturnsErrors()
    {
        var pipeline = ConfigValidationPipeline.CreateDefault();
        var config = CreateValidConfig() with
        {
            DataSource = DataSourceKind.Alpaca,
            Alpaca = new AlpacaOptions
            {
                KeyId = "",
                SecretKey = "",
                Feed = "iex"
            }
        };

        var results = pipeline.Validate(config);

        results.Should().Contain(r => r.IsError && r.Property.Contains("KeyId"));
        results.Should().Contain(r => r.IsError && r.Property.Contains("SecretKey"));
    }

    [Fact]
    public void Validate_AlpacaWithPlaceholders_ReturnsErrors()
    {
        var pipeline = ConfigValidationPipeline.CreateDefault();
        var config = CreateValidConfig() with
        {
            DataSource = DataSourceKind.Alpaca,
            Alpaca = new AlpacaOptions
            {
                KeyId = "__SET_ME__",
                SecretKey = "YOUR_SECRET_KEY",
                Feed = "iex"
            }
        };

        var results = pipeline.Validate(config);

        results.Should().Contain(r => r.IsError && r.Message.Contains("placeholder"));
    }

    [Fact]
    public void Validate_InvalidFeed_ReturnsError()
    {
        var pipeline = ConfigValidationPipeline.CreateDefault();
        var config = CreateValidConfig() with
        {
            DataSource = DataSourceKind.Alpaca,
            Alpaca = new AlpacaOptions
            {
                KeyId = "VALIDKEYID123",
                SecretKey = "VALIDSECRET123",
                Feed = "invalid"
            }
        };

        var results = pipeline.Validate(config);

        results.Should().Contain(r => r.IsError && r.Property.Contains("Feed"));
    }

    [Fact]
    public void Validate_DuplicateSymbols_ReturnsError()
    {
        var pipeline = ConfigValidationPipeline.CreateDefault();
        var config = CreateValidConfig() with
        {
            Symbols =
            [
                new SymbolConfig("SPY", SubscribeTrades: true),
                new SymbolConfig("SPY", SubscribeTrades: true)
            ]
        };

        var results = pipeline.Validate(config);

        results.Should().Contain(r => r.IsError && r.Message.Contains("Duplicate"));
    }

    [Fact]
    public void Validate_NoSubscriptionsEnabled_ReturnsWarning()
    {
        var pipeline = ConfigValidationPipeline.CreateDefault();
        var config = CreateValidConfig() with
        {
            Symbols =
            [
                new SymbolConfig("SPY", SubscribeTrades: false, SubscribeDepth: false),
                new SymbolConfig("AAPL", SubscribeTrades: false, SubscribeDepth: false)
            ]
        };

        var results = pipeline.Validate(config);

        results.Should().Contain(r => r.Severity == ConfigValidationSeverity.Warning);
    }

    [Fact]
    public void Validate_InvalidDepthLevels_ReturnsError()
    {
        var pipeline = ConfigValidationPipeline.CreateDefault();
        var config = CreateValidConfig() with
        {
            Symbols =
            [
                new SymbolConfig("SPY", SubscribeDepth: true, DepthLevels: 0)
            ]
        };

        var results = pipeline.Validate(config);

        results.Should().Contain(r => r.IsError && r.Property.Contains("DepthLevels"));
    }

    [Fact]
    public void Validate_ExcessiveDepthLevels_ReturnsError()
    {
        var pipeline = ConfigValidationPipeline.CreateDefault();
        var config = CreateValidConfig() with
        {
            Symbols =
            [
                new SymbolConfig("SPY", SubscribeDepth: true, DepthLevels: 100)
            ]
        };

        var results = pipeline.Validate(config);

        results.Should().Contain(r => r.IsError && r.Property.Contains("DepthLevels"));
    }

    [Fact]
    public void Validate_DuplicateSymbols_IsCaseInsensitive()
    {
        var pipeline = ConfigValidationPipeline.CreateDefault();
        var config = CreateValidConfig() with
        {
            Symbols =
            [
                new SymbolConfig("spy", SubscribeTrades: true),
                new SymbolConfig("SPY", SubscribeTrades: true)
            ]
        };

        var results = pipeline.Validate(config);

        results.Should().Contain(r => r.IsError && r.Message.Contains("Duplicate"));
    }

    [Fact]
    public void ValidationResult_HasCorrectSeverityLevels()
    {
        var error = new ConfigValidationResult(ConfigValidationSeverity.Error, "Test", "Message");
        var warning = new ConfigValidationResult(ConfigValidationSeverity.Warning, "Test", "Message");
        var info = new ConfigValidationResult(ConfigValidationSeverity.Info, "Test", "Message");

        error.IsError.Should().BeTrue();
        warning.IsError.Should().BeFalse();
        info.IsError.Should().BeFalse();
    }

    [Fact]
    public void ValidationResult_SupportsSuggestions()
    {
        const string suggestion = "Try setting this value";
        var result = new ConfigValidationResult(
            ConfigValidationSeverity.Error,
            "TestProperty",
            "Test message",
            suggestion);

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
            Symbols =
            [
                new SymbolConfig("SPY", SubscribeTrades: true),
                new SymbolConfig("AAPL", SubscribeTrades: true)
            ]
        };
    }
}
