namespace Meridian.Contracts.Workstation;

public sealed record FeatureCapabilitySettingsResponse(
    IReadOnlyList<FeatureCapabilityToggleDto> Capabilities);

public sealed record FeatureCapabilityToggleDto(
    string CapabilityKey,
    string DisplayName,
    string Description,
    bool IsEnabled,
    bool DefaultEnabled,
    bool IsPermanent,
    bool IsOverridden,
    bool CanToggle,
    string? DisabledReason);

public sealed record FeatureCapabilityToggleRequest(
    bool IsEnabled);
