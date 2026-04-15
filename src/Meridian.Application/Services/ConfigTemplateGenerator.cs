using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Meridian.Application.Config;
using Meridian.Application.Logging;
using Serilog;

namespace Meridian.Application.Services;

/// <summary>
/// Service for generating configuration templates.
/// Implements QW-76: Config Template Generator.
/// </summary>
public sealed class ConfigTemplateGenerator
{
    private readonly ILogger _log = LoggingSetup.ForContext<ConfigTemplateGenerator>();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// Generates a minimal configuration template.
    /// </summary>
    public ConfigTemplate GenerateMinimal()
    {
        var config = new
        {
            DataRoot = "data",
            Compress = false,
            DataSource = "IB",
            IB = new
            {
                Host = "127.0.0.1",
                Port = 7497,
                ClientId = 1,
                UsePaperTrading = true,
                SubscribeDepth = true,
                DepthLevels = 10,
                TickByTick = true
            },
            IBClientPortal = new
            {
                Enabled = false,
                BaseUrl = "https://localhost:5000",
                AllowSelfSignedCertificates = true
            },
            Symbols = new[]
            {
                new { Symbol = "SPY", SubscribeTrades = true, SubscribeDepth = true, DepthLevels = 10 }
            }
        };

        return new ConfigTemplate
        {
            Name = "Minimal",
            Description = "Minimal configuration with basic settings",
            Json = JsonSerializer.Serialize(config, JsonOptions),
            Category = ConfigTemplateCategory.Basic
        };
    }

    /// <summary>
    /// Generates a full configuration template with all options.
    /// </summary>
    public ConfigTemplate GenerateFull()
    {
        var config = new AppConfig(
            DataRoot: "data",
            Compress: false,
            DataSource: DataSourceKind.IB,
            Alpaca: new AlpacaOptions(
                KeyId: "YOUR_ALPACA_KEY_ID",
                SecretKey: "YOUR_ALPACA_SECRET_KEY",
                Feed: "iex",
                UseSandbox: false
            ),
            IB: new IBOptions(
                Host: "127.0.0.1",
                Port: 7497,
                ClientId: 1,
                UsePaperTrading: true,
                SubscribeDepth: true,
                DepthLevels: 10,
                TickByTick: true
            ),
            IBClientPortal: new IBClientPortalOptions(
                Enabled: false,
                BaseUrl: "https://localhost:5000",
                AllowSelfSignedCertificates: true
            ),
            Storage: new StorageConfig(
                NamingConvention: "BySymbol",
                DatePartition: "Daily",
                IncludeProvider: false,
                FilePrefix: null,
                RetentionDays: null,
                MaxTotalMegabytes: null
            ),
            Symbols: new[]
            {
                new SymbolConfig(Symbol: "SPY", SubscribeTrades: true, SubscribeDepth: true, DepthLevels: 10),
                new SymbolConfig(Symbol: "AAPL", SubscribeTrades: true, SubscribeDepth: false, DepthLevels: 5)
            },
            Backfill: new BackfillConfig(
                Enabled: false,
                Provider: "composite",
                Symbols: new[] { "SPY", "QQQ" },
                From: DateOnly.FromDateTime(DateTime.Now.AddYears(-1)),
                To: DateOnly.FromDateTime(DateTime.Now)
            )
        );

        return new ConfigTemplate
        {
            Name = "Full",
            Description = "Complete configuration template with all available options",
            Json = JsonSerializer.Serialize(config, JsonOptions),
            Category = ConfigTemplateCategory.Advanced
        };
    }

