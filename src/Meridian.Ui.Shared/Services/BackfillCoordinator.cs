using Meridian.Infrastructure.Adapters.Core;
using BackfillPreviewResult = Meridian.Application.Backfill.BackfillPreviewResult;
using BackfillRequest = Meridian.Application.Backfill.BackfillRequest;
using BackfillResult = Meridian.Contracts.Backfill.BackfillResult;
using CoreBackfillCoordinator = Meridian.Application.Backfill.BackfillCoordinator;

namespace Meridian.Ui.Shared.Services;

/// <summary>
/// Read-model façade over the core <see cref="CoreBackfillCoordinator"/> for UI hosts.
/// The core coordinator owns all execution, provider discovery, and preview/estimation
/// logic; this wrapper only adapts the UI <see cref="ConfigStore"/> seam and exposes the
/// surface consumed by workstation endpoints.
/// </summary>
public sealed class BackfillCoordinator : IDisposable
{
    private readonly CoreBackfillCoordinator _core;
    private readonly bool _ownsCore;

    /// <summary>
    /// Creates a façade that owns a private core coordinator built from the UI config store.
    /// </summary>
    /// <param name="store">UI configuration store.</param>
    /// <param name="registry">Optional provider registry for unified provider discovery.</param>
    /// <param name="factory">Optional provider factory for creating providers if registry is empty.</param>
    public BackfillCoordinator(ConfigStore store, ProviderRegistry? registry = null, ProviderFactory? factory = null)
    {
        // Convert Ui.Shared.ConfigStore wrapper to the core ConfigStore for the core coordinator
        var coreStore = new Meridian.Application.UI.ConfigStore(store.ConfigPath);
        _core = new CoreBackfillCoordinator(coreStore, registry, factory);
        _ownsCore = true;
    }

    /// <summary>
    /// Creates a façade over an externally owned core coordinator (typically the
    /// composition-root singleton) so UI endpoints and non-UI callers share one run gate.
    /// </summary>
    public BackfillCoordinator(CoreBackfillCoordinator core)
    {
        ArgumentNullException.ThrowIfNull(core);
        _core = core;
    }

    /// <summary>
    /// Gets descriptions of all available backfill providers.
    /// </summary>
    public IEnumerable<object> DescribeProviders() => _core.DescribeProviders();

    /// <summary>
    /// Tries to read the last backfill result.
    /// </summary>
    public BackfillResult? TryReadLast() => _core.TryReadLast();

    /// <summary>
    /// Returns the per-symbol checkpoint map, or <c>null</c> when no checkpoints exist.
    /// </summary>
    public IReadOnlyDictionary<string, DateOnly>? TryReadSymbolCheckpoints() => _core.TryReadSymbolCheckpoints();

    /// <summary>
    /// Returns the per-symbol bar-count sidecar, or <c>null</c> when no bar-count data exists.
    /// </summary>
    public IReadOnlyDictionary<string, long>? TryReadSymbolBarCounts() => _core.TryReadSymbolBarCounts();

    /// <summary>
    /// Runs a backfill operation for the specified request.
    /// </summary>
    public Task<BackfillResult> RunAsync(BackfillRequest request, CancellationToken ct = default)
        => _core.RunAsync(request, ct);

    public void ValidateRequest(BackfillRequest request) => _core.ValidateRequest(request);

    /// <summary>
    /// Gets health status of all providers.
    /// </summary>
    public Task<IReadOnlyDictionary<string, ProviderHealthStatus>> CheckProviderHealthAsync(CancellationToken ct = default)
        => _core.CheckProviderHealthAsync(ct);

    /// <summary>
    /// Resolve a symbol using OpenFIGI.
    /// </summary>
    public Task<Meridian.Infrastructure.Adapters.Core.SymbolResolution.SymbolResolution?> ResolveSymbolAsync(string symbol, CancellationToken ct = default)
        => _core.ResolveSymbolAsync(symbol, ct);

    /// <summary>
    /// Gets backfill progress snapshot from the core coordinator, if available.
    /// </summary>
    public object? GetProgress() => _core.GetProgress();

    /// <summary>
    /// Previews a backfill operation without actually fetching data.
    /// Returns information about what would be backfilled.
    /// </summary>
    public Task<BackfillPreviewResult> PreviewAsync(BackfillRequest request, CancellationToken ct = default)
        => _core.PreviewAsync(request, ct);

    public void Dispose()
    {
        if (_ownsCore)
        {
            _core.Dispose();
        }
    }
}
