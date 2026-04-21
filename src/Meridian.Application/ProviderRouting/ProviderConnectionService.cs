using Meridian.Application.Config;
using Meridian.Contracts.Api;
using Meridian.ProviderSdk;

namespace Meridian.Application.ProviderRouting;

/// <summary>
/// CRUD service for provider connections stored in application configuration.
/// </summary>
public sealed class ProviderConnectionService
{
    private readonly UI.ConfigStore _store;

    public ProviderConnectionService(UI.ConfigStore store)
    {
        _store = store;
    }

    public Task<IReadOnlyList<ProviderConnectionDto>> GetConnectionsAsync(CancellationToken ct = default)
    {
        var cfg = _store.Load();
        var connections = (cfg.ProviderConnections?.Connections ?? Array.Empty<ProviderConnectionConfig>())
            .Select(ProviderRoutingMapper.ToDto)
            .ToArray();
        return Task.FromResult<IReadOnlyList<ProviderConnectionDto>>(connections);
    }

    public Task<ProviderConnectionDto?> GetConnectionAsync(string connectionId, CancellationToken ct = default)
    {
        var connection = (_store.Load().ProviderConnections?.Connections ?? Array.Empty<ProviderConnectionConfig>())
            .FirstOrDefault(c => string.Equals(c.ConnectionId, connectionId, StringComparison.OrdinalIgnoreCase));
        return Task.FromResult(connection is null ? null : ProviderRoutingMapper.ToDto(connection));
    }

    public async Task<ProviderConnectionDto> UpsertAsync(CreateProviderConnectionRequest request, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ProviderFamilyId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.DisplayName);

        var cfg = _store.Load();
        var section = ProviderRoutingConfigExtensions.GetSection(cfg);
        var connections = (section.Connections ?? Array.Empty<ProviderConnectionConfig>()).ToList();

        var connectionId = string.IsNullOrWhiteSpace(request.ConnectionId)
            ? Guid.NewGuid().ToString("N")
            : request.ConnectionId;

        var next = new ProviderConnectionConfig(
            ConnectionId: connectionId,
            ProviderFamilyId: request.ProviderFamilyId.Trim(),
            DisplayName: request.DisplayName.Trim(),
            ConnectionType: ProviderRoutingMapper.ParseEnum(request.ConnectionType, ProviderConnectionType.DataVendor),
            ConnectionMode: ProviderRoutingMapper.ParseEnum(request.ConnectionMode, ProviderConnectionMode.ReadOnly),
            Enabled: request.Enabled,
            CredentialReference: request.CredentialReference,
            InstitutionId: request.InstitutionId,
            ExternalAccountId: request.ExternalAccountId,
            Scope: ProviderRoutingMapper.ToConnectionScope(request.Scope),
            Tags: request.Tags,
            Description: request.Description,
            ProductionReady: request.ProductionReady);

        var existingIndex = connections.FindIndex(c => string.Equals(c.ConnectionId, connectionId, StringComparison.OrdinalIgnoreCase));
        if (existingIndex >= 0)
            connections[existingIndex] = next;
        else
            connections.Add(next);

        await _store.SaveAsync(cfg with
        {
            ProviderConnections = section with
            {
                Connections = connections.ToArray()
            }
        }, ct).ConfigureAwait(false);

        return ProviderRoutingMapper.ToDto(next);
    }

    public async Task<bool> DeleteAsync(string connectionId, CancellationToken ct = default)
    {
        var cfg = _store.Load();
        var section = ProviderRoutingConfigExtensions.GetSection(cfg);
        var connections = (section.Connections ?? Array.Empty<ProviderConnectionConfig>()).ToList();
        var removed = connections.RemoveAll(c => string.Equals(c.ConnectionId, connectionId, StringComparison.OrdinalIgnoreCase)) > 0;
        if (!removed)
            return false;

        var bindings = (section.Bindings ?? Array.Empty<ProviderBindingConfig>())
            .Where(b => !string.Equals(b.ConnectionId, connectionId, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        var certifications = (section.Certifications ?? Array.Empty<ProviderCertificationConfig>())
            .Where(c => !string.Equals(c.ConnectionId, connectionId, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        await _store.SaveAsync(cfg with
        {
            ProviderConnections = section with
            {
                Connections = connections.ToArray(),
                Bindings = bindings,
                Certifications = certifications
            }
        }, ct).ConfigureAwait(false);

        return true;
    }
}
