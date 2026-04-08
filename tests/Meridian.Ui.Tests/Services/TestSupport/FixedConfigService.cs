using Meridian.Contracts.Configuration;
using Meridian.Ui.Services;
using Meridian.Ui.Services.Contracts;

namespace Meridian.Ui.Tests.Services;

internal sealed class FixedConfigService : IConfigService
{
    private readonly AppConfigDto? _config;

    public FixedConfigService(string configPath, AppConfigDto? config)
    {
        ConfigPath = configPath;
        _config = config;
    }

    public string ConfigPath { get; }

    public Task<AppConfigDto?> LoadConfigAsync(CancellationToken ct = default) => Task.FromResult(_config);
    public Task SaveConfigAsync(AppConfigDto config, CancellationToken ct = default) => Task.CompletedTask;
    public Task SaveDataSourceAsync(string dataSource, CancellationToken ct = default) => Task.CompletedTask;
    public Task SaveAlpacaOptionsAsync(AlpacaOptionsDto options, CancellationToken ct = default) => Task.CompletedTask;
    public Task SaveStorageConfigAsync(string dataRoot, bool compress, StorageConfigDto storage, CancellationToken ct = default) => Task.CompletedTask;
    public Task AddOrUpdateSymbolAsync(SymbolConfigDto symbol, CancellationToken ct = default) => Task.CompletedTask;
    public Task AddSymbolAsync(SymbolConfigDto symbol, CancellationToken ct = default) => Task.CompletedTask;
    public Task DeleteSymbolAsync(string symbol, CancellationToken ct = default) => Task.CompletedTask;
    public Task<DataSourceConfigDto[]> GetDataSourcesAsync(CancellationToken ct = default) => Task.FromResult(Array.Empty<DataSourceConfigDto>());
    public Task<DataSourcesConfigDto> GetDataSourcesConfigAsync(CancellationToken ct = default)
        => Task.FromResult(_config?.DataSources ?? new DataSourcesConfigDto());
    public Task AddOrUpdateDataSourceAsync(DataSourceConfigDto dataSource, CancellationToken ct = default) => Task.CompletedTask;
    public Task DeleteDataSourceAsync(string id, CancellationToken ct = default) => Task.CompletedTask;
    public Task SetDefaultDataSourceAsync(string id, bool isHistorical, CancellationToken ct = default) => Task.CompletedTask;
    public Task ToggleDataSourceAsync(string id, bool enabled, CancellationToken ct = default) => Task.CompletedTask;
    public Task UpdateFailoverSettingsAsync(bool enableFailover, int failoverTimeoutSeconds, CancellationToken ct = default) => Task.CompletedTask;
    public Task<AppSettings> GetAppSettingsAsync(CancellationToken ct = default) => Task.FromResult(new AppSettings());
    public Task SaveAppSettingsAsync(AppSettings settings, CancellationToken ct = default) => Task.CompletedTask;
    public Task UpdateServiceUrlAsync(string serviceUrl, int timeoutSeconds = 30, int backfillTimeoutMinutes = 60, CancellationToken ct = default) => Task.CompletedTask;
    public Task InitializeAsync(CancellationToken ct = default) => Task.CompletedTask;
    public Task<DiagnosticValidationResult> ValidateConfigAsync(CancellationToken ct = default)
        => Task.FromResult(new DiagnosticValidationResult { IsValid = true });
}

internal sealed class PathFixture : IDisposable
{
    public PathFixture(string prefix)
    {
        RootPath = Path.Combine(Path.GetTempPath(), $"{prefix}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(RootPath);
        ConfigPath = Path.Combine(RootPath, "appsettings.json");
    }

    public string RootPath { get; }
    public string ConfigPath { get; }

    public void Dispose()
    {
        try
        {
            Directory.Delete(RootPath, recursive: true);
        }
        catch
        {
        }
    }
}
