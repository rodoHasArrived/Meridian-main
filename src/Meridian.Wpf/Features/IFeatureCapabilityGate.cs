using Meridian.Core.Config;

namespace Meridian.Wpf.Features;

/// <summary>
/// Provides feature availability queries, overrides, and an observable stream of capability changes.
/// </summary>
public interface IFeatureCapabilityGate
{
    IObservable<CapabilityChangedEvent> Changes { get; }

    bool IsEnabled(string key);

    /// <summary>
    /// Sets the override and persists it, blocking the caller until the configuration file is
    /// written (which can wait on the configuration file lock). Do not call from the UI thread;
    /// use <see cref="SetEnabledAsync"/> there.
    /// </summary>
    void SetEnabled(string key, bool enabled);

    /// <summary>
    /// Sets the override immediately and persists it without blocking the caller. The change
    /// notification is published on the caller's context once the override is saved.
    /// </summary>
    Task SetEnabledAsync(string key, bool enabled, CancellationToken ct = default);
}
