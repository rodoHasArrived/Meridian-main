namespace Meridian.Application.Config;

/// <summary>
/// Runtime feature-capability overrides loaded from configuration.
/// </summary>
public sealed record FeatureCapabilityOptions(
    Dictionary<string, bool>? Overrides = null
)
{
    public FeatureCapabilityOptions()
        : this((Dictionary<string, bool>?)null)
    {
    }
}
