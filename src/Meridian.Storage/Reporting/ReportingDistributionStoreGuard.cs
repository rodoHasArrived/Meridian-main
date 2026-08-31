using System.Text.Json;
using Npgsql;

namespace Meridian.Storage.Reporting;

internal static class ReportingDistributionStoreGuard
{
    internal static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    internal static string NormalizeRequired(
        string value,
        string parameterName,
        int maximumLength,
        bool requireCanonical = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        var normalized = value.Trim();
        if (normalized.Length > maximumLength)
        {
            throw new ArgumentException($"{parameterName} cannot exceed {maximumLength} characters.", parameterName);
        }

        if (requireCanonical && !string.Equals(value, normalized, StringComparison.Ordinal))
        {
            throw new ArgumentException($"{parameterName} must not contain surrounding whitespace.", parameterName);
        }

        return normalized;
    }

    internal static void ValidateSha256(string value, string parameterName)
    {
        if (value is null
            || value.Length != 64
            || value.Any(static character =>
                !(character is >= '0' and <= '9' or >= 'a' and <= 'f')))
        {
            throw new ArgumentException(
                $"{parameterName} must be a lowercase 64-character SHA-256 digest.",
                parameterName);
        }
    }

    internal static void ValidateStringSet(
        IReadOnlyList<string> values,
        string parameterName,
        int maximumValueLength)
    {
        ArgumentNullException.ThrowIfNull(values, parameterName);
        var unique = new HashSet<string>(StringComparer.Ordinal);
        foreach (var value in values)
        {
            var normalized = NormalizeRequired(value, parameterName, maximumValueLength, requireCanonical: true);
            if (!unique.Add(normalized))
            {
                throw new ArgumentException($"{parameterName} cannot contain duplicate values.", parameterName);
            }
        }
    }

    internal static void RequireUtc(DateTimeOffset value, string parameterName)
    {
        if (value.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException($"{parameterName} must use a UTC offset.", parameterName);
        }
    }

    internal static DateTimeOffset ReadUtcTimestamp(NpgsqlDataReader reader, int ordinal) =>
        new(DateTime.SpecifyKind(reader.GetDateTime(ordinal), DateTimeKind.Utc));

    internal static DateTimeOffset? ReadNullableUtcTimestamp(NpgsqlDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : ReadUtcTimestamp(reader, ordinal);

    internal static void ValidateIdentifier(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value)
            || !(char.IsAsciiLetter(value[0]) || value[0] == '_')
            || !value.All(static character => char.IsAsciiLetterOrDigit(character) || character == '_'))
        {
            throw new ArgumentException(
                $"PostgreSQL identifier '{value}' is not supported. Use letters, digits, and underscores, and start with a letter or underscore.",
                parameterName);
        }
    }
}
