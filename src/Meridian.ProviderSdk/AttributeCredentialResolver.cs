using System.Reflection;

namespace Meridian.Infrastructure.Contracts;

/// <summary>
/// Resolves provider credentials by scanning <see cref="RequiresCredentialAttribute"/>
/// declarations on a provider type and reading the first matching non-empty environment
/// variable from each attribute's <see cref="RequiresCredentialAttribute.EnvironmentVariables"/>
/// list, with an optional config-value fallback.
/// </summary>
/// <remarks>
/// <para>
/// This is the preferred credential resolution mechanism for new providers.
/// The legacy per-provider credential resolver interface requires a dedicated method per provider
/// and is preserved only for backward compatibility with existing providers.
/// </para>
/// <para><b>Usage:</b></para>
/// <code>
/// var context = AttributeCredentialResolver.ForType(typeof(MyProvider));
/// var apiKey = context.Get("API_KEY");
/// </code>
/// </remarks>
public sealed class AttributeCredentialResolver : ICredentialContext
{
    private readonly Dictionary<string, string?> _resolved;

    private AttributeCredentialResolver(
        IEnumerable<RequiresCredentialAttribute> attributes,
        Func<string, string?>? configLookup)
    {
        _resolved = new Dictionary<string, string?>(StringComparer.Ordinal);

        foreach (var attr in attributes)
        {
            string? value = null;

            // 1. Environment variables — try each in order (first non-empty wins).
            foreach (var envVar in attr.EnvironmentVariables)
            {
                value = Environment.GetEnvironmentVariable(envVar);
                if (!string.IsNullOrEmpty(value))
                    break;
            }

            // 2. Config-based fallback, if provided.
            if (string.IsNullOrEmpty(value) && configLookup is not null)
                value = configLookup(attr.Name);

            _resolved[attr.Name] = value;
        }
    }

    /// <summary>
    /// Creates an <see cref="AttributeCredentialResolver"/> for <paramref name="providerType"/>
    /// by reading all <see cref="RequiresCredentialAttribute"/> declarations on that type.
    /// </summary>
    /// <param name="providerType">The provider class to inspect.</param>
    /// <param name="configLookup">
    /// Optional fallback called with the logical credential name when no environment variable
    /// yields a value. Return <see langword="null"/> if not available via config either.
    /// </param>
    public static AttributeCredentialResolver ForType(
        Type providerType,
        Func<string, string?>? configLookup = null)
    {
        ArgumentNullException.ThrowIfNull(providerType);
        var attrs = providerType.GetCustomAttributes<RequiresCredentialAttribute>(inherit: true);
        return new AttributeCredentialResolver(attrs, configLookup);
    }

    /// <summary>
    /// Returns all <see cref="RequiresCredentialAttribute"/> declarations on the given
    /// <paramref name="providerType"/>, including those inherited from base classes.
    /// </summary>
    public static IReadOnlyList<RequiresCredentialAttribute> GetAttributes(Type providerType)
    {
        ArgumentNullException.ThrowIfNull(providerType);
        return providerType
            .GetCustomAttributes<RequiresCredentialAttribute>(inherit: true)
            .ToList();
    }

    /// <inheritdoc/>
    public string? Get(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        return _resolved.TryGetValue(name, out var value) ? value : null;
    }

    /// <inheritdoc/>
    public bool IsConfigured(string name) => !string.IsNullOrEmpty(Get(name));
}
