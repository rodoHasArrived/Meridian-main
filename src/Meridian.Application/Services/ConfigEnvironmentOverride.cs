using System.Reflection;
using System.Text.Json;
using Meridian.Application.Config;
using Meridian.Application.Logging;
using Serilog;

namespace Meridian.Application.Services;

/// <summary>
/// Service for applying environment variable overrides to configuration.
/// Implements QW-25: Config Environment Override.
/// </summary>
public sealed class ConfigEnvironmentOverride
{
    private readonly ILogger _log = LoggingSetup.ForContext<ConfigEnvironmentOverride>();

    /// <summary>
    /// Environment variable prefix for configuration overrides.
    /// </summary>
    public const string EnvPrefix = "MDC_";

    /// <summary>
    /// Mapping of environment variables to configuration paths.
    /// </summary>
    private static readonly Dictionary<string, string> EnvToConfigMapping = new(StringComparer.OrdinalIgnoreCase)
    {
        // Core settings
        ["MDC_DATA_ROOT"] = "DataRoot",
        ["MDC_COMPRESS"] = "Compress",
        ["MDC_DATASOURCE"] = "DataSource",
        ["MDC_SYNTHETIC_MODE"] = "Synthetic:Enabled",

        // Alpaca settings
        ["MDC_ALPACA_KEY_ID"] = "Alpaca:KeyId",
        ["MDC_ALPACA_SECRET_KEY"] = "Alpaca:SecretKey",
        ["MDC_ALPACA_FEED"] = "Alpaca:Feed",
        ["MDC_ALPACA_SANDBOX"] = "Alpaca:UseSandbox",
        ["MDC_ALPACA_QUOTES"] = "Alpaca:SubscribeQuotes",

        // Legacy Alpaca env vars (without MDC_ prefix)
        ["ALPACA_KEY_ID"] = "Alpaca:KeyId",
        ["ALPACA_SECRET_KEY"] = "Alpaca:SecretKey",

        // Storage settings
        ["MDC_STORAGE_NAMING"] = "Storage:NamingConvention",
        ["MDC_STORAGE_PARTITION"] = "Storage:DatePartition",
        ["MDC_STORAGE_RETENTION_DAYS"] = "Storage:RetentionDays",
        ["MDC_STORAGE_MAX_MB"] = "Storage:MaxTotalMegabytes",

        // Backfill settings
        ["MDC_BACKFILL_ENABLED"] = "Backfill:Enabled",
        ["MDC_BACKFILL_PROVIDER"] = "Backfill:Provider",
        ["MDC_BACKFILL_SYMBOLS"] = "Backfill:Symbols",
        ["MDC_BACKFILL_FROM"] = "Backfill:From",
        ["MDC_BACKFILL_TO"] = "Backfill:To",

        // Provider API keys
        ["POLYGON_API_KEY"] = "Backfill:Providers:Polygon:ApiKey",
        ["TIINGO_API_TOKEN"] = "Backfill:Providers:Tiingo:ApiToken",
        ["FINNHUB_API_KEY"] = "Backfill:Providers:Finnhub:ApiKey",
        ["ALPHA_VANTAGE_API_KEY"] = "Backfill:Providers:AlphaVantage:ApiKey",

        // StockSharp core settings
        ["MDC_STOCKSHARP_ENABLED"] = "StockSharp:Enabled",
        ["MDC_STOCKSHARP_CONNECTOR"] = "StockSharp:ConnectorType",
        ["MDC_STOCKSHARP_ADAPTER_TYPE"] = "StockSharp:AdapterType",
        ["MDC_STOCKSHARP_ADAPTER_ASSEMBLY"] = "StockSharp:AdapterAssembly",
        ["MDC_STOCKSHARP_STORAGE_PATH"] = "StockSharp:StoragePath",
        ["MDC_STOCKSHARP_BINARY"] = "StockSharp:UseBinaryStorage",
        ["MDC_STOCKSHARP_REALTIME"] = "StockSharp:EnableRealTime",
        ["MDC_STOCKSHARP_HISTORICAL"] = "StockSharp:EnableHistorical",

        // StockSharp - Rithmic
        ["MDC_STOCKSHARP_RITHMIC_SERVER"] = "StockSharp:Rithmic:Server",
        ["MDC_STOCKSHARP_RITHMIC_USERNAME"] = "StockSharp:Rithmic:UserName",
        ["MDC_STOCKSHARP_RITHMIC_PASSWORD"] = "StockSharp:Rithmic:Password",
        ["MDC_STOCKSHARP_RITHMIC_CERTFILE"] = "StockSharp:Rithmic:CertFile",
        ["MDC_STOCKSHARP_RITHMIC_PAPER"] = "StockSharp:Rithmic:UsePaperTrading",

        // StockSharp - IQFeed
        ["MDC_STOCKSHARP_IQFEED_HOST"] = "StockSharp:IQFeed:Host",
        ["MDC_STOCKSHARP_IQFEED_LEVEL1_PORT"] = "StockSharp:IQFeed:Level1Port",
        ["MDC_STOCKSHARP_IQFEED_LEVEL2_PORT"] = "StockSharp:IQFeed:Level2Port",
        ["MDC_STOCKSHARP_IQFEED_LOOKUP_PORT"] = "StockSharp:IQFeed:LookupPort",
        ["MDC_STOCKSHARP_IQFEED_PRODUCT_ID"] = "StockSharp:IQFeed:ProductId",
        ["MDC_STOCKSHARP_IQFEED_PRODUCT_VERSION"] = "StockSharp:IQFeed:ProductVersion",

        // StockSharp - CQG
        ["MDC_STOCKSHARP_CQG_USERNAME"] = "StockSharp:CQG:UserName",
        ["MDC_STOCKSHARP_CQG_PASSWORD"] = "StockSharp:CQG:Password",
        ["MDC_STOCKSHARP_CQG_DEMO"] = "StockSharp:CQG:UseDemoServer",

        // StockSharp - Interactive Brokers
        ["MDC_STOCKSHARP_IB_HOST"] = "StockSharp:InteractiveBrokers:Host",
        ["MDC_STOCKSHARP_IB_PORT"] = "StockSharp:InteractiveBrokers:Port",
        ["MDC_STOCKSHARP_IB_CLIENT_ID"] = "StockSharp:InteractiveBrokers:ClientId"
    };

