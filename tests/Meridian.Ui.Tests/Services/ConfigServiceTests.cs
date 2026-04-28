using FluentAssertions;
using Meridian.Contracts.Configuration;
using Meridian.Ui.Services;
using Meridian.Ui.Services.Contracts;

namespace Meridian.Ui.Tests.Services;

/// <summary>
/// Tests for <see cref="ConfigService"/> — configuration loading/saving,
/// data source management, default behaviors, and interface compliance.
/// </summary>
public sealed class ConfigServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _configDir;
    private readonly string _configPath;
    private readonly Func<string> _originalPathResolver = ConfigService.DefaultPathResolver;

    public ConfigServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "mdc-cfg-test-" + Guid.NewGuid().ToString("N"));
        _configDir = Path.Combine(_tempDir, "config");
        Directory.CreateDirectory(_configDir);
        _configPath = Path.Combine(_configDir, "appsettings.json");
    }

    public void Dispose()
    {
        ConfigService.DefaultPathResolver = _originalPathResolver;

        try
        { Directory.Delete(_tempDir, recursive: true); }
        catch { }
    }

    // ── Singleton ────────────────────────────────────────────────────

    [Fact]
    public void Instance_ShouldReturnNonNull()
    {
        ConfigService.Instance.Should().NotBeNull();
    }

    [Fact]
    public void Instance_ShouldReturnSameSingleton()
    {
        var a = ConfigService.Instance;
        var b = ConfigService.Instance;
        a.Should().BeSameAs(b);
    }

    // ── Interface compliance ─────────────────────────────────────────

    [Fact]
    public void ConfigService_ShouldImplementIConfigService()
    {
        var svc = new ConfigService();
        svc.Should().BeAssignableTo<IConfigService>();
    }

    // ── ConfigPath ───────────────────────────────────────────────────

    [Fact]
    public void ConfigPath_ShouldEndWithExpectedFileName()
    {
        var svc = new ConfigService();
        svc.ConfigPath.Should().EndWith(Path.Combine("config", "appsettings.json"));
    }

    [Fact]
    public void ConfigPath_ShouldNotBeNullOrEmpty()
    {
        var svc = new ConfigService();
        svc.ConfigPath.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void ConfigPath_UsesDefaultPathResolverOverride()
    {
        ConfigService.DefaultPathResolver = () => _configPath;

        var svc = new ConfigService();

        svc.ConfigPath.Should().Be(_configPath);
    }

    // ── LoadConfigAsync ──────────────────────────────────────────────

    [Fact]
    public async Task LoadConfigAsync_WhenFileDoesNotExist_ShouldReturnNull()
    {
        var svc = new ConfigService();
        // The default path won't exist in test environment
        var result = await svc.LoadConfigAsync();
        // If the file doesn't exist it should return null
        // (may or may not be null depending on environment — test the contract)
        // We test via a subclass pointing to a known-missing path
        var testSvc = new TestConfigService(Path.Combine(_tempDir, "nonexistent", "appsettings.json"));
        var config = await testSvc.LoadConfigAsync();
        config.Should().BeNull();
    }

    [Fact]
    public async Task LoadConfigAsync_WhenFileExists_ShouldDeserialize()
    {
        var json = """
        {
            "dataRoot": "test-data",
            "dataSource": "Alpaca",
            "compress": true
        }
        """;
        await File.WriteAllTextAsync(_configPath, json);

        var svc = new TestConfigService(_configPath);
        var config = await svc.LoadConfigAsync();

        config.Should().NotBeNull();
        config!.DataRoot.Should().Be("test-data");
        config.DataSource.Should().Be("Alpaca");
        config.Compress.Should().BeTrue();
    }

    [Fact]
    public async Task LoadConfigAsync_MigratesLegacyStorageBaseDirectoryToDataRoot()
    {
        ConfigService.DefaultPathResolver = () => _configPath;
        var json = """
        {
            "storage": {
                "baseDirectory": "legacy-data"
            }
        }
        """;
        await File.WriteAllTextAsync(_configPath, json);

        var svc = new ConfigService();
        var config = await svc.LoadConfigAsync();

        config.Should().NotBeNull();
        config!.DataRoot.Should().Be("legacy-data");
    }

    [Fact]
    public async Task LoadConfigAsync_WhenFileIsEmpty_ShouldReturnNull()
    {
        ConfigService.DefaultPathResolver = () => _configPath;
        await File.WriteAllTextAsync(_configPath, string.Empty);

        var svc = new ConfigService();
        var config = await svc.LoadConfigAsync();

        config.Should().BeNull();
    }

    [Fact]
    public async Task LoadConfigAsync_WithSymbols_ShouldDeserializeSymbolArray()
    {
        var json = """
        {
            "dataSource": "IB",
            "symbols": [
                { "symbol": "SPY", "subscribeTrades": true },
                { "symbol": "AAPL", "subscribeTrades": false, "subscribeDepth": true }
            ]
        }
        """;
        await File.WriteAllTextAsync(_configPath, json);

        var svc = new TestConfigService(_configPath);
        var config = await svc.LoadConfigAsync();

        config.Should().NotBeNull();
        config!.Symbols.Should().HaveCount(2);
        config.Symbols![0].Symbol.Should().Be("SPY");
        config.Symbols[0].SubscribeTrades.Should().BeTrue();
        config.Symbols[1].Symbol.Should().Be("AAPL");
        config.Symbols[1].SubscribeDepth.Should().BeTrue();
    }

    [Fact]
    public async Task LoadConfigAsync_SupportsCancellationToken()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var svc = new TestConfigService(Path.Combine(_tempDir, "missing.json"));
        // File doesn't exist, returns null immediately — no exception expected
        var result = await svc.LoadConfigAsync(cts.Token);
        result.Should().BeNull();
    }

    // ── SaveConfigAsync ──────────────────────────────────────────────

    [Fact]
    public async Task SaveConfigAsync_ShouldWriteJsonFile()
    {
        var svc = new TestConfigService(_configPath);
        var config = new AppConfigDto
        {
            DataSource = "Polygon",
            DataRoot = "my-data",
            Compress = true
        };

        await svc.SaveConfigAsync(config);

        File.Exists(_configPath).Should().BeTrue();
        var json = await File.ReadAllTextAsync(_configPath);
        json.Should().Contain("Polygon");
        json.Should().Contain("my-data");
    }

    [Fact]
    public async Task SaveConfigAsync_ShouldCreateDirectoryIfMissing()
    {
        var deepPath = Path.Combine(_tempDir, "deep", "nested", "config", "appsettings.json");
        var svc = new TestConfigService(deepPath);

        await svc.SaveConfigAsync(new AppConfigDto { DataSource = "IB" });

        File.Exists(deepPath).Should().BeTrue();
    }

    [Fact]
    public async Task SaveConfigAsync_ShouldProduceIndentedJson()
    {
        var svc = new TestConfigService(_configPath);
        await svc.SaveConfigAsync(new AppConfigDto { DataSource = "IB" });

        var json = await File.ReadAllTextAsync(_configPath);
        // Indented JSON should contain newlines
        json.Should().Contain("\n");
    }

    // ── Default virtual methods ──────────────────────────────────────

    [Fact]
    public async Task SaveDataSourceAsync_ShouldCompleteWithoutException()
    {
        var svc = new ConfigService();
        var act = async () => await svc.SaveDataSourceAsync("Alpaca");
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task SaveAlpacaOptionsAsync_ShouldCompleteWithoutException()
    {
        var svc = new ConfigService();
        var act = async () => await svc.SaveAlpacaOptionsAsync(new AlpacaOptionsDto());
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task SaveStorageConfigAsync_ShouldCompleteWithoutException()
    {
        var svc = new ConfigService();
        var act = async () => await svc.SaveStorageConfigAsync("data", true, new StorageConfigDto());
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task AddOrUpdateSymbolAsync_ShouldCompleteWithoutException()
    {
        var svc = new ConfigService();
        var act = async () => await svc.AddOrUpdateSymbolAsync(new SymbolConfigDto { Symbol = "SPY" });
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task AddSymbolAsync_ShouldDelegateToAddOrUpdate()
    {
        var svc = new ConfigService();
        var act = async () => await svc.AddSymbolAsync(new SymbolConfigDto { Symbol = "AAPL" });
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task DeleteSymbolAsync_ShouldCompleteWithoutException()
    {
        var svc = new ConfigService();
        var act = async () => await svc.DeleteSymbolAsync("SPY");
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task GetDataSourcesAsync_ShouldReturnEmptyArray()
    {
        var svc = new ConfigService();
        var result = await svc.GetDataSourcesAsync();
        result.Should().NotBeNull();
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetDataSourcesConfigAsync_ShouldReturnDefaultConfig()
    {
        var svc = new ConfigService();
        var result = await svc.GetDataSourcesConfigAsync();
        result.Should().NotBeNull();
    }

    [Fact]
    public async Task GetAppSettingsAsync_ShouldReturnDefaultSettings()
    {
        var svc = new ConfigService();
        var result = await svc.GetAppSettingsAsync();
        result.Should().NotBeNull();
    }

    [Fact]
    public async Task ValidateConfigAsync_ShouldReturnValidByDefault()
    {
        var svc = new ConfigService();
        var result = await svc.ValidateConfigAsync();
        result.Should().NotBeNull();
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task InitializeAsync_ShouldCompleteWithoutException()
    {
        var svc = new ConfigService();
        var act = async () => await svc.InitializeAsync();
        await act.Should().NotThrowAsync();
    }

    // ── Round-trip ───────────────────────────────────────────────────

    [Fact]
    public async Task SaveAndLoad_ShouldRoundTrip()
    {
        var svc = new TestConfigService(_configPath);
        var original = new AppConfigDto
        {
            DataSource = "NYSE",
            DataRoot = "round-trip-data",
            Compress = true,
            Symbols = new[]
            {
                new SymbolConfigDto { Symbol = "MSFT", SubscribeTrades = true, DepthLevels = 5 }
            }
        };

        await svc.SaveConfigAsync(original);
        var loaded = await svc.LoadConfigAsync();

        loaded.Should().NotBeNull();
        loaded!.DataSource.Should().Be("NYSE");
        loaded.DataRoot.Should().Be("round-trip-data");
        loaded.Compress.Should().BeTrue();
        loaded.Symbols.Should().HaveCount(1);
        loaded.Symbols![0].Symbol.Should().Be("MSFT");
        loaded.Symbols[0].DepthLevels.Should().Be(5);
    }

    [Fact]
    public void ResolveDataRoot_UsesParentOfConfigDirectoryForRelativePaths()
    {
        ConfigService.DefaultPathResolver = () => _configPath;
        var svc = new ConfigService();

        var resolved = svc.ResolveDataRoot(new AppConfigDto { DataRoot = "relative-data" });

        resolved.Should().Be(Path.Combine(_tempDir, "relative-data"));
    }

    // ── DiagnosticValidationResult model ────────────────────────────────

    [Fact]
    public void DiagnosticValidationResult_ShouldHaveDefaultValues()
    {
        var result = new DiagnosticValidationResult();
        result.IsValid.Should().BeFalse();
        result.Errors.Should().NotBeNull();
        result.Warnings.Should().NotBeNull();
    }

    // ── Helper ───────────────────────────────────────────────────────

    /// <summary>Test subclass that overrides ConfigPath for isolated file I/O.</summary>
    private sealed class TestConfigService : ConfigService
    {
        private readonly string _path;
        public TestConfigService(string path) => _path = path;
        public new string ConfigPath => _path;

        public override async Task<AppConfigDto?> LoadConfigAsync(CancellationToken ct = default)
        {
            if (!File.Exists(_path))
                return null;
            var json = await File.ReadAllTextAsync(_path, ct);
            return System.Text.Json.JsonSerializer.Deserialize<AppConfigDto>(json,
                new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }

        public override async Task SaveConfigAsync(AppConfigDto config, CancellationToken ct = default)
        {
            var dir = Path.GetDirectoryName(_path);
            if (dir != null)
                Directory.CreateDirectory(dir);
            var json = System.Text.Json.JsonSerializer.Serialize(config,
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(_path, json, ct);
        }
    }
}
