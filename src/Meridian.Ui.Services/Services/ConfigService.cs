using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Meridian.Contracts.Configuration;
using Meridian.Ui.Services.Contracts;

namespace Meridian.Ui.Services;

/// <summary>
/// Default configuration service for the shared UI services layer.
/// Provides basic config loading/saving from the standard appsettings path.
/// Platform-specific projects may override this by setting the Instance property.
/// </summary>
public class ConfigService : IConfigService
{
    private static readonly Lazy<ConfigService> _instance = new(() => new ConfigService());
    private static readonly Func<string> _defaultPathResolver = () =>
        Path.Combine(AppContext.BaseDirectory, "config", "appsettings.json");

    public static ConfigService Instance => _instance.Value;
    public static Func<string> DefaultPathResolver { get; set; } = _defaultPathResolver;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public string ConfigPath { get; }

    public ConfigService()
    {
        var configPath = DefaultPathResolver();
        ConfigPath = Path.IsPathRooted(configPath)
            ? configPath
            : Path.GetFullPath(configPath);
    }

    public virtual async Task<AppConfig?> LoadConfigAsync(CancellationToken ct = default)
    {
        if (!File.Exists(ConfigPath))
            return null;
        try
        {
            var json = await File.ReadAllTextAsync(ConfigPath, ct);
            var config = JsonSerializer.Deserialize<AppConfig>(json, _jsonOptions);
            if (config == null)
                return null;

            config.DataRoot = MeridianPathDefaults.ResolveConfiguredDataRootFromJson(json, config.DataRoot);
            return config;
        }
        catch (IOException) { return null; }
        catch (JsonException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    public virtual async Task SaveConfigAsync(AppConfig config, CancellationToken ct = default)
    {
        var dir = Path.GetDirectoryName(ConfigPath);
        if (dir != null)
            Directory.CreateDirectory(dir);

        config.DataRoot = string.IsNullOrWhiteSpace(config.DataRoot)
            ? MeridianPathDefaults.DefaultDataRoot
            : config.DataRoot;

        var json = JsonSerializer.Serialize(config, _jsonOptions);
        await File.WriteAllTextAsync(ConfigPath, json, ct);
    }

    public string ResolveDataRoot(AppConfig? config = null)
        => MeridianPathDefaults.ResolveDataRoot(ConfigPath, config?.DataRoot);

    public async Task SaveDataSourceAsync(string dataSource, CancellationToken ct = default)
    {
        var config = await LoadConfigAsync(ct) ?? new AppConfig();
        config.DataSource = dataSource;
        await SaveConfigAsync(config, ct);
    }

    public async Task SaveAlpacaOptionsAsync(AlpacaOptions options, CancellationToken ct = default)
    {
        var config = await LoadConfigAsync(ct) ?? new AppConfig();
        config.Alpaca = options;
        await SaveConfigAsync(config, ct);
    }

    public async Task SaveStorageConfigAsync(string dataRoot, bool compress, StorageConfig storage, CancellationToken ct = default)
    {
        var config = await LoadConfigAsync(ct) ?? new AppConfig();
        config.DataRoot = dataRoot;
        config.Compress = compress;
        config.Storage = storage;
        await SaveConfigAsync(config, ct);
    }

    public async Task AddOrUpdateSymbolAsync(SymbolConfig symbol, CancellationToken ct = default)
    {
        var config = await LoadConfigAsync(ct) ?? new AppConfig();
        var symbols = config.Symbols?.ToList() ?? new List<SymbolConfig>();
        var existingIndex = symbols.FindIndex(s =>
            string.Equals(s.Symbol, symbol.Symbol, StringComparison.OrdinalIgnoreCase));
        if (existingIndex >= 0)
            symbols[existingIndex] = symbol;
        else
            symbols.Add(symbol);
        config.Symbols = symbols.ToArray();
        await SaveConfigAsync(config, ct);
    }

    public Task AddSymbolAsync(SymbolConfig symbol, CancellationToken ct = default)
        => AddOrUpdateSymbolAsync(symbol, ct);

    public async Task DeleteSymbolAsync(string symbol, CancellationToken ct = default)
    {
        var config = await LoadConfigAsync(ct) ?? new AppConfig();
        var symbols = config.Symbols?.ToList() ?? new List<SymbolConfig>();
        symbols.RemoveAll(s => string.Equals(s.Symbol, symbol, StringComparison.OrdinalIgnoreCase));
        config.Symbols = symbols.ToArray();
        await SaveConfigAsync(config, ct);
    }

    public async Task<DataSourceConfig[]> GetDataSourcesAsync(CancellationToken ct = default)
    {
        var config = await LoadConfigAsync(ct);
        return config?.DataSources?.Sources ?? Array.Empty<DataSourceConfig>();
    }

    public async Task<DataSourcesConfig> GetDataSourcesConfigAsync(CancellationToken ct = default)
    {
        var config = await LoadConfigAsync(ct);
        return config?.DataSources ?? new DataSourcesConfig();
    }

    public async Task AddOrUpdateDataSourceAsync(DataSourceConfig dataSource, CancellationToken ct = default)
    {
        var config = await LoadConfigAsync(ct) ?? new AppConfig();
        var dataSources = config.DataSources ?? new DataSourcesConfig();
        var sources = dataSources.Sources?.ToList() ?? new List<DataSourceConfig>();
        var existingIndex = sources.FindIndex(s =>
            string.Equals(s.Id, dataSource.Id, StringComparison.OrdinalIgnoreCase));
        if (existingIndex >= 0)
            sources[existingIndex] = dataSource;
        else
            sources.Add(dataSource);
        dataSources.Sources = sources.ToArray();
        config.DataSources = dataSources;
        await SaveConfigAsync(config, ct);
    }

    public async Task DeleteDataSourceAsync(string id, CancellationToken ct = default)
    {
        var config = await LoadConfigAsync(ct) ?? new AppConfig();
        var dataSources = config.DataSources ?? new DataSourcesConfig();
        var sources = dataSources.Sources?.ToList() ?? new List<DataSourceConfig>();
        sources.RemoveAll(s => string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase));
        dataSources.Sources = sources.ToArray();
        config.DataSources = dataSources;
        await SaveConfigAsync(config, ct);
    }

    public async Task SetDefaultDataSourceAsync(string id, bool isHistorical, CancellationToken ct = default)
    {
        var config = await LoadConfigAsync(ct) ?? new AppConfig();
        var dataSources = config.DataSources ?? new DataSourcesConfig();
        if (isHistorical)
            dataSources.DefaultHistoricalSourceId = id;
        else
            dataSources.DefaultRealTimeSourceId = id;
        config.DataSources = dataSources;
        await SaveConfigAsync(config, ct);
    }

    public async Task ToggleDataSourceAsync(string id, bool enabled, CancellationToken ct = default)
    {
        var config = await LoadConfigAsync(ct) ?? new AppConfig();
        var dataSources = config.DataSources ?? new DataSourcesConfig();
        var source = dataSources.Sources?.FirstOrDefault(s =>
            string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase));
        if (source != null)
        {
            source.Enabled = enabled;
            await SaveConfigAsync(config, ct);
        }
    }

