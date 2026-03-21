#if STOCKSHARP
using System.ComponentModel;
using System.Globalization;
using System.Net;
using System.Security;
using StockSharp.Algo;
using StockSharp.BusinessEntities;
using StockSharp.Messages;
#endif
using Meridian.Application.Config;
using Meridian.Application.Logging;
using Serilog;

namespace Meridian.Infrastructure.Adapters.StockSharp;

/// <summary>
/// Factory for creating StockSharp connectors based on configuration.
/// Supports multiple connector types: Rithmic, IQFeed, CQG, Interactive Brokers, and more.
/// </summary>
public static class StockSharpConnectorFactory
{
    private static readonly ILogger Log = LoggingSetup.ForContext("StockSharpConnectorFactory");

    /// <summary>
    /// Centralizes platform/package guard messages used by conditional-compilation stubs.
    /// </summary>
    private static Exception ThrowPlatformNotSupported(string message) => new NotSupportedException(message);

#if STOCKSHARP
    /// <summary>
    /// Create a connector instance based on the configured type.
    /// </summary>
    /// <param name="config">StockSharp configuration with connector settings.</param>
    /// <returns>Configured StockSharp Connector ready for connection.</returns>
    /// <exception cref="NotSupportedException">Thrown when connector type is not supported.</exception>
    public static Connector Create(StockSharpConfig config)
    {
        if (config == null)
            throw new ArgumentNullException(nameof(config));

        var connector = new Connector();

        Log.Information("Creating StockSharp connector: {ConnectorType}", config.ConnectorType);

        // Add the appropriate message adapter based on connector type
        switch (config.ConnectorType.ToLowerInvariant())
        {
            case "rithmic":
                ConfigureRithmic(connector, config.Rithmic);
                break;

            case "iqfeed":
                ConfigureIQFeed(connector, config.IQFeed);
                break;

            case "cqg":
                ConfigureCQG(connector, config.CQG);
                break;

            case "interactivebrokers":
            case "ib":
                ConfigureInteractiveBrokers(connector, config.InteractiveBrokers);
                break;

            case "binance":
                ConfigureBinance(connector, config.Binance);
                break;

            case "coinbase":
                ConfigureCoinbase(connector, config.Coinbase);
                break;

            case "kraken":
                ConfigureKraken(connector, config.Kraken);
                break;

            default:
                ConfigureCustomAdapter(connector, config);
                break;
        }

        return connector;
    }

    /// <summary>
    /// Configure Rithmic adapter for futures data (CME, NYMEX, etc.).
    /// Rithmic provides low-latency direct market access for futures trading.
    /// </summary>
    private static void ConfigureRithmic(Connector connector, RithmicConfig? cfg)
    {
#if STOCKSHARP_RITHMIC
        Log.Debug("Configuring Rithmic adapter: Server={Server}, User={User}",
            cfg?.Server ?? "Rithmic Test", cfg?.UserName ?? "(not set)");

        var adapter = new StockSharp.Rithmic.RithmicMessageAdapter(
            connector.TransactionIdGenerator)
        {
            Server = cfg?.Server ?? "Rithmic Test",
            UserName = cfg?.UserName ?? "",
            Password = ToSecureString(cfg?.Password ?? ""),
            CertFile = cfg?.CertFile ?? ""
        };

        connector.Adapter.InnerAdapters.Add(adapter);
        Log.Information("Rithmic adapter configured successfully");
#else
        throw ThrowPlatformNotSupported("Rithmic support requires StockSharp.Rithmic NuGet package. Install with: dotnet add package StockSharp.Rithmic");
#endif
    }

