using FluentValidation.Results;

namespace Meridian.Application.Config;

/// <summary>
/// Abstraction over configuration validation so that validation logic
/// can be composed as a pipeline: Field → Semantic → Connectivity.
/// </summary>
public interface IConfigValidator
{
    /// <summary>
    /// Validates the given configuration and returns a list of validation results.
    /// </summary>
    IReadOnlyList<ConfigValidationResult> Validate(AppConfig config);
}

/// <summary>
/// A single validation finding (error, warning, or info).
/// </summary>
public sealed record ConfigValidationResult(
    ConfigValidationSeverity Severity,
    string Property,
    string Message,
    string? Suggestion = null)
{
    public bool IsError => Severity == ConfigValidationSeverity.Error;
}

/// <summary>
/// Severity level of a configuration validation finding.
/// </summary>
public enum ConfigValidationSeverity : byte
{
    Info,
    Warning,
    Error
}

/// <summary>
/// Pipeline-based configuration validator that runs field-level, semantic,
/// and optional connectivity validation stages in order.
/// Consolidates ConfigValidationHelper, ConfigValidatorCli, and PreflightChecker
/// field-validation logic into a single composable pipeline.
/// </summary>
public sealed class ConfigValidationPipeline : IConfigValidator
{
    private readonly IReadOnlyList<IConfigValidationStage> _stages;

    public ConfigValidationPipeline(IEnumerable<IConfigValidationStage> stages)
    {
        _stages = stages?.ToList() ?? throw new ArgumentNullException(nameof(stages));
    }

    /// <summary>
    /// Creates the default pipeline with Field, Semantic, and Credential Security stages.
    /// </summary>
    public static ConfigValidationPipeline CreateDefault()
    {
        return new ConfigValidationPipeline(new IConfigValidationStage[]
        {
            new FieldValidationStage(),
            new SemanticValidationStage(),
            new CredentialSecurityStage()
        });
    }

    public IReadOnlyList<ConfigValidationResult> Validate(AppConfig config)
    {
        var results = new List<ConfigValidationResult>();

        foreach (var stage in _stages)
        {
            var stageResults = stage.Validate(config);
            results.AddRange(stageResults);

            // Stop running subsequent stages if the current one produced errors
            if (stageResults.Any(r => r.IsError))
                break;
        }

        return results;
    }
}

/// <summary>
/// A single stage in the configuration validation pipeline.
/// </summary>
public interface IConfigValidationStage
{
    IReadOnlyList<ConfigValidationResult> Validate(AppConfig config);
}

/// <summary>
/// Field-level validation using FluentValidation rules (AppConfigValidator).
/// </summary>
public sealed class FieldValidationStage : IConfigValidationStage
{
    public IReadOnlyList<ConfigValidationResult> Validate(AppConfig config)
    {
        var validator = new AppConfigValidator();
        var result = validator.Validate(config);

        return result.Errors
            .Select(e => new ConfigValidationResult(
                ConfigValidationSeverity.Error,
                e.PropertyName,
                e.ErrorMessage,
                GetSuggestion(e)))
            .ToList();
    }

    private static string? GetSuggestion(ValidationFailure error)
    {
        return error.PropertyName switch
        {
            "DataRoot" => "Set a valid directory path for storing market data",
            "Alpaca.KeyId" => "Set ALPACA__KEYID environment variable or update config",
            "Alpaca.SecretKey" => "Set ALPACA__SECRETKEY environment variable or update config",
            "Alpaca.Feed" => "Use 'iex' for free data or 'sip' for paid subscription",
            "IB.Host" => "Set the TWS or IB Gateway host, usually 127.0.0.1 for a local workstation",
            "IB.Port" => "Use 7497 for paper TWS, 7496 for live TWS, or your configured Gateway socket port",
            "IB.ClientId" => "Use a non-negative client id that does not conflict with another active IB API session",
            "IB.UsePaperTrading" => "Keep paper trading enabled unless you explicitly intend to route live orders",
            "IBClientPortal.Enabled" => "Enable this only when Client Portal Gateway is running and you want portfolio/account import",
            "IBClientPortal.BaseUrl" => "Point this at the Client Portal HTTP endpoint, typically https://localhost:5000",
            "IBClientPortal.AllowSelfSignedCertificates" => "Enable this when using the local Client Portal Gateway default self-signed HTTPS certificate",
            "StockSharp.Enabled" => "Set StockSharp:Enabled to true when using StockSharp",
            "StockSharp.ConnectorType" => "Use Rithmic, IQFeed, CQG, InteractiveBrokers, or Custom with AdapterType",
            var p when p.Contains("Symbol") => "Symbol must be 1-20 uppercase characters",
            var p when p.Contains("DepthLevels") => "Depth levels should be between 1 and 50",
            _ => null
        };
    }
}