    /// <summary>
    /// Generates an Alpaca-specific configuration template.
    /// </summary>
    public ConfigTemplate GenerateAlpaca()
    {
        var config = new
        {
            DataRoot = "data",
            Compress = true,
            DataSource = "Alpaca",
            Alpaca = new
            {
                KeyId = "${ALPACA_KEY_ID}",
                SecretKey = "${ALPACA_SECRET_KEY}",
                Feed = "iex",
                UseSandbox = false,
                SubscribeQuotes = true
            },
            Storage = new
            {
                NamingConvention = "BySymbol",
                DatePartition = "Daily"
            },
            Symbols = new[]
            {
                new { Symbol = "SPY", SubscribeTrades = true, SubscribeDepth = false },
                new { Symbol = "QQQ", SubscribeTrades = true, SubscribeDepth = false },
                new { Symbol = "AAPL", SubscribeTrades = true, SubscribeDepth = false }
            }
        };

        return new ConfigTemplate
        {
            Name = "Alpaca",
            Description = "Configuration for Alpaca Markets data streaming",
            Json = JsonSerializer.Serialize(config, JsonOptions),
            Category = ConfigTemplateCategory.Provider,
            EnvironmentVariables = new Dictionary<string, string>
            {
                ["ALPACA_KEY_ID"] = "Your Alpaca API Key ID",
                ["ALPACA_SECRET_KEY"] = "Your Alpaca Secret Key"
            }
        };
    }

    /// <summary>
    /// Generates a StockSharp-specific configuration template.
    /// </summary>
    public ConfigTemplate GenerateStockSharp()
    {
        var config = new
        {
            DataRoot = "data",
            Compress = true,
            DataSource = "StockSharp",
            StockSharp = new
            {
                Enabled = true,
                ConnectorType = "Rithmic",
                AdapterType = "${MDC_STOCKSHARP_ADAPTER_TYPE}",
                AdapterAssembly = "${MDC_STOCKSHARP_ADAPTER_ASSEMBLY}",
                EnableRealTime = true,
                EnableHistorical = true,
                UseBinaryStorage = false,
                StoragePath = "data/stocksharp/{connector}",
                Rithmic = new
                {
                    Server = "${MDC_STOCKSHARP_RITHMIC_SERVER}",
                    UserName = "${MDC_STOCKSHARP_RITHMIC_USERNAME}",
                    Password = "${MDC_STOCKSHARP_RITHMIC_PASSWORD}",
                    CertFile = "${MDC_STOCKSHARP_RITHMIC_CERTFILE}",
                    UsePaperTrading = true
                }
            },
            Storage = new
            {
                NamingConvention = "BySymbol",
                DatePartition = "Daily"
            },
            Symbols = new[]
            {
                new { Symbol = "ES", Exchange = "CME", SecurityType = "FUT", SubscribeTrades = true, SubscribeDepth = true }
            }
        };

        return new ConfigTemplate
        {
            Name = "StockSharp",
            Description = "Configuration for StockSharp connectors (Rithmic example)",
            Json = JsonSerializer.Serialize(config, JsonOptions),
            Category = ConfigTemplateCategory.Provider,
            EnvironmentVariables = new Dictionary<string, string>
            {
                ["MDC_STOCKSHARP_CONNECTOR"] = "StockSharp connector type (Rithmic, IQFeed, CQG, InteractiveBrokers, Custom)",
                ["MDC_STOCKSHARP_ADAPTER_TYPE"] = "Fully qualified StockSharp adapter type (for custom connectors)",
                ["MDC_STOCKSHARP_ADAPTER_ASSEMBLY"] = "Adapter assembly name (if needed)",
                ["MDC_STOCKSHARP_RITHMIC_SERVER"] = "Rithmic server name",
                ["MDC_STOCKSHARP_RITHMIC_USERNAME"] = "Rithmic username",
                ["MDC_STOCKSHARP_RITHMIC_PASSWORD"] = "Rithmic password",
                ["MDC_STOCKSHARP_RITHMIC_CERTFILE"] = "Path to Rithmic certificate file"
            }
        };
    }