    /// <summary>
    /// Configure IQFeed adapter for equities and options data.
    /// IQFeed provides comprehensive US equities data with historical lookups.
    /// </summary>
    private static void ConfigureIQFeed(Connector connector, IQFeedConfig? cfg)
    {
#if STOCKSHARP_IQFEED
        Log.Debug("Configuring IQFeed adapter: Host={Host}, L1Port={L1Port}",
            cfg?.Host ?? "127.0.0.1", cfg?.Level1Port ?? 9100);

        var adapter = new StockSharp.IQFeed.IQFeedMessageAdapter(
            connector.TransactionIdGenerator)
        {
            Level1Address = new IPEndPoint(
                IPAddress.Parse(cfg?.Host ?? "127.0.0.1"),
                cfg?.Level1Port ?? 9100),
            Level2Address = new IPEndPoint(
                IPAddress.Parse(cfg?.Host ?? "127.0.0.1"),
                cfg?.Level2Port ?? 9200),
            LookupAddress = new IPEndPoint(
                IPAddress.Parse(cfg?.Host ?? "127.0.0.1"),
                cfg?.LookupPort ?? 9300)
        };

        connector.Adapter.InnerAdapters.Add(adapter);
        Log.Information("IQFeed adapter configured successfully");
#else
        throw ThrowPlatformNotSupported("IQFeed support requires StockSharp.IQFeed NuGet package. Install with: dotnet add package StockSharp.IQFeed");
#endif
    }

    /// <summary>
    /// Configure CQG adapter for futures and options data.
    /// CQG provides excellent historical data coverage for futures markets.
    /// </summary>
    private static void ConfigureCQG(Connector connector, CQGConfig? cfg)
    {
#if STOCKSHARP_CQG
        Log.Debug("Configuring CQG adapter: User={User}, DemoServer={Demo}",
            cfg?.UserName ?? "(not set)", cfg?.UseDemoServer ?? true);

        var adapter = new StockSharp.Cqg.Com.CqgComMessageAdapter(
            connector.TransactionIdGenerator)
        {
            UserName = cfg?.UserName ?? "",
            Password = ToSecureString(cfg?.Password ?? "")
        };

        connector.Adapter.InnerAdapters.Add(adapter);
        Log.Information("CQG adapter configured successfully");
#else
        throw ThrowPlatformNotSupported("CQG support requires StockSharp.Cqg.Com NuGet package. Install with: dotnet add package StockSharp.Cqg.Com");
#endif
    }

    /// <summary>
    /// Configure Interactive Brokers adapter.
    /// IB provides global multi-asset coverage through TWS/Gateway.
    /// </summary>
    private static void ConfigureInteractiveBrokers(Connector connector, StockSharpIBConfig? cfg)
    {
#if STOCKSHARP_INTERACTIVEBROKERS
        Log.Debug("Configuring Interactive Brokers adapter: Host={Host}, Port={Port}, ClientId={ClientId}",
            cfg?.Host ?? "127.0.0.1", cfg?.Port ?? 7496, cfg?.ClientId ?? 1);

        var adapter = new StockSharp.InteractiveBrokers.InteractiveBrokersMessageAdapter(
            connector.TransactionIdGenerator)
        {
            Address = new IPEndPoint(
                IPAddress.Parse(cfg?.Host ?? "127.0.0.1"),
                cfg?.Port ?? 7496),
            ClientId = cfg?.ClientId ?? 1
        };

        connector.Adapter.InnerAdapters.Add(adapter);
        Log.Information("Interactive Brokers adapter configured successfully");
#else
        throw ThrowPlatformNotSupported("Interactive Brokers support requires StockSharp.InteractiveBrokers NuGet package. Install with: dotnet add package StockSharp.InteractiveBrokers");
#endif
    }

