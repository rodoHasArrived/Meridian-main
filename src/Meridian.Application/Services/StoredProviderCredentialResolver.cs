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
        //
        // Provider construction is synchronous, so this method cannot await the lease or the
        // vault. An asynchronous caller reads them first with PrepareAsync; inside that prepared
        // scope the record is already in hand and nothing here blocks.
        var (scope, stored) = ActivePreparation.Value is { } prepared && prepared.Owner == this
            && prepared.TryGet(providerId, out var preparedRead)
            ? preparedRead.GetOrThrow()
            : ReadStoredCredentialsBlocking(providerId);
        var values = new Dictionary<string, string?>(StringComparer.Ordinal);
        if (stored is not null)
        {
            foreach (var attribute in AttributeCredentialResolver.GetAttributes(providerType))
                values[attribute.Name] = ResolveStoredValue(stored, descriptor, attribute.Name);
        }
        return new ResolvedCredentialContext(values, stored?.Environment ?? scope?.Environment);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Reads the selected vault records under one ownership lease, exactly as a synchronous
    /// <see cref="CreateContext"/> would read each of them. A record that fails to read does not
    /// fail the preparation: the failure is kept and rethrown when that provider is constructed,
    /// so one unreadable record still only affects its own provider.
    /// </remarks>
    public async ValueTask<IPreparedProviderCredentials> PrepareAsync(
        IReadOnlyCollection<Type>? providerTypes = null,
        CancellationToken ct = default)
    {
        var providerIds = providerTypes is null
            ? ProviderCredentialCatalog.All.Select(static entry => entry.ProviderId)
            : providerTypes.Select(ResolveProviderId).Where(static id => id is not null).Select(static id => id!);
        var distinct = providerIds.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (distinct.Length == 0)
            return PreparedProviderCredentials.None;

        var reads = new Dictionary<string, PreparedRead>(StringComparer.OrdinalIgnoreCase);
        using (var ownershipLease = _configStore is null
            ? null
            : await _configStore.AcquireWriteLeaseAsync(ct).ConfigureAwait(false))
        {
            foreach (var providerId in distinct)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    reads[providerId] = new PreparedRead(
                        await ReadStoredCredentialsUnderLeaseAsync(providerId, ct).ConfigureAwait(false), null);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    reads[providerId] = new PreparedRead(
                        default, System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex));
                }
            }
        }

        return new Preparation(this, reads);
    }

    /// <summary>
    /// The unprepared path: the caller is synchronous and nothing was read ahead, so it has to
    /// wait. The asynchronous chain runs as one unit on the thread pool and is waited on once;
    /// its continuations never need the caller's synchronization context, so a caller that owns
    /// one cannot deadlock against its own blocked thread.
    /// </summary>
    private (ProviderCredentialScope? Scope, ProviderCredentialReadResult? Stored) ReadStoredCredentialsBlocking(
        string providerId)
        => Task.Run(() => ReadStoredCredentialsAsync(providerId)).GetAwaiter().GetResult();

    private async Task<(ProviderCredentialScope? Scope, ProviderCredentialReadResult? Stored)> ReadStoredCredentialsAsync(
        string providerId)
    {
        using var ownershipLease = _configStore is null
            ? null
            : await _configStore.AcquireWriteLeaseAsync().ConfigureAwait(false);
        return await ReadStoredCredentialsUnderLeaseAsync(providerId, CancellationToken.None).ConfigureAwait(false);
    }

    /// <summary>Resolves ownership and reads the vault record. The caller holds the lease.</summary>
    private async Task<(ProviderCredentialScope? Scope, ProviderCredentialReadResult? Stored)> ReadStoredCredentialsUnderLeaseAsync(
        string providerId,
        CancellationToken ct)
    {
        var scope = _scope ?? (_configStore is null ? null : ProviderRouting.ProviderRuntimeCredentialService.ResolveScope(
            _configStore.LoadRequired(), providerId));
        var stored = scope is null
            ? await _credentialStore.ReadForProviderAsync(providerId, ct).ConfigureAwait(false)
            : await (_credentialStore as IScopedProviderCredentialStore
                ?? throw new InvalidOperationException("Credential vault does not support scoped ownership."))
                .ReadScopedAsync(providerId, scope, ct).ConfigureAwait(false);
        return (scope, stored);
    }

    /// <summary>
    /// The preparation active on the current execution flow. Set only by
    /// <see cref="Preparation.Activate"/>, which is synchronous, so the value is visible to the
    /// construction that follows and restored when the scope is disposed.
    /// </summary>
    private static readonly AsyncLocal<Preparation?> ActivePreparation = new();

    private readonly record struct PreparedRead(
        (ProviderCredentialScope? Scope, ProviderCredentialReadResult? Stored) Value,
        System.Runtime.ExceptionServices.ExceptionDispatchInfo? Failure)
    {
        public (ProviderCredentialScope? Scope, ProviderCredentialReadResult? Stored) GetOrThrow()
        {
            Failure?.Throw();
            return Value;
        }
    }

    private sealed class Preparation(
        StoredProviderCredentialResolver owner,
        IReadOnlyDictionary<string, PreparedRead> reads) : IPreparedProviderCredentials
    {
        public StoredProviderCredentialResolver Owner { get; } = owner;

        public bool TryGet(string providerId, out PreparedRead read) => reads.TryGetValue(providerId, out read);

        public IDisposable Activate()
        {
            var previous = ActivePreparation.Value;
            ActivePreparation.Value = this;
            return new Scope(previous);
        }

        private sealed class Scope(Preparation? previous) : IDisposable
        {
            private bool _disposed;

            public void Dispose()
            {
                if (_disposed)
                    return;
                _disposed = true;
                ActivePreparation.Value = previous;
            }
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
