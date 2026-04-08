using System.Text.Json;
using Meridian.Application.Backfill;
using Meridian.Application.Config;
using Meridian.Application.Monitoring;
using Meridian.Contracts.Configuration;
using Meridian.Infrastructure.Contracts;
using Meridian.Storage;

namespace Meridian.Application.UI;

/// <summary>
/// Service for loading and persisting application configuration.
/// Provides access to config files, status files, and provider metrics.
/// This is the single implementation shared by console, web, and desktop hosts.
/// </summary>
/// <remarks>
/// <para><b>Configuration Path Resolution:</b></para>
/// <list type="bullet">
/// <item><description>If configPath is provided, it is used directly</description></item>
/// <item><description>If configPath is null, uses PathResolver delegate if set</description></item>
/// <item><description>Otherwise searches the current directory and its ancestors for config/appsettings.json or appsettings.json</description></item>
/// </list>
/// </remarks>
[ImplementsAdr("ADR-001", "Consolidated configuration store shared by all hosts")]
public sealed class ConfigStore
{
    /// <summary>
    /// Default path resolver that returns the nearest standard Meridian configuration path.
    /// </summary>
    public static Func<string> DefaultPathResolver { get; set; } = ResolveDefaultPath;

    /// <summary>
    /// Gets the path to the configuration file.
    /// </summary>
    public string ConfigPath { get; }

    /// <summary>
    /// Creates a new ConfigStore with the default configuration path resolution.
    /// </summary>
    public ConfigStore()
    {
        ConfigPath = ResolvePath(null);
    }

    /// <summary>
    /// Creates a new ConfigStore with a custom configuration path.
    /// </summary>
    /// <param name="configPath">Full or relative path to the configuration file.</param>
    public ConfigStore(string? configPath)
    {
        ConfigPath = ResolvePath(configPath);
    }

    private static string ResolvePath(string? configPath)
    {
        var path = configPath ?? DefaultPathResolver();
        return Path.IsPathRooted(path) ? path : Path.GetFullPath(path);
    }

    private static string ResolveDefaultPath()
        => DefaultConfigPathResolver.Resolve();

    /// <summary>
    /// Static method to load configuration from a path.
    /// Used by ConfigurationService.LoadAndPrepareConfig() for consolidated config loading.
    /// </summary>
    public static AppConfig LoadConfig(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                Console.WriteLine($"[Warning] Configuration file not found: {path}");
                Console.WriteLine("Using default configuration. Copy config/appsettings.sample.json to config/appsettings.json to customize.");
                return CreateDefaultConfig(path);
            }

            var json = File.ReadAllText(path);
            var config = JsonSerializer.Deserialize<AppConfig>(json, AppConfigJsonOptions.Read);
            return NormalizeLoadedConfig(path, json, config);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Error] Failed to load configuration: {ex.Message}");
            return CreateDefaultConfig(path);
        }
    }

    /// <summary>
    /// Instance method to load configuration from the configured path.
    /// Delegates to the static LoadConfig method.
    /// </summary>
    public AppConfig Load() => LoadConfig(ConfigPath);

    public async Task SaveAsync(AppConfig cfg, CancellationToken ct = default)
    {
        try
        {
            var json = JsonSerializer.Serialize(cfg, AppConfigJsonOptions.Write);
            var dir = Path.GetDirectoryName(ConfigPath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }
            await File.WriteAllTextAsync(ConfigPath, json, ct);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to save configuration: {ex.Message}", ex);
        }
    }

    public string? TryLoadStatusJson()
    {
        try
        {
            var statusPath = GetStatusPath();
            return File.Exists(statusPath) ? File.ReadAllText(statusPath) : null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    public string GetStatusPath(AppConfig? cfg = null)
    {
        cfg ??= Load();
        var root = GetDataRoot(cfg);
        return Path.Combine(root, "_status", "status.json");
    }

    public string GetBackfillStatusPath(AppConfig? cfg = null)
    {
        cfg ??= Load();
        var root = GetDataRoot(cfg);
        return Path.Combine(root, "_status", "backfill.json");
    }

    public BackfillResult? TryLoadBackfillStatus()
    {
        var cfg = Load();
        var store = new BackfillStatusStore(GetDataRoot(cfg));
        return store.TryRead();
    }

    public string GetDataRoot(AppConfig? cfg = null)
    {
        cfg ??= Load();
        return MeridianPathDefaults.ResolveDataRoot(ConfigPath, cfg.DataRoot);
    }

    public string GetProviderMetricsPath(AppConfig? cfg = null)
    {
        cfg ??= Load();
        var root = GetDataRoot(cfg);
        return Path.Combine(root, "_status", "providers.json");
    }

    public ProviderMetricsStatus? TryLoadProviderMetrics()
    {
        try
        {
            var path = GetProviderMetricsPath();
            if (!File.Exists(path))
                return null;

            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<ProviderMetricsStatus>(json, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static AppConfig CreateDefaultConfig(string path)
        => new(DataRoot: MeridianPathDefaults.ResolveDataRoot(path, null));

    private static AppConfig NormalizeLoadedConfig(string path, string json, AppConfig? config)
    {
        var configuredDataRoot = MeridianPathDefaults.ResolveConfiguredDataRootFromJson(
            json,
            config?.DataRoot);

        var resolvedDataRoot = MeridianPathDefaults.ResolveDataRoot(path, configuredDataRoot);
        var effectiveConfig = config ?? new AppConfig();

        return effectiveConfig with { DataRoot = resolvedDataRoot };
    }
}
