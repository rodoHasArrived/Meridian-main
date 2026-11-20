using System.Collections.Immutable;
using Meridian.Contracts.Operations;
using Meridian.Infrastructure.Adapters.Alpaca;
using Meridian.Infrastructure.Adapters.NasdaqDataLink;

namespace Meridian.Infrastructure.Adapters.Core;

/// <summary>Run-local collection of typed routing observations. Vendor errors never enter this collector.</summary>
public sealed class BackfillProvenanceCapture
{
    private readonly object _gate = new();
    private readonly Dictionary<IHistoricalDataProvider, Counts> _counts = new(ReferenceEqualityComparer.Instance);
    private readonly ImmutableArray<IHistoricalDataProvider> _candidates;
    private readonly bool _isComposite;
    private bool _fallbackOccurred;

    public BackfillProvenanceCapture(IHistoricalDataProvider provider, DataGranularity granularity,
        bool symbolTimelineResolution, bool background = false, Func<string, int>? resolveStaleToleranceDays = null)
    {
        _isComposite = provider is CompositeHistoricalDataProvider;
        var composite = provider as CompositeHistoricalDataProvider;
        _candidates = composite?.GetReceiptCandidates(granularity) ?? [provider];
        Sources = _candidates.Select(candidate => Describe(candidate) with
        {
            StaleToleranceDays = composite?.ResolveValidationProfile(candidate.Name).StaleToleranceDays
                ?? resolveStaleToleranceDays?.Invoke(candidate.Name)
                ?? BackfillBarValidation.DefaultStaleToleranceDays
        }).ToImmutableArray();
        var connections = _candidates.Select(ProviderRuntimeProvenance.Find).OfType<ProviderRuntimeProvenance.Connection>().ToArray();
        Access = new(connections.Select(c => c.TenantId).OfType<string>().Distinct(StringComparer.Ordinal).ToImmutableArray(),
            connections.Where(c => c.Id is not null && c.RuntimeRevision is not null)
                .DistinctBy(c => c.Id).ToImmutableDictionary(c => c.Id!, c => c.RuntimeRevision!, StringComparer.Ordinal));
        Validation = new ProviderProvenanceValidation(
            background ? "backfill-worker/v1" : "historical-backfill/v1",
            StaleToleranceDays: resolveStaleToleranceDays?.Invoke(provider.Name) ?? BackfillBarValidation.DefaultStaleToleranceDays,
            FutureToleranceDays: BackfillBarValidation.FutureToleranceDays,
            TryOtherProvidersForStaleBars: _isComposite && !granularity.IsIntraday(),
            SymbolResolution: composite?.ResolvesSymbols == true || symbolTimelineResolution,
            CrossValidation: !granularity.IsIntraday() && composite?.CrossValidates == true,
            RateLimitRotation: composite?.RotatesProviders,
            RateLimitRotationThreshold: composite?.RotationThreshold,
            RateLimitRetries: composite?.RateLimitRetries,
            RateLimitRetryBudgetSeconds: composite?.RateLimitRetryBudgetSeconds,
            FailureBackoffSeconds: composite?.FailureBackoffSeconds);
    }

    public ImmutableArray<ProviderProvenanceSource> Sources { get; }
    public ProviderProvenanceAccess Access { get; }
    public ProviderProvenanceValidation Validation { get; }
    public string RoutingDecision => _isComposite ? "Composite failover" : "Explicit provider";

    public static bool CanUseProviderForTenant(IHistoricalDataProvider provider, string? tenantId) =>
        tenantId is null || ProviderRuntimeProvenance.Find(provider)?.TenantId is not { } owner ||
        string.Equals(owner, tenantId, StringComparison.Ordinal);

    public Task<bool[]> CheckAvailabilityAsync(CancellationToken ct) =>
        Task.WhenAll(_candidates.Select(provider => CheckAsync(provider, ct)));

    private static async Task<bool> CheckAsync(IHistoricalDataProvider provider, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        try
        {
            return await provider.IsAvailableAsync(timeout.Token).WaitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            return false;
        }
    }

    internal static ProviderProvenanceSource Describe(IHistoricalDataProvider provider)
    {
        var connection = ProviderRuntimeProvenance.Find(provider);
        var providerId = ProviderProvenanceLabels.ProviderId(provider.Name);
        var feed = provider switch
        {
            AlpacaHistoricalDataProvider alpaca => alpaca.HistoricalFeed,
            NasdaqDataLinkHistoricalDataProvider nasdaq => nasdaq.IsWikiDataset ? "WIKI" : "Custom dataset",
            _ => provider.IsSimulated ? "Synthetic generator" : "Default historical feed"
        };
        var entitlement = providerId switch
        {
            "stooq" or "yahoo" => "Public feed",
            "nasdaq" when provider is NasdaqDataLinkHistoricalDataProvider { UsesAnonymousFeed: true } => "Public feed",
            "synthetic" => "Local simulation",
            _ => "Entitlement not reported"
        };
        var semantics = provider switch
        {
            AlpacaHistoricalDataProvider alpaca => $"Historical bars ({alpaca.HistoricalAdjustment} adjustment)",
            _ when provider.IsSimulated => "Synthetic bars",
            _ when providerId == "fred" => "Historical economic observations",
            _ => "Provider-reported historical bars"
        };
        var environment = providerId switch
        {
            "stooq" or "yahoo" => "Public",
            "nasdaq" when provider is NasdaqDataLinkHistoricalDataProvider { UsesAnonymousFeed: true } => "Public",
            "synthetic" => "Local",
            _ => connection?.Environment ?? ProviderProvenanceLabels.Unknown
        };
        return new(providerId, connection?.Id, environment, feed, entitlement, semantics, provider.Priority);
    }

