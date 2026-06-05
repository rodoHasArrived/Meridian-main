using Meridian.Application.Config.Credentials;
using Meridian.DataIntegration.Credentials;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Infrastructure.Contracts;

namespace Meridian.Application.Services;

/// <summary>
/// Resolves provider credentials from Meridian's encrypted provider store before falling back to
/// legacy environment/config resolution.
/// </summary>
public sealed class StoredProviderCredentialResolver : IProviderCredentialResolver
{
    private readonly IProviderCredentialStore _credentialStore;
    private readonly IProviderCredentialResolver _fallback;

    public StoredProviderCredentialResolver(
        IProviderCredentialStore credentialStore,
        IProviderCredentialResolver fallback)
    {
        _credentialStore = credentialStore ?? throw new ArgumentNullException(nameof(credentialStore));
        _fallback = fallback ?? throw new ArgumentNullException(nameof(fallback));
    }

    public ICredentialContext CreateContext(
        Type providerType,
        IReadOnlyDictionary<string, string?>? configuredValues = null)
    {
        ArgumentNullException.ThrowIfNull(providerType);

        var fallbackContext = _fallback.CreateContext(providerType, configuredValues);
        var providerId = ResolveProviderId(providerType);
        if (providerId is null)
        {
            return fallbackContext;
        }

        var descriptor = ProviderCredentialCatalog.Find(providerId);
        if (descriptor is null)
        {
            return fallbackContext;
        }

        var stored = ReadStore(providerId);
        if (stored is null)
        {
            return fallbackContext;
        }

        var values = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var attribute in AttributeCredentialResolver.GetAttributes(providerType))
        {
            values[attribute.Name] = FirstNonBlank(
                ResolveStoredValue(stored, descriptor, attribute.Name),
                fallbackContext.Get(attribute.Name));
        }

        return new ResolvedCredentialContext(values);
    }

    private ProviderCredentialReadResult? ReadStore(string providerId)
    {
        try
        {
            return _credentialStore.ReadForProviderAsync(providerId).GetAwaiter().GetResult();
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static string? ResolveStoredValue(
        ProviderCredentialReadResult stored,
        ProviderCredentialCatalogEntry descriptor,
        string credentialName)
    {
        if (stored.Credentials.TryGetValue(credentialName, out var directValue))
        {
            return directValue;
        }

        var field = descriptor.RequiredFields.FirstOrDefault(field =>
            string.Equals(field.Name, credentialName, StringComparison.OrdinalIgnoreCase) ||
            field.EnvironmentNames.Any(env => string.Equals(env, credentialName, StringComparison.OrdinalIgnoreCase)));

        return field is null ? null : stored.Get(field.Name);
    }

    private static string? ResolveProviderId(Type providerType)
    {
        var name = providerType.Name;
        if (name.Contains("Alpaca", StringComparison.OrdinalIgnoreCase))
        {
            return "alpaca";
        }

        if (name.Contains("Polygon", StringComparison.OrdinalIgnoreCase))
        {
            return "polygon";
        }

        if (name.Contains("Finnhub", StringComparison.OrdinalIgnoreCase))
        {
            return "finnhub";
        }

        if (name.Contains("Tiingo", StringComparison.OrdinalIgnoreCase))
        {
            return "tiingo";
        }

        if (name.Contains("AlphaVantage", StringComparison.OrdinalIgnoreCase))
        {
            return "alphavantage";
        }

        if (name.Contains("NasdaqDataLink", StringComparison.OrdinalIgnoreCase))
        {
            return "nasdaqdatalink";
        }

        if (name.Contains("OpenFigi", StringComparison.OrdinalIgnoreCase))
        {
            return "openfigi";
        }

        return null;
    }

    private static string? FirstNonBlank(params string?[] values)
        => values.FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value))?.Trim();

    private sealed class ResolvedCredentialContext : ICredentialContext
    {
        private readonly IReadOnlyDictionary<string, string?> _values;

        public ResolvedCredentialContext(IReadOnlyDictionary<string, string?> values)
        {
            _values = values;
        }

        public string? Get(string name)
        {
            ArgumentException.ThrowIfNullOrEmpty(name);
            return _values.TryGetValue(name, out var value) ? value : null;
        }

        public bool IsConfigured(string name)
            => !string.IsNullOrWhiteSpace(Get(name));
    }
}
