using System.Collections.Concurrent;
using System.Net;
using Meridian.Contracts.Backfill;
using Meridian.Core.Config;
using Meridian.Core.Exceptions;
using Meridian.Infrastructure.Adapters.Core.SymbolResolution;
using Serilog;

namespace Meridian.Infrastructure.Adapters.Core;

/// <summary>A built historical adapter with the priority in effect for newly started work.</summary>
public sealed record BackfillRoutingProvider(IHistoricalDataProvider Provider, int Priority)
{
    public string ProviderId => ProviderIdentity.NormalizeId(Provider.Name);
}

/// <summary>Immutable provider order and routing options acquired by a job when it starts.</summary>
public sealed class BackfillRoutingSnapshot
{
    private readonly BackfillRoutingAdmission _admission;
    private readonly BackfillConfig _settings;
    private readonly string? _defaultProviderId;
    private readonly IReadOnlyDictionary<string, int> _priorities;

    public BackfillRoutingSnapshot(
        IEnumerable<BackfillRoutingProvider> providers,
        BackfillConfig settings,
        BackfillRoutingAdmission admission,
        DateTimeOffset appliedAtUtc)
    {
        Providers = Array.AsReadOnly(Order(providers).ToArray());
        _priorities = Providers.ToDictionary(entry => entry.Provider.Name, entry => entry.Priority, StringComparer.OrdinalIgnoreCase);
        _settings = settings;
        _admission = admission;
        AppliedAtUtc = appliedAtUtc;
        if (!string.IsNullOrWhiteSpace(settings.Provider) &&
            !string.Equals(settings.Provider, "composite", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(settings.Provider, "auto", StringComparison.OrdinalIgnoreCase))
            _defaultProviderId = ProviderIdentity.NormalizeId(settings.Provider);
    }

    public IReadOnlyList<BackfillRoutingProvider> Providers { get; }
    public DateTimeOffset AppliedAtUtc { get; }
    public bool FallbackEnabled => _settings.EnableFallback;
    public bool RotationEnabled => _settings.EnableRateLimitRotation;

    public IReadOnlyList<BackfillRoutingProvider> DefaultOrder => SelectProviders([]);

    public int GetPriority(IHistoricalDataProvider provider) => _priorities.GetValueOrDefault(provider.Name, provider.Priority);

    /// <summary>Applies request authority without changing the active route or shared admission state.</summary>
    public BackfillRoutingSnapshot ForTenant(string? tenantId) => tenantId is null ? this : new(
        Providers.Where(entry => BackfillProvenanceCapture.CanUseProviderForTenant(entry.Provider, tenantId)),
        _settings, _admission, AppliedAtUtc);

    public static IOrderedEnumerable<BackfillRoutingProvider> Order(IEnumerable<BackfillRoutingProvider> providers)
        => providers.OrderBy(entry => entry.Provider.IsSimulated)
            .ThenBy(entry => entry.Priority)
            .ThenBy(entry => entry.ProviderId, StringComparer.Ordinal);

    public IReadOnlyList<BackfillRoutingProvider> SelectProviders(IReadOnlyList<string> preferences)
    {
        var preferred = Preferences(preferences);
        var byId = Providers.ToDictionary(entry => entry.ProviderId, StringComparer.Ordinal);
        if (!FallbackEnabled)
        {
            if (preferred.Count == 0)
                return Providers.Take(1).ToArray();
            return !string.IsNullOrWhiteSpace(preferred[0]) &&
                byId.TryGetValue(ProviderIdentity.NormalizeId(preferred[0]), out var first) ? [first] : [];
        }

        return preferred.Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(ProviderIdentity.NormalizeId)
            .Where(byId.ContainsKey)
            .Select(id => byId[id])
            .Concat(Providers)
            .DistinctBy(entry => entry.ProviderId)
            .OrderBy(entry => entry.Provider.IsSimulated)
            .ToArray();
    }

    private IReadOnlyList<string> Preferences(IReadOnlyList<string> preferences)
    {
        if (preferences.Count > 0)
            return preferences;
        return _defaultProviderId is { } id ? [id] : [];
    }

    public CompositeHistoricalDataProvider CreateComposite(
        IReadOnlyList<string> preferences,
        ISymbolResolver? symbolResolver,
        ILogger log,
        BackfillProgressTracker? progressTracker,
        Action<BackfillRoutingAttempt>? captureAttempt = null,
        Func<CrossProviderTolerancePolicy>? crossValidationPolicy = null)
    {
        var candidates = SelectProviders(preferences).Select(entry => entry.Provider).ToArray();
        var considered = Preferences(preferences);
        var unavailable = considered.Take(FallbackEnabled ? considered.Count : 1)
            .Where(id => !string.IsNullOrWhiteSpace(id)).Select(ProviderIdentity.NormalizeId)
            .Where(id => id.Length <= 64 && id.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.')
                && !Providers.Any(entry => entry.ProviderId == id)).ToArray();
        return new CompositeHistoricalDataProvider(
            candidates,
            _settings.EnableSymbolResolution ? symbolResolver : null,
            enableRateLimitRotation: RotationEnabled,
            rateLimitRotationThreshold: _settings.RateLimitRotationThreshold,
            log: log,
            progressTracker: progressTracker,
            validationProfiles: _settings.ValidationProfiles,
            rateLimitHistory: _admission.History,
            crossValidationPolicy: crossValidationPolicy,
            routingAdmission: _admission,
            captureAttempt: captureAttempt,
            unavailablePreferences: unavailable,
            routingPriorities: _priorities);
    }
}

/// <summary>Concurrency and attempt observations shared by all routing snapshots in a host.</summary>
public sealed class BackfillRoutingAdmission : IDisposable
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _slots = new(StringComparer.OrdinalIgnoreCase);
    private readonly int _perProvider;

