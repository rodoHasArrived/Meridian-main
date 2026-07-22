using FluentValidation;
using Serilog;

namespace Meridian.Core.Config;

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
            .WithMessage("DataSource must be IB, Alpaca, Polygon, Yahoo, NYSE, or Synthetic");

        When(x => x.DataSource == DataSourceKind.Alpaca, () =>
        {
            RuleFor(x => x.Alpaca)
                .NotNull()
                .WithMessage("Alpaca configuration is required when DataSource is set to Alpaca")
                .SetValidator(new AlpacaOptionsValidator()!);
        });

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

        When(x => x.Storage != null, () =>
        {
            RuleFor(x => x.Storage)
                .SetValidator(new StorageConfigValidator()!);
        });

        When(x => x.Symbols != null && x.Symbols.Length > 0, () =>
        {
            RuleForEach(x => x.Symbols)
                .SetValidator(new SymbolConfigValidator());

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
            .Must(key => !CredentialPlaceholderDetector.ContainsPlaceholderMarker(key))
            .WithMessage("Alpaca KeyId appears to be a placeholder value - please set a real API key");

        RuleFor(x => x.SecretKey)
            .NotEmpty()
            .WithMessage("Alpaca SecretKey is required")
            .MinimumLength(10)
            .WithMessage("Alpaca SecretKey appears to be invalid (too short)")
            .Must(key => !CredentialPlaceholderDetector.ContainsPlaceholderMarker(key))
            .WithMessage("Alpaca SecretKey appears to be a placeholder value - please set a real API key");

        RuleFor(x => x.Feed)
            .NotEmpty()
            .WithMessage("Alpaca Feed must be specified (e.g., 'iex', 'sip')")
            .Must(feed => feed == "iex" || feed == "sip")
            .WithMessage("Alpaca Feed must be either 'iex' or 'sip'");
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
/// Validates StorageConfig settings.
/// </summary>
public sealed class StorageConfigValidator : AbstractValidator<StorageConfig>
{
    private static readonly string[] ValidNamingConventions = ["flat", "bysymbol", "bydate", "bytype"];
    private static readonly string[] ValidDatePartitions = ["none", "daily", "hourly", "monthly"];
    private static readonly string[] ValidProfiles = ["research", "lowlatency", "archival"];

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
                .WithMessage("Profile must be one of: Strategy (stored id Research), LowLatency, Archival");
        });
    }

    private static bool BeValidNamingConvention(string value)
        => IsAllowedValue(value, ValidNamingConventions);

    private static bool BeValidDatePartition(string value)
        => IsAllowedValue(value, ValidDatePartitions);

    private static bool BeValidProfile(string value)
        => IsAllowedValue(value, ValidProfiles);

    private static bool IsAllowedValue(string value, IReadOnlyCollection<string> allowedValues)
        => !string.IsNullOrWhiteSpace(value) &&
           allowedValues.Contains(value.Trim().ToLowerInvariant());
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