/// <summary>
/// Semantic validation that checks cross-property constraints and configuration consistency.
/// Duplicate-symbol and retention-days checks are now handled by <see cref="AppConfigValidator"/>
/// in the field validation stage. This stage handles warning-level checks that should not
/// affect <see cref="FluentValidation.Results.ValidationResult.IsValid"/>.
/// </summary>
public sealed class SemanticValidationStage : IConfigValidationStage
{
    public IReadOnlyList<ConfigValidationResult> Validate(AppConfig config)
    {
        var results = new List<ConfigValidationResult>();

        // Warn if symbols are configured but none have subscriptions enabled
        if (config.Symbols is { Length: > 0 } &&
            !config.Symbols.Any(s => s.SubscribeTrades || s.SubscribeDepth))
        {
            results.Add(new ConfigValidationResult(
                ConfigValidationSeverity.Warning,
                "Symbols",
                "No symbols have trades or depth subscriptions enabled",
                "Enable SubscribeTrades or SubscribeDepth for at least one symbol"));
        }

        if (config.IB is { UsePaperTrading: false })
        {
            results.Add(new ConfigValidationResult(
                ConfigValidationSeverity.Warning,
                "IB.UsePaperTrading",
                "Interactive Brokers is configured for live routing.",
                "Confirm that live trading is intentional and keep UsePaperTrading=true for paper-safe defaults."));
        }

        if (config.IBClientPortal is { Enabled: true, AllowSelfSignedCertificates: false } clientPortal &&
            Uri.TryCreate(clientPortal.BaseUrl, UriKind.Absolute, out var baseUri) &&
            string.Equals(baseUri.Host, "localhost", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(baseUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            results.Add(new ConfigValidationResult(
                ConfigValidationSeverity.Warning,
                "IBClientPortal.AllowSelfSignedCertificates",
                "Client Portal is configured for local HTTPS without allowing self-signed certificates.",
                "Enable AllowSelfSignedCertificates or switch BaseUrl to a trusted certificate endpoint."));
        }

        return results;
    }
}

/// <summary>
/// Checks for credentials that appear to be hardcoded in the config file rather than
/// set via environment variables. Emits warnings to prevent accidental credential commits.
/// </summary>
public sealed class CredentialSecurityStage : IConfigValidationStage
{
    private static readonly string[] PlaceholderPatterns =
    {
        "your-", "YOUR_", "__SET_ME__", "REPLACE_", "ENTER_", "INSERT_",
        "TODO", "xxx", "change-me", "placeholder"
    };

    public IReadOnlyList<ConfigValidationResult> Validate(AppConfig config)
    {
        var results = new List<ConfigValidationResult>();

        CheckCredential(results, "Alpaca.KeyId", config.Alpaca?.KeyId, "ALPACA__KEYID");
        CheckCredential(results, "Alpaca.SecretKey", config.Alpaca?.SecretKey, "ALPACA__SECRETKEY");
        CheckCredential(results, "Polygon.ApiKey", config.Polygon?.ApiKey, "POLYGON__APIKEY");

        if (config.Backfill?.Providers != null)
        {
            var providers = config.Backfill.Providers;
            CheckCredential(results, "Backfill.Providers.Tiingo.ApiToken", providers.Tiingo?.ApiToken, "TIINGO__TOKEN");
            CheckCredential(results, "Backfill.Providers.Finnhub.ApiKey", providers.Finnhub?.ApiKey, "FINNHUB__APIKEY");
            CheckCredential(results, "Backfill.Providers.AlphaVantage.ApiKey", providers.AlphaVantage?.ApiKey, "ALPHAVANTAGE__APIKEY");
            CheckCredential(results, "Backfill.Providers.Fred.ApiKey", providers.Fred?.ApiKey, "FRED__APIKEY");
            CheckCredential(results, "Backfill.Providers.Nasdaq.ApiKey", providers.Nasdaq?.ApiKey, "NASDAQ__APIKEY");
        }

        return results;
    }

    private static void CheckCredential(
        List<ConfigValidationResult> results,
        string propertyName,
        string? value,
        string envVarName)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;

        // Skip placeholder values - those are expected in sample configs
        if (IsPlaceholder(value))
            return;

        // If the value looks like a real credential (not a placeholder), warn
        results.Add(new ConfigValidationResult(
            ConfigValidationSeverity.Warning,
            propertyName,
            $"Credential '{propertyName}' appears to be set directly in config file. " +
            "Use environment variables instead to avoid accidental commits.",
            $"Set environment variable {envVarName} and remove the value from the config file"));
    }

    private static bool IsPlaceholder(string value)
    {
        return PlaceholderPatterns.Any(p =>
            value.Contains(p, StringComparison.OrdinalIgnoreCase));
    }
}
