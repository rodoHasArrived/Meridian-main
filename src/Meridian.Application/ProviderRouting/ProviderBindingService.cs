using Meridian.Application.Config;
using Meridian.Contracts.Api;
using Meridian.ProviderSdk;

namespace Meridian.Application.ProviderRouting;

/// <summary>
/// CRUD service for provider bindings and policy previews.
/// </summary>
public sealed class ProviderBindingService
{
    private readonly UI.ConfigStore _store;

    public ProviderBindingService(UI.ConfigStore store)
    {
        _store = store;
    }

    public Task<IReadOnlyList<ProviderBindingDto>> GetBindingsAsync(CancellationToken ct = default)
    {
        var bindings = (ProviderRoutingConfigExtensions.GetSection(_store.Load()).Bindings ?? Array.Empty<ProviderBindingConfig>())
            .Select(ProviderRoutingMapper.ToDto)
            .ToArray();
        return Task.FromResult<IReadOnlyList<ProviderBindingDto>>(bindings);
    }

    public async Task<ProviderBindingDto> UpsertAsync(UpdateProviderBindingRequest request, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Capability);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ConnectionId);

        var cfg = _store.Load();
        var section = ProviderRoutingConfigExtensions.GetSection(cfg);
        var bindings = (section.Bindings ?? Array.Empty<ProviderBindingConfig>()).ToList();

        var bindingId = string.IsNullOrWhiteSpace(request.BindingId)
            ? Guid.NewGuid().ToString("N")
            : request.BindingId;

        var next = new ProviderBindingConfig(
            BindingId: bindingId,
            Capability: ProviderRoutingMapper.ParseEnum(request.Capability, ProviderCapabilityKind.RealtimeMarketData),
            ConnectionId: request.ConnectionId,
            Target: ProviderRoutingMapper.ToBindingTarget(request.Target),
            Priority: request.Priority,
            Enabled: request.Enabled,
            FailoverConnectionIds: request.FailoverConnectionIds,
            SafetyModeOverride: string.IsNullOrWhiteSpace(request.SafetyModeOverride)
                ? null
                : ProviderRoutingMapper.ParseEnum(request.SafetyModeOverride, ProviderSafetyMode.HealthAwareFailover),
            Notes: request.Notes);

        var existingIndex = bindings.FindIndex(b => string.Equals(b.BindingId, bindingId, StringComparison.OrdinalIgnoreCase));
        if (existingIndex >= 0)
            bindings[existingIndex] = next;
        else
            bindings.Add(next);

        await _store.SaveAsync(cfg with
        {
            ProviderConnections = section with
            {
                Bindings = bindings.ToArray()
            }
        }, ct).ConfigureAwait(false);

        return ProviderRoutingMapper.ToDto(next);
    }

    public async Task<bool> DeleteAsync(string bindingId, CancellationToken ct = default)
    {
        var cfg = _store.Load();
        var section = ProviderRoutingConfigExtensions.GetSection(cfg);
        var bindings = (section.Bindings ?? Array.Empty<ProviderBindingConfig>()).ToList();
        var removed = bindings.RemoveAll(b => string.Equals(b.BindingId, bindingId, StringComparison.OrdinalIgnoreCase)) > 0;
        if (!removed)
            return false;

        await _store.SaveAsync(cfg with
        {
            ProviderConnections = section with
            {
                Bindings = bindings.ToArray()
            }
        }, ct).ConfigureAwait(false);

        return true;
    }

    public Task<IReadOnlyList<ProviderPolicyDto>> GetPoliciesAsync(CancellationToken ct = default)
    {
        var cfg = _store.Load();
        var policies = ProviderRoutingConfigExtensions.GetEffectivePolicies(cfg)
            .Select(ProviderRoutingMapper.ToDto)
            .ToArray();
        return Task.FromResult<IReadOnlyList<ProviderPolicyDto>>(policies);
    }
}
