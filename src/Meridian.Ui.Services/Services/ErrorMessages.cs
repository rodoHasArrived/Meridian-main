namespace Meridian.Ui.Services;

/// <summary>
/// Centralized error message templates with actionable remediation suggestions.
///
/// All error messages should include:
/// 1. What happened (the error)
/// 2. Why it might have happened (context)
/// 3. What the user can do about it (remedy)
///
/// This approach reduces support burden and improves user experience.
/// </summary>
public static class ErrorMessages
{

    /// <summary>
    /// Error when unable to connect to a data provider.
    /// </summary>
    public static (string Title, string Message, string Remedy) ConnectionFailed(string provider) =>
        ("Connection Failed",
         $"Unable to connect to {provider}.",
         $"Check your network connection and verify your {provider} API credentials in Settings → Credentials.");

    /// <summary>
    /// Error when connection times out.
    /// </summary>
    public static (string Title, string Message, string Remedy) ConnectionTimeout(string provider) =>
        ("Connection Timeout",
         $"Connection to {provider} timed out.",
         "The server may be busy or unreachable. Wait a moment and try again, or check your network connection.");

    /// <summary>
    /// Error when authentication fails.
    /// </summary>
    public static (string Title, string Message, string Remedy) AuthenticationFailed(string provider) =>
        ("Authentication Failed",
         $"{provider} rejected your credentials.",
         $"Verify your API key in Settings → Credentials. If you recently changed your {provider} password, you may need to regenerate your API key.");

    /// <summary>
    /// Error when provider is unavailable.
    /// </summary>
    public static (string Title, string Message, string Remedy) ProviderUnavailable(string provider) =>
        ("Provider Unavailable",
         $"{provider} is currently unavailable.",
         "This may be a temporary outage. Check the provider's status page or try again later.");



    /// <summary>
    /// Error when rate limit is exceeded.
    /// </summary>
    public static (string Title, string Message, string Remedy) RateLimitExceeded(string provider, TimeSpan? retryAfter) =>
        ("Rate Limit Exceeded",
         $"You've exceeded {provider}'s rate limit.",
         retryAfter.HasValue
             ? $"Please wait {FormatTimeSpan(retryAfter.Value)} before making more requests."
             : $"Please wait a few minutes before making more requests. Consider upgrading your {provider} plan for higher limits.");



    /// <summary>
    /// Error when API key is missing.
    /// </summary>
    public static (string Title, string Message, string Remedy) ApiKeyMissing(string provider) =>
        ("API Key Required",
         $"No API key configured for {provider}.",
         $"Add your {provider} API key in Settings → Credentials → {provider}.");

    /// <summary>
    /// Error when configuration file is invalid.
    /// </summary>
    public static (string Title, string Message, string Remedy) InvalidConfiguration(string details) =>
        ("Invalid Configuration",
         $"Configuration error: {details}",
         "Check your appsettings.json file for syntax errors. You can reset to defaults in Settings → Advanced → Reset Configuration.");

    /// <summary>
    /// Error when data directory doesn't exist.
    /// </summary>
    public static (string Title, string Message, string Remedy) DataDirectoryNotFound(string path) =>
        ("Data Directory Not Found",
         $"The data directory does not exist: {path}",
         "Create the directory manually or update the data path in Settings → Storage.");



    /// <summary>
    /// Error when symbol is not found.
    /// </summary>
    public static (string Title, string Message, string Remedy) SymbolNotFound(string symbol) =>
        ("Symbol Not Found",
         $"The symbol '{symbol}' was not found.",
         "Verify the symbol is correct. Use the symbol search to find the exact symbol format for your data provider.");

    /// <summary>
    /// Error when symbol format is invalid.
    /// </summary>
    public static (string Title, string Message, string Remedy) InvalidSymbolFormat(string symbol) =>
        ("Invalid Symbol Format",
         $"The symbol '{symbol}' has an invalid format.",
         "Symbols should contain only uppercase letters, numbers, and dots. Maximum 10 characters.");

    /// <summary>
    /// Error when symbol subscription fails.
    /// </summary>
    public static (string Title, string Message, string Remedy) SubscriptionFailed(string symbol, string reason) =>
        ("Subscription Failed",
         $"Failed to subscribe to {symbol}: {reason}",
         "Check that the collector is running and connected. Try removing and re-adding the symbol.");



    /// <summary>
    /// Error when unable to save data.
    /// </summary>
    public static (string Title, string Message, string Remedy) SaveFailed(string item) =>
        ("Save Failed",
         $"Unable to save {item}.",
         "Check that you have write permissions to the data directory and sufficient disk space.");

    /// <summary>
    /// Error when unable to load data.
    /// </summary>
    public static (string Title, string Message, string Remedy) LoadFailed(string item) =>
        ("Load Failed",
         $"Unable to load {item}.",
         "The file may be corrupted or missing. Check if the file exists and is readable.");

    /// <summary>
    /// Error when disk space is low.
    /// </summary>
    public static (string Title, string Message, string Remedy) LowDiskSpace(string availableSpace) =>
        ("Low Disk Space",
         $"Disk space is running low ({availableSpace} available).",
         "Free up disk space or change the data directory to a drive with more space. Consider running archive maintenance to compress old data.");



    /// <summary>
    /// Error when backfill fails.
    /// </summary>
    public static (string Title, string Message, string Remedy) BackfillFailed(string symbol, string reason) =>
        ("Backfill Failed",
         $"Failed to download historical data for {symbol}: {reason}",
         "Try a different date range or provider. Some providers may not have data for certain symbols or time periods.");

    /// <summary>
    /// Error when backfill is partially complete.
    /// </summary>
    public static (string Title, string Message, string Remedy) BackfillPartial(string symbol, int downloaded, int expected) =>
        ("Backfill Incomplete",
         $"Downloaded {downloaded} bars for {symbol} (expected {expected}).",
         "Some data may be missing for weekends, holidays, or due to provider limitations. This is often expected.");



    /// <summary>
    /// Generic unexpected error.
    /// </summary>
    public static (string Title, string Message, string Remedy) UnexpectedError(string operation, string details) =>
        ("Unexpected Error",
         $"An unexpected error occurred while {operation}: {details}",
         "Try the operation again. If the problem persists, check the logs in Settings → Diagnostics → View Logs.");

    /// <summary>
    /// Error from an exception.
    /// </summary>
    public static (string Title, string Message, string Remedy) FromException(Exception ex, string operation) =>
        ("Operation Failed",
         $"Failed to {operation}: {ex.Message}",
         "Check the logs for more details. If the problem persists, please report it at github.com/rodoHasArrived/Meridian/issues");



    /// <summary>
    /// Formats a TimeSpan into a human-readable string.
    /// </summary>
    private static string FormatTimeSpan(TimeSpan ts)
    {
        if (ts.TotalSeconds < 60)
            return $"{(int)ts.TotalSeconds} seconds";
        if (ts.TotalMinutes < 60)
            return $"{(int)ts.TotalMinutes} minutes";
        return $"{(int)ts.TotalHours} hours";
    }

    /// <summary>
    /// Creates a formatted error message with title, message, and remedy.
    /// </summary>
    public static string Format((string Title, string Message, string Remedy) error)
    {
        return $"{error.Message}\n\nSuggestion: {error.Remedy}";
    }

}