    /// <summary>
    /// Configure Binance crypto exchange adapter.
    /// Supports spot, USDT futures, and coin-margined futures markets.
    /// </summary>
    /// <remarks>
    /// Requires StockSharp crowdfunding membership for crypto connectors.
    /// </remarks>
    private static void ConfigureBinance(Connector connector, BinanceConfig? cfg)
    {
#if STOCKSHARP_BINANCE
        Log.Debug("Configuring Binance adapter: MarketType={MarketType}, Testnet={Testnet}",
            cfg?.MarketType ?? "Spot", cfg?.UseTestnet ?? false);

        var adapter = new StockSharp.Binance.BinanceMessageAdapter(
            connector.TransactionIdGenerator)
        {
            Key = ToSecureString(cfg?.ApiKey ?? ""),
            Secret = ToSecureString(cfg?.ApiSecret ?? ""),
            IsDemo = cfg?.UseTestnet ?? false
        };

        connector.Adapter.InnerAdapters.Add(adapter);
        Log.Information("Binance adapter configured successfully for {MarketType}", cfg?.MarketType ?? "Spot");
#else
        throw ThrowPlatformNotSupported("Binance support requires StockSharp.Binance NuGet package and crowdfunding membership. See: https://stocksharp.com/store/ for more information.");
#endif
    }

    /// <summary>
    /// Configure Coinbase crypto exchange adapter.
    /// Supports Coinbase Pro (Advanced Trade) API.
    /// </summary>
    private static void ConfigureCoinbase(Connector connector, CoinbaseConfig? cfg)
    {
#if STOCKSHARP_COINBASE
        Log.Debug("Configuring Coinbase adapter: Sandbox={Sandbox}", cfg?.UseSandbox ?? false);

        var adapter = new StockSharp.Coinbase.CoinbaseMessageAdapter(
            connector.TransactionIdGenerator)
        {
            Key = ToSecureString(cfg?.ApiKey ?? ""),
            Secret = ToSecureString(cfg?.ApiSecret ?? ""),
            Passphrase = ToSecureString(cfg?.Passphrase ?? "")
        };

        connector.Adapter.InnerAdapters.Add(adapter);
        Log.Information("Coinbase adapter configured successfully");
#else
        throw ThrowPlatformNotSupported("Coinbase support requires StockSharp.Coinbase NuGet package and crowdfunding membership. See: https://stocksharp.com/store/ for more information.");
#endif
    }

    /// <summary>
    /// Configure Kraken crypto exchange adapter.
    /// Supports spot markets with WebSocket streams.
    /// </summary>
    private static void ConfigureKraken(Connector connector, KrakenConfig? cfg)
    {
#if STOCKSHARP_KRAKEN
        Log.Debug("Configuring Kraken adapter: OrderBookDepth={Depth}", cfg?.OrderBookDepth ?? 25);

        var adapter = new StockSharp.Kraken.KrakenMessageAdapter(
            connector.TransactionIdGenerator)
        {
            Key = ToSecureString(cfg?.ApiKey ?? ""),
            Secret = ToSecureString(cfg?.ApiSecret ?? "")
        };

        connector.Adapter.InnerAdapters.Add(adapter);
        Log.Information("Kraken adapter configured successfully");
#else
        throw ThrowPlatformNotSupported("Kraken support requires StockSharp.Kraken NuGet package and crowdfunding membership. See: https://stocksharp.com/store/ for more information.");
#endif
    }

    /// <summary>
    /// Convert plain text password to SecureString.
    /// </summary>
    private static SecureString ToSecureString(string value)
    {
        var secure = new SecureString();
        foreach (var c in value)
            secure.AppendChar(c);
        secure.MakeReadOnly();
        return secure;
    }

