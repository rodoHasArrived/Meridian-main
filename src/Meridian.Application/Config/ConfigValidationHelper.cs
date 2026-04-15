using FluentValidation;
using Serilog;

namespace Meridian.Application.Config;

/// <summary>
/// Validates AppConfig using FluentValidation patterns.
/// </summary>
public sealed class AppConfigValidator : AbstractValidator<AppConfig>
{
    public AppConfigValidator()
    {
        RuleFor(x => x.DataRoot)
            .NotEmpty()
            .WithMessage("DataRoot must be specified")
            .Must(BeValidPath)
            .WithMessage("DataRoot must be a valid directory path");

        RuleFor(x => x.DataSource)
            .IsInEnum()
            .WithMessage("DataSource must be IB, Alpaca, Polygon, StockSharp, or NYSE");

        // Alpaca-specific validation
        When(x => x.DataSource == DataSourceKind.Alpaca, () =>
        {
            RuleFor(x => x.Alpaca)
                .NotNull()
                .WithMessage("Alpaca configuration is required when DataSource is set to Alpaca")
                .SetValidator(new AlpacaOptionsValidator()!);
        });

        // Interactive Brokers-specific validation
        When(x => x.DataSource == DataSourceKind.IB || x.IB != null, () =>
        {
            RuleFor(x => x.IB)
                .NotNull()
                .WithMessage("Interactive Brokers configuration is required when DataSource is set to IB")
                .SetValidator(new IBOptionsValidator()!);
        });

        When(x => x.IBClientPortal != null, () =>
        {
            RuleFor(x => x.IBClientPortal)
                .SetValidator(new IBClientPortalOptionsValidator()!);
        });

        // StockSharp-specific validation
        When(x => x.DataSource == DataSourceKind.StockSharp, () =>
        {
            RuleFor(x => x.StockSharp)
                .NotNull()
                .WithMessage("StockSharp configuration is required when DataSource is set to StockSharp")
                .SetValidator(new StockSharpConfigValidator()!);
        });

        // Storage configuration validation
        When(x => x.Storage != null, () =>
        {
            RuleFor(x => x.Storage)
                .SetValidator(new StorageConfigValidator()!);
        });

        // Symbol configuration validation
        When(x => x.Symbols != null && x.Symbols.Length > 0, () =>
        {
            RuleForEach(x => x.Symbols)
                .SetValidator(new SymbolConfigValidator());

            // Check for duplicate symbols (case-insensitive)
            RuleFor(x => x.Symbols)
                .Must(symbols =>
                {
                    if (symbols == null)
                        return true;
                    var distinctCount = symbols.Select(s => s.Symbol)
                        .Distinct(StringComparer.OrdinalIgnoreCase).Count();
                    return distinctCount == symbols.Length;
                })
                .WithMessage("Duplicate symbols found in configuration");
        });
    }

    private static bool BeValidPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        try
        {
            // Check for invalid path characters
            var invalidChars = Path.GetInvalidPathChars();
            return !path.Any(c => invalidChars.Contains(c));
        }
        catch (Exception ex)
        {
            Log.ForContext("SourceContext", "ConfigValidation")
               .Warning(ex, "Path validation failed for path '{Path}'", path);
            return false;
        }
    }
}

/// <summary>
/// Validates AlpacaOptions configuration.
/// </summary>
public sealed class AlpacaOptionsValidator : AbstractValidator<AlpacaOptions>
{
    public AlpacaOptionsValidator()
    {
        RuleFor(x => x.KeyId)
            .NotEmpty()
            .WithMessage("Alpaca KeyId is required")
            .MinimumLength(10)
            .WithMessage("Alpaca KeyId appears to be invalid (too short)")
            .Must(key => !IsPlaceholder(key))
            .WithMessage("Alpaca KeyId appears to be a placeholder value - please set a real API key");

        RuleFor(x => x.SecretKey)
            .NotEmpty()
            .WithMessage("Alpaca SecretKey is required")
            .MinimumLength(10)
            .WithMessage("Alpaca SecretKey appears to be invalid (too short)")
            .Must(key => !IsPlaceholder(key))
            .WithMessage("Alpaca SecretKey appears to be a placeholder value - please set a real API key");

        RuleFor(x => x.Feed)
            .NotEmpty()
            .WithMessage("Alpaca Feed must be specified (e.g., 'iex', 'sip')")
            .Must(feed => feed == "iex" || feed == "sip")
            .WithMessage("Alpaca Feed must be either 'iex' or 'sip'");
    }

    private static bool IsPlaceholder(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var placeholders = new[] { "__SET_ME__", "YOUR_", "REPLACE_", "ENTER_", "INSERT_", "TODO" };
        return placeholders.Any(p => value.Contains(p, StringComparison.OrdinalIgnoreCase));
    }
}

