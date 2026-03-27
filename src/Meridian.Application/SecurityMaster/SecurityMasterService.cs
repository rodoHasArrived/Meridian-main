using Meridian.Contracts.SecurityMaster;
using Meridian.FSharp.SecurityMasterInterop;
using Meridian.Storage.SecurityMaster;
using Microsoft.Extensions.Logging;

namespace Meridian.Application.SecurityMaster;

public sealed class SecurityMasterService : ISecurityMasterService
{
    private readonly ISecurityMasterEventStore _eventStore;
    private readonly ISecurityMasterSnapshotStore _snapshotStore;
    private readonly ISecurityMasterStore _store;
    private readonly SecurityMasterAggregateRebuilder _rebuilder;
    private readonly SecurityMasterOptions _options;
    private readonly ILogger<SecurityMasterService> _logger;

    public SecurityMasterService(
        ISecurityMasterEventStore eventStore,
        ISecurityMasterSnapshotStore snapshotStore,
        ISecurityMasterStore store,
        SecurityMasterAggregateRebuilder rebuilder,
        SecurityMasterOptions options,
        ILogger<SecurityMasterService> logger)
    {
        _eventStore = eventStore;
        _snapshotStore = snapshotStore;
        _store = store;
        _rebuilder = rebuilder;
        _options = options;
        _logger = logger;
    }

    public Task<SecurityDetailDto> CreateAsync(CreateSecurityRequest request, CancellationToken ct = default)
        => ExecuteCreateAsync(request, ct);

    public async Task<SecurityDetailDto> AmendTermsAsync(AmendSecurityTermsRequest request, CancellationToken ct = default)
    {
        var aliasProjection = await _store.GetProjectionAsync(request.SecurityId, ct).ConfigureAwait(false);
        var current = await _rebuilder.RebuildEconomicDefinitionAsync(request.SecurityId, aliasProjection, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Security '{request.SecurityId}' was not found.");

        var currentProjection = SecurityEconomicDefinitionAdapter.ToProjection(current, aliasProjection?.Aliases);
        var currentRecord = SecurityMasterMapping.ToRecord(currentProjection);
        var result = SecurityMasterCommandFacade.Amend(currentRecord, SecurityMasterMapping.ToAmendCommand(request, currentProjection));
        var projection = CreateProjectionFromResult(result, currentProjection.Aliases);
        var economic = SecurityEconomicDefinitionAdapter.ToEconomicRecord(projection);
        var envelope = SecurityMasterMapping.ToEventEnvelope(
            economic,
            "TermsAmended",
            request.UpdatedBy,
            request.SourceSystem,
            request.Reason,
            projection.Version);

        await _eventStore.AppendAsync(request.SecurityId, request.ExpectedVersion, [envelope], ct).ConfigureAwait(false);
        await _store.UpsertProjectionAsync(projection, ct).ConfigureAwait(false);
        await SaveSnapshotIfNeededAsync(economic, ct).ConfigureAwait(false);

        return SecurityMasterMapping.ToDetail(projection);
    }

    public async Task DeactivateAsync(DeactivateSecurityRequest request, CancellationToken ct = default)
    {
        var aliasProjection = await _store.GetProjectionAsync(request.SecurityId, ct).ConfigureAwait(false);
        var current = await _rebuilder.RebuildEconomicDefinitionAsync(request.SecurityId, aliasProjection, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Security '{request.SecurityId}' was not found.");

        var currentProjection = SecurityEconomicDefinitionAdapter.ToProjection(current, aliasProjection?.Aliases);
        var currentRecord = SecurityMasterMapping.ToRecord(currentProjection);
        var result = SecurityMasterCommandFacade.Deactivate(currentRecord, SecurityMasterMapping.ToDeactivateCommand(request));
        var projection = CreateProjectionFromResult(result, currentProjection.Aliases);
        var economic = SecurityEconomicDefinitionAdapter.ToEconomicRecord(projection);
        var envelope = SecurityMasterMapping.ToEventEnvelope(
            economic,
            "SecurityDeactivated",
            request.UpdatedBy,
            request.SourceSystem,
            request.Reason,
            projection.Version);

        await _eventStore.AppendAsync(request.SecurityId, request.ExpectedVersion, [envelope], ct).ConfigureAwait(false);
        await _store.UpsertProjectionAsync(projection, ct).ConfigureAwait(false);
        await SaveSnapshotIfNeededAsync(economic, ct).ConfigureAwait(false);
    }

    public Task<SecurityAliasDto> UpsertAliasAsync(UpsertSecurityAliasRequest request, CancellationToken ct = default)
    {
        var alias = new SecurityAliasDto(
            request.AliasId,
            request.SecurityId,
            request.AliasKind,
            request.AliasValue,
            request.Provider,
            request.Scope,
            request.Reason,
            request.CreatedBy,
            DateTimeOffset.UtcNow,
            request.ValidFrom,
            request.ValidTo,
            true);

        return UpsertAliasAsyncCore(alias, ct);
    }

    private async Task<SecurityDetailDto> ExecuteCreateAsync(CreateSecurityRequest request, CancellationToken ct)
    {
        var result = SecurityMasterCommandFacade.Create(SecurityMasterMapping.ToCreateCommand(request));
        var projection = CreateProjectionFromResult(result);
        var economic = SecurityEconomicDefinitionAdapter.ToEconomicRecord(projection);
        var envelope = SecurityMasterMapping.ToEventEnvelope(
            economic,
            "SecurityCreated",
            request.UpdatedBy,
            request.SourceSystem,
            request.Reason,
            projection.Version);

        await _eventStore.AppendAsync(request.SecurityId, expectedVersion: 0, [envelope], ct).ConfigureAwait(false);
        await _store.UpsertProjectionAsync(projection, ct).ConfigureAwait(false);
        await SaveSnapshotIfNeededAsync(economic, ct).ConfigureAwait(false);

        return SecurityMasterMapping.ToDetail(projection);
    }

    private static SecurityProjectionRecord CreateProjectionFromResult(
        SecurityMasterCommandResultWrapper result,
        IReadOnlyList<SecurityAliasDto>? aliases = null)
    {
        if (!result.IsSuccess || result.Snapshot is null)
        {
            var errorText = string.Join("; ", result.ErrorDetails.Select(e => $"[{e.Code}] {e.Message}"));
            throw new InvalidOperationException(errorText);
        }

        return SecurityMasterMapping.ToProjection(result.Snapshot, aliases);
    }

    private async Task<SecurityAliasDto> UpsertAliasAsyncCore(SecurityAliasDto alias, CancellationToken ct)
    {
        await _store.UpsertAliasAsync(alias, ct).ConfigureAwait(false);
        return alias;
    }

    private Task SaveSnapshotIfNeededAsync(SecurityEconomicDefinitionRecord definition, CancellationToken ct)
    {
        if (!ShouldSaveSnapshot(definition))
        {
            return Task.CompletedTask;
        }

        var snapshot = SecurityMasterMapping.ToSnapshot(definition, DateTimeOffset.UtcNow);
        return _snapshotStore.SaveAsync(snapshot, ct);
    }

    private bool ShouldSaveSnapshot(SecurityEconomicDefinitionRecord definition)
        => definition.Version == 1
            || definition.Status == SecurityStatusDto.Inactive
            || (_options.SnapshotIntervalVersions > 0 && definition.Version % _options.SnapshotIntervalVersions == 0);
}
