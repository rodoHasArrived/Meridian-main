using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Meridian.Contracts.Configuration;
using Meridian.Ui.Services;
using Meridian.Ui.Services.Services;

namespace Meridian.Wpf.Services;

/// <summary>
/// Result of configuration validation.
/// </summary>
public sealed class ConfigServiceValidationResult
{
    public bool IsValid { get; set; } = true;
    public string[] Errors { get; set; } = Array.Empty<string>();
    public string[] Warnings { get; set; } = Array.Empty<string>();

    public static ConfigServiceValidationResult Success() => new() { IsValid = true };
    public static ConfigServiceValidationResult Failure(params string[] errors) => new()
    {
        IsValid = false,
        Errors = errors
    };
}

/// <summary>
/// Result of inline validation for a single provider field change.
/// Used to provide immediate feedback in the WPF settings panel.
/// </summary>
public sealed class InlineValidationResult
{
    public string[] Errors { get; set; } = Array.Empty<string>();
    public string[] Warnings { get; set; } = Array.Empty<string>();
    public bool IsValid => Errors.Length == 0;
    public bool HasWarnings => Warnings.Length > 0;
}

/// <summary>
/// WPF platform-specific configuration service.
/// Extends <see cref="ConfigServiceBase"/> with file I/O and audit trail integration.
/// Part of Phase 2 service extraction.
/// </summary>
public sealed class ConfigService : ConfigServiceBase
{
    private static readonly Lazy<ConfigService> _instance = new(() => new ConfigService());

    private bool _initialized;

    public static ConfigService Instance => _instance.Value;

    public bool IsInitialized => _initialized;

    public override string ConfigPath => FirstRunService.Instance.ConfigFilePath;

    public string ResolveDataRoot(AppConfigDto? config = null)
        => MeridianPathDefaults.ResolveDataRoot(ConfigPath, config?.DataRoot);

    private ConfigService()
    {
    }

    public Task InitializeAsync()
    {
        _initialized = true;
        return Task.CompletedTask;
    }

    /// <summary>
    /// Validates the current configuration (WPF-specific result type).
    /// </summary>
    public async Task<ConfigServiceValidationResult> ValidateConfigAsync(CancellationToken ct = default)
    {
        var result = await ValidateConfigDetailAsync();
        return new ConfigServiceValidationResult
        {
            IsValid = result.IsValid,
            Errors = result.Errors,
            Warnings = result.Warnings
        };
    }

    /// <summary>
    /// Gets the data sources configuration (WPF-specific alias).
    /// </summary>
    public Task<DataSourcesConfigDto> GetDataSourcesConfigAsync()
        => GetDataSourcesConfigDtoAsync();

    /// <summary>
    /// Gets the data sources configuration. Convenience alias for WPF views.
    /// </summary>
    public Task<DataSourcesConfigDto> GetDataSourcesAsync()
        => GetDataSourcesConfigDtoAsync();

    /// <summary>
    /// Gets configured symbols. Convenience alias for WPF views.
    /// </summary>
    public Task<SymbolConfigDto[]> GetSymbolsAsync()
        => GetConfiguredSymbolsAsync();

    /// <summary>
    /// Saves the full data sources configuration. Replaces all sources.
    /// </summary>
    public async Task SaveDataSourcesAsync(DataSourcesConfigDto dataSources, CancellationToken ct = default)
    {
        var config = await LoadConfigCoreAsync(ct) ?? new AppConfigDto();
        config.DataSources = dataSources;
        await SaveConfigCoreAsync(config, ct);
    }

    /// <summary>
    /// Adds a new data source or updates an existing one (matched by <see cref="DataSourceConfigDto.Id"/>).
    /// </summary>
    public async Task AddOrUpdateDataSourceAsync(DataSourceConfigDto source, CancellationToken ct = default)
    {
        var config = await LoadConfigCoreAsync(ct) ?? new AppConfigDto();
        config.DataSources ??= new DataSourcesConfigDto();

        var sources = (config.DataSources.Sources ?? Array.Empty<DataSourceConfigDto>()).ToList();
        var index = sources.FindIndex(s => s.Id == source.Id);
        if (index >= 0)
            sources[index] = source;
        else
            sources.Add(source);

        config.DataSources.Sources = sources.ToArray();
        await SaveConfigCoreAsync(config, ct);
    }

    /// <summary>
    /// Deletes the data source with the specified <paramref name="id"/>.
    /// </summary>
    public async Task DeleteDataSourceAsync(string id, CancellationToken ct = default)
    {
        var config = await LoadConfigCoreAsync(ct) ?? new AppConfigDto();
        if (config.DataSources?.Sources == null) return;

        config.DataSources.Sources = config.DataSources.Sources
            .Where(s => s.Id != id)
            .ToArray();
        await SaveConfigCoreAsync(config, ct);
    }

    /// <summary>
    /// Sets the default real-time or historical data source.
    /// </summary>
    public async Task SetDefaultDataSourceAsync(string id, bool isHistorical, CancellationToken ct = default)
    {
        var config = await LoadConfigCoreAsync(ct) ?? new AppConfigDto();
        config.DataSources ??= new DataSourcesConfigDto();

        if (isHistorical)
            config.DataSources.DefaultHistoricalSourceId = id;
        else
            config.DataSources.DefaultRealTimeSourceId = id;

        await SaveConfigCoreAsync(config, ct);
    }

