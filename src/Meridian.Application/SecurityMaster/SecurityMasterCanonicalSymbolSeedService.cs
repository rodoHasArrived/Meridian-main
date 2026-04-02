using Meridian.Contracts.Catalog;
using Meridian.Contracts.SecurityMaster;
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
public sealed class SecurityMasterCanonicalSymbolSeedService
{
    private readonly SecurityMasterProjectionCache _cache;
    private readonly ICanonicalSymbolRegistry _registry;
    private readonly ILogger<SecurityMasterCanonicalSymbolSeedService> _logger;

    // Thread-safe reverse lookup: canonical ticker (case-insensitive) → SecurityId.
    // Replaced atomically by SeedAsync; read concurrently by EventCanonicalizer.
    private volatile IReadOnlyDictionary<string, Guid> _tickerToSecurityId =
        new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);

    public SecurityMasterCanonicalSymbolSeedService(
        SecurityMasterProjectionCache cache,
        ICanonicalSymbolRegistry registry,
        ILogger<SecurityMasterCanonicalSymbolSeedService> logger)
    {
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Resolves a canonical ticker to its Security Master ID.
    /// Returns <see langword="null"/> when the ticker is unknown or the seed has not yet run.
    /// </summary>
    public Guid? TryGetSecurityId(string canonicalTicker)
    {
        if (string.IsNullOrWhiteSpace(canonicalTicker))
            return null;

        return _tickerToSecurityId.TryGetValue(canonicalTicker, out var id) ? id : null;
    }

    /// <summary>
    /// Reads all active securities from the projection cache, registers provider-aware symbol
    /// definitions into the canonical registry, and atomically replaces the ticker→SecurityId
    /// reverse-lookup map.
    /// </summary>
    public async Task SeedAsync(CancellationToken ct = default)
    {
        var snapshot = _cache.Snapshot();
        if (snapshot.Count == 0)
        {
            _logger.LogDebug("Security Master projection cache is empty; canonical registry seed skipped.");
            return;
        }

        var definitions = new List<CanonicalSymbolDefinition>(snapshot.Count);
        var reverseMap = new Dictionary<string, Guid>(snapshot.Count, StringComparer.OrdinalIgnoreCase);

        foreach (var record in snapshot)
        {
            ct.ThrowIfCancellationRequested();

            if (record.Status == SecurityStatusDto.Inactive)
                continue;

            var ticker = record.PrimaryIdentifierValue;
            if (string.IsNullOrWhiteSpace(ticker))
                continue;

            // Build the alias list from provider-specific symbols and standard identifiers.
            var aliases = new List<string>();
            foreach (var alias in record.Aliases)
            {
                if (alias.IsEnabled && !string.IsNullOrWhiteSpace(alias.AliasValue))
                    aliases.Add(alias.AliasValue);
            }

            string? isin = null, cusip = null, figi = null, sedol = null;
            foreach (var id in record.Identifiers)
            {
                switch (id.Kind)
                {
                    case SecurityIdentifierKind.Isin:
                        isin = id.Value;
                        if (!string.IsNullOrWhiteSpace(id.Value))
                            aliases.Add(id.Value);
                        break;
                    case SecurityIdentifierKind.Cusip:
                        cusip = id.Value;
                        if (!string.IsNullOrWhiteSpace(id.Value))
                            aliases.Add(id.Value);
                        break;
                    case SecurityIdentifierKind.Figi:
                        figi = id.Value;
                        if (!string.IsNullOrWhiteSpace(id.Value))
                            aliases.Add(id.Value);
                        break;
                    case SecurityIdentifierKind.Sedol:
                        sedol = id.Value;
                        if (!string.IsNullOrWhiteSpace(id.Value))
                            aliases.Add(id.Value);
                        break;
                }
            }

            definitions.Add(new CanonicalSymbolDefinition
            {
                Canonical = ticker,
                DisplayName = record.DisplayName,
                AssetClass = record.AssetClass,
                Aliases = aliases,
                Isin = isin,
                Cusip = cusip,
                Figi = figi,
                Sedol = sedol,
            });

            reverseMap[ticker] = record.SecurityId;
        }

        if (definitions.Count == 0)
        {
            _logger.LogDebug("No active securities found in Security Master projection cache; registry seed skipped.");
            return;
        }

        var registered = await _registry.RegisterBatchAsync(definitions, ct).ConfigureAwait(false);

        // Atomically replace the reverse lookup so readers immediately see the new map.
        _tickerToSecurityId = reverseMap;

        _logger.LogInformation(
            "Seeded canonical symbol registry from Security Master: {Registered}/{Total} active securities registered.",
            registered, snapshot.Count);
    }
}
