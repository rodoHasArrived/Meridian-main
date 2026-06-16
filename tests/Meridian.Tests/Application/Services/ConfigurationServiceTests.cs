using FluentAssertions;
using Meridian.Application.Config;
using Meridian.Core.Config;
using Meridian.Application.Services;
using Meridian.Application.UI;
using Meridian.Infrastructure.Adapters.Alpaca;
using Xunit;

namespace Meridian.Tests.Application.Services;

/// <summary>
/// Tests for ConfigurationService focusing on self-healing fixes,
/// credential resolution, validation, and provider filtering.
/// </summary>
public class ConfigurationServiceTests : IAsyncDisposable
{
    private readonly ConfigurationService _sut;

    public ConfigurationServiceTests()
    {
        _sut = new ConfigurationService();
    }

    public async ValueTask DisposeAsync()
    {
        await _sut.DisposeAsync();
    }

    #region Self-Healing: Empty Symbols

    [Fact]
    public void ApplySelfHealingFixes_EmptySymbols_AddsDefaultSymbol()
    {
        // Arrange
        var config = new AppConfig(Symbols: Array.Empty<SymbolConfig>());

        // Act
        var (fixedConfig, fixes, _) = _sut.ApplySelfHealingFixes(config);

        // Assert
        fixedConfig.Symbols.Should().NotBeNullOrEmpty();
        fixedConfig.Symbols![0].Symbol.Should().Be("SPY");
        fixes.Should().Contain(f => f.Contains("default symbol"));
    }

    [Fact]
    public void ApplySelfHealingFixes_NullSymbols_AddsDefaultSymbol()
    {
        // Arrange
        var config = new AppConfig(Symbols: null);

        // Act
        var (fixedConfig, fixes, _) = _sut.ApplySelfHealingFixes(config);

        // Assert
        fixedConfig.Symbols.Should().NotBeNull();
        fixedConfig.Symbols!.Length.Should().BeGreaterThan(0);
        fixes.Should().Contain(f => f.Contains("default symbol"));
    }

    [Fact]
    public void ApplySelfHealingFixes_ValidSymbols_DoesNotModify()
    {
        // Arrange
        var config = new AppConfig(
            Symbols: new[] { new SymbolConfig("AAPL", SubscribeTrades: true, SubscribeDepth: true, DepthLevels: 10) });

        // Act
        var (fixedConfig, fixes, _) = _sut.ApplySelfHealingFixes(config);

        // Assert
        fixedConfig.Symbols!.Length.Should().Be(1);
        fixedConfig.Symbols[0].Symbol.Should().Be("AAPL");
        fixes.Should().NotContain(f => f.Contains("default symbol"));
    }

    #endregion

