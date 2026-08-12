using System.Globalization;
using Meridian.Contracts.Integrations;

namespace Meridian.Application.Integrations;

/// <summary>
/// The single owner of provider-integration field coercion: decimal and date parsing, transform
/// parameter lookup, and negative-value sign conditioning.
/// </summary>
/// <remarks>
/// A dry run exists to predict what a real import will do, so the dry-run, REST dry-run, and
/// quarantine-replay paths have to coerce a value identically. Keeping one copy here makes that
/// equivalence structural rather than coincidental, and keeps the operator-visible issue codes and
/// remediation text — which are part of the contract — in one place.
/// </remarks>
internal static class ProviderIntegrationFieldTransforms
{
    /// <summary>
    /// Parses a provider value as a decimal, recording <c>transform.decimal.invalid</c> when it
    /// cannot be coerced.
    /// </summary>
    /// <remarks>
    /// Commas are stripped as thousands separators and the value is parsed invariant-culture. That
    /// means a European-formatted number (<c>1.234,56</c>) is read as <c>1.23456</c> rather than
    /// rejected. This is existing behavior, preserved deliberately here so the decision lives in one
    /// place; see the tests for the cases it pins.
    /// </remarks>
    public static object? ParseDecimal(string value, string targetField, List<ValidationIssueDto> issues)
    {
        ArgumentNullException.ThrowIfNull(issues);

        var normalized = value.Replace(",", string.Empty, StringComparison.Ordinal).Trim();
        if (decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        issues.Add(new ValidationIssueDto(
            "transform.decimal.invalid",
            ProviderIntegrationIssueSeverityDto.Critical,
            $"Value '{value}' could not be parsed as a decimal.",
            targetField,
            "Confirm the source number format or choose the correct decimal parsing transform."));
        return null;
    }

    /// <summary>
    /// Parses a provider value as a date and renders it as <c>yyyy-MM-dd</c> UTC, recording
    /// <c>transform.date.invalid</c> when it cannot be coerced.
    /// </summary>
    public static object? ParseDate(string value, string targetField, List<ValidationIssueDto> issues)
    {
        ArgumentNullException.ThrowIfNull(issues);

        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed))
        {
            return parsed.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        issues.Add(new ValidationIssueDto(
            "transform.date.invalid",
            ProviderIntegrationIssueSeverityDto.Critical,
            $"Value '{value}' could not be parsed as a date.",
            targetField,
            "Confirm the provider date format or choose the correct date parsing transform."));
        return null;
    }

    /// <summary>Reads one transform parameter off a field mapping, or <see langword="null"/>.</summary>
    public static string? GetTransformParameter(FieldMappingDto mapping, string key)
        => mapping.Transform?.Parameters.TryGetValue(key, out var value) == true ? value : null;

    /// <summary>Splits a comma-separated transform parameter into its trimmed, non-empty entries.</summary>
    public static IReadOnlyList<string> SplitTransformList(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>
    /// Parses a decimal and flips its sign to negative when the mapping's condition field holds one
    /// of the configured <c>negativeValues</c>.
    /// </summary>
    /// <param name="readConditionValue">
    /// Resolves the condition field from the caller's record shape. This is the only genuine
    /// difference between the CSV-backed and JSON-backed callers: CSV looks the column up in a
    /// dictionary after normalizing the path, JSON reads it out of a <c>JsonElement</c>.
    /// </param>
    public static object? ParseSignedAmount(
        string value,
        FieldMappingDto mapping,
        Func<string, string?> readConditionValue,
        List<ValidationIssueDto> issues)
    {
        ArgumentNullException.ThrowIfNull(readConditionValue);

        var parsed = ParseDecimal(value, mapping.TargetField, issues);
        if (parsed is not decimal amount)
        {
            return null;
        }

        var conditionPath = GetTransformParameter(mapping, "conditionSourcePath")
            ?? GetTransformParameter(mapping, "conditionColumn");
        if (string.IsNullOrWhiteSpace(conditionPath))
        {
            return amount;
        }

        var conditionValue = readConditionValue(conditionPath);
        if (string.IsNullOrWhiteSpace(conditionValue))
        {
            return amount;
        }

        var negativeValues = SplitTransformList(GetTransformParameter(mapping, "negativeValues"));
        return negativeValues.Contains(conditionValue.Trim(), StringComparer.OrdinalIgnoreCase)
            ? -Math.Abs(amount)
            : amount;
    }
}
