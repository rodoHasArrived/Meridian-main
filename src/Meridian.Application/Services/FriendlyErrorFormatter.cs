using System.Text;

namespace Meridian.Application.Services;

/// <summary>
/// Formats errors with error codes, actionable suggestions, and user-friendly messages.
/// Provides consistent error presentation across the application.
/// </summary>
public static class FriendlyErrorFormatter
{
    /// <summary>
    /// Error categories with codes and suggestions.
    /// </summary>
    private static readonly Dictionary<string, ErrorInfo> ErrorInfos = new(StringComparer.OrdinalIgnoreCase)
    {
        // Configuration errors
        ["CONFIG_NOT_FOUND"] = new ErrorInfo(
            Code: "Meridian-CFG-001",
            Title: "Configuration file not found",
            Suggestion: "Run 'Meridian --wizard' to create a configuration file, or copy appsettings.sample.json to appsettings.json",
            DocsLink: "docs/guides/getting-started.md"
        ),
        ["CONFIG_INVALID_JSON"] = new ErrorInfo(
            Code: "Meridian-CFG-002",
            Title: "Invalid JSON in configuration file",
            Suggestion: "Check for syntax errors such as trailing commas, missing quotes, or unescaped characters. Use a JSON validator.",
            DocsLink: "docs/guides/configuration.md"
        ),
        ["CONFIG_VALIDATION_FAILED"] = new ErrorInfo(
            Code: "Meridian-CFG-003",
            Title: "Configuration validation failed",
            Suggestion: "Run 'Meridian --validate-config' for detailed validation errors",
            DocsLink: "docs/guides/configuration.md"
        ),
        ["CONFIG_MISSING_CREDENTIALS"] = new ErrorInfo(
            Code: "Meridian-CFG-004",
            Title: "Missing API credentials",
            Suggestion: "Set the required environment variables (e.g., ALPACA_KEY_ID, ALPACA_SECRET_KEY) or configure them in appsettings.json",
            DocsLink: "docs/guides/credentials.md"
        ),

        // Connection errors
        ["CONN_TIMEOUT"] = new ErrorInfo(
            Code: "Meridian-CON-001",
            Title: "Connection timeout",
            Suggestion: "Check your internet connection and firewall settings. The provider may be experiencing issues.",
            DocsLink: "docs/troubleshooting.md"
        ),
        ["CONN_REFUSED"] = new ErrorInfo(
            Code: "Meridian-CON-002",
            Title: "Connection refused",
            Suggestion: "Ensure the service is running and accepting connections on the specified host/port",
            DocsLink: "docs/troubleshooting.md"
        ),
        ["CONN_SSL_ERROR"] = new ErrorInfo(
            Code: "Meridian-CON-003",
            Title: "SSL/TLS error",
            Suggestion: "Check your system's SSL certificates are up to date. Try updating your .NET runtime.",
            DocsLink: "docs/troubleshooting.md"
        ),

        // Authentication errors
        ["AUTH_INVALID_KEY"] = new ErrorInfo(
            Code: "Meridian-AUTH-001",
            Title: "Invalid API key",
            Suggestion: "Verify your API key is correct. Check for extra whitespace or copy/paste errors.",
            DocsLink: "docs/guides/credentials.md"
        ),
        ["AUTH_EXPIRED"] = new ErrorInfo(
            Code: "Meridian-AUTH-002",
            Title: "API key expired",
            Suggestion: "Generate a new API key from the provider's dashboard",
            DocsLink: "docs/guides/credentials.md"
        ),
        ["AUTH_INSUFFICIENT_PERMISSIONS"] = new ErrorInfo(
            Code: "Meridian-AUTH-003",
            Title: "Insufficient permissions",
            Suggestion: "Your API key may not have the required permissions. Check your account settings with the provider.",
            DocsLink: null
        ),

        // Rate limit errors
        ["RATE_LIMIT_EXCEEDED"] = new ErrorInfo(
            Code: "Meridian-RATE-001",
            Title: "Rate limit exceeded",
            Suggestion: "Wait before retrying. Consider reducing request frequency or upgrading your API plan.",
            DocsLink: "docs/providers/rate-limits.md"
        ),
        ["RATE_LIMIT_QUOTA"] = new ErrorInfo(
            Code: "Meridian-RATE-002",
            Title: "Daily/monthly quota exceeded",
            Suggestion: "Your usage quota has been reached. Wait for the reset period or upgrade your plan.",
            DocsLink: null
        ),

        // Data errors
        ["DATA_SYMBOL_NOT_FOUND"] = new ErrorInfo(
            Code: "Meridian-DATA-001",
            Title: "Symbol not found",
            Suggestion: "Verify the symbol is correct. Check if it's supported by the provider.",
            DocsLink: "docs/guides/symbols.md"
        ),
        ["DATA_NO_DATA_AVAILABLE"] = new ErrorInfo(
            Code: "Meridian-DATA-002",
            Title: "No data available",
            Suggestion: "The requested data may not exist for this time period or symbol. Try a different date range.",
            DocsLink: null
        ),
        ["DATA_PARSE_ERROR"] = new ErrorInfo(
            Code: "Meridian-DATA-003",
            Title: "Data parsing error",
            Suggestion: "The provider returned unexpected data format. This may be a temporary issue - try again.",
            DocsLink: null
        ),

        // Storage errors
        ["STORAGE_PERMISSION_DENIED"] = new ErrorInfo(
            Code: "Meridian-STOR-001",
            Title: "Storage permission denied",
            Suggestion: "Check that the application has write permission to the data directory",
            DocsLink: "docs/troubleshooting.md"
        ),
        ["STORAGE_DISK_FULL"] = new ErrorInfo(
            Code: "Meridian-STOR-002",
            Title: "Disk space exhausted",
            Suggestion: "Free up disk space, enable compression, or configure a storage limit",
            DocsLink: "docs/guides/storage.md"
        ),
        ["STORAGE_FILE_LOCKED"] = new ErrorInfo(
            Code: "Meridian-STOR-003",
            Title: "File is locked by another process",
            Suggestion: "Close any other applications that may be accessing the data files",
            DocsLink: null
        ),

        // Provider-specific errors
        ["IB_GATEWAY_NOT_RUNNING"] = new ErrorInfo(
            Code: "Meridian-IB-001",
            Title: "IB Gateway/TWS not running",
            Suggestion: "Start Interactive Brokers TWS or IB Gateway and enable API connections in the settings",
            DocsLink: "docs/providers/interactive-brokers.md"
        ),
        ["ALPACA_MARKET_CLOSED"] = new ErrorInfo(
            Code: "Meridian-ALP-001",
            Title: "Market is closed",
            Suggestion: "Real-time data is only available during market hours. Use historical data for off-hours.",
            DocsLink: null
        )
    };

