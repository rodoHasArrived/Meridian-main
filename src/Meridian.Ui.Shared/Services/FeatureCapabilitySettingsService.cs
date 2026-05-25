using Meridian.Application.Config;
using Meridian.Contracts.Workstation;
using CoreConfigStore = Meridian.Application.UI.ConfigStore;

namespace Meridian.Ui.Shared.Services;

public sealed class FeatureCapabilitySettingsService
{
    private readonly CoreConfigStore _configStore;
    private readonly IReadOnlyList<FeatureCapabilityDescriptor> _descriptors;

    public FeatureCapabilitySettingsService(
        CoreConfigStore configStore,
        IEnumerable<FeatureCapabilityDescriptor>? descriptors = null)
    {
        _configStore = configStore ?? throw new ArgumentNullException(nameof(configStore));
        _descriptors = NormalizeDescriptors(descriptors);
    }

    public FeatureCapabilitySettingsResponse Get()
    {
        var overrides = NormalizeOverrides(_configStore.Load().FeatureCapabilities?.Overrides);
        return BuildResponse(overrides);
    }

    public async Task<FeatureCapabilitySettingsResponse?> SetAsync(
        string capabilityKey,
        bool isEnabled,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(capabilityKey))
        {
            return null;
        }

        var descriptor = _descriptors.FirstOrDefault(
            item => string.Equals(item.CapabilityKey, capabilityKey, StringComparison.OrdinalIgnoreCase));
        if (descriptor is null)
        {
            return null;
        }

        var overrides = NormalizeOverrides(_configStore.Load().FeatureCapabilities?.Overrides);
        overrides[descriptor.CapabilityKey] = descriptor.IsPermanent || isEnabled;
        await _configStore.SaveFeatureCapabilityOverridesAsync(overrides, ct).ConfigureAwait(false);
        return BuildResponse(overrides);
    }

    private FeatureCapabilitySettingsResponse BuildResponse(IReadOnlyDictionary<string, bool> overrides)
        => new(_descriptors.Select(descriptor =>
        {
            var isOverridden = overrides.TryGetValue(descriptor.CapabilityKey, out var overrideValue);
            var isEnabled = descriptor.IsPermanent || (isOverridden ? overrideValue : descriptor.DefaultEnabled);
            return new FeatureCapabilityToggleDto(
                CapabilityKey: descriptor.CapabilityKey,
                DisplayName: descriptor.DisplayName,
                Description: descriptor.Description,
                IsEnabled: isEnabled,
                DefaultEnabled: descriptor.DefaultEnabled,
                IsPermanent: descriptor.IsPermanent,
                IsOverridden: isOverridden,
                CanToggle: !descriptor.IsPermanent,
                DisabledReason: descriptor.IsPermanent ? "Required for workstation navigation." : null);
        }).ToArray());

    private static Dictionary<string, bool> NormalizeOverrides(IReadOnlyDictionary<string, bool>? overrides)
    {
        var normalized = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        if (overrides is null)
        {
            return normalized;
        }

        foreach (var (key, value) in overrides)
        {
            var normalizedKey = key.Trim();
            if (normalizedKey.Length == 0 || normalized.ContainsKey(normalizedKey))
            {
                continue;
            }

            normalized[normalizedKey] = value;
        }

        return normalized;
    }

    private static IReadOnlyList<FeatureCapabilityDescriptor> NormalizeDescriptors(IEnumerable<FeatureCapabilityDescriptor>? descriptors)
    {
        if (descriptors is null)
        {
            return FeatureCapabilityCatalog.All;
        }

        if (ReferenceEquals(descriptors, FeatureCapabilityCatalog.All))
        {
            return FeatureCapabilityCatalog.All;
        }

        var supplied = descriptors.ToArray();
        return supplied.Length == 0
            ? FeatureCapabilityCatalog.All
            : supplied
                .GroupBy(static descriptor => descriptor.CapabilityKey, StringComparer.OrdinalIgnoreCase)
                .Select(static group => group.First())
                .OrderBy(static descriptor => descriptor.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToArray();
    }
}
