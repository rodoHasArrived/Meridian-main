using Meridian.Application.Logging;
using Serilog;

namespace Meridian.Application.Config.Credentials;

/// <summary>
/// Centralized credential resolver for all market data providers.
/// Resolves credentials from multiple sources with environment variables taking precedence.
/// </summary>
/// <remarks>
/// <para><b>Resolution Order (first match wins):</b></para>
/// <list type="number">
/// <item><description>Environment variables (recommended for production)</description></item>
/// <item><description>Secrets file (for local development)</description></item>
/// <item><description>Configuration file (not recommended for sensitive data)</description></item>
/// </list>
/// <para><b>Environment Variables:</b></para>
/// <list type="bullet">
/// <item><description>ALPACA_KEY_ID, ALPACA_SECRET_KEY - Alpaca Markets</description></item>
/// <item><description>POLYGON_API_KEY - Polygon.io</description></item>
/// <item><description>TIINGO_API_TOKEN - Tiingo</description></item>
/// <item><description>FINNHUB_API_KEY - Finnhub</description></item>
/// <item><description>ALPHA_VANTAGE_API_KEY - Alpha Vantage</description></item>
/// <item><description>FRED_API_KEY - FRED Economic Data</description></item>
/// <item><description>NASDAQ_API_KEY - Nasdaq Data Link</description></item>
/// <item><description>OPENFIGI_API_KEY - OpenFIGI</description></item>
/// <item><description>NYSE_API_KEY, NYSE_API_SECRET, NYSE_CLIENT_ID - NYSE</description></item>
/// <item><description>RABBITMQ_USERNAME, RABBITMQ_PASSWORD - RabbitMQ</description></item>
/// <item><description>AZURE_SERVICEBUS_CONNECTION_STRING - Azure Service Bus</description></item>
/// </list>
/// </remarks>
public sealed class ProviderCredentialResolver
{
    private readonly ILogger _log;

    public ProviderCredentialResolver(ILogger? log = null)
    {
        _log = log ?? LoggingSetup.ForContext<ProviderCredentialResolver>();
    }

    /// <summary>
    /// Resolves Alpaca credentials from environment or config.
    /// </summary>
    public (string? KeyId, string? SecretKey) ResolveAlpaca(string? configKeyId = null, string? configSecretKey = null)
    {
        var keyId = ResolveCredential("ALPACA_KEY_ID", configKeyId, "Alpaca KeyId");
        var secretKey = ResolveCredential("ALPACA_SECRET_KEY", configSecretKey, "Alpaca SecretKey");
        return (keyId, secretKey);
    }

    /// <summary>
    /// Resolves Polygon.io API key from environment or config.
    /// </summary>
    public string? ResolvePolygon(string? configApiKey = null)
    {
        return ResolveCredential("POLYGON_API_KEY", configApiKey, "Polygon ApiKey");
    }

    /// <summary>
    /// Resolves Tiingo API token from environment or config.
    /// </summary>
    public string? ResolveTiingo(string? configApiToken = null)
    {
        return ResolveCredential("TIINGO_API_TOKEN", configApiToken, "Tiingo ApiToken");
    }

    /// <summary>
    /// Resolves Finnhub API key from environment or config.
    /// </summary>
    public string? ResolveFinnhub(string? configApiKey = null)
    {
        return ResolveCredential("FINNHUB_API_KEY", configApiKey, "Finnhub ApiKey");
    }

    /// <summary>
    /// Resolves Alpha Vantage API key from environment or config.
    /// </summary>
    public string? ResolveAlphaVantage(string? configApiKey = null)
    {
        return ResolveCredential("ALPHA_VANTAGE_API_KEY", configApiKey, "Alpha Vantage ApiKey");
    }

    /// <summary>
    /// Resolves FRED API key from environment or config.
    /// </summary>
    public string? ResolveFred(string? configApiKey = null)
    {
        return ResolveCredential("FRED_API_KEY", configApiKey, "FRED ApiKey");
    }

    /// <summary>
    /// Resolves Nasdaq Data Link API key from environment or config.
    /// </summary>
    public string? ResolveNasdaq(string? configApiKey = null)
    {
        return ResolveCredential("NASDAQ_API_KEY", configApiKey, "Nasdaq ApiKey");
    }

    /// <summary>
    /// Resolves OpenFIGI API key from environment or config.
    /// </summary>
    public string? ResolveOpenFigi(string? configApiKey = null)
    {
        return ResolveCredential("OPENFIGI_API_KEY", configApiKey, "OpenFIGI ApiKey");
    }

    /// <summary>
    /// Resolves NYSE credentials from environment or config.
    /// </summary>
    public (string? ApiKey, string? ApiSecret, string? ClientId) ResolveNyse(
        string? configApiKey = null,
        string? configApiSecret = null,
        string? configClientId = null)
    {
        var apiKey = ResolveCredential("NYSE_API_KEY", configApiKey, "NYSE ApiKey");
        var apiSecret = ResolveCredential("NYSE_API_SECRET", configApiSecret, "NYSE ApiSecret");
        var clientId = ResolveCredential("NYSE_CLIENT_ID", configClientId, "NYSE ClientId");
        return (apiKey, apiSecret, clientId);
    }