    /// <summary>
    /// Configure a custom adapter using AdapterType/AdapterAssembly and ConnectionParams.
    /// </summary>
    private static void ConfigureCustomAdapter(Connector connector, StockSharpConfig config)
    {
        var adapterTypeName = config.AdapterType;
        if (string.IsNullOrWhiteSpace(adapterTypeName) && config.ConnectionParams != null
            && config.ConnectionParams.TryGetValue("AdapterType", out var adapterType))
        {
            adapterTypeName = adapterType;
        }

        if (string.IsNullOrWhiteSpace(adapterTypeName))
        {
            throw new NotSupportedException(
                $"Connector type '{config.ConnectorType}' is not supported. " +
                "Set StockSharp.AdapterType (or ConnectionParams:AdapterType) to use a custom connector.");
        }

        var adapterAssembly = config.AdapterAssembly;
        if (string.IsNullOrWhiteSpace(adapterAssembly) && config.ConnectionParams != null
            && config.ConnectionParams.TryGetValue("AdapterAssembly", out var assemblyName))
        {
            adapterAssembly = assemblyName;
        }

        var resolvedTypeName = adapterTypeName.Contains(',')
            ? adapterTypeName
            : string.IsNullOrWhiteSpace(adapterAssembly)
                ? adapterTypeName
                : $"{adapterTypeName}, {adapterAssembly}";

        var adapterType = Type.GetType(resolvedTypeName, throwOnError: false);
        if (adapterType == null)
        {
            throw new NotSupportedException(
                $"Unable to load StockSharp adapter '{resolvedTypeName}'. " +
                "Ensure the connector package is installed and the type name is correct.");
        }

        object? adapterInstance = null;

        try
        {
            adapterInstance = Activator.CreateInstance(adapterType, connector.TransactionIdGenerator);
        }
        catch (MissingMethodException)
        {
            adapterInstance = Activator.CreateInstance(adapterType);
        }

        if (adapterInstance is not IMessageAdapter adapter)
        {
            throw new NotSupportedException(
                $"Adapter type '{resolvedTypeName}' does not implement IMessageAdapter.");
        }

        ApplyAdapterSettings(adapter, config.ConnectionParams);

        connector.Adapter.InnerAdapters.Add(adapter);
        Log.Information("Custom StockSharp adapter configured successfully: {AdapterType}", resolvedTypeName);
    }

    private static void ApplyAdapterSettings(IMessageAdapter adapter, IReadOnlyDictionary<string, string>? settings)
    {
        if (settings == null) return;

        foreach (var (key, value) in settings)
        {
            if (key.Equals("AdapterType", StringComparison.OrdinalIgnoreCase)
                || key.Equals("AdapterAssembly", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var property = adapter.GetType().GetProperty(key);
            if (property == null || !property.CanWrite)
            {
                Log.Debug("StockSharp adapter property not found or not writable: {Property}", key);
                continue;
            }

            var targetType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
            try
            {
                object? convertedValue;
                if (targetType.IsEnum)
                {
                    convertedValue = Enum.Parse(targetType, value, ignoreCase: true);
                }
                else if (targetType == typeof(TimeSpan))
                {
                    convertedValue = TimeSpan.Parse(value, CultureInfo.InvariantCulture);
                }
                else
                {
                    var converter = TypeDescriptor.GetConverter(targetType);
                    convertedValue = converter.ConvertFromInvariantString(value);
                }

                property.SetValue(adapter, convertedValue);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to apply adapter setting {Key}={Value}", key, value);
            }
        }
    }

#else
    // Stub implementation when StockSharp is not available

    /// <summary>
    /// Stub: StockSharp packages not installed.
    /// </summary>
    public static object Create(StockSharpConfig config)
    {
        throw ThrowPlatformNotSupported("StockSharp integration requires StockSharp.Algo NuGet package. Install with: dotnet add package StockSharp.Algo");
    }
#endif

    /// <summary>
    /// Get a list of all supported connector types.
    /// </summary>
    public static IReadOnlyList<string> SupportedConnectorTypes => new[]
    {
        "Rithmic",      // Futures (CME, NYMEX, COMEX, CBOT)
        "IQFeed",       // US Equities, Options
        "CQG",          // Futures, Options
        "InteractiveBrokers", // Global multi-asset
        "Custom"        // Custom adapters via AdapterType
    };

    /// <summary>
    /// Check if a connector type is supported.
    /// </summary>
    public static bool IsSupported(string connectorType)
    {
        return SupportedConnectorTypes.Any(c =>
            c.Equals(connectorType, StringComparison.OrdinalIgnoreCase));
    }
}
