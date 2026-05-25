namespace Meridian.Application.Config;

public sealed record FeatureCapabilityDescriptor(
    string CapabilityKey,
    string DisplayName,
    string Description,
    bool DefaultEnabled,
    bool IsPermanent);

public sealed record CapabilityChangedEvent(
    string CapabilityKey,
    bool IsEnabled);
