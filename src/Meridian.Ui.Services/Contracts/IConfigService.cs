using System.Threading;
using System.Threading.Tasks;

namespace Meridian.Ui.Services.Contracts;

/// <summary>
/// Interface for managing application configuration.
/// Enables testability and dependency injection.
/// </summary>
public interface IConfigService
{
    /// <summary>
    /// Gets the configuration file path.
    /// </summary>
    string ConfigPath { get; }

    /// <summary>
    /// Loads the application configuration.
    /// </summary>
    Task<AppConfig?> LoadConfigAsync(CancellationToken ct = default);

    /// <summary>
    /// Saves the application configuration.
    /// </summary>
    Task SaveConfigAsync(AppConfig config, CancellationToken ct = default);

    /// <summary>
    /// Saves the data source setting.
    /// </summary>
    Task SaveDataSourceAsync(string dataSource, CancellationToken ct = default);

    /// <summary>
    /// Saves Alpaca provider options.
    /// </summary>
    Task SaveAlpacaOptionsAsync(AlpacaOptions options, CancellationToken ct = default);

    /// <summary>
    /// Saves storage configuration.
    /// </summary>
    Task SaveStorageConfigAsync(string dataRoot, bool compress, StorageConfig storage, CancellationToken ct = default);

    /// <summary>
    /// Adds or updates a symbol in the configuration.
    /// </summary>
    Task AddOrUpdateSymbolAsync(SymbolConfig symbol, CancellationToken ct = default);

    /// <summary>
    /// Adds a symbol to the configuration (alias for AddOrUpdateSymbolAsync).
    /// </summary>
    Task AddSymbolAsync(SymbolConfig symbol, CancellationToken ct = default);

    /// <summary>
    /// Deletes a symbol from the configuration.
    /// </summary>
    Task DeleteSymbolAsync(string symbol, CancellationToken ct = default);

    /// <summary>
    /// Gets all configured data sources.
    /// </summary>
    Task<DataSourceConfig[]> GetDataSourcesAsync(CancellationToken ct = default);

    /// <summary>
    /// Gets the data sources configuration.
    /// </summary>
    Task<DataSourcesConfig> GetDataSourcesConfigAsync(CancellationToken ct = default);

    /// <summary>
    /// Adds or updates a data source configuration.
    /// </summary>
    Task AddOrUpdateDataSourceAsync(DataSourceConfig dataSource, CancellationToken ct = default);

    /// <summary>
    /// Deletes a data source by ID.
    /// </summary>
    Task DeleteDataSourceAsync(string id, CancellationToken ct = default);

    /// <summary>
    /// Sets the default data source for real-time or historical data.
    /// </summary>
    Task SetDefaultDataSourceAsync(string id, bool isHistorical, CancellationToken ct = default);

    /// <summary>
    /// Toggles a data source's enabled state.
    /// </summary>
    Task ToggleDataSourceAsync(string id, bool enabled, CancellationToken ct = default);

    /// <summary>
    /// Updates failover settings for data sources.
    /// </summary>
    Task UpdateFailoverSettingsAsync(bool enableFailover, int failoverTimeoutSeconds, CancellationToken ct = default);

    /// <summary>
    /// Gets the app settings including service URL configuration.
    /// </summary>
    Task<AppSettings> GetAppSettingsAsync(CancellationToken ct = default);

    /// <summary>
    /// Saves app settings including service URL configuration.
    /// </summary>
    Task SaveAppSettingsAsync(AppSettings settings, CancellationToken ct = default);

    /// <summary>
    /// Updates the service URL configuration.
    /// </summary>
    Task UpdateServiceUrlAsync(string serviceUrl, int timeoutSeconds = 30, int backfillTimeoutMinutes = 60, CancellationToken ct = default);

    /// <summary>
    /// Loads configuration and initializes services with configured URLs.
    /// Should be called during app startup.
    /// </summary>
    Task InitializeAsync(CancellationToken ct = default);

    /// <summary>
    /// Validates the current configuration.
    /// </summary>
    Task<DiagnosticValidationResult> ValidateConfigAsync(CancellationToken ct = default);
}

/// <summary>
/// Result of configuration validation for UI diagnostics.
/// </summary>
public sealed class DiagnosticValidationResult
{
    public bool IsValid { get; init; }
    public string[] Errors { get; init; } = [];
    public string[] Warnings { get; init; } = [];
}
