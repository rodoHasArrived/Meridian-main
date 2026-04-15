using FluentAssertions;
using Meridian.Application.Config;
using Xunit;

namespace Meridian.Tests;

public class ConfigValidatorTests
{
    private readonly AppConfigValidator _validator;

    public ConfigValidatorTests()
    {
        _validator = new AppConfigValidator();
    }

    [Fact]
    public void Validate_DefaultConfig_IsValid()
    {
        // Arrange
        var config = new AppConfig();

        // Act
        var result = _validator.Validate(config);

        // Assert
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_EmptyDataRoot_IsInvalid()
    {
        // Arrange
        var config = new AppConfig(DataRoot: "");

        // Act
        var result = _validator.Validate(config);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "DataRoot");
    }

    [Fact]
    public void Validate_AlpacaWithoutApiKeys_IsInvalid()
    {
        // Arrange
        var config = new AppConfig(
            DataSource: DataSourceKind.Alpaca,
            Alpaca: new AlpacaOptions
            {
                KeyId = "",
                SecretKey = ""
            }
        );

        // Act
        var result = _validator.Validate(config);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName.Contains("KeyId"));
    }

    [Fact]
    public void Validate_AlpacaWithPlaceholderKeys_IsInvalid()
    {
        // Arrange
        var config = new AppConfig(
            DataSource: DataSourceKind.Alpaca,
            Alpaca: new AlpacaOptions
            {
                KeyId = "__SET_ME__",
                SecretKey = "__SET_ME__"
            }
        );

        // Act
        var result = _validator.Validate(config);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName.Contains("KeyId"));
    }

    [Fact]
    public void Validate_AlpacaWithValidKeys_IsValid()
    {
        // Arrange
        var config = new AppConfig(
            DataSource: DataSourceKind.Alpaca,
            Alpaca: new AlpacaOptions
            {
                KeyId = "AKXXXXXXXXXXXXXXXX",
                SecretKey = "secretxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx",
                Feed = "iex"
            }
        );

        // Act
        var result = _validator.Validate(config);

        // Assert
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_AlpacaWithInvalidFeed_IsInvalid()
    {
        // Arrange
        var config = new AppConfig(
            DataSource: DataSourceKind.Alpaca,
            Alpaca: new AlpacaOptions
            {
                KeyId = "AKXXXXXXXXXXXXXXXX",
                SecretKey = "secretxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx",
                Feed = "invalid"
            }
        );

        // Act
        var result = _validator.Validate(config);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName.Contains("Feed"));
    }

    [Fact]
    public void Validate_IB_DoesNotRequireAlpacaConfig()
    {
        // Arrange
        var config = new AppConfig(
            DataSource: DataSourceKind.IB,
            IB: new IBOptions(),
            Alpaca: null
        );

        // Act
        var result = _validator.Validate(config);

        // Assert
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void IBOptions_Defaults_ArePaperSafe()
    {
        var options = new IBOptions();

        options.Port.Should().Be(7497);
        options.ClientId.Should().Be(1);
        options.UsePaperTrading.Should().BeTrue();
    }

    [Fact]
    public void ConfigValidationPipeline_WarnsWhenIbIsSetToLiveTrading()
    {
        var pipeline = ConfigValidationPipeline.CreateDefault();
        var config = new AppConfig(
            DataSource: DataSourceKind.IB,
            IB: new IBOptions(UsePaperTrading: false));

        var results = pipeline.Validate(config);

        results.Should().Contain(r =>
            r.Property == "IB.UsePaperTrading" &&
            r.Severity == ConfigValidationSeverity.Warning);
    }

    [Fact]
    public void ConfigValidationPipeline_WarnsWhenLocalClientPortalHttpsDisablesSelfSignedCertificates()
    {
        var pipeline = ConfigValidationPipeline.CreateDefault();
        var config = new AppConfig(
            IBClientPortal: new IBClientPortalOptions(
                Enabled: true,
                BaseUrl: "https://localhost:5000",
                AllowSelfSignedCertificates: false));

        var results = pipeline.Validate(config);

        results.Should().Contain(r =>
            r.Property == "IBClientPortal.AllowSelfSignedCertificates" &&
            r.Severity == ConfigValidationSeverity.Warning);
    }

    [Fact]
    public void Validate_ValidSymbolConfig_IsValid()
    {
        // Arrange
        var config = new AppConfig(
            Symbols: new[]
            {
                new SymbolConfig(
                    Symbol: "SPY",
                    SubscribeTrades: true,
                    SubscribeDepth: true,
                    DepthLevels: 10,
                    SecurityType: "STK",
                    Exchange: "SMART",
                    Currency: "USD"
                )
            }
        );

        // Act
        var result = _validator.Validate(config);

        // Assert
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_EmptySymbol_IsInvalid()
    {
        // Arrange
        var config = new AppConfig(
            Symbols: new[]
            {
                new SymbolConfig(Symbol: "")
            }
        );

        // Act
        var result = _validator.Validate(config);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName.Contains("Symbol"));
    }

    [Fact]
    public void Validate_InvalidDepthLevels_IsInvalid()
    {
        // Arrange
        var config = new AppConfig(
            Symbols: new[]
            {
                new SymbolConfig(
                    Symbol: "SPY",
                    DepthLevels: 100 // Too high
                )
            }
        );

        // Act
        var result = _validator.Validate(config);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName.Contains("DepthLevels"));
    }

    [Fact]
    public void Validate_InvalidSecurityType_IsInvalid()
    {
        // Arrange
        var config = new AppConfig(
            Symbols: new[]
            {
                new SymbolConfig(
                    Symbol: "SPY",
                    SecurityType: "INVALID"
                )
            }
        );

        // Act
        var result = _validator.Validate(config);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName.Contains("SecurityType"));
    }

    [Theory]
    [InlineData("STK")]
    [InlineData("ETF")]
    [InlineData("OPT")]
    [InlineData("IND_OPT")]
    [InlineData("FOP")]
    [InlineData("FUT")]
    [InlineData("SSF")]
    [InlineData("CASH")]
    [InlineData("FOREX")]
    [InlineData("FX")]
    [InlineData("IND")]
    [InlineData("CMDTY")]
    [InlineData("CRYPTO")]
    [InlineData("CFD")]
    [InlineData("BOND")]
    [InlineData("FUND")]
    [InlineData("WAR")]
    [InlineData("BAG")]
    [InlineData("MARGIN")]
    public void Validate_ValidSecurityTypes_AreValid(string securityType)
    {
        // Arrange
        var config = new AppConfig(
            Symbols: new[]
            {
                new SymbolConfig(
                    Symbol: "TEST",
                    SecurityType: securityType
                )
            }
        );

        // Act
        var result = _validator.Validate(config);

        // Assert
        result.Errors.Should().NotContain(e => e.PropertyName.Contains("SecurityType"));
    }
}
