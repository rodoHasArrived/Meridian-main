using Meridian.Core.Config;
using Meridian.Contracts.Api;
using Meridian.ProviderSdk;
using Meridian.DataIntegration.Credentials;

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

    /// <summary>Lists only connections with retained ownership matching the authorized tenant.</summary>
    public Task<IReadOnlyList<ProviderConnectionDto>> GetConnectionsForTenantAsync(string tenantId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ct.ThrowIfCancellationRequested();
        var connections = (_store.Load().ProviderConnections?.Connections ?? [])
            .GroupBy(connection => connection.ConnectionId, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() == 1).Select(group => group.Single())
            .Where(connection => string.Equals(connection.TenantId, tenantId.Trim(), StringComparison.Ordinal))
            .Select(ProviderRoutingMapper.ToDto).ToArray();
        return Task.FromResult<IReadOnlyList<ProviderConnectionDto>>(connections);
    }

    public Task<ProviderConnectionDto?> GetConnectionAsync(string connectionId, CancellationToken ct = default)
    {
        var connection = (_store.Load().ProviderConnections?.Connections ?? Array.Empty<ProviderConnectionConfig>())
            .FirstOrDefault(c => string.Equals(c.ConnectionId, connectionId, StringComparison.OrdinalIgnoreCase));
        return Task.FromResult(connection is null ? null : ProviderRoutingMapper.ToDto(connection));
    }

    public Task<ProviderConnectionDto> UpsertAsync(CreateProviderConnectionRequest request, CancellationToken ct = default)
        => UpsertInternalAsync(request, null, null, ct);

    /// <summary>Creates or updates a connection for a server-authorized tenant. Legacy ownership is never inferred.</summary>
    public Task<ProviderConnectionDto> UpsertForTenantAsync(CreateProviderConnectionRequest request, string tenantId, string environment, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(environment);
        return UpsertInternalAsync(request, tenantId.Trim(), environment.Trim().ToLowerInvariant(), ct);
    }

    /// <summary>Resolves retained credential ownership only for the authorized tenant.</summary>
    public Task<ProviderCredentialScope?> GetCredentialScopeForTenantAsync(string connectionId, string tenantId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ct.ThrowIfCancellationRequested();
        var matches = (_store.Load().ProviderConnections?.Connections ?? [])
            .Where(c => string.Equals(c.ConnectionId, connectionId, StringComparison.OrdinalIgnoreCase)).ToArray();
        var connection = matches.Length == 1 ? matches[0] : null;
        if (connection is null || !string.Equals(connection.TenantId, tenantId.Trim(), StringComparison.Ordinal))
            return Task.FromResult<ProviderCredentialScope?>(null);
        if (string.IsNullOrWhiteSpace(connection.ExternalAccountId) || string.IsNullOrWhiteSpace(connection.CredentialEnvironment))
            throw new InvalidDataException("Retained credential ownership is incomplete.");
        return Task.FromResult<ProviderCredentialScope?>(new ProviderCredentialScope(connection.TenantId!, connection.ConnectionId,
            connection.ExternalAccountId, connection.CredentialEnvironment));
    }

    private async Task<ProviderConnectionDto> UpsertInternalAsync(CreateProviderConnectionRequest request, string? tenantId, string? environment, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ProviderFamilyId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.DisplayName);

        var cfg = _store.LoadRequired();
        var section = ProviderRoutingConfigExtensions.GetSection(cfg);
        var connections = (section.Connections ?? Array.Empty<ProviderConnectionConfig>()).ToList();

        var connectionId = string.IsNullOrWhiteSpace(request.ConnectionId)
            ? Guid.NewGuid().ToString("N")
            : request.ConnectionId.Trim();

        if (connections.Count(c => string.Equals(c.ConnectionId, connectionId, StringComparison.OrdinalIgnoreCase)) > 1)
            throw new InvalidOperationException("Connection ownership is ambiguous.");
        var existingIndex = connections.FindIndex(c => string.Equals(c.ConnectionId, connectionId, StringComparison.OrdinalIgnoreCase));
        var existing = existingIndex >= 0 ? connections[existingIndex] : null;
        if (existing is not null && !string.Equals(existing.TenantId, tenantId, StringComparison.Ordinal))
            throw new InvalidOperationException("Connection ownership does not match the authorized tenant.");
        if (tenantId is not null)
        {
            var scope = new ProviderCredentialScope(tenantId, connectionId, request.ExternalAccountId ?? string.Empty, environment!);
            if (existing is not null && (existing.ExternalAccountId != scope.ExternalAccountId || existing.CredentialEnvironment != scope.Environment ||
                !string.Equals(existing.ProviderFamilyId, request.ProviderFamilyId.Trim(), StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("Existing credential ownership cannot be reassigned.");
            // A provider-wide or caller-supplied vault reference is not ownership evidence.
            if (!string.IsNullOrWhiteSpace(request.CredentialReference) && request.CredentialReference != existing?.CredentialReference)
                throw new InvalidOperationException("Credential references must be assigned by the scoped credential workflow.");
            connectionId = scope.ConnectionId;
            request = request with { ExternalAccountId = scope.ExternalAccountId };
        }

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
            ProductionReady: request.ProductionReady,
            TenantId: tenantId,
            CredentialEnvironment: environment);

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

    public Task<bool> DeleteAsync(string connectionId, CancellationToken ct = default)
        => DeleteInternalAsync(connectionId, null, ct);

    /// <summary>Deletes configuration only after matching its retained tenant owner.</summary>
    public Task<bool> DeleteForTenantAsync(string connectionId, string tenantId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        return DeleteInternalAsync(connectionId, tenantId.Trim(), ct);
    }

    private async Task<bool> DeleteInternalAsync(string connectionId, string? tenantId, CancellationToken ct)
    {
        var cfg = _store.LoadRequired();
        var section = ProviderRoutingConfigExtensions.GetSection(cfg);
        var connections = (section.Connections ?? Array.Empty<ProviderConnectionConfig>()).ToList();
        if (connections.Any(c => string.Equals(c.ConnectionId, connectionId, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(c.TenantId, tenantId, StringComparison.Ordinal)))
            throw new InvalidOperationException("Connection ownership does not match the authorized tenant.");
        if (connections.Count(c => string.Equals(c.ConnectionId, connectionId, StringComparison.OrdinalIgnoreCase)) > 1)
            throw new InvalidOperationException("Connection ownership is ambiguous.");
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
