using Meridian.Contracts.Catalog;
using Meridian.Contracts.SecurityMaster;
using Meridian.DataIntegration.Canonicalization;
using Meridian.Storage.SecurityMaster;
using Microsoft.Extensions.Logging;

namespace Meridian.Application.SecurityMaster;

/// <summary>
/// Seeds the <see cref="ICanonicalSymbolRegistry"/> from the Security Master projection cache
/// on startup, and maintains a reverse lookup from canonical ticker to security ID for
/// per-event enrichment in the canonicalization pipeline.
/// </summary>
/// <remarks>
/// <para>
/// This service is called by <see cref="SecurityMasterProjectionWarmupService"/> after the
/// projection cache has been populated.  It is safe to call <see cref="SeedAsync"/> multiple
/// times (idempotent: the last call wins).
/// </para>
/// <para>
/// The <see cref="TryGetSecurityId"/> method is thread-safe and suitable for use in
/// hot-path event processing.  It reads a volatile snapshot reference so it sees the most
/// recent seed without any per-call locking.
/// </para>
/// </remarks>
public sealed class SecurityMasterCanonicalSymbolSeedService : ICanonicalSecurityIdLookup
{
    private readonly SecurityMasterProjectionCache _cache;
    private readonly ICanonicalSymbolRegistry _registry;
    private readonly ILogger<SecurityMasterCanonicalSymbolSeedService> _logger;

    private readonly ISecurityMasterStore? _authoritativeStore;

    private readonly SemaphoreSlim _seedLock = new(1, 1);
    private long _nextValidityTransitionUtcTicks;

    internal TimeProvider Clock { get; init; } = TimeProvider.System;

    /// <summary>The next live identifier-window boundary in the last successfully published seed.</summary>
    internal DateTimeOffset? NextValidityTransitionUtc
    {
        get
        {
            var ticks = Interlocked.Read(ref _nextValidityTransitionUtcTicks);
            return ticks == 0 ? null : new DateTimeOffset(ticks, TimeSpan.Zero);
        }
    }

    internal event Action? ValidityScheduleChanged;

