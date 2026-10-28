using Meridian.Core.Config;

namespace Meridian.Wpf.Features;

/// <summary>
/// Provides feature availability queries, overrides, and an observable stream of capability changes.
/// </summary>
public interface IFeatureCapabilityGate
{
    IObservable<CapabilityChangedEvent> Changes { get; }

    bool IsEnabled(string key);

    void SetEnabled(string key, bool enabled);
}