    /// <summary>
    /// Formats an exception into a user-friendly error message.
    /// </summary>
    public static FormattedError Format(Exception exception)
    {
        var errorKey = ClassifyException(exception);
        var info = ErrorInfos.GetValueOrDefault(errorKey) ?? new ErrorInfo(
            Code: "Meridian-GEN-001",
            Title: "An error occurred",
            Suggestion: "Check the logs for more details",
            DocsLink: "docs/troubleshooting.md"
        );

        return new FormattedError(
            Code: info.Code,
            Title: info.Title,
            Message: exception.Message,
            Suggestion: info.Suggestion,
            DocsLink: info.DocsLink,
            Details: exception.InnerException?.Message
        );
    }

    /// <summary>
    /// Formats an error with a known key.
    /// </summary>
    public static FormattedError Format(string errorKey, string? additionalMessage = null)
    {
        var info = ErrorInfos.GetValueOrDefault(errorKey) ?? new ErrorInfo(
            Code: "Meridian-GEN-001",
            Title: errorKey,
            Suggestion: null,
            DocsLink: null
        );

        return new FormattedError(
            Code: info.Code,
            Title: info.Title,
            Message: additionalMessage ?? info.Title,
            Suggestion: info.Suggestion,
            DocsLink: info.DocsLink,
            Details: null
        );
    }

    /// <summary>
    /// Displays an error to the console with formatting.
    /// </summary>
    public static void DisplayError(FormattedError error, TextWriter? output = null)
    {
        output ??= Console.Error;

        output.WriteLine();
        output.WriteLine($"  Error [{error.Code}]: {error.Title}");
        output.WriteLine("  " + new string('-', 50));

        if (!string.IsNullOrEmpty(error.Message) && error.Message != error.Title)
        {
            output.WriteLine($"  Message: {error.Message}");
        }

        if (!string.IsNullOrEmpty(error.Details))
        {
            output.WriteLine($"  Details: {error.Details}");
        }

        if (!string.IsNullOrEmpty(error.Suggestion))
        {
            output.WriteLine();
            output.WriteLine($"  Suggestion: {error.Suggestion}");
        }

        if (!string.IsNullOrEmpty(error.DocsLink))
        {
            output.WriteLine($"  Documentation: {error.DocsLink}");
        }

        output.WriteLine();
    }