/// <summary>
/// Validates Interactive Brokers socket options.
/// </summary>
public sealed class IBOptionsValidator : AbstractValidator<IBOptions>
{
    public IBOptionsValidator()
    {
        RuleFor(x => x.Host)
            .NotEmpty()
            .WithMessage("Interactive Brokers host is required");

        RuleFor(x => x.Port)
            .InclusiveBetween(1, 65535)
            .WithMessage("Interactive Brokers port must be between 1 and 65535");

        RuleFor(x => x.ClientId)
            .GreaterThanOrEqualTo(0)
            .WithMessage("Interactive Brokers client ID must be zero or greater");

        When(x => x.SubscribeDepth, () =>
        {
            RuleFor(x => x.DepthLevels)
                .InclusiveBetween(1, 50)
                .WithMessage("Interactive Brokers depth levels must be between 1 and 50");
        });
    }
}

/// <summary>
/// Validates Interactive Brokers Client Portal options.
/// </summary>
public sealed class IBClientPortalOptionsValidator : AbstractValidator<IBClientPortalOptions>
{
    public IBClientPortalOptionsValidator()
    {
        When(x => x.Enabled, () =>
        {
            RuleFor(x => x.BaseUrl)
                .NotEmpty()
                .WithMessage("Interactive Brokers Client Portal base URL is required when enabled")
                .Must(BeAbsoluteHttpUrl)
                .WithMessage("Interactive Brokers Client Portal base URL must be an absolute HTTP or HTTPS URL");
        });
    }

    private static bool BeAbsoluteHttpUrl(string? value)
    {
        return Uri.TryCreate(value, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
    }
}

/// <summary>
/// Validates StockSharpConfig settings.
/// </summary>
public sealed class StockSharpConfigValidator : AbstractValidator<StockSharpConfig>
{
    private static readonly string[] SupportedConnectors =
        ["rithmic", "iqfeed", "cqg", "interactivebrokers", "ib"];

    private static bool HasCustomAdapter(StockSharpConfig config)
    {
        if (!string.IsNullOrWhiteSpace(config.AdapterType))
        {
            return true;
        }

        return config.ConnectionParams != null
               && config.ConnectionParams.TryGetValue("AdapterType", out var adapterType)
               && !string.IsNullOrWhiteSpace(adapterType);
    }