    /// <summary>
    /// Updates the failover settings (enabled flag and timeout).
    /// </summary>
    public async Task UpdateFailoverSettingsAsync(bool enabled, int timeoutSeconds, CancellationToken ct = default)
    {
        var config = await LoadConfigCoreAsync(ct) ?? new AppConfigDto();
        config.DataSources ??= new DataSourcesConfigDto();
        config.DataSources.EnableFailover = enabled;
        config.DataSources.FailoverTimeoutSeconds = timeoutSeconds;
        await SaveConfigCoreAsync(config, ct);
    }


    /// Returns null when no active source is set.
    /// </summary>
    public async Task<string?> GetActiveDataSourceAsync(CancellationToken ct = default)
    {
        var config = await LoadConfigCoreAsync(ct) ?? new AppConfigDto();
        return config.DataSource;
    }

    /// <summary>
    /// Reloads the configuration from disk.
    /// Call after an external edit to the config file.
    /// </summary>
    public async Task ReloadConfigAsync(CancellationToken ct = default)
    {
        // Simply re-read from disk — LoadConfigCoreAsync already reads fresh each call.
        await LoadConfigCoreAsync(ct);
    }

    /// <summary>
    /// Validates a single provider's options inline (for real-time field validation).
    /// Returns a list of validation messages (empty if valid).
    /// </summary>
    public async Task<InlineValidationResult> ValidateProviderInlineAsync(
        string providerId,
        BackfillProviderOptionsDto options,
        CancellationToken ct = default)
    {
        var warnings = new List<string>();
        var errors = new List<string>();

        if (options.Priority is < 0)
        {
            errors.Add("Priority must be a non-negative integer.");
        }

        if (options.RateLimitPerMinute is <= 0)
        {
            errors.Add("Rate limit per minute must be greater than zero.");
        }

        if (options.RateLimitPerHour is <= 0)
        {
            errors.Add("Rate limit per hour must be greater than zero.");
        }

        // Check for duplicate priorities against other providers
        if (options.Priority.HasValue)
        {
            var providers = await GetBackfillProvidersConfigAsync(ct);
            foreach (var (otherId, otherOpts) in EnumerateProviders(providers))
            {
                if (string.Equals(otherId, NormalizeProviderId(providerId), StringComparison.OrdinalIgnoreCase))
                    continue;
                if (otherOpts?.Enabled == true && otherOpts.Priority == options.Priority)
                {
                    warnings.Add($"Priority {options.Priority} is also used by '{otherId}'. Fallback order may be ambiguous.");
                    break;
                }
            }
        }

        return new InlineValidationResult
        {
            Errors = errors.ToArray(),
            Warnings = warnings.ToArray(),
        };
    }

    /// <summary>
    /// Adds or updates a single backfill provider configuration entry.
    /// Records the change in the audit trail.
    /// </summary>
    public new async Task SetBackfillProviderOptionsAsync(string providerId, BackfillProviderOptionsDto options, CancellationToken ct = default)
    {
        var (previousJson, newJson) = await base.SetBackfillProviderOptionsAsync(providerId, options);

        Ui.Services.BackfillProviderConfigService.Instance.RecordAuditEntry(
            NormalizeProviderId(providerId),
            "update",
            previousJson,
            newJson);
    }

    /// <summary>
    /// Resets a provider's configuration back to defaults.
    /// </summary>
    public async Task ResetBackfillProviderOptionsAsync(string providerId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(providerId))
        {
            throw new ArgumentException("Provider id is required", nameof(providerId));
        }

        var normalizedId = NormalizeProviderId(providerId);
        var defaultOptions = await Ui.Services.BackfillProviderConfigService.Instance
            .GetDefaultOptionsAsync(normalizedId);

        var previousJson = await base.ResetBackfillProviderOptionsAsync(providerId, defaultOptions);

        Ui.Services.BackfillProviderConfigService.Instance.RecordAuditEntry(
            normalizedId,
            "reset",
            previousJson,
            JsonSerializer.Serialize(defaultOptions, SharedJsonOptions));
    }

    protected override async Task<AppConfigDto?> LoadConfigCoreAsync(CancellationToken ct)
    {
        try
        {
            if (!File.Exists(ConfigPath))
            {
                return new AppConfigDto();
            }

            var json = await File.ReadAllTextAsync(ConfigPath, ct);
            if (string.IsNullOrWhiteSpace(json))
            {
                return new AppConfigDto();
            }

            var config = JsonSerializer.Deserialize<AppConfigDto>(json, SharedJsonOptions) ?? new AppConfigDto();
            config.DataRoot = MeridianPathDefaults.ResolveConfiguredDataRootFromJson(json, config.DataRoot);
            return config;
        }
        catch (Exception ex)
        {
            LogError("Failed to load configuration", ex);
            return new AppConfigDto();
        }
    }

    protected override async Task SaveConfigCoreAsync(AppConfigDto config, CancellationToken ct)
    {
        try
        {
            var directory = Path.GetDirectoryName(ConfigPath);
            if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            config.DataRoot = string.IsNullOrWhiteSpace(config.DataRoot)
                ? MeridianPathDefaults.DefaultDataRoot
                : config.DataRoot;

            var json = JsonSerializer.Serialize(config, SharedJsonOptions);
            await File.WriteAllTextAsync(ConfigPath, json, ct);
        }
        catch (Exception ex)
        {
            LogError("Failed to save configuration", ex);
            throw;
        }
    }

    protected override void LogError(string message, Exception? exception)
    {
        LoggingService.Instance.LogError(message, exception);
    }

    // Keep backward-compatible internal method for existing callers
    internal Task<AppConfigDto?> LoadConfigAsync()
        => LoadConfigCoreAsync(CancellationToken.None);
}
