using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Meridian.Contracts.Configuration;

namespace Meridian.Ui.Services.Services;

/// <summary>
/// Result of configuration validation.
/// Platform-agnostic version for shared use.
/// </summary>
public sealed class ConfigValidationResultDetail
{
    public bool IsValid { get; set; } = true;
    public string[] Errors { get; set; } = Array.Empty<string>();
    public string[] Warnings { get; set; } = Array.Empty<string>();

    public static ConfigValidationResultDetail Success() => new() { IsValid = true };
    public static ConfigValidationResultDetail Failure(params string[] errors) => new()
    {
        IsValid = false,
        Errors = errors
    };
}

/// <summary>
/// Abstract base class for configuration management shared between platforms.
/// Provides platform-agnostic config validation, backfill provider management,
/// data source management, and symbol management.
/// Platform-specific config file I/O is delegated to derived classes.
/// Part of Phase 2 service extraction.
/// </summary>
public abstract class ConfigServiceBase
{
    protected static readonly JsonSerializerOptions SharedJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true
    };

    /// <summary>
    /// Gets the path to the configuration file.
    /// </summary>
    public abstract string ConfigPath { get; }

    /// <summary>
    /// Loads the application configuration DTO from persistent storage.
    /// </summary>
    protected abstract Task<AppConfigDto?> LoadConfigCoreAsync(CancellationToken ct = default);

    /// <summary>
    /// Saves the application configuration DTO to persistent storage.
    /// </summary>
    protected abstract Task SaveConfigCoreAsync(AppConfigDto config, CancellationToken ct = default);

    /// <summary>
    /// Logs an error message. Delegated to platform-specific logging.
    /// </summary>
    protected abstract void LogError(string message, Exception? exception = null);

    /// <summary>
    /// Validates the current configuration.
    /// </summary>
    public async Task<ConfigValidationResultDetail> ValidateConfigDetailAsync(CancellationToken ct = default)
    {
        var config = await LoadConfigCoreAsync(ct) ?? new AppConfigDto();
        var errors = new List<string>();
        var warnings = new List<string>();

        var backfill = config.Backfill;
        var providers = backfill?.Providers;

        if (backfill?.Enabled == true && providers == null)
        {
            warnings.Add("Backfill is enabled but no per-provider settings are configured. Defaults will be used.");
        }

        if (providers != null)
        {
            var providerEntries = EnumerateProviders(providers).ToList();
            var enabledEntries = providerEntries.Where(p => p.Options?.Enabled ?? false).ToList();

            if (backfill?.Enabled == true && enabledEntries.Count == 0)
            {
                errors.Add("Backfill is enabled but all historical providers are disabled.");
            }

            foreach (var (providerId, options) in providerEntries)
            {
                if (options == null)
                    continue;

                if (options.Priority is < 0)
                {
                    errors.Add($"Provider '{providerId}' has invalid priority {options.Priority}. Priority must be >= 0.");
                }

                if (options.RateLimitPerMinute is <= 0)
                {
                    errors.Add($"Provider '{providerId}' has invalid rateLimitPerMinute {options.RateLimitPerMinute}. Value must be > 0.");
                }

                if (options.RateLimitPerHour is <= 0)
                {
                    errors.Add($"Provider '{providerId}' has invalid rateLimitPerHour {options.RateLimitPerHour}. Value must be > 0.");
                }
            }

            var duplicatePriorityGroups = enabledEntries
                .Where(p => p.Options?.Priority != null)
                .GroupBy(p => p.Options!.Priority!.Value)
                .Where(g => g.Count() > 1)
                .ToList();

            foreach (var group in duplicatePriorityGroups)
            {
                var providerList = string.Join(", ", group.Select(g => g.ProviderId));
                warnings.Add($"Enabled providers share priority {group.Key}: {providerList}. Fallback order may be ambiguous.");
            }
        }

        return new ConfigValidationResultDetail
        {
            IsValid = errors.Count == 0,
            Errors = errors.ToArray(),
            Warnings = warnings.ToArray()
        };
    }

    /// <summary>
    /// Gets the data sources configuration.
    /// </summary>
    public async Task<DataSourcesConfigDto> GetDataSourcesConfigDtoAsync(CancellationToken ct = default)
    {
        var config = await LoadConfigCoreAsync(ct) ?? new AppConfigDto();
        return config.DataSources ?? new DataSourcesConfigDto();
    }

    /// <summary>
    /// Adds or updates a data source configuration.
    /// </summary>
    public async Task AddOrUpdateDataSourceAsync(DataSourceConfigDto dataSource, CancellationToken ct = default)
    {
        var config = await LoadConfigCoreAsync(ct) ?? new AppConfigDto();
        var dataSources = config.DataSources ?? new DataSourcesConfigDto();
        var sources = dataSources.Sources?.ToList() ?? new List<DataSourceConfigDto>();

        var existingIndex = sources.FindIndex(s =>
            string.Equals(s.Id, dataSource.Id, StringComparison.OrdinalIgnoreCase));

        if (existingIndex >= 0)
        {
            sources[existingIndex] = dataSource;
        }
        else
        {
            sources.Add(dataSource);
        }

        dataSources.Sources = sources.ToArray();
        config.DataSources = dataSources;
        await SaveConfigCoreAsync(config, ct);
    }

    /// <summary>
    /// Deletes a data source by ID.
    /// </summary>
    public async Task DeleteDataSourceAsync(string id, CancellationToken ct = default)
    {
        var config = await LoadConfigCoreAsync(ct) ?? new AppConfigDto();
        var dataSources = config.DataSources ?? new DataSourcesConfigDto();
        var sources = dataSources.Sources?.ToList() ?? new List<DataSourceConfigDto>();

        sources.RemoveAll(s => string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase));

        dataSources.Sources = sources.ToArray();
        config.DataSources = dataSources;
        await SaveConfigCoreAsync(config, ct);
    }

    /// <summary>
    /// Sets the default data source for real-time or historical data.
    /// </summary>
    public async Task SetDefaultDataSourceAsync(string id, bool isHistorical, CancellationToken ct = default)
    {
        var config = await LoadConfigCoreAsync(ct) ?? new AppConfigDto();
        var dataSources = config.DataSources ?? new DataSourcesConfigDto();

        if (isHistorical)
        {
            dataSources.DefaultHistoricalSourceId = id;
        }
        else
        {
            dataSources.DefaultRealTimeSourceId = id;
        }

        config.DataSources = dataSources;
        await SaveConfigCoreAsync(config, ct);
    }

    /// <summary>
    /// Updates failover settings for data sources.
    /// </summary>
    public async Task UpdateFailoverSettingsAsync(bool enableFailover, int failoverTimeoutSeconds, CancellationToken ct = default)
    {
        var config = await LoadConfigCoreAsync(ct) ?? new AppConfigDto();
        var dataSources = config.DataSources ?? new DataSourcesConfigDto();

        dataSources.EnableFailover = enableFailover;
        dataSources.FailoverTimeoutSeconds = failoverTimeoutSeconds;

        config.DataSources = dataSources;
        await SaveConfigCoreAsync(config, ct);
    }

    /// <summary>
    /// Gets backfill provider configuration, creating defaults when missing.
    /// </summary>
    public async Task<BackfillProvidersConfigDto> GetBackfillProvidersConfigAsync(CancellationToken ct = default)
    {
        var config = await LoadConfigCoreAsync(ct) ?? new AppConfigDto();
        config.Backfill ??= new BackfillConfigDto();
        config.Backfill.Providers ??= new BackfillProvidersConfigDto();
        return config.Backfill.Providers;
    }

    /// <summary>
    /// Gets options for a single historical backfill provider.
    /// </summary>
    public async Task<BackfillProviderOptionsDto?> GetBackfillProviderOptionsAsync(string providerId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(providerId))
        {
            throw new ArgumentException("Provider id is required", nameof(providerId));
        }

        var providers = await GetBackfillProvidersConfigAsync(ct);
        return GetProviderOptions(providers, providerId);
    }

    /// <summary>
    /// Sets backfill provider options.
    /// Returns the previous and new JSON for audit trail purposes.
    /// </summary>
    public async Task<(string? PreviousJson, string NewJson)> SetBackfillProviderOptionsAsync(
        string providerId,
        BackfillProviderOptionsDto options,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(providerId))
        {
            throw new ArgumentException("Provider id is required", nameof(providerId));
        }

        ArgumentNullException.ThrowIfNull(options);
        ValidateProviderOptions(providerId, options);

        var config = await LoadConfigCoreAsync(ct) ?? new AppConfigDto();
        config.Backfill ??= new BackfillConfigDto();
        config.Backfill.Providers ??= new BackfillProvidersConfigDto();

        var previousOptions = GetProviderOptions(config.Backfill.Providers, NormalizeProviderId(providerId));
        var previousJson = previousOptions != null
            ? JsonSerializer.Serialize(previousOptions, SharedJsonOptions)
            : null;
        var newJson = JsonSerializer.Serialize(options, SharedJsonOptions);

        SetProviderOptions(config.Backfill.Providers, providerId, options);
        await SaveConfigCoreAsync(config, ct);

        return (previousJson, newJson);
    }

    /// <summary>
    /// Resets a provider's configuration back to defaults.
    /// Returns the previous JSON for audit trail purposes.
    /// </summary>
    public async Task<string?> ResetBackfillProviderOptionsAsync(
        string providerId,
        BackfillProviderOptionsDto defaultOptions,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(providerId))
        {
            throw new ArgumentException("Provider id is required", nameof(providerId));
        }

        var config = await LoadConfigCoreAsync(ct) ?? new AppConfigDto();
        config.Backfill ??= new BackfillConfigDto();
        config.Backfill.Providers ??= new BackfillProvidersConfigDto();

        var previousOptions = GetProviderOptions(config.Backfill.Providers, NormalizeProviderId(providerId));
        var previousJson = previousOptions != null
            ? JsonSerializer.Serialize(previousOptions, SharedJsonOptions)
            : null;

        SetProviderOptions(config.Backfill.Providers, providerId, defaultOptions);
        await SaveConfigCoreAsync(config, ct);

        return previousJson;
    }

    /// <summary>
    /// Gets the configured symbols from configuration.
    /// </summary>
    public async Task<SymbolConfigDto[]> GetConfiguredSymbolsAsync(CancellationToken ct = default)
    {
        var config = await LoadConfigCoreAsync(ct) ?? new AppConfigDto();
        return config.Symbols ?? Array.Empty<SymbolConfigDto>();
    }

    /// <summary>
    /// Saves symbols to the configuration.
    /// </summary>
    public async Task SaveSymbolsAsync(SymbolConfigDto[] symbols, CancellationToken ct = default)
    {
        var config = await LoadConfigCoreAsync(ct) ?? new AppConfigDto();
        config.Symbols = symbols;
        await SaveConfigCoreAsync(config, ct);
    }

    /// <summary>
    /// Adds a symbol to the configuration.
    /// </summary>
    public async Task AddSymbolAsync(SymbolConfigDto symbol, CancellationToken ct = default)
    {
        var config = await LoadConfigCoreAsync(ct) ?? new AppConfigDto();
        var existing = config.Symbols?.ToList() ?? new List<SymbolConfigDto>();
        if (existing.All(s => !string.Equals(s.Symbol, symbol.Symbol, StringComparison.OrdinalIgnoreCase)))
        {
            existing.Add(symbol);
            config.Symbols = existing.ToArray();
            await SaveConfigCoreAsync(config, ct);
        }
    }

    /// <summary>
    /// Removes a symbol from the configuration.
    /// </summary>
    public async Task RemoveSymbolAsync(string symbolName, CancellationToken ct = default)
    {
        var config = await LoadConfigCoreAsync(ct) ?? new AppConfigDto();
        var existing = config.Symbols?.ToList() ?? new List<SymbolConfigDto>();
        existing.RemoveAll(s => string.Equals(s.Symbol, symbolName, StringComparison.OrdinalIgnoreCase));
        config.Symbols = existing.ToArray();
        await SaveConfigCoreAsync(config, ct);
    }


    protected internal static IEnumerable<(string ProviderId, BackfillProviderOptionsDto? Options)> EnumerateProviders(BackfillProvidersConfigDto providers)
    {
        yield return ("alpaca", providers.Alpaca);
        yield return ("polygon", providers.Polygon);
        yield return ("tiingo", providers.Tiingo);
        yield return ("finnhub", providers.Finnhub);
        yield return ("stooq", providers.Stooq);
        yield return ("yahoo", providers.Yahoo);
        yield return ("alphavantage", providers.AlphaVantage);
        yield return ("nasdaqdatalink", providers.NasdaqDataLink);
    }

    internal static BackfillProviderOptionsDto? GetProviderOptions(BackfillProvidersConfigDto providers, string providerId)
    {
        return NormalizeProviderId(providerId) switch
        {
            "alpaca" => providers.Alpaca,
            "polygon" => providers.Polygon,
            "tiingo" => providers.Tiingo,
            "finnhub" => providers.Finnhub,
            "stooq" => providers.Stooq,
            "yahoo" => providers.Yahoo,
            "alphavantage" => providers.AlphaVantage,
            "nasdaqdatalink" => providers.NasdaqDataLink,
            _ => throw new ArgumentOutOfRangeException(nameof(providerId), providerId, "Unknown backfill provider id")
        };
    }

    internal static void SetProviderOptions(BackfillProvidersConfigDto providers, string providerId, BackfillProviderOptionsDto options)
    {
        switch (NormalizeProviderId(providerId))
        {
            case "alpaca":
                providers.Alpaca = options;
                break;
            case "polygon":
                providers.Polygon = options;
                break;
            case "tiingo":
                providers.Tiingo = options;
                break;
            case "finnhub":
                providers.Finnhub = options;
                break;
            case "stooq":
                providers.Stooq = options;
                break;
            case "yahoo":
                providers.Yahoo = options;
                break;
            case "alphavantage":
                providers.AlphaVantage = options;
                break;
            case "nasdaqdatalink":
                providers.NasdaqDataLink = options;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(providerId), providerId, "Unknown backfill provider id");
        }
    }

    public static string NormalizeProviderId(string providerId)
    {
        var normalized = providerId.Trim().ToLowerInvariant();
        return normalized switch
        {
            "yahoofinance" => "yahoo",
            "nasdaq" => "nasdaqdatalink",
            _ => normalized
        };
    }

    internal static void ValidateProviderOptions(string providerId, BackfillProviderOptionsDto options)
    {
        if (options.Priority is < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), options.Priority, $"Priority for provider '{providerId}' must be >= 0.");
        }

        if (options.RateLimitPerMinute is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), options.RateLimitPerMinute, $"RateLimitPerMinute for provider '{providerId}' must be > 0.");
        }

        if (options.RateLimitPerHour is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), options.RateLimitPerHour, $"RateLimitPerHour for provider '{providerId}' must be > 0.");
        }
    }

}