    public StockSharpConfigValidator()
    {
        RuleFor(x => x.Enabled)
            .Equal(true)
            .WithMessage("StockSharp must be enabled when DataSource is set to StockSharp");

        RuleFor(x => x.ConnectorType)
            .NotEmpty()
            .WithMessage("StockSharp ConnectorType is required");

        RuleFor(x => x)
            .Must(config => SupportedConnectors.Contains(config.ConnectorType.ToLowerInvariant()) || HasCustomAdapter(config))
            .WithMessage("Custom StockSharp connectors require AdapterType (or ConnectionParams.AdapterType) to be set");

        When(x => string.Equals(x.ConnectorType, "rithmic", StringComparison.OrdinalIgnoreCase), () =>
        {
            RuleFor(x => x.Rithmic)
                .NotNull()
                .WithMessage("Rithmic configuration is required when ConnectorType is Rithmic")
                .SetValidator(new RithmicConfigValidator()!);
        });

        When(x => string.Equals(x.ConnectorType, "iqfeed", StringComparison.OrdinalIgnoreCase), () =>
        {
            RuleFor(x => x.IQFeed)
                .NotNull()
                .WithMessage("IQFeed configuration is required when ConnectorType is IQFeed")
                .SetValidator(new IQFeedConfigValidator()!);
        });

        When(x => string.Equals(x.ConnectorType, "cqg", StringComparison.OrdinalIgnoreCase), () =>
        {
            RuleFor(x => x.CQG)
                .NotNull()
                .WithMessage("CQG configuration is required when ConnectorType is CQG")
                .SetValidator(new CQGConfigValidator()!);
        });

        When(x => string.Equals(x.ConnectorType, "interactivebrokers", StringComparison.OrdinalIgnoreCase)
                  || string.Equals(x.ConnectorType, "ib", StringComparison.OrdinalIgnoreCase), () =>
        {
            RuleFor(x => x.InteractiveBrokers)
                .NotNull()
                .WithMessage("Interactive Brokers configuration is required when ConnectorType is InteractiveBrokers")
                .SetValidator(new StockSharpIBConfigValidator()!);
        });
    }
}

/// <summary>
/// Validates RithmicConfig settings.
/// </summary>
public sealed class RithmicConfigValidator : AbstractValidator<RithmicConfig>
{
    public RithmicConfigValidator()
    {
        RuleFor(x => x.Server)
            .NotEmpty()
            .WithMessage("Rithmic server is required");

        RuleFor(x => x.UserName)
            .NotEmpty()
            .WithMessage("Rithmic username is required");

        RuleFor(x => x.Password)
            .NotEmpty()
            .WithMessage("Rithmic password is required");
    }
}

/// <summary>
/// Validates IQFeedConfig settings.
/// </summary>
public sealed class IQFeedConfigValidator : AbstractValidator<IQFeedConfig>
{
    public IQFeedConfigValidator()
    {
        RuleFor(x => x.Host)
            .NotEmpty()
            .WithMessage("IQFeed host is required");

        RuleFor(x => x.Level1Port)
            .GreaterThan(0)
            .WithMessage("IQFeed Level1Port must be greater than 0");

        RuleFor(x => x.Level2Port)
            .GreaterThan(0)
            .WithMessage("IQFeed Level2Port must be greater than 0");

        RuleFor(x => x.LookupPort)
            .GreaterThan(0)
            .WithMessage("IQFeed LookupPort must be greater than 0");
    }
}

/// <summary>
/// Validates CQGConfig settings.
/// </summary>
public sealed class CQGConfigValidator : AbstractValidator<CQGConfig>
{
    public CQGConfigValidator()
    {
        RuleFor(x => x.UserName)
            .NotEmpty()
            .WithMessage("CQG username is required");

        RuleFor(x => x.Password)
            .NotEmpty()
            .WithMessage("CQG password is required");
    }
}

/// <summary>
/// Validates StockSharpIBConfig settings.
/// </summary>
public sealed class StockSharpIBConfigValidator : AbstractValidator<StockSharpIBConfig>
{
    public StockSharpIBConfigValidator()
    {
        RuleFor(x => x.Host)
            .NotEmpty()
            .WithMessage("Interactive Brokers host is required");

        RuleFor(x => x.Port)
            .GreaterThan(0)
            .WithMessage("Interactive Brokers port must be greater than 0");

        RuleFor(x => x.ClientId)
            .GreaterThan(0)
            .WithMessage("Interactive Brokers client ID must be greater than 0");
    }
}

/// <summary>
/// Validates StorageConfig settings.
/// </summary>
public sealed class StorageConfigValidator : AbstractValidator<StorageConfig>
{
    public StorageConfigValidator()
    {
        RuleFor(x => x.NamingConvention)
            .Must(BeValidNamingConvention)
            .WithMessage("NamingConvention must be one of: Flat, BySymbol, ByDate, ByType");

        RuleFor(x => x.DatePartition)
            .Must(BeValidDatePartition)
            .WithMessage("DatePartition must be one of: None, Daily, Hourly, Monthly");

        When(x => x.RetentionDays.HasValue, () =>
        {
            RuleFor(x => x.RetentionDays!.Value)
                .GreaterThan(0)
                .WithMessage("RetentionDays must be greater than 0");
        });

        When(x => x.MaxTotalMegabytes.HasValue, () =>
        {
            RuleFor(x => x.MaxTotalMegabytes!.Value)
                .GreaterThan(0)
                .WithMessage("MaxTotalMegabytes must be greater than 0");
        });

        When(x => !string.IsNullOrWhiteSpace(x.Profile), () =>
        {
            RuleFor(x => x.Profile!)
                .Must(BeValidProfile)
                .WithMessage("Profile must be one of: Research, LowLatency, Archival");
        });
    }

    private static bool BeValidNamingConvention(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var valid = new[] { "flat", "bysymbol", "bydate", "bytype" };
        return valid.Contains(value.ToLowerInvariant());
    }

    private static bool BeValidDatePartition(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var valid = new[] { "none", "daily", "hourly", "monthly" };
        return valid.Contains(value.ToLowerInvariant());
    }

    private static bool BeValidProfile(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var valid = new[] { "research", "lowlatency", "archival" };
        return valid.Contains(value.ToLowerInvariant());
    }
}

/// <summary>
/// Validates SymbolConfig settings.
/// </summary>
public sealed class SymbolConfigValidator : AbstractValidator<SymbolConfig>
{
    private static readonly string[] ValidSecurityTypes =
    {
        "STK", "OPT", "IND_OPT", "FUT", "FOP", "SSF", "CASH", "FOREX", "FX", "IND",
        "CFD", "BOND", "CMDTY", "CRYPTO", "ETF", "FUND", "WAR", "BAG", "MARGIN"
    };

    public SymbolConfigValidator()
    {
        RuleFor(x => x.Symbol)
            .NotEmpty()
            .WithMessage("Symbol cannot be empty")
            .Matches(@"^[A-Z0-9\-\.\/]+$")
            .WithMessage("Symbol must contain only uppercase letters, numbers, hyphens, dots, or slashes");

        When(x => !string.IsNullOrWhiteSpace(x.SecurityType), () =>
        {
            RuleFor(x => x.SecurityType)
                .Must(st => ValidSecurityTypes.Contains(st!, StringComparer.OrdinalIgnoreCase))
                .WithMessage($"SecurityType must be one of: {string.Join(", ", ValidSecurityTypes)}");
        });

        When(x => x.SubscribeDepth, () =>
        {
            RuleFor(x => x.DepthLevels)
                .GreaterThan(0)
                .WithMessage("DepthLevels must be greater than 0 when SubscribeDepth is true")
                .LessThanOrEqualTo(50)
                .WithMessage("DepthLevels should not exceed 50 (exchange limits typically apply)");
        });
    }
}