    internal void Observe(IHistoricalDataProvider provider, BackfillProviderObservation observation)
    {
        lock (_gate)
        {
            if (!_counts.TryGetValue(provider, out var counts))
                _counts.Add(provider, counts = new Counts());
            _fallbackOccurred |= _isComposite && !ReferenceEquals(_candidates[0], provider);
            switch (observation)
            {
                case BackfillProviderObservation.Requested: counts.Requests++; break;
                case BackfillProviderObservation.Data: counts.ResponsesWithData++; break;
                case BackfillProviderObservation.Unavailable: counts.Unavailable++; break;
                case BackfillProviderObservation.Empty: counts.EmptyResponses++; break;
                case BackfillProviderObservation.Stale: counts.StaleResponses++; break;
                case BackfillProviderObservation.StaleAccepted: counts.StaleResponsesAccepted++; break;
            }
        }
    }

    public async Task<IReadOnlyList<T>> ObserveDirectAsync<T>(IHistoricalDataProvider provider, Func<Task<IReadOnlyList<T>>> fetch)
    {
        Observe(provider, BackfillProviderObservation.Requested);
        try
        {
            var data = await fetch().ConfigureAwait(false);
            Observe(provider, data.Count > 0 ? BackfillProviderObservation.Data : BackfillProviderObservation.Empty);
            return data;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Observe(provider, BackfillProviderObservation.Unavailable);
            throw;
        }
    }

    public ProviderProvenanceReceipt Complete(ProviderProvenanceRequest request, DateTimeOffset startedUtc,
        DateTimeOffset completedUtc, string? unavailableReason = null)
    {
        var providers = Sources.ToBuilder();
        bool fallbackOccurred;
        lock (_gate)
        {
            for (var index = 0; index < _candidates.Length; index++)
            {
                if (!_counts.TryGetValue(_candidates[index], out var counts))
                    continue;
                providers[index] = providers[index] with
                {
                    Requests = counts.Requests,
                    ResponsesWithData = counts.ResponsesWithData,
                    Unavailable = counts.Unavailable,
                    EmptyResponses = counts.EmptyResponses,
                    StaleResponses = counts.StaleResponses,
                    StaleResponsesAccepted = counts.StaleResponsesAccepted
                };
            }
            fallbackOccurred = _fallbackOccurred;
        }
        var semantics = providers.Where(p => p.ResponsesWithData > 0 || p.StaleResponsesAccepted > 0)
            .Select(p => p.DataSemantics).Distinct(StringComparer.Ordinal).Take(2).ToArray();
        return new ProviderProvenanceReceipt(
            ProviderProvenanceReceipt.CurrentSchemaVersion, Guid.NewGuid(), "HistoricalBars", RoutingDecision,
            providers.ToImmutable(), _isComposite ? Sources.Select(p => p.ProviderId).ToImmutableArray() : [],
            fallbackOccurred, Validation, request, startedUtc, completedUtc,
            semantics.Length switch { 0 => "No provider data returned", 1 => semantics[0], _ => "Mixed historical data semantics" },
            unavailableReason);
    }

    public static ProviderProvenanceRequest DescribeRequest(DateOnly? from, DateOnly? to, IEnumerable<string> symbols,
        DataGranularity granularity, int maxConcurrentSymbols, IReadOnlyDictionary<string, int>? priorities = null,
        bool resumeFromCheckpoint = false)
    {
        var selected = symbols.ToArray();
        var safeSymbols = selected.Where(ProviderProvenanceLabels.IsSymbol).ToImmutableArray();
        var safePriorities = priorities is null ? ImmutableArray<ProviderProvenanceSymbolPriority>.Empty : safeSymbols
            .Where(priorities.ContainsKey).Select(symbol => new ProviderProvenanceSymbolPriority(symbol, priorities[symbol]))
            .ToImmutableArray();
        return new(from, to, safeSymbols, granularity.ToString(), maxConcurrentSymbols, safePriorities,
            resumeFromCheckpoint, WithheldSymbolCount: selected.Length - safeSymbols.Length);
    }

    private sealed class Counts
    {
        public long Requests;
        public long ResponsesWithData;
        public long Unavailable;
        public long EmptyResponses;
        public long StaleResponses;
        public long StaleResponsesAccepted;
    }
}

internal enum BackfillProviderObservation
{
    Requested, Data, Unavailable, Empty, Stale, StaleAccepted
}