    /// <summary>
    /// Generates a backfill-focused configuration template.
    /// </summary>
    public ConfigTemplate GenerateBackfill()
    {
        var config = new
        {
            DataRoot = "data/historical",
            Compress = true,
            DataSource = "IB",
            IB = new
            {
                Host = "127.0.0.1",
                Port = 7497,
                ClientId = 1,
                UsePaperTrading = true,
                SubscribeDepth = false,
                DepthLevels = 10,
                TickByTick = false
            },
            IBClientPortal = new
            {
                Enabled = false,
                BaseUrl = "https://localhost:5000",
                AllowSelfSignedCertificates = true
            },
            Backfill = new
            {
                Enabled = true,
                Provider = "composite",
                Symbols = new[] { "SPY", "QQQ", "AAPL", "MSFT", "GOOGL" },
                From = DateTime.Now.AddYears(-1).ToString("yyyy-MM-dd"),
                To = DateTime.Now.ToString("yyyy-MM-dd"),
                Granularity = "daily",
                EnableFallback = true,
                PreferAdjustedPrices = true,
                EnableSymbolResolution = true,
                EnableRateLimitRotation = true,
                SkipExistingData = true,
                Providers = new
                {
                    Yahoo = new { Enabled = true, Priority = 10 },
                    Stooq = new { Enabled = true, Priority = 20 },
                    Nasdaq = new { Enabled = true, Priority = 30 }
                }
            },
            Storage = new
            {
                NamingConvention = "BySymbol",
                DatePartition = "Monthly"
            }
        };

        return new ConfigTemplate
        {
            Name = "Backfill",
            Description = "Configuration optimized for historical data backfill",
            Json = JsonSerializer.Serialize(config, JsonOptions),
            Category = ConfigTemplateCategory.UseCase
        };
    }

    /// <summary>
    /// Generates a production configuration template.
    /// </summary>
    public ConfigTemplate GenerateProduction()
    {
        var config = new
        {
            DataRoot = "/var/lib/meridian/data",
            Compress = true,
            DataSource = "Alpaca",
            Alpaca = new
            {
                KeyId = "${ALPACA_KEY_ID}",
                SecretKey = "${ALPACA_SECRET_KEY}",
                Feed = "sip",
                UseSandbox = false,
                SubscribeQuotes = true
            },
            Storage = new
            {
                NamingConvention = "BySymbol",
                DatePartition = "Daily",
                RetentionDays = 365,
                MaxTotalMegabytes = 102400
            },
            Symbols = new[]
            {
                new { Symbol = "SPY", SubscribeTrades = true, SubscribeDepth = true, DepthLevels = 10 },
                new { Symbol = "QQQ", SubscribeTrades = true, SubscribeDepth = true, DepthLevels = 10 }
            },
            Serilog = new
            {
                MinimumLevel = new
                {
                    Default = "Information",
                    Override = new { Microsoft = "Warning", System = "Warning" }
                }
            }
        };

        return new ConfigTemplate
        {
            Name = "Production",
            Description = "Production-ready configuration with security and reliability settings",
            Json = JsonSerializer.Serialize(config, JsonOptions),
            Category = ConfigTemplateCategory.Advanced,
            EnvironmentVariables = new Dictionary<string, string>
            {
                ["ALPACA_KEY_ID"] = "Your Alpaca API Key ID",
                ["ALPACA_SECRET_KEY"] = "Your Alpaca Secret Key"
            }
        };
    }

    /// <summary>
    /// Generates a Docker/container configuration template.
    /// </summary>
    public ConfigTemplate GenerateDocker()
    {
        var config = new
        {
            DataRoot = "/data",
            Compress = true,
            DataSource = "${MDC_DATASOURCE:-IB}",
            Alpaca = new
            {
                KeyId = "${ALPACA_KEY_ID}",
                SecretKey = "${ALPACA_SECRET_KEY}",
                Feed = "${ALPACA_FEED:-iex}"
            },
            Storage = new
            {
                NamingConvention = "BySymbol",
                DatePartition = "Daily"
            },
            Symbols = new[]
            {
                new { Symbol = "${MDC_SYMBOLS:-SPY}", SubscribeTrades = true, SubscribeDepth = true }
            }
        };

        return new ConfigTemplate
        {
            Name = "Docker",
            Description = "Configuration template for Docker/container deployments with environment variable placeholders",
            Json = JsonSerializer.Serialize(config, JsonOptions),
            Category = ConfigTemplateCategory.Deployment,
            EnvironmentVariables = new Dictionary<string, string>
            {
                ["MDC_DATASOURCE"] = "Data source provider (IB, Alpaca, Polygon, StockSharp, NYSE)",
                ["MDC_SYMBOLS"] = "Comma-separated list of symbols",
                ["ALPACA_KEY_ID"] = "Alpaca API Key ID",
                ["ALPACA_SECRET_KEY"] = "Alpaca Secret Key",
                ["ALPACA_FEED"] = "Alpaca data feed (iex or sip)"
            }
        };
    }