    /// <summary>
    /// Applies environment variable overrides to configuration.
    /// </summary>
    public AppConfig ApplyOverrides(AppConfig config)
    {
        var appliedOverrides = new List<string>();
        var result = config;

        foreach (var (envVar, configPath) in EnvToConfigMapping)
        {
            var value = Environment.GetEnvironmentVariable(envVar);
            if (string.IsNullOrEmpty(value))
                continue;

            try
            {
                result = ApplyOverride(result, configPath, value);
                appliedOverrides.Add($"{envVar} -> {configPath}");
            }
            catch (Exception ex)
            {
                _log.Warning(ex, "Failed to apply environment override {EnvVar} to {Path}", envVar, configPath);
            }
        }

        // Also check for generic MDC_ prefixed variables
        foreach (System.Collections.DictionaryEntry env in Environment.GetEnvironmentVariables())
        {
            var key = env.Key.ToString();
            if (key == null || !key.StartsWith(EnvPrefix, StringComparison.OrdinalIgnoreCase))
                continue;

            // Skip already mapped variables
            if (EnvToConfigMapping.ContainsKey(key))
                continue;

            // Convert MDC_SOME_SETTING to Some:Setting
            var configPath = ConvertEnvVarToConfigPath(key);
            var value = env.Value?.ToString();

            if (string.IsNullOrEmpty(value))
                continue;

            try
            {
                result = ApplyOverride(result, configPath, value);
                appliedOverrides.Add($"{key} -> {configPath}");
            }
            catch (Exception ex)
            {
                _log.Debug(ex, "Could not apply generic override {EnvVar} to {Path}", key, configPath);
            }
        }

        if (appliedOverrides.Count > 0)
        {
            _log.Information("Applied {Count} environment variable overrides: {Overrides}",
                appliedOverrides.Count, string.Join(", ", appliedOverrides));
        }

        return result;
    }

