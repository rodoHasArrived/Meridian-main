using Meridian.Application.UI;
using Meridian.Contracts.Configuration;
using Meridian.Core.Config;
using Meridian.Core.IO;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.ProviderSdk;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Meridian.Application.Backfill;

/// <summary>Publishes historical routing settings over boot-built adapters for the next job.</summary>
public sealed class BackfillRoutingRuntime(ConfigStore store, IServiceProvider services)
    : IHostedService, IConfigurationRuntimeApplier, IDisposable
{
    private static readonly Dictionary<string, string> EditableProviders = BackfillProviderCatalog.Providers
        .Where(provider => provider.Id != "openfigi")
        .ToDictionary(provider => ProviderIdentity.NormalizeId(provider.Id), provider => provider.Id, StringComparer.Ordinal);

    private readonly object _gate = new();
    private BackfillRoutingSnapshot? _current;
    private BackfillConfig? _applied;
    private IHistoricalDataProvider[] _available = [];
    private IHistoricalDataProvider[] _owned = [];
    private BackfillRoutingAdmission? _admission;
    private IDisposable? _attachment;
    private bool _disposed;

    public string Name => "Historical backfill routing";
    public bool ReconcilePendingPaths => true;
    public BackfillRoutingSnapshot? Current => Volatile.Read(ref _current);
    public BackfillRoutingAdmission? Admission => _admission;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var factory = services.GetService<ProviderFactory>();
        if (factory is null)
            return;

        var config = store.Load();
        var backfill = config.Backfill ?? new BackfillConfig();
        var prepared = await factory.PrepareCredentialsAsync(cancellationToken).ConfigureAwait(false);
        using (prepared.Activate())
        {
            var registry = services.GetService<ProviderRegistry>();
            var active = registry?.GetBackfillProviders();
            var owned = factory.CreateStandbyBackfillProviders();
            if (active is null)
            {
                active = factory.CreateBackfillProviders();
                owned = [.. owned, .. active];
            }

            _owned = owned.ToArray();
            _available = active.Concat(_owned).DistinctBy(ProviderId).ToArray();
        }

        var writer = services.GetService<IAtomicFileWriter>();
        var history = writer is null ? null : ProviderRateLimitHistory.ForDataRoot(store.GetDataRoot(config), writer);
        _admission = new BackfillRoutingAdmission(_available, backfill.Jobs?.MaxConcurrentPerProvider ?? 2, history);
        _applied = backfill;
        Volatile.Write(ref _current, BuildSnapshot(backfill, _admission));
        _attachment = ConfigurationRuntimeAppliers.Attach(store.ConfigPath, this);
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        Interlocked.Exchange(ref _attachment, null)?.Dispose();
        return Task.CompletedTask;
    }

    public bool Handles(string path)
    {
        var parts = path.Split('.');
        if (!string.Equals(parts[0], "Backfill", StringComparison.OrdinalIgnoreCase))
            return false;
        if (parts.Length == 2)
        {
            return parts[1].ToUpperInvariant() is "PROVIDER" or "ENABLEFALLBACK" or
                "ENABLERATELIMITROTATION" or "RATELIMITROTATIONTHRESHOLD";
        }
        return parts.Length == 4 && string.Equals(parts[1], "Providers", StringComparison.OrdinalIgnoreCase) &&
            EditableProviders.ContainsKey(ProviderIdentity.NormalizeId(parts[2])) &&
            (string.Equals(parts[3], "Enabled", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(parts[3], "Priority", StringComparison.OrdinalIgnoreCase));
    }

    public Task<IReadOnlyList<ConfigurationApplyEntry>> ApplyAsync(
        AppConfig previous,
        AppConfig next,
        IReadOnlyList<string> changedPaths,
        CancellationToken ct)
    {
        lock (_gate)
        {
            ct.ThrowIfCancellationRequested();
            if (_disposed || _applied is null || _admission is null)
                return Task.FromResult(Outcomes(ConfigurationApplyKind.Deferred, "The historical routing runtime is unavailable."));

            var settings = next.Backfill ?? new BackfillConfig();
            var previousOptions = _applied.Providers ?? new BackfillProvidersConfig();
            var options = settings.Providers ?? new BackfillProvidersConfig();
            var unavailable = EditableProviders.OrderBy(pair => pair.Key, StringComparer.Ordinal).FirstOrDefault(pair =>
                !BackfillProviderCatalog.Read(previousOptions, pair.Value).Enabled &&
                BackfillProviderCatalog.Read(options, pair.Value).Enabled &&
                !_available.Any(provider => ProviderId(provider) == pair.Key)).Key;
            if (unavailable is not null)
            {
                return Task.FromResult(Outcomes(ConfigurationApplyKind.Deferred,
                    $"{unavailable} has no adapter built with the host's active credentials. The previous routing order is still in effect."));
            }

            var routingSettings = _applied with
            {
                Providers = settings.Providers,
                Provider = settings.Provider,
                EnableFallback = settings.EnableFallback,
                EnableRateLimitRotation = settings.EnableRateLimitRotation,
                RateLimitRotationThreshold = settings.RateLimitRotationThreshold
            };
            var snapshot = BuildSnapshot(routingSettings, _admission);
            Volatile.Write(ref _current, snapshot);
            _applied = routingSettings;
            return Task.FromResult(Outcomes(ConfigurationApplyKind.AppliedLive, "Applies to the next job."));
        }

        IReadOnlyList<ConfigurationApplyEntry> Outcomes(ConfigurationApplyKind kind, string detail)
            => changedPaths.Select(path => new ConfigurationApplyEntry(path, kind, detail)).ToArray();
    }

    private BackfillRoutingSnapshot BuildSnapshot(BackfillConfig settings, BackfillRoutingAdmission admission)
    {
        var options = settings.Providers ?? new BackfillProvidersConfig();
        var providers = new List<BackfillRoutingProvider>();
        foreach (var provider in _available)
        {
            if (!EditableProviders.TryGetValue(ProviderId(provider), out var catalogId))
            {
                providers.Add(new BackfillRoutingProvider(provider, provider.Priority));
                continue;
            }
            var option = BackfillProviderCatalog.Read(options, catalogId);
            if (option.Enabled)
                providers.Add(new BackfillRoutingProvider(provider, option.Priority));
        }
        return new BackfillRoutingSnapshot(providers, settings, admission, DateTimeOffset.UtcNow);
    }

    private static string ProviderId(IHistoricalDataProvider provider) => ProviderIdentity.NormalizeId(provider.Name);

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
                return;
            _disposed = true;
            Interlocked.Exchange(ref _attachment, null)?.Dispose();
            Volatile.Write(ref _current, null);
            _admission?.Dispose();
            foreach (var provider in _owned)
                provider.Dispose();
        }
    }
}