    public async Task UpdateFailoverSettingsAsync(bool enableFailover, int failoverTimeoutSeconds, CancellationToken ct = default)
    {
        var config = await LoadConfigAsync(ct) ?? new AppConfig();
        var dataSources = config.DataSources ?? new DataSourcesConfig();
        dataSources.EnableFailover = enableFailover;
        dataSources.FailoverTimeoutSeconds = failoverTimeoutSeconds;
        config.DataSources = dataSources;
        await SaveConfigAsync(config, ct);
    }

    public Task<AppSettings> GetAppSettingsAsync(CancellationToken ct = default)
        => Task.FromResult(new AppSettings());

    public Task SaveAppSettingsAsync(AppSettings settings, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task UpdateServiceUrlAsync(string serviceUrl, int timeoutSeconds = 30, int backfillTimeoutMinutes = 60, CancellationToken ct = default)
    {
        ApiClientService.Instance.Configure(serviceUrl, timeoutSeconds, backfillTimeoutMinutes);
        return Task.CompletedTask;
    }

    public Task InitializeAsync(CancellationToken ct = default)
        => Task.CompletedTask;

    public Task<DiagnosticValidationResult> ValidateConfigAsync(CancellationToken ct = default)
        => Task.FromResult(new DiagnosticValidationResult { IsValid = true });
}