    /// <summary>
    /// Gets all recognized environment variables and their current values.
    /// </summary>
    public IReadOnlyList<EnvironmentOverrideInfo> GetRecognizedVariables()
    {
        var variables = new List<EnvironmentOverrideInfo>();

        foreach (var (envVar, configPath) in EnvToConfigMapping)
        {
            var value = Environment.GetEnvironmentVariable(envVar);
            var isSensitive = IsSensitiveVariable(envVar);

            variables.Add(new EnvironmentOverrideInfo
            {
                EnvironmentVariable = envVar,
                ConfigPath = configPath,
                CurrentValue = isSensitive && !string.IsNullOrEmpty(value) ? "[SET]" : value,
                IsSet = !string.IsNullOrEmpty(value),
                IsSensitive = isSensitive
            });
        }

        return variables.OrderBy(v => v.EnvironmentVariable).ToList();
    }

    /// <summary>
    /// Gets documentation for all supported environment variables.
    /// </summary>
    public string GetDocumentation()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("# Environment Variable Configuration");
        sb.AppendLine();
        sb.AppendLine("The following environment variables can be used to override configuration:");
        sb.AppendLine();

        var groups = EnvToConfigMapping
            .GroupBy(kvp => GetCategory(kvp.Key))
            .OrderBy(g => g.Key);

        foreach (var group in groups)
        {
            sb.AppendLine($"## {group.Key}");
            sb.AppendLine();

            foreach (var (envVar, configPath) in group.OrderBy(kvp => kvp.Key))
            {
                var desc = GetVariableDescription(envVar);
                sb.AppendLine($"- `{envVar}` -> `{configPath}`");
                if (!string.IsNullOrEmpty(desc))
                    sb.AppendLine($"  {desc}");
            }

            sb.AppendLine();
        }

