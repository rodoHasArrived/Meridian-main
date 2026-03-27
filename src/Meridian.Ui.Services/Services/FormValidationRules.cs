using System.Text.RegularExpressions;

namespace Meridian.Ui.Services.Services;

/// <summary>
/// Shared validation rules for form validation across desktop applications.
/// Extracted from FormValidationService implementations to eliminate duplicate validation logic.
/// </summary>
public static class FormValidationRules
{
    /// <summary>
    /// Validates that a field is not empty.
    /// </summary>
    /// <param name="value">The value to validate.</param>
    /// <param name="fieldName">The name of the field for error messages (default: "This field").</param>
    /// <returns>Validation result.</returns>
    public static ValidationResult ValidateRequired(string? value, string fieldName = "This field")
    {
        if (string.IsNullOrWhiteSpace(value))
            return ValidationResult.Error($"{fieldName} is required.");
        return ValidationResult.Success();
    }

    /// <summary>
    /// Validates a stock symbol format.
    /// Symbols must be 1-10 characters, start with a letter, and contain only letters, numbers, dots, dashes, and slashes.
    /// </summary>
    /// <param name="value">The symbol to validate.</param>
    /// <returns>Validation result.</returns>
    public static ValidationResult ValidateSymbol(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return ValidationResult.Error("Symbol is required.");

        var trimmed = value.Trim();
        if (trimmed.Length < 1 || trimmed.Length > 10)
            return ValidationResult.Error("Symbol must be between 1 and 10 characters.");

        // Symbol must start with a letter and contain only letters, numbers, dots, dashes, and slashes
        if (!Regex.IsMatch(trimmed, @"^[A-Za-z][A-Za-z0-9./-]*$"))
            return ValidationResult.Error("Symbol must start with a letter and can only contain letters, numbers, dots, dashes, and slashes.");

        return ValidationResult.Success();
    }

    /// <summary>
    /// Validates a comma-separated list of symbols.
    /// </summary>
    /// <param name="value">The comma-separated symbol list.</param>
    /// <param name="maxSymbols">Maximum number of symbols allowed (default: 100).</param>
    /// <returns>Validation result.</returns>
    public static ValidationResult ValidateSymbolList(string? value, int maxSymbols = 100)
    {
        if (string.IsNullOrWhiteSpace(value))
            return ValidationResult.Error("At least one symbol is required.");

        var symbols = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (symbols.Length == 0)
            return ValidationResult.Error("At least one symbol is required.");
        if (symbols.Length > maxSymbols)
            return ValidationResult.Error($"Maximum {maxSymbols} symbols allowed.");

        var invalidSymbols = new List<string>();
        foreach (var symbol in symbols)
        {
            var result = ValidateSymbol(symbol);
            if (!result.IsValid)
                invalidSymbols.Add(symbol);
        }

        if (invalidSymbols.Count > 0)
            return ValidationResult.Error($"Invalid symbol(s): {string.Join(", ", invalidSymbols.Take(5))}{(invalidSymbols.Count > 5 ? "..." : "")}");

        return ValidationResult.Success();
    }

    /// <summary>
    /// Validates a date range for data collection or backfill.
    /// </summary>
    /// <param name="fromDate">Start date.</param>
    /// <param name="toDate">End date.</param>
    /// <returns>Validation result.</returns>
    public static ValidationResult ValidateDateRange(DateTimeOffset? fromDate, DateTimeOffset? toDate)
    {
        if (!fromDate.HasValue || !toDate.HasValue)
            return ValidationResult.Error("Both start and end dates are required.");
        if (fromDate.Value > toDate.Value)
            return ValidationResult.Error("Start date must be before or equal to end date.");
        if (fromDate.Value > DateTimeOffset.Now)
            return ValidationResult.Error("Start date cannot be in the future.");

        var daysDiff = (toDate.Value - fromDate.Value).TotalDays;
        if (daysDiff > 365 * 10)
            return ValidationResult.Warning("Date range spans more than 10 years. This may take a long time.");

        return ValidationResult.Success();
    }

    /// <summary>
    /// Validates a date string in ISO format (YYYY-MM-DD).
    /// </summary>
    /// <param name="dateStr">The date string to validate.</param>
    /// <returns>Validation result.</returns>
    public static ValidationResult ValidateDateRange(string? dateStr)
    {
        if (string.IsNullOrWhiteSpace(dateStr))
            return ValidationResult.Error("Date is required.");

        // Try to parse as DateOnly in strict ISO format: YYYY-MM-DD only
        if (!DateOnly.TryParseExact(dateStr, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out _))
            return ValidationResult.Error("Invalid date format. Use YYYY-MM-DD format.");

        return ValidationResult.Success();
    }

