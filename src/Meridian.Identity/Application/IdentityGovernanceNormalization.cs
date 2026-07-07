namespace Meridian.Identity;

/// <summary>
/// Shared normalization helpers for Identity governance stores and services.
/// Centralizes actor resolution, required-value normalization, correlation-id defaulting,
/// and audit-id generation that were previously duplicated across
/// <see cref="FileUserAccountStore"/>, <see cref="ScopedAccessService"/>, and
/// <see cref="FileRolePermissionProfileStore"/>.
/// </summary>
internal static class IdentityGovernanceNormalization
{
    /// <summary>Length of the <c>yyyyMMddHHmmssfff</c> timestamp component used in audit ids.</summary>
    private const string AuditTimestampFormat = "yyyyMMddHHmmssfff";

    /// <summary>
    /// Trims <paramref name="value"/> and throws when it is null, empty, or whitespace.
    /// </summary>
    public static string NormalizeRequired(string? value, string parameterName)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            throw new ArgumentException($"{parameterName} is required.", parameterName);
        }

        return trimmed;
    }

    /// <summary>
    /// Resolves the effective actor for a governed mutation: an explicit authenticated
    /// <paramref name="actor"/> wins, otherwise the request-supplied
    /// <paramref name="requestedBy"/> is required.
    /// </summary>
    public static string ResolveActor(string? actor, string? requestedBy)
    {
        if (!string.IsNullOrWhiteSpace(actor))
        {
            return actor.Trim();
        }

        return NormalizeRequired(requestedBy, "RequestedBy");
    }

    /// <summary>
    /// Returns the trimmed <paramref name="correlationId"/>, or a deterministic
    /// <paramref name="prefix"/>-scoped value when none is supplied.
    /// </summary>
    public static string NormalizeCorrelationId(string? correlationId, string prefix)
        => string.IsNullOrWhiteSpace(correlationId)
            ? $"{prefix}-{Guid.NewGuid():N}"
            : correlationId.Trim();

    /// <summary>
    /// Builds a governance audit id of the form <c>{prefix}-{timestamp}-{guid}</c>, truncated to
    /// <paramref name="maxLength"/> characters.
    /// </summary>
    public static string NewAuditId(string prefix, DateTimeOffset timestamp, int maxLength = 64)
    {
        var value = $"{prefix}-{timestamp.ToString(AuditTimestampFormat, System.Globalization.CultureInfo.InvariantCulture)}-{Guid.NewGuid():N}";
        return value[..Math.Min(maxLength, value.Length)];
    }
}
