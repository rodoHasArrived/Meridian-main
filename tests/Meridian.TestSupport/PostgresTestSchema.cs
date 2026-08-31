namespace Meridian.TestSupport;

/// <summary>
/// Generates run-scoped PostgreSQL schema names so concurrent test runs stay isolated.
/// </summary>
public static class PostgresTestSchema
{
    /// <summary>PostgreSQL's maximum identifier size in bytes for ASCII schema names.</summary>
    public const int MaxIdentifierLength = 63;

    /// <summary>
    /// Returns a unique schema name of the form <c>{prefix}_test_{guid:N}</c>, matching the
    /// per-run naming each module previously produced inline (for example
    /// <c>ledger_test_…</c>, <c>dl_test_…</c>, <c>sm_test_…</c>). Safe lowercase ASCII
    /// prefixes are truncated when needed so PostgreSQL never silently truncates two generated
    /// identifiers to the same 63-byte value.
    /// </summary>
    public static string NewSchemaName(string prefix)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prefix);
        if ((prefix[0] != '_' && !char.IsAsciiLetterLower(prefix[0])) ||
            !prefix.All(static c =>
                char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c == '_'))
        {
            throw new ArgumentException(
                "Schema prefixes must start with a lowercase ASCII letter or underscore and " +
                "contain only lowercase ASCII letters, digits, and underscores.",
                nameof(prefix));
        }

        const string separator = "_test_";
        const int guidLength = 32;
        var maximumPrefixLength = MaxIdentifierLength - separator.Length - guidLength;
        var safePrefix = prefix.Length <= maximumPrefixLength
            ? prefix
            : prefix[..maximumPrefixLength];

        return $"{safePrefix}{separator}{Guid.NewGuid():N}";
    }
}
