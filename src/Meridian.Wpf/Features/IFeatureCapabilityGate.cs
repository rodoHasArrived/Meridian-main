namespace Meridian.Wpf.Features;

public interface IFeatureCapabilityGate
{
    IObservable<CapabilityChangedEvent> Changes { get; }

    bool IsEnabled(string key);

    void SetEnabled(string key, bool enabled);
}