    /// <summary>
    /// Resolves RabbitMQ credentials from environment or config.
    /// </summary>
    public (string Username, string Password) ResolveRabbitMq(
        string? configUsername = null,
        string? configPassword = null)
    {
        var resolvedUsername = ResolveCredential("RABBITMQ_USERNAME", configUsername, "RabbitMQ Username");
        var resolvedPassword = ResolveCredential("RABBITMQ_PASSWORD", configPassword, "RabbitMQ Password");
        var username = resolvedUsername ?? "guest";
        var password = resolvedPassword ?? "guest";

        if (resolvedUsername is null && resolvedPassword is null)
        {
            _log.Warning("RabbitMQ credentials not configured; defaulting to guest/guest");
        }

        return (username, password);
    }

    /// <summary>
    /// Resolves Azure Service Bus connection string from environment or config.
    /// </summary>
    public string? ResolveAzureServiceBus(string? configConnectionString = null)
    {
        return ResolveCredential("AZURE_SERVICEBUS_CONNECTION_STRING", configConnectionString, "Azure Service Bus ConnectionString");
    }

    /// <summary>
    /// Resolves a single credential value with environment variable taking precedence.
    /// </summary>
    /// <param name="envVarName">Environment variable name to check first.</param>
    /// <param name="configValue">Configuration file value to fall back to.</param>
    /// <param name="credentialName">Human-readable credential name for logging.</param>
    /// <returns>Resolved credential value or null if not found.</returns>
    public string? ResolveCredential(string envVarName, string? configValue, string credentialName)
    {
        // 1. Try environment variable first (recommended)
        var envValue = Environment.GetEnvironmentVariable(envVarName);
        if (!string.IsNullOrWhiteSpace(envValue))
        {
            _log.Debug("Resolved {CredentialName} from environment variable {EnvVar}", credentialName, envVarName);
            return envValue.Trim();
        }

        // 2. Try config value if provided and not a placeholder
        if (!string.IsNullOrWhiteSpace(configValue) && !IsPlaceholder(configValue))
        {
            _log.Debug("Resolved {CredentialName} from configuration (consider using env var {EnvVar} instead)",
                credentialName, envVarName);
            return configValue.Trim();
        }

        // 3. Not found
        _log.Debug("{CredentialName} not configured. Set {EnvVar} environment variable.", credentialName, envVarName);
        return null;
    }

    /// <summary>
    /// Checks if a value is a placeholder that should be treated as empty.
    /// </summary>
    private static bool IsPlaceholder(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return true;

        var normalized = value.Trim().ToUpperInvariant();
        return normalized is
            "__SET_ME__" or
            "SET_ME" or
            "YOUR_KEY_HERE" or
            "YOUR_API_KEY" or
            "YOUR_SECRET" or
            "CHANGE_ME" or
            "TODO" or
            "XXX" or
            "PLACEHOLDER" or
            "<YOUR_KEY>" or
            "<API_KEY>" or
            "";
    }

    /// <summary>
    /// Validates that required credentials are present for a provider.
    /// </summary>
    /// <param name="providerName">Provider name for error messages.</param>
    /// <param name="credentials">Dictionary of credential name to value pairs.</param>
    /// <exception cref="InvalidOperationException">If any required credential is missing.</exception>
    public void ValidateRequired(string providerName, params (string Name, string? Value, string EnvVar)[] credentials)
    {
        var missing = credentials
            .Where(c => string.IsNullOrWhiteSpace(c.Value))
            .ToList();

        if (missing.Count > 0)
        {
            var envVars = string.Join(", ", missing.Select(m => m.EnvVar));
            throw new InvalidOperationException(
                $"Missing required credentials for {providerName}. Set environment variable(s): {envVars}");
        }
    }

    /// <summary>
    /// Gets a summary of configured credentials (without revealing values).
    /// </summary>
    public IReadOnlyDictionary<string, bool> GetCredentialStatus()
    {
        return new Dictionary<string, bool>
        {
            ["Alpaca"] = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ALPACA_KEY_ID")),
            ["Polygon"] = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("POLYGON_API_KEY")),
            ["Tiingo"] = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("TIINGO_API_TOKEN")),
            ["Finnhub"] = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("FINNHUB_API_KEY")),
            ["AlphaVantage"] = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ALPHA_VANTAGE_API_KEY")),
            ["FRED"] = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("FRED_API_KEY")),
            ["Nasdaq"] = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NASDAQ_API_KEY")),
            ["OpenFigi"] = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("OPENFIGI_API_KEY")),
            ["NYSE"] = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NYSE_API_KEY")),
            ["RabbitMQ"] = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("RABBITMQ_PASSWORD")),
            ["AzureServiceBus"] = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("AZURE_SERVICEBUS_CONNECTION_STRING"))
        };
    }
}