    // Thread-safe reverse lookup: canonical ticker (case-insensitive) → SecurityId.
    // Replaced atomically by SeedAsync; read concurrently by EventCanonicalizer.
    private volatile IReadOnlyDictionary<string, Guid> _tickerToSecurityId =
        new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);

    public SecurityMasterCanonicalSymbolSeedService(
        SecurityMasterProjectionCache cache,
        ICanonicalSymbolRegistry registry,
        ILogger<SecurityMasterCanonicalSymbolSeedService> logger,
        ISecurityMasterStore? store = null,
        SecurityMasterOptions? options = null)
    {
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        // Registry withdrawal requires complete membership even if startup cache warming failed.
        // A supplied store is authoritative in every cache mode; optional cache-only hosts retain
        // their original contract and must supply a complete snapshot themselves.
        _authoritativeStore = store;
    }

    /// <summary>
    /// Resolves a canonical ticker to its Security Master ID.
    /// Returns <see langword="null"/> when the ticker is unknown or the seed has not yet run.
    /// </summary>
    public Guid? TryGetSecurityId(string canonicalTicker)
    {
        if (string.IsNullOrWhiteSpace(canonicalTicker))
            return null;

        return _tickerToSecurityId.TryGetValue(canonicalTicker.Trim(), out var id) ? id : null;
    }

    /// <summary>
    /// Reads all active securities from the projection cache, registers provider-aware symbol
    /// definitions into the canonical registry, and atomically replaces the ticker→SecurityId
    /// reverse-lookup map.
    /// </summary>
    public async Task SeedAsync(CancellationToken ct = default)
    {
        await _seedLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await ReconcileSnapshotAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            _seedLock.Release();
        }
    }

    private IReadOnlyCollection<Guid> GetReconciliationScope(IReadOnlyCollection<SecurityProjectionRecord> snapshot)
    {
        if (_authoritativeStore is null)
        {
            // Optional cache-only hosts cannot prove complete membership. Reconcile only the IDs
            // their snapshot actually supplies; absence cannot withdraw another retained ID.
            return snapshot.Select(record => record.SecurityId).Distinct().ToArray();
        }

        // Durable membership can shrink while this process is stopped. Include retained registry
        // provenance as well as current IDs so a deleted security withdraws its owned mappings on
        // the first seed after restart. An ID or matching spelling alone never proves ownership.
        return snapshot.Select(record => record.SecurityId)
            .Concat(_registry.GetAll()
                .Where(definition => definition.SecurityId.HasValue
                    && (definition.SecurityMasterOwnsCanonical
                        || definition.AliasDefinitions.Any(alias => IsSecurityMasterSource(alias.Source))
                        || definition.ProviderSymbols.Values.Any(provider => !provider.IsOverride
                            && IsSecurityMasterSource(provider.Source))))
                .Select(definition => definition.SecurityId!.Value))
            .Distinct()
            .ToArray();
    }

    private static bool IsSecurityMasterSource(string? source)
        => string.Equals(source, SymbolMappingSources.SecurityMaster, StringComparison.OrdinalIgnoreCase)
           || string.Equals(source, SymbolMappingSources.SecurityMasterIdentifier, StringComparison.OrdinalIgnoreCase);

    private async Task ReconcileSnapshotAsync(CancellationToken ct)
    {
        // Capture only after taking the gate: a slow earlier seed must never overwrite a
        // later projection refresh with an older snapshot.
        // Cache membership is not authoritative during failed startup warming or a disconnected
        // listener. Seed from durable membership without changing whether the host warms its cache.
        var snapshot = _authoritativeStore is null
            ? _cache.Snapshot()
            : await _authoritativeStore.LoadAllAsync(ct).ConfigureAwait(false);

        var definitions = new List<CanonicalSymbolDefinition>(snapshot.Count);
        var reverseMap = new Dictionary<string, Guid>(snapshot.Count, StringComparer.OrdinalIgnoreCase);
        var now = Clock.GetUtcNow();
        DateTimeOffset? nextValidityTransition = null;

        void TrackBoundary(DateTimeOffset? boundary)
        {
            if (boundary is { } candidate && candidate > now
                && (!nextValidityTransition.HasValue || candidate < nextValidityTransition.Value))
                nextValidityTransition = candidate;
        }

        foreach (var record in snapshot)
        {
            ct.ThrowIfCancellationRequested();

            if (record.Status == SecurityStatusDto.Inactive)
                continue;

            var ticker = record.PrimaryIdentifierValue?.Trim();
            if (string.IsNullOrWhiteSpace(ticker))
                continue;

            // The canonical registry normalizes tickers. Detect conflicts under that same key
            // before publishing anything, including in hosts implementing only the base registry.
            if (reverseMap.TryGetValue(ticker, out var incumbentId) && incumbentId != record.SecurityId)
            {
                throw new InvalidOperationException(
                    $"Canonical ticker '{ticker}' is assigned to multiple active Security Master IDs; resolve the identifier conflict before reseeding.");
            }

            // Build the alias list from provider-specific symbols and standard identifiers.
            var aliases = new List<string>();
            var aliasDefinitions = new List<CanonicalSymbolAliasDefinition>();
            var providerSymbols = new Dictionary<string, ProviderSymbolDefinition>(StringComparer.OrdinalIgnoreCase);
            foreach (var alias in record.Aliases)
            {
                if (string.IsNullOrWhiteSpace(alias.AliasValue))
                    continue;

                if (alias.IsEnabled)
                {
                    TrackBoundary(alias.ValidFrom);
                    TrackBoundary(alias.ValidTo);
                }

                var isCurrent = alias.IsEnabled && alias.ValidFrom <= now &&
                    (alias.ValidTo is null || alias.ValidTo > now);
                aliasDefinitions.Add(new CanonicalSymbolAliasDefinition(
                    alias.AliasValue,
                    Source: SymbolMappingSources.SecurityMaster,
                    Provider: alias.Provider,
                    ValidFrom: alias.ValidFrom,
                    ValidTo: alias.ValidTo,
                    IsActive: isCurrent));

                if (!isCurrent)
                    continue;

                if (string.IsNullOrWhiteSpace(alias.Provider))
                {
                    aliases.Add(alias.AliasValue);
                }
                else
                {
                    providerSymbols[alias.Provider.Trim().ToLowerInvariant()] = new ProviderSymbolDefinition(
                        alias.AliasValue,
                        SymbolMappingSources.SecurityMaster,
                        IsOverride: false,
                        UpdatedAt: now);
                }
            }

            string? isin = null, cusip = null, figi = null, sedol = null;
            foreach (var id in record.Identifiers)
            {
                if (id.Kind is not (SecurityIdentifierKind.Isin or SecurityIdentifierKind.Cusip
                    or SecurityIdentifierKind.Figi or SecurityIdentifierKind.Sedol)
                    || string.IsNullOrWhiteSpace(id.Value))
                {
                    continue;
                }

                var isCurrent = id.ValidFrom <= now && (id.ValidTo is null || id.ValidTo > now);
                TrackBoundary(id.ValidFrom);
                TrackBoundary(id.ValidTo);
                // Retain the window for audit/history, but only current identifiers may resolve
                // through the live registry or populate its standard-identifier indexes.
                aliasDefinitions.Add(new CanonicalSymbolAliasDefinition(
                    id.Value,
                    // The typed-identifier marker asserts this seed supplied the indexed field.
                    // Inactive history owns its alias only; claiming the field would let a later
                    // reseed clear an independently supplied identifier with the same spelling.
                    Source: isCurrent ? SymbolMappingSources.SecurityMasterIdentifier : SymbolMappingSources.SecurityMaster,
                    Provider: null,
                    ValidFrom: id.ValidFrom,
                    ValidTo: id.ValidTo,
                    IsActive: isCurrent));
                if (!isCurrent)
                    continue;

                switch (id.Kind)
                {
                    case SecurityIdentifierKind.Isin:
                        isin = id.Value;
                        break;
                    case SecurityIdentifierKind.Cusip:
                        cusip = id.Value;
                        break;
                    case SecurityIdentifierKind.Figi:
                        figi = id.Value;
                        break;
                    case SecurityIdentifierKind.Sedol:
                        sedol = id.Value;
                        break;
                }

                // Reconciliation owns this alias, so correction or removal withdraws it.
                aliases.Add(id.Value);
            }

            definitions.Add(new CanonicalSymbolDefinition
            {
                Canonical = ticker,
                SecurityId = record.SecurityId,
                DisplayName = record.DisplayName,
                AssetClass = record.AssetClass,
                Aliases = aliases,
                AliasDefinitions = aliasDefinitions,
                ProviderSymbols = providerSymbols,
                Isin = isin,
                Cusip = cusip,
                Figi = figi,
                Sedol = sedol,
            });

            reverseMap[ticker] = record.SecurityId;
        }

        // Registries that can reconcile Security Master-owned entries drop superseded aliases; a
        // registry implementing only the base contract keeps the merge-only seed it always had,
        // so the reverse lookup below is still populated in that host.
        var registered = _registry is ICanonicalSymbolRegistrySecurityMasterWriter writer
            ? await writer.ReconcileSecurityMasterAsync(
                GetReconciliationScope(snapshot), definitions, ct).ConfigureAwait(false)
            : await _registry.RegisterBatchAsync(definitions, ct).ConfigureAwait(false);

        // Atomically replace the reverse lookup so readers immediately see the new map.
        _tickerToSecurityId = reverseMap;

        // The timer describes the same successfully published snapshot as the live registry.
        // A failed durable seed keeps its previous boundary so the hosted owner retries it.
        var nextTicks = nextValidityTransition?.UtcTicks ?? 0;
        if (Interlocked.Exchange(ref _nextValidityTransitionUtcTicks, nextTicks) != nextTicks)
            ValidityScheduleChanged?.Invoke();

        _logger.LogInformation(
            "Seeded canonical symbol registry from Security Master: {Registered}/{Total} active securities registered.",
            registered, snapshot.Count);
    }
}