    /// <summary>
    /// Validates an API key format.
    /// </summary>
    /// <param name="value">The API key to validate.</param>
    /// <returns>Validation result.</returns>
    public static ValidationResult ValidateApiKey(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return ValidationResult.Error("API key is required.");
        if (value.Length < 8)
            return ValidationResult.Error("API key seems too short. Please check the key.");
        if (value.Contains(' '))
            return ValidationResult.Error("API key should not contain spaces.");
        return ValidationResult.Success();
    }

    /// <summary>
    /// Validates a URL format.
    /// </summary>
    /// <param name="value">The URL to validate.</param>
    /// <param name="fieldName">The name of the field for error messages (default: "URL").</param>
    /// <returns>Validation result.</returns>
    public static ValidationResult ValidateUrl(string? value, string fieldName = "URL")
    {
        if (string.IsNullOrWhiteSpace(value))
            return ValidationResult.Error($"{fieldName} is required.");
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
            return ValidationResult.Error($"Invalid {fieldName} format. Example: http://localhost:8080");
        if (uri.Scheme != "http" && uri.Scheme != "https")
            return ValidationResult.Error($"{fieldName} must start with http:// or https://");
        return ValidationResult.Success();
    }

    /// <summary>
    /// Validates a network port number.
    /// </summary>
    /// <param name="value">The port number to validate.</param>
    /// <returns>Validation result.</returns>
    public static ValidationResult ValidatePort(int? value)
    {
        if (!value.HasValue)
            return ValidationResult.Error("Port is required.");
        if (value < 1 || value > 65535)
            return ValidationResult.Error("Port must be between 1 and 65535.");
        if (value < 1024)
            return ValidationResult.Warning("Port below 1024 may require administrator privileges.");
        return ValidationResult.Success();
    }

    /// <summary>
    /// Validates a file path format.
    /// </summary>
    /// <param name="value">The file path to validate.</param>
    /// <returns>Validation result.</returns>
    public static ValidationResult ValidateFilePath(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return ValidationResult.Error("File path is required.");
        try
        {
            _ = System.IO.Path.GetFullPath(value);
            return ValidationResult.Success();
        }
        catch
        {
            return ValidationResult.Error("Invalid file path format.");
        }
    }

    /// <summary>
    /// Validates that a numeric value is within a specified range.
    /// </summary>
    /// <param name="value">The numeric value to validate.</param>
    /// <param name="min">Minimum allowed value.</param>
    /// <param name="max">Maximum allowed value.</param>
    /// <param name="fieldName">The name of the field for error messages (default: "Value").</param>
    /// <returns>Validation result.</returns>
    public static ValidationResult ValidateNumericRange(double? value, double min, double max, string fieldName = "Value")
    {
        if (!value.HasValue)
            return ValidationResult.Error($"{fieldName} is required.");
        if (value < min || value > max)
            return ValidationResult.Error($"{fieldName} must be between {min} and {max}.");
        return ValidationResult.Success();
    }
}

/// <summary>
/// Represents the result of a validation operation.
/// </summary>
public sealed class ValidationResult
{
    /// <summary>
    /// Gets whether the validation passed.
    /// </summary>
    public bool IsValid { get; private init; }

    /// <summary>
    /// Gets whether this is a warning (valid but with advisory message).
    /// </summary>
    public bool IsWarning { get; private init; }

    /// <summary>
    /// Gets the validation message (empty for success, descriptive for errors/warnings).
    /// </summary>
    public string Message { get; private init; } = string.Empty;

    private ValidationResult() { }

    /// <summary>
    /// Creates a successful validation result.
    /// </summary>
    public static ValidationResult Success() => new() { IsValid = true };

    /// <summary>
    /// Creates a failed validation result with an error message.
    /// </summary>
    /// <param name="message">The error message.</param>
    public static ValidationResult Error(string message) => new() { IsValid = false, Message = message };

    /// <summary>
    /// Creates a warning validation result (valid but with advisory message).
    /// </summary>
    /// <param name="message">The warning message.</param>
    public static ValidationResult Warning(string message) => new() { IsValid = true, IsWarning = true, Message = message };

    /// <summary>
    /// Implicit conversion to bool for convenient checking.
    /// </summary>
    public static implicit operator bool(ValidationResult result) => result.IsValid;
}

/// <summary>
/// Extension methods for working with multiple validation results.
/// </summary>
public static class ValidationExtensions
{
    /// <summary>
    /// Combines multiple validation results, returning the first error or warning found.
    /// </summary>
    /// <param name="results">The validation results to combine.</param>
    /// <returns>Combined validation result.</returns>
    public static ValidationResult Combine(params ValidationResult[] results)
    {
        foreach (var result in results)
            if (!result.IsValid && !result.IsWarning)
                return result;

        foreach (var result in results)
            if (result.IsWarning)
                return result;

        return ValidationResult.Success();
    }
}