    public BackfillRoutingAdmission(IEnumerable<IHistoricalDataProvider> providers, int perProvider, ProviderRateLimitHistory? history = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(perProvider, 1);
        _perProvider = perProvider;
        History = history ?? ProviderRateLimitHistory.Shared;
        Tracker = new ProviderRateLimitTracker();
        var candidates = providers.ToArray();
        foreach (var provider in candidates)
            Tracker.RegisterProvider(provider);
        Health = new ProviderHealthTracker(candidates.Select(provider => provider.Name), TimeSpan.FromMinutes(5));
    }

    internal ProviderRateLimitTracker Tracker { get; }
    internal ProviderRateLimitHistory History { get; }
    internal ProviderHealthTracker Health { get; }

    public RateLimitStatus? GetStatus(string providerName) => Tracker.GetStatus(providerName);

    /// <summary>Uses the same admission and observations for a strictly pinned request.</summary>
    public async Task<IReadOnlyList<TResult>> FetchPinnedAsync<TResult>(
        IHistoricalDataProvider provider,
        string symbol,
        Func<CancellationToken, Task<IReadOnlyList<TResult>>> fetch,
        Action<BackfillRoutingAttempt>? capture,
        CancellationToken ct,
        BackfillProvenanceCapture? provenance = null)
    {
        using var slot = await AcquireAsync(provider.Name, ct).ConfigureAwait(false);
        void CheckAdmission()
        {
            ct.ThrowIfCancellationRequested();
            if (Tracker.IsRateLimited(provider.Name))
            {
                provenance?.Observe(provider, BackfillProviderObservation.Unavailable);
                capture?.Invoke(new(symbol, provider.Name, "skipped-rate-limited", 0, false));
                throw new RateLimitException("Provider is rate limited; coverage was not checked.",
                    provider.Name, symbol, Tracker.GetTimeUntilReset(provider.Name));
            }
        }

        CheckAdmission();
        if (provider is not IBackfillRequestMeteredProvider && HistoricalBackfillRequestScope.Current is { } observation)
        {
            await observation.BeforeUnmeteredFetch(provider, ct).ConfigureAwait(false);
            CheckAdmission();
            observation.UnmeteredFetchStarted?.Invoke(provider.Name);
        }

        Tracker.RecordRequest(provider.Name);
        try
        {
            var result = await (provenance is null ? fetch(ct) : provenance.ObserveDirectAsync(provider, () => fetch(ct))).ConfigureAwait(false);
            capture?.Invoke(new(symbol, provider.Name, result.Count == 0 ? "no-data" : "completed", result.Count, true));
            return result;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            capture?.Invoke(new(symbol, provider.Name, "cancelled", 0, true));
            throw;
        }
        catch (Exception error)
        {
            var rateLimit = CompositeHistoricalDataProvider.FindRateLimitException(error);
            var isHttpLimit = CompositeHistoricalDataProvider.HasHttpTooManyRequestsStatus(error);
            if (rateLimit is not null || isHttpLimit)
            {
                Tracker.RecordRateLimitHit(provider.Name, rateLimit?.RetryAfter);
                History.Record(provider.Name, Tracker.GetStatus(provider.Name), httpStatus: isHttpLimit ? (int)HttpStatusCode.TooManyRequests : null);
            }
            capture?.Invoke(new(symbol, provider.Name, rateLimit is not null || isHttpLimit ? "rate-limited" : "failed", 0, true));
            throw;
        }
    }

    internal async ValueTask<IDisposable> AcquireAsync(string providerName, CancellationToken ct)
    {
        var semaphore = _slots.GetOrAdd(providerName, _ => new SemaphoreSlim(_perProvider));
        await semaphore.WaitAsync(ct).ConfigureAwait(false);
        return new Slot(semaphore);
    }

    public void Dispose()
    {
        Tracker.Dispose();
        foreach (var semaphore in _slots.Values)
            semaphore.Dispose();
    }

    private sealed class Slot(SemaphoreSlim semaphore) : IDisposable
    {
        public void Dispose() => semaphore.Release();
    }
}