    /// <summary>
    /// Gets a short one-line error message.
    /// </summary>
    public static string GetShortMessage(Exception exception)
    {
        var error = Format(exception);
        return $"[{error.Code}] {error.Title}";
    }

    /// <summary>
    /// Gets all registered error codes with descriptions.
    /// </summary>
    public static IEnumerable<(string Code, string Title, string? Suggestion)> GetAllErrorCodes()
    {
        return ErrorInfos.Values
            .Select(e => (e.Code, e.Title, e.Suggestion))
            .OrderBy(e => e.Code);
    }

    /// <summary>
    /// Displays the error code reference.
    /// </summary>
    public static void DisplayErrorCodeReference(TextWriter? output = null)
    {
        output ??= Console.Out;

        output.WriteLine();
        output.WriteLine("  Error Code Reference");
        output.WriteLine("  " + new string('=', 60));
        output.WriteLine();

        var groups = ErrorInfos.Values
            .GroupBy(e => e.Code.Split('-')[1])
            .OrderBy(g => g.Key);

        foreach (var group in groups)
        {
            var categoryName = group.Key switch
            {
                "CFG" => "Configuration Errors",
                "CON" => "Connection Errors",
                "AUTH" => "Authentication Errors",
                "RATE" => "Rate Limit Errors",
                "DATA" => "Data Errors",
                "STOR" => "Storage Errors",
                "IB" => "Interactive Brokers Errors",
                "ALP" => "Alpaca Errors",
                _ => "Other Errors"
            };

            output.WriteLine($"  {categoryName}:");

            foreach (var error in group.OrderBy(e => e.Code))
            {
                output.WriteLine($"    {error.Code}: {error.Title}");
            }

            output.WriteLine();
        }
    }

    private static string ClassifyException(Exception ex)
    {
        var message = ex.Message.ToLowerInvariant();
        var typeName = ex.GetType().Name.ToLowerInvariant();

        // Connection errors
        if (typeName.Contains("timeout") || message.Contains("timeout"))
            return "CONN_TIMEOUT";

        if (typeName.Contains("socket") && message.Contains("refused"))
            return "CONN_REFUSED";

        if (message.Contains("ssl") || message.Contains("tls") || message.Contains("certificate"))
            return "CONN_SSL_ERROR";

        // Auth errors
        if (message.Contains("401") || message.Contains("unauthorized") || message.Contains("invalid key"))
            return "AUTH_INVALID_KEY";

        if (message.Contains("403") || message.Contains("forbidden") || message.Contains("permission"))
            return "AUTH_INSUFFICIENT_PERMISSIONS";

        // Rate limits
        if (message.Contains("429") || message.Contains("rate limit") || message.Contains("too many requests"))
            return "RATE_LIMIT_EXCEEDED";

        if (message.Contains("quota"))
            return "RATE_LIMIT_QUOTA";

        // Data errors
        if (message.Contains("not found") && (message.Contains("symbol") || message.Contains("ticker")))
            return "DATA_SYMBOL_NOT_FOUND";

        if (message.Contains("no data") || message.Contains("empty"))
            return "DATA_NO_DATA_AVAILABLE";

        // Storage errors
        if (message.Contains("access denied") || message.Contains("permission"))
            return "STORAGE_PERMISSION_DENIED";

        if (message.Contains("disk full") || message.Contains("no space"))
            return "STORAGE_DISK_FULL";

        // Config errors
        if (typeName.Contains("json"))
            return "CONFIG_INVALID_JSON";

        if (message.Contains("configuration") || message.Contains("appsettings"))
            return "CONFIG_VALIDATION_FAILED";

        return "UNKNOWN";
    }

    private sealed record ErrorInfo(
        string Code,
        string Title,
        string? Suggestion,
        string? DocsLink
    );
}

/// <summary>
/// A formatted error ready for display.
/// </summary>
public sealed record FormattedError(
    string Code,
    string Title,
    string Message,
    string? Suggestion,
    string? DocsLink,
    string? Details
);