        return sb.ToString();
    }

    private AppConfig ApplyOverride(AppConfig config, string path, string value)
    {
        var parts = path.Split(':');

        return parts[0] switch
        {
            "DataRoot" => config with { DataRoot = value },
            "Compress" => config with { Compress = ParseBool(value) },
            "DataSource" => config with { DataSource = ParseDataSource(value) },
            "Synthetic" => ApplySyntheticOverride(config, parts.Skip(1).ToArray(), value),
            "Alpaca" => ApplyAlpacaOverride(config, parts.Skip(1).ToArray(), value),
            "StockSharp" => ApplyStockSharpOverride(config, parts.Skip(1).ToArray(), value),
            "Storage" => ApplyStorageOverride(config, parts.Skip(1).ToArray(), value),
            "Backfill" => ApplyBackfillOverride(config, parts.Skip(1).ToArray(), value),
            _ => config
        };
    }


    private AppConfig ApplySyntheticOverride(AppConfig config, string[] path, string value)
    {
        var synthetic = config.Synthetic ?? new SyntheticMarketDataConfig();

        if (path.Length == 0)
            return config;

        synthetic = path[0] switch
        {
            "Enabled" => synthetic with { Enabled = ParseBool(value) },
            _ => synthetic
        };

        if (path[0].Equals("Enabled", StringComparison.OrdinalIgnoreCase) && ParseBool(value))
        {
            var backfill = config.Backfill ?? new BackfillConfig();
            var providers = backfill.Providers ?? new BackfillProvidersConfig();
            providers = providers with { Synthetic = synthetic };
            backfill = backfill with { Providers = providers };
            return config with { Synthetic = synthetic, Backfill = backfill, DataSource = DataSourceKind.Synthetic };
        }

        return config with { Synthetic = synthetic };
    }

    private AppConfig ApplyAlpacaOverride(AppConfig config, string[] path, string value)
    {
        var alpaca = config.Alpaca ?? new AlpacaOptions();

        if (path.Length == 0)
            return config;

        alpaca = path[0] switch
        {
            "KeyId" => alpaca with { KeyId = value },
            "SecretKey" => alpaca with { SecretKey = value },
            "Feed" => alpaca with { Feed = value },
            "UseSandbox" => alpaca with { UseSandbox = ParseBool(value) },
            "SubscribeQuotes" => alpaca with { SubscribeQuotes = ParseBool(value) },
            _ => alpaca
        };

        return config with { Alpaca = alpaca };
    }

    private AppConfig ApplyStorageOverride(AppConfig config, string[] path, string value)
    {
        var storage = config.Storage ?? new StorageConfig();

        if (path.Length == 0)
            return config;

        storage = path[0] switch
        {
            "NamingConvention" => storage with { NamingConvention = value },
            "DatePartition" => storage with { DatePartition = value },
            "RetentionDays" => storage with { RetentionDays = ParseInt(value) },
            "MaxTotalMegabytes" => storage with { MaxTotalMegabytes = ParseLong(value) },
            _ => storage
        };

        return config with { Storage = storage };
    }

    private AppConfig ApplyBackfillOverride(AppConfig config, string[] path, string value)
    {
        var backfill = config.Backfill ?? new BackfillConfig();

        if (path.Length == 0)
            return config;

        if (path[0] == "Providers" && path.Length >= 3)
        {
            // Handle provider-specific settings
            var providers = backfill.Providers ?? new BackfillProvidersConfig();
            // For simplicity, we'll skip nested provider config in this implementation
            return config;
        }

        backfill = path[0] switch
        {
            "Enabled" => backfill with { Enabled = ParseBool(value) },
            "Provider" => backfill with { Provider = value },
            "Symbols" => backfill with { Symbols = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) },
            "From" => backfill with { From = DateOnly.TryParse(value, out var from) ? from : backfill.From },
            "To" => backfill with { To = DateOnly.TryParse(value, out var to) ? to : backfill.To },
            _ => backfill
        };

        return config with { Backfill = backfill };
    }

    private AppConfig ApplyStockSharpOverride(AppConfig config, string[] path, string value)
    {
        var stockSharp = config.StockSharp ?? new StockSharpConfig();

        if (path.Length == 0)
            return config;

        stockSharp = path[0] switch
        {
            "Enabled" => stockSharp with { Enabled = ParseBool(value) },
            "ConnectorType" => stockSharp with { ConnectorType = value },
            "AdapterType" => stockSharp with { AdapterType = value },
            "AdapterAssembly" => stockSharp with { AdapterAssembly = value },
            "StoragePath" => stockSharp with { StoragePath = value },
            "UseBinaryStorage" => stockSharp with { UseBinaryStorage = ParseBool(value) },
            "EnableRealTime" => stockSharp with { EnableRealTime = ParseBool(value) },
            "EnableHistorical" => stockSharp with { EnableHistorical = ParseBool(value) },
            "Rithmic" => ApplyRithmicOverride(stockSharp, path.Skip(1).ToArray(), value),
            "IQFeed" => ApplyIqFeedOverride(stockSharp, path.Skip(1).ToArray(), value),
            "CQG" => ApplyCqgOverride(stockSharp, path.Skip(1).ToArray(), value),
            "InteractiveBrokers" => ApplyStockSharpIbOverride(stockSharp, path.Skip(1).ToArray(), value),
            _ => stockSharp
        };

        return config with { StockSharp = stockSharp };
    }

    private static StockSharpConfig ApplyRithmicOverride(StockSharpConfig config, string[] path, string value)
    {
        var rithmic = config.Rithmic ?? new RithmicConfig();

        if (path.Length == 0)
            return config;

        rithmic = path[0] switch
        {
            "Server" => rithmic with { Server = value },
            "UserName" => rithmic with { UserName = value },
            "Password" => rithmic with { Password = value },
            "CertFile" => rithmic with { CertFile = value },
            "UsePaperTrading" => rithmic with { UsePaperTrading = ParseBool(value) },
            _ => rithmic
        };

        return config with { Rithmic = rithmic };
    }

    private static StockSharpConfig ApplyIqFeedOverride(StockSharpConfig config, string[] path, string value)
    {
        var iqFeed = config.IQFeed ?? new IQFeedConfig();

        if (path.Length == 0)
            return config;

        iqFeed = path[0] switch
        {
            "Host" => iqFeed with { Host = value },
            "Level1Port" => iqFeed with { Level1Port = ParseInt(value) ?? iqFeed.Level1Port },
            "Level2Port" => iqFeed with { Level2Port = ParseInt(value) ?? iqFeed.Level2Port },
            "LookupPort" => iqFeed with { LookupPort = ParseInt(value) ?? iqFeed.LookupPort },
            "ProductId" => iqFeed with { ProductId = value },
            "ProductVersion" => iqFeed with { ProductVersion = value },
            _ => iqFeed
        };

        return config with { IQFeed = iqFeed };
    }

    private static StockSharpConfig ApplyCqgOverride(StockSharpConfig config, string[] path, string value)
    {
        var cqg = config.CQG ?? new CQGConfig();

        if (path.Length == 0)
            return config;

        cqg = path[0] switch
        {
            "UserName" => cqg with { UserName = value },
            "Password" => cqg with { Password = value },
            "UseDemoServer" => cqg with { UseDemoServer = ParseBool(value) },
            _ => cqg
        };

        return config with { CQG = cqg };
    }

    private static StockSharpConfig ApplyStockSharpIbOverride(StockSharpConfig config, string[] path, string value)
    {
        var ib = config.InteractiveBrokers ?? new StockSharpIBConfig();

        if (path.Length == 0)
            return config;

        ib = path[0] switch
        {
            "Host" => ib with { Host = value },
            "Port" => ib with { Port = ParseInt(value) ?? ib.Port },
            "ClientId" => ib with { ClientId = ParseInt(value) ?? ib.ClientId },
            _ => ib
        };

        return config with { InteractiveBrokers = ib };
    }

    private static string ConvertEnvVarToConfigPath(string envVar)
    {
        // Remove MDC_ prefix
        var path = envVar.Substring(EnvPrefix.Length);

        // Convert SOME_SETTING to Some:Setting
        var parts = path.Split('_');
        return string.Join(":", parts.Select(p =>
            char.ToUpper(p[0]) + p.Substring(1).ToLower()));
    }

    private static bool ParseBool(string value)
    {
        return value.Equals("true", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("1", StringComparison.Ordinal) ||
               value.Equals("yes", StringComparison.OrdinalIgnoreCase);
    }

    private static int? ParseInt(string value)
    {
        return int.TryParse(value, out var result) ? result : null;
    }

    private static long? ParseLong(string value)
    {
        return long.TryParse(value, out var result) ? result : null;
    }

    private static DataSourceKind ParseDataSource(string value)
    {
        return Enum.TryParse<DataSourceKind>(value, ignoreCase: true, out var result)
            ? result
            : DataSourceKind.IB;
    }

    private static bool IsSensitiveVariable(string envVar)
    {
        var sensitivePatterns = new[] { "KEY", "SECRET", "PASSWORD", "TOKEN", "PASS" };
        return sensitivePatterns.Any(p => envVar.Contains(p, StringComparison.OrdinalIgnoreCase));
    }

    private static string GetCategory(string envVar)
    {
        if (envVar.StartsWith("MDC_ALPACA") || envVar.StartsWith("ALPACA"))
            return "Alpaca Configuration";
        if (envVar.StartsWith("MDC_STOCKSHARP"))
            return "StockSharp Configuration";
        if (envVar.StartsWith("MDC_STORAGE"))
            return "Storage Configuration";
        if (envVar.StartsWith("MDC_BACKFILL"))
            return "Backfill Configuration";
        if (envVar.StartsWith("MDC_SYNTHETIC"))
            return "Synthetic Provider";
        if (envVar.Contains("API_KEY") || envVar.Contains("TOKEN"))
            return "API Keys";
        return "Core Configuration";
    }

    private static string GetVariableDescription(string envVar)
    {
        return envVar switch
        {
            "MDC_DATA_ROOT" => "Root directory for data storage",
            "MDC_COMPRESS" => "Enable gzip compression (true/false)",
            "MDC_DATASOURCE" => "Data source provider (IB, Alpaca, Polygon, StockSharp, NYSE, Synthetic)",
            "MDC_SYNTHETIC_MODE" => "Enable the built-in synthetic/offline market data provider",
            "MDC_ALPACA_FEED" => "Alpaca data feed (iex or sip)",
            "MDC_STOCKSHARP_CONNECTOR" => "StockSharp connector type (Rithmic, IQFeed, CQG, InteractiveBrokers, Custom)",
            "MDC_STOCKSHARP_ADAPTER_TYPE" => "StockSharp adapter type for custom connectors",
            "MDC_STOCKSHARP_ADAPTER_ASSEMBLY" => "StockSharp adapter assembly name for custom connectors",
            "MDC_STOCKSHARP_STORAGE_PATH" => "StockSharp storage path",
            "MDC_BACKFILL_SYMBOLS" => "Comma-separated list of symbols",
            _ => ""
        };
    }
}

/// <summary>
/// Information about an environment variable override.
/// </summary>
public sealed class EnvironmentOverrideInfo
{
    public string EnvironmentVariable { get; set; } = "";
    public string ConfigPath { get; set; } = "";
    public string? CurrentValue { get; set; }
    public bool IsSet { get; set; }
    public bool IsSensitive { get; set; }
}