    #region Self-Healing: Invalid Depth Levels

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(100)]
    [InlineData(51)]
    public void ApplySelfHealingFixes_InvalidDepthLevels_ClampsToValidRange(int invalidDepth)
    {
        // Arrange
        var config = new AppConfig(
            Symbols: new[] { new SymbolConfig("SPY", SubscribeDepth: true, DepthLevels: invalidDepth) });

        // Act
        var (fixedConfig, fixes, _) = _sut.ApplySelfHealingFixes(config);

        // Assert
        fixedConfig.Symbols![0].DepthLevels.Should().BeInRange(1, 50);
        fixes.Should().Contain(f => f.Contains("depth levels"));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(10)]
    [InlineData(25)]
    [InlineData(50)]
    public void ApplySelfHealingFixes_ValidDepthLevels_DoesNotModify(int validDepth)
    {
        // Arrange
        var config = new AppConfig(
            Symbols: new[] { new SymbolConfig("SPY", SubscribeDepth: true, DepthLevels: validDepth) });

        // Act
        var (fixedConfig, fixes, _) = _sut.ApplySelfHealingFixes(config);

        // Assert
        fixedConfig.Symbols![0].DepthLevels.Should().Be(validDepth);
        fixes.Should().NotContain(f => f.Contains("depth levels"));
    }

    [Fact]
    public void ApplySelfHealingFixes_DepthDisabled_IgnoresInvalidLevels()
    {
        // Arrange - SubscribeDepth is false, so invalid depth levels should be ignored
        var config = new AppConfig(
            Symbols: new[] { new SymbolConfig("SPY", SubscribeDepth: false, DepthLevels: 100) });

        // Act
        var (fixedConfig, fixes, _) = _sut.ApplySelfHealingFixes(config);

        // Assert
        fixedConfig.Symbols![0].DepthLevels.Should().Be(100);
        fixes.Should().NotContain(f => f.Contains("depth levels"));
    }

    #endregion

    #region Self-Healing: Invalid Storage Naming Convention

    [Theory]
    [InlineData("InvalidConvention")]
    [InlineData("random")]
    [InlineData("")]
    public void ApplySelfHealingFixes_InvalidNamingConvention_DefaultsToBySymbol(string invalidConvention)
    {
        // Arrange
        var config = new AppConfig(
            Storage: new StorageConfig(NamingConvention: invalidConvention),
            Symbols: new[] { new SymbolConfig("SPY") });

        // Act
        var (fixedConfig, fixes, _) = _sut.ApplySelfHealingFixes(config);

        // Assert
        fixedConfig.Storage!.NamingConvention.Should().Be("BySymbol");
        fixes.Should().Contain(f => f.Contains("naming convention"));
    }

    [Theory]
    [InlineData("flat")]
    [InlineData("bysymbol")]
    [InlineData("BySymbol")]
    [InlineData("bydate")]
    [InlineData("bytype")]
    [InlineData("hierarchical")]
    [InlineData("canonical")]
    public void ApplySelfHealingFixes_ValidNamingConvention_DoesNotModify(string validConvention)
    {
        // Arrange
        var config = new AppConfig(
            Storage: new StorageConfig(NamingConvention: validConvention),
            Symbols: new[] { new SymbolConfig("SPY") });

        // Act
        var (fixedConfig, fixes, _) = _sut.ApplySelfHealingFixes(config);

        // Assert
        fixedConfig.Storage!.NamingConvention.Should().Be(validConvention);
        fixes.Should().NotContain(f => f.Contains("naming convention"));
    }

    #endregion

    #region Self-Healing: Invalid Date Partition

    [Theory]
    [InlineData("InvalidPartition")]
    [InlineData("yearly")]
    [InlineData("")]
    public void ApplySelfHealingFixes_InvalidDatePartition_DefaultsToDaily(string invalidPartition)
    {
        // Arrange
        var config = new AppConfig(
            Storage: new StorageConfig(DatePartition: invalidPartition),
            Symbols: new[] { new SymbolConfig("SPY") });

        // Act
        var (fixedConfig, fixes, _) = _sut.ApplySelfHealingFixes(config);

        // Assert
        fixedConfig.Storage!.DatePartition.Should().Be("Daily");
        fixes.Should().Contain(f => f.Contains("date partition"));
    }

    [Theory]
    [InlineData("none")]
    [InlineData("daily")]
    [InlineData("Daily")]
    [InlineData("hourly")]
    [InlineData("monthly")]
    public void ApplySelfHealingFixes_ValidDatePartition_DoesNotModify(string validPartition)
    {
        // Arrange
        var config = new AppConfig(
            Storage: new StorageConfig(DatePartition: validPartition),
            Symbols: new[] { new SymbolConfig("SPY") });

        // Act
        var (fixedConfig, fixes, _) = _sut.ApplySelfHealingFixes(config);

        // Assert
        fixedConfig.Storage!.DatePartition.Should().Be(validPartition);
        fixes.Should().NotContain(f => f.Contains("date partition"));
    }

    #endregion

    #region Self-Healing: Backfill Date Range

    [Fact]
    public void ApplySelfHealingFixes_BackfillFromAfterTo_SwapsDates()
    {
        // Arrange
        var from = new DateOnly(2024, 6, 1);
        var to = new DateOnly(2024, 1, 1);
        var config = new AppConfig(
            Backfill: new BackfillConfig(From: from, To: to),
            Symbols: new[] { new SymbolConfig("SPY") });

        // Act
        var (fixedConfig, fixes, _) = _sut.ApplySelfHealingFixes(config);

        // Assert
        fixedConfig.Backfill!.From.Should().Be(to);
        fixedConfig.Backfill.To.Should().Be(from);
        fixes.Should().Contain(f => f.Contains("Swapped backfill"));
    }

    [Fact]
    public void ApplySelfHealingFixes_BackfillFutureEndDate_ClampedToToday()
    {
        // Arrange
        var futureDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30));
        var config = new AppConfig(
            Backfill: new BackfillConfig(
                From: new DateOnly(2024, 1, 1),
                To: futureDate),
            Symbols: new[] { new SymbolConfig("SPY") });

        // Act
        var (fixedConfig, fixes, _) = _sut.ApplySelfHealingFixes(config);

        // Assert
        (fixedConfig.Backfill!.To <= DateOnly.FromDateTime(DateTime.UtcNow)).Should().BeTrue();
        fixes.Should().Contain(f => f.Contains("future"));
    }

    [Fact]
    public void ApplySelfHealingFixes_BackfillValidDateRange_DoesNotModify()
    {
        // Arrange
        var from = new DateOnly(2024, 1, 1);
        var to = new DateOnly(2024, 6, 1);
        var config = new AppConfig(
            Backfill: new BackfillConfig(From: from, To: to),
            Symbols: new[] { new SymbolConfig("SPY") });

        // Act
        var (fixedConfig, fixes, _) = _sut.ApplySelfHealingFixes(config);

        // Assert
        fixedConfig.Backfill!.From.Should().Be(from);
        fixedConfig.Backfill.To.Should().Be(to);
        fixes.Should().NotContain(f => f.Contains("Swapped"));
        fixes.Should().NotContain(f => f.Contains("future"));
    }

    [Fact]
    public void ApplySelfHealingFixes_NullBackfill_DoesNotThrow()
    {
        // Arrange
        var config = new AppConfig(
            Backfill: null,
            Symbols: new[] { new SymbolConfig("SPY") });

        // Act
        var act = () => _sut.ApplySelfHealingFixes(config);

        // Assert
        act.Should().NotThrow();
    }

    #endregion

    #region Self-Healing: IB Gateway Not Available

    [Fact]
    public async Task ApplySelfHealingFixes_IBSelectedGatewayUnavailable_GeneratesWarning()
    {
        // Arrange - IB gateway won't be running in tests
        var config = new AppConfig(
            DataSource: DataSourceKind.IB,
            Symbols: new[] { new SymbolConfig("SPY") });

        // Act
        await using var service = new ConfigurationService(ibGatewayAvailabilityProbe: () => false);

        var (_, appliedFixes, warnings) = service.ApplySelfHealingFixes(config);

        // Assert - Either switched to alternative OR warning generated
        // In CI, no IB gateway is available. If alternative credentials exist,
        // it will switch (appliedFixes). If not, it will warn.
        var hasFixOrWarning =
            appliedFixes.Any(f => f.Contains("IB") || f.Contains("alternative")) ||
            warnings.Any(w => w.Contains("IB Gateway") || w.Contains("alternative"));

        hasFixOrWarning.Should().BeTrue(
            "Should either apply fix to switch providers OR generate warning about IB Gateway");
    }

    #endregion

    #region Self-Healing: Multiple Fixes Combined

    [Fact]
    public void ApplySelfHealingFixes_MultipleIssues_AppliesAllFixes()
    {
        // Arrange
        var config = new AppConfig(
            Symbols: null,
            Storage: new StorageConfig(
                NamingConvention: "INVALID",
                DatePartition: "INVALID"),
            Backfill: new BackfillConfig(
                From: new DateOnly(2024, 6, 1),
                To: new DateOnly(2024, 1, 1)));

        // Act
        var (fixedConfig, fixes, _) = _sut.ApplySelfHealingFixes(config);

        // Assert
        fixes.Count.Should().BeGreaterThanOrEqualTo(3);
        fixedConfig.Symbols.Should().NotBeNullOrEmpty();
        fixedConfig.Storage!.NamingConvention.Should().Be("BySymbol");
        fixedConfig.Storage.DatePartition.Should().Be("Daily");
        (fixedConfig.Backfill!.From <= fixedConfig.Backfill.To!.Value).Should().BeTrue();
    }

    #endregion

    #region Validation

    [Fact]
    public void ValidateConfig_DefaultConfig_ReturnsValid()
    {
        // Arrange
        var config = new AppConfig();

        // Act
        var isValid = _sut.ValidateConfig(config, out var errors);

        // Assert
        isValid.Should().BeTrue();
        errors.Should().BeEmpty();
    }

    [Fact]
    public void ValidateConfig_EmptyDataRoot_ReturnsInvalid()
    {
        // Arrange
        var config = new AppConfig(DataRoot: "");

        // Act
        var isValid = _sut.ValidateConfig(config, out var errors);

        // Assert
        isValid.Should().BeFalse();
        errors.Should().NotBeEmpty();
    }

    [Fact]
    public void ValidateConfig_AlpacaWithoutCredentials_ReturnsInvalid()
    {
        // Arrange
        var config = new AppConfig(
            DataSource: DataSourceKind.Alpaca,
            Alpaca: new AlpacaOptions(KeyId: "", SecretKey: ""));

        // Act
        var isValid = _sut.ValidateConfig(config, out var errors);

        // Assert
        isValid.Should().BeFalse();
        errors.Should().NotBeEmpty();
    }

    #endregion

    #region Environment Name Resolution

    [Fact]
    public void GetEnvironmentName_ReturnsExpectedValue()
    {
        // Act
        var envName = ConfigurationService.GetEnvironmentName();

        // Assert - in tests, may be null or set
        // Just verify it doesn't throw and returns nullable string
        (envName is null || envName is string).Should().BeTrue();
    }

    #endregion

    #region Pipeline Processing

    [Fact]
    public void ProcessConfig_DefaultConfig_ReturnsValidatedConfig()
    {
        // Arrange
        var config = new AppConfig();

        // Act
        var validated = _sut.ProcessConfig(config);

        // Assert
        validated.Should().NotBeNull();
        validated.Config.Should().NotBeNull();
        validated.Source.Should().Be(ConfigurationOrigin.Programmatic);
    }

    [Fact]
    public void ProcessConfig_WithSelfHealing_AppliesFixes()
    {
        // Arrange
        var config = new AppConfig(
            Symbols: null,
            Storage: new StorageConfig(NamingConvention: "INVALID"));

        // Act
        var validated = _sut.ProcessConfig(config, PipelineOptions.Default);

        // Assert
        validated.Config.Symbols.Should().NotBeNullOrEmpty();
        validated.Config.Storage!.NamingConvention.Should().Be("BySymbol");
        validated.AppliedFixes.Should().NotBeEmpty();
    }

    [Fact]
    public void ProcessConfig_StrictMode_DoesNotApplySelfHealing()
    {
        // Arrange
        var config = new AppConfig(
            Symbols: new[] { new SymbolConfig("SPY") },
            Storage: new StorageConfig(NamingConvention: "INVALID"));

        // Act
        var validated = _sut.ProcessConfig(config, PipelineOptions.Strict);

        // Assert
        validated.Config.Storage!.NamingConvention.Should().Be("INVALID");
        validated.AppliedFixes.Should().BeEmpty();
    }

    [Fact]
    public void ProcessConfig_LenientMode_SkipsValidation()
    {
        // Arrange
        var config = new AppConfig(DataRoot: "");

        // Act
        var validated = _sut.ProcessConfig(config, PipelineOptions.Lenient);

        // Assert
        validated.ValidationErrors.Should().BeEmpty();
    }

    #endregion

    #region Credential Resolution


    [Fact]
    public void ResolveAllCredentials_AlpacaEnvOverlay_UsesEnvironmentCredentials()
    {
        using var keyScope = new EnvironmentVariableScope("ALPACA_KEY_ID", "env-alpaca-key");
        using var secretScope = new EnvironmentVariableScope("ALPACA_SECRET_KEY", "env-alpaca-secret");
        var config = new AppConfig(DataSource: DataSourceKind.Alpaca, Alpaca: new AlpacaOptions("configured-key", "configured-secret"));

        var resolved = _sut.ResolveAllCredentials(config);

        resolved.Alpaca!.KeyId.Should().Be("env-alpaca-key");
        resolved.Alpaca.SecretKey.Should().Be("env-alpaca-secret");
    }

    [Fact]
    public void ResolveAllCredentials_PolygonConfiguredCredential_UsesConfiguredWhenEnvironmentMissing()
    {
        using var keyScope = new EnvironmentVariableScope("POLYGON_API_KEY", null);
        var config = new AppConfig(DataSource: DataSourceKind.Polygon, Polygon: new PolygonOptions("configured-polygon-key"));

        var resolved = _sut.ResolveAllCredentials(config);

        resolved.Polygon!.ApiKey.Should().Be("configured-polygon-key");
    }

    [Fact]
    public void ResolveAllCredentials_PolygonEnvironmentCredential_OverridesConfiguredCredential()
    {
        using var keyScope = new EnvironmentVariableScope("POLYGON_API_KEY", "env-polygon-key");
        var config = new AppConfig(DataSource: DataSourceKind.Polygon, Polygon: new PolygonOptions("configured-polygon-key"));

        var resolved = _sut.ResolveAllCredentials(config);

        resolved.Polygon!.ApiKey.Should().Be("env-polygon-key");
    }

    [Fact]
    public void ResolveAllCredentials_BackfillProviderCredentialOverlays_UsesEnvironmentCredentials()
    {
        using var alpacaKey = new EnvironmentVariableScope("ALPACA_KEY_ID", "env-backfill-alpaca-key");
        using var alpacaSecret = new EnvironmentVariableScope("ALPACA_SECRET_KEY", "env-backfill-alpaca-secret");
        using var tiingo = new EnvironmentVariableScope("TIINGO_API_TOKEN", "env-tiingo-token");
        using var finnhub = new EnvironmentVariableScope("FINNHUB_API_KEY", "env-finnhub-key");
        using var alpha = new EnvironmentVariableScope("ALPHA_VANTAGE_API_KEY", "env-alpha-key");
        using var nasdaq = new EnvironmentVariableScope("NASDAQ_DATA_LINK_API_KEY", "env-nasdaq-key");
        using var fred = new EnvironmentVariableScope("FRED_API_KEY", "env-fred-key");
        var config = new AppConfig(
            Symbols: new[] { new SymbolConfig("SPY") },
            Backfill: new BackfillConfig(Providers: new BackfillProvidersConfig(
                Alpaca: new AlpacaBackfillConfig(KeyId: "configured-alpaca-key", SecretKey: "configured-alpaca-secret"),
                Tiingo: new TiingoConfig(ApiToken: "configured-tiingo-token"),
                Finnhub: new FinnhubConfig(ApiKey: "configured-finnhub-key"),
                AlphaVantage: new AlphaVantageConfig(ApiKey: "configured-alpha-key"),
                Nasdaq: new NasdaqDataLinkConfig(ApiKey: "configured-nasdaq-key"),
                Fred: new FredConfig(ApiKey: "configured-fred-key"))));

        var resolved = _sut.ResolveAllCredentials(config);

        resolved.Backfill!.Providers!.Alpaca!.KeyId.Should().Be("env-backfill-alpaca-key");
        resolved.Backfill.Providers.Alpaca.SecretKey.Should().Be("env-backfill-alpaca-secret");
        resolved.Backfill.Providers.Tiingo!.ApiToken.Should().Be("env-tiingo-token");
        resolved.Backfill.Providers.Finnhub!.ApiKey.Should().Be("env-finnhub-key");
        resolved.Backfill.Providers.AlphaVantage!.ApiKey.Should().Be("env-alpha-key");
        resolved.Backfill.Providers.Nasdaq!.ApiKey.Should().Be("env-nasdaq-key");
        resolved.Backfill.Providers.Fred!.ApiKey.Should().Be("env-fred-key");
    }

    [Fact]
    public async Task LoadAndPrepareConfig_EnvironmentOverlayMerge_AppliesOverlayValues()
    {
        using var envScope = new EnvironmentVariableScope("MDC_ENVIRONMENT", "Test");
        using var dotnetScope = new EnvironmentVariableScope("DOTNET_ENVIRONMENT", null);
        var directory = Path.Combine(Path.GetTempPath(), $"meridian-config-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var basePath = Path.Combine(directory, "appsettings.json");
            var overlayPath = Path.Combine(directory, "appsettings.Test.json");
            await new ConfigStore(basePath).SaveAsync(new AppConfig(DataRoot: "base-data", DataSource: DataSourceKind.Synthetic, Symbols: new[] { new SymbolConfig("SPY") }));
            await new ConfigStore(overlayPath).SaveAsync(new AppConfig(DataRoot: "overlay-data", DataSource: DataSourceKind.Polygon, Polygon: new PolygonOptions("overlay-key"), Symbols: Array.Empty<SymbolConfig>()));

            var loaded = _sut.LoadAndPrepareConfig(basePath, applySelfHealing: false);

            loaded.DataRoot.Should().Be(Path.GetFullPath(Path.Combine(directory, "overlay-data")));
            loaded.DataSource.Should().Be(DataSourceKind.Polygon);
            loaded.Polygon!.ApiKey.Should().Be("overlay-key");
            loaded.Symbols.Should().ContainSingle(s => s.Symbol == "SPY");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void ResolveAllCredentials_NoCredentialsConfigured_ReturnsOriginalConfig()
    {
        // Arrange - no environment variables set for any providers
        var config = new AppConfig(DataSource: DataSourceKind.IB);

        // Act
        var resolved = _sut.ResolveAllCredentials(config);

        // Assert
        resolved.Should().NotBeNull();
        resolved.DataSource.Should().Be(DataSourceKind.IB);
    }

    [Fact]
    public void ResolveAllCredentials_AlpacaWithExistingCredentials_PreservesConfig()
    {
        // Arrange
        var config = new AppConfig(
            DataSource: DataSourceKind.Alpaca,
            Alpaca: new AlpacaOptions(KeyId: "test-key", SecretKey: "test-secret"));

        // Act
        var resolved = _sut.ResolveAllCredentials(config);

        // Assert
        resolved.Alpaca.Should().NotBeNull();
    }

    [Fact]
    public void CreateCredentialContext_UsesConfiguredValuesForAnnotatedProvider()
    {
        // Arrange
        using var keyScope = new EnvironmentVariableScope("ALPACA_KEY_ID", null);
        using var secretScope = new EnvironmentVariableScope("ALPACA_SECRET_KEY", null);
        var configuredValues = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["ALPACA_KEY_ID"] = "configured-key",
            ["ALPACA_SECRET_KEY"] = "configured-secret"
        };

        // Act
        var context = _sut.CreateCredentialContext(typeof(AlpacaHistoricalDataProvider), configuredValues);

        // Assert
        context.Get("ALPACA_KEY_ID").Should().Be("configured-key");
        context.Get("ALPACA_SECRET_KEY").Should().Be("configured-secret");
    }

    #endregion

    #region Dispose

    [Fact]
    public async Task DisposeAsync_CanBeCalledMultipleTimes()
    {
        // Arrange
        var service = new ConfigurationService();

        // Act
        var act = async () =>
        {
            await service.DisposeAsync();
            await service.DisposeAsync();
        };

        // Assert
        await act.Should().NotThrowAsync();
    }

    private sealed class EnvironmentVariableScope : IDisposable
    {
        private readonly string _name;
        private readonly string? _previousValue;

        public EnvironmentVariableScope(string name, string? value)
        {
            _name = name;
            _previousValue = Environment.GetEnvironmentVariable(name);
            Environment.SetEnvironmentVariable(name, value);
        }

        public void Dispose()
        {
            Environment.SetEnvironmentVariable(_name, _previousValue);
        }
    }

    #endregion
}