    /// <summary>
    /// Gets all available templates.
    /// </summary>
    public IReadOnlyList<ConfigTemplate> GetAllTemplates()
    {
        return new[]
        {
            GenerateMinimal(),
            GenerateFull(),
            GenerateAlpaca(),
            GenerateStockSharp(),
            GenerateBackfill(),
            GenerateProduction(),
            GenerateDocker()
        };
    }

    /// <summary>
    /// Gets a template by name.
    /// </summary>
    public ConfigTemplate? GetTemplate(string name)
    {
        return name.ToLowerInvariant() switch
        {
            "minimal" => GenerateMinimal(),
            "full" => GenerateFull(),
            "alpaca" => GenerateAlpaca(),
            "stocksharp" => GenerateStockSharp(),
            "backfill" => GenerateBackfill(),
            "production" => GenerateProduction(),
            "docker" => GenerateDocker(),
            _ => null
        };
    }

    /// <summary>
    /// Generates a template based on current configuration.
    /// </summary>
    public ConfigTemplate GenerateFromConfig(AppConfig config, string name = "Custom")
    {
        return new ConfigTemplate
        {
            Name = name,
            Description = $"Generated from current configuration at {DateTimeOffset.UtcNow:O}",
            Json = JsonSerializer.Serialize(config, JsonOptions),
            Category = ConfigTemplateCategory.Custom
        };
    }

    /// <summary>
    /// Validates a configuration template.
    /// </summary>
    public ConfigTemplateValidationResult ValidateTemplate(string json)
    {
        var result = new ConfigTemplateValidationResult();

        try
        {
            var config = JsonSerializer.Deserialize<AppConfig>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (config == null)
            {
                result.Errors.Add("Failed to deserialize configuration");
                return result;
            }

            // Validate required fields
            if (string.IsNullOrWhiteSpace(config.DataRoot))
            {
                result.Warnings.Add("DataRoot is not specified, will use default 'data'");
            }

            if (config.DataSource == DataSourceKind.Alpaca)
            {
                if (config.Alpaca == null)
                {
                    result.Errors.Add("Alpaca configuration is required when DataSource is 'Alpaca'");
                }
                else if (string.IsNullOrWhiteSpace(config.Alpaca.KeyId) ||
                         config.Alpaca.KeyId == "YOUR_ALPACA_KEY_ID" ||
                         config.Alpaca.KeyId.StartsWith("${"))
                {
                    result.Warnings.Add("Alpaca KeyId needs to be configured (use environment variable or direct value)");
                }
            }

            if (config.Symbols == null || config.Symbols.Length == 0)
            {
                result.Warnings.Add("No symbols configured, SPY will be used as default");
            }

            result.IsValid = result.Errors.Count == 0;
        }
        catch (JsonException ex)
        {
            result.Errors.Add($"Invalid JSON: {ex.Message}");
        }

        return result;
    }
}

/// <summary>
/// A configuration template.
/// </summary>
public sealed class ConfigTemplate
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Json { get; set; } = "{}";
    public ConfigTemplateCategory Category { get; set; }
    public Dictionary<string, string>? EnvironmentVariables { get; set; }
}

/// <summary>
/// Configuration template categories.
/// </summary>
public enum ConfigTemplateCategory : byte
{
    Basic,
    Advanced,
    Provider,
    UseCase,
    Deployment,
    Custom
}

/// <summary>
/// Result of template validation.
/// </summary>
public sealed class ConfigTemplateValidationResult
{
    public bool IsValid { get; set; }
    public List<string> Errors { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
}
