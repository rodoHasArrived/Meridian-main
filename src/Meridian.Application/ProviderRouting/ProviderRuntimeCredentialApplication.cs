using System.Collections.Concurrent;
using Meridian.Core.Config;
using Meridian.DataIntegration.Credentials;

namespace Meridian.Application.ProviderRouting;

public enum ProviderRuntimeCredentialApplyOutcome
{
    /// <summary>Running instances now resolve credentials under the bound scope.</summary>
    Applied,
    /// <summary>No in-process recycle exists; the host must be restarted deliberately.</summary>
    PendingRestart
}

/// <summary>
/// Opt-in seam that recycles a provider's running instances under a changed host binding. Hosts without an
/// in-process recycle keep the default, which never activates anything hot.
/// </summary>
public interface IProviderRuntimeCredentialApplier
{
    Task<ProviderRuntimeCredentialApplyOutcome> ApplyAsync(ProviderRuntimeCredentialBinding binding, string actor, CancellationToken ct = default);
}

/// <summary>Default: provider instances are constructed once per host process, so a changed binding waits for a restart.</summary>
public sealed class NoInProcessProviderRecycleApplier : IProviderRuntimeCredentialApplier
{
    public Task<ProviderRuntimeCredentialApplyOutcome> ApplyAsync(ProviderRuntimeCredentialBinding binding, string actor, CancellationToken ct = default)
        => Task.FromResult(ProviderRuntimeCredentialApplyOutcome.PendingRestart);
}

/// <summary>
/// In-process record of which host bindings changed after this host started and which have since been
/// applied. A provider with no entry was bound before start (or never), so its binding is in effect.
/// </summary>
public sealed class ProviderRuntimeCredentialApplicationTracker(TimeProvider? timeProvider = null)
{
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;
    private readonly ConcurrentDictionary<string, (DateTimeOffset? BoundAt, DateTimeOffset? AppliedAt)> _entries = new(StringComparer.OrdinalIgnoreCase);

    public void MarkBound(string providerId)
    {
        var now = _timeProvider.GetUtcNow();
        _entries.AddOrUpdate(Key(providerId), _ => (now, null), (_, entry) => (now, entry.AppliedAt));
    }

    public void MarkApplied(string providerId)
    {
        var now = _timeProvider.GetUtcNow();
        _entries.AddOrUpdate(Key(providerId), _ => (null, now), (_, entry) => (entry.BoundAt, now));
    }

    public (DateTimeOffset? BoundAt, DateTimeOffset? AppliedAt, bool PendingRestart) Get(string providerId)
    {
        if (!_entries.TryGetValue(Key(providerId), out var entry))
            return (null, null, false);
        var pending = entry.BoundAt is { } bound && (entry.AppliedAt is null || entry.AppliedAt < bound);
        return (entry.BoundAt, entry.AppliedAt, pending);
    }

    private static string Key(string providerId) => ProviderCredentialCatalog.NormalizeProviderId(providerId);
}
