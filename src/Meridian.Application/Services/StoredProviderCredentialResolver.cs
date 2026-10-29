using Meridian.Application.Config.Credentials;
using Meridian.DataIntegration.Credentials;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Infrastructure.Contracts;
using Meridian.Infrastructure.DataSources;

namespace Meridian.Application.Services;

/// <summary>
/// Resolves catalog-managed provider credentials exclusively through the credential store.
/// Unmanaged provider types retain their legacy resolver; managed records never mix sources.
/// </summary>
public sealed class StoredProviderCredentialResolver : IProviderCredentialResolver
{
    private readonly IProviderCredentialStore _credentialStore;
    private readonly IProviderCredentialResolver _fallback;
    private readonly ProviderCredentialScope? _scope;
    private readonly UI.ConfigStore? _configStore;

    public StoredProviderCredentialResolver(
        IProviderCredentialStore credentialStore,
        IProviderCredentialResolver fallback)
    {
        _credentialStore = credentialStore ?? throw new ArgumentNullException(nameof(credentialStore));
        _fallback = fallback ?? throw new ArgumentNullException(nameof(fallback));
    }

    /// <summary>Resolves host defaults from the current retained configuration at provider construction.</summary>
    public StoredProviderCredentialResolver(IProviderCredentialStore credentialStore,
        IProviderCredentialResolver fallback, UI.ConfigStore configStore) : this(credentialStore, fallback)
    {
        _configStore = configStore ?? throw new ArgumentNullException(nameof(configStore));
    }

    /// <summary>
    /// Binds runtime credential resolution to an already authorized connection scope.
    /// The caller must derive this scope from trusted ownership context. Missing scoped
    /// records never fall back to provider-wide credentials, configuration or environment.
    /// </summary>
    public StoredProviderCredentialResolver(
        IScopedProviderCredentialStore credentialStore,
        IProviderCredentialResolver fallback,
        ProviderCredentialScope scope)
        : this(credentialStore, fallback)
    {
        _scope = scope ?? throw new ArgumentNullException(nameof(scope));
    }

    public ICredentialContext CreateContext(
        Type providerType,
        IReadOnlyDictionary<string, string?>? configuredValues = null)
    {
        ArgumentNullException.ThrowIfNull(providerType);

        var providerId = ResolveProviderId(providerType);
        if (providerId is null)
        {
            if (_scope is not null)
                throw new InvalidOperationException("Scoped credentials require a catalog-managed provider.");
            return _fallback.CreateContext(providerType, configuredValues);
        }

        var descriptor = ProviderCredentialCatalog.Find(providerId);
        if (descriptor is null)
        {
            // A scoped runtime never reaches provider-wide configuration or environment secrets,
            // even if the catalog entry disappears between identity resolution and this lookup.
            if (_scope is not null)
                throw new InvalidOperationException("Scoped credentials require a catalog-managed provider.");
            return _fallback.CreateContext(providerType, configuredValues);
        }

        // The store owns the complete record, including its environment-fallback policy.
        // Missing/removed fields must never be supplied from another account's config or environment.
        // Storage failures propagate instead of silently switching credential ownership.
        // Hold ownership stable while opening the selected vault record. A configuration writer
        // cannot reassign/remove the connection between authorization and secret resolution.
        using var ownershipLease = _configStore?.AcquireWriteLeaseAsync().GetAwaiter().GetResult();
        var scope = _scope ?? (_configStore is null ? null : ProviderRouting.ProviderRuntimeCredentialService.ResolveScope(
            _configStore.LoadRequired(), providerId));
        var stored = scope is null
            ? _credentialStore.ReadForProviderAsync(providerId).GetAwaiter().GetResult()
            : (_credentialStore as IScopedProviderCredentialStore
                ?? throw new InvalidOperationException("Credential vault does not support scoped ownership."))
                .ReadScopedAsync(providerId, scope).GetAwaiter().GetResult();
        var values = new Dictionary<string, string?>(StringComparer.Ordinal);
        if (stored is not null)
        {
            foreach (var attribute in AttributeCredentialResolver.GetAttributes(providerType))
                values[attribute.Name] = ResolveStoredValue(stored, descriptor, attribute.Name);
        }
        return new ResolvedCredentialContext(values, stored?.Environment ?? scope?.Environment);
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
        for (var candidate = providerType; candidate is not null; candidate = candidate.BaseType)
        {
            var declaredProviderId = candidate.GetDataSourceAttribute()?.Id;
            if (string.IsNullOrWhiteSpace(declaredProviderId))
            {
                continue;
            }

            return ProviderCredentialCatalog.Find(declaredProviderId)?.ProviderId;
        }

        return null;
    }

    private sealed class ResolvedCredentialContext : ICredentialContext
    {
        public bool IsAuthoritative => true;

        private readonly IReadOnlyDictionary<string, string?> _values;

        public string? CredentialEnvironment { get; }

        public ResolvedCredentialContext(IReadOnlyDictionary<string, string?> values, string? credentialEnvironment)
        {
            _values = values;
            CredentialEnvironment = credentialEnvironment;
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
