using Meridian.Contracts.SecurityMaster;
using Meridian.FSharp.SecurityMasterInterop;
using Meridian.Infrastructure.Adapters.Polygon;
using Meridian.Storage.SecurityMaster;
using Microsoft.Extensions.Logging;

namespace Meridian.Application.SecurityMaster;

public sealed class SecurityMasterService : ISecurityMasterService, ISecurityMasterAmender
{
    private readonly ISecurityMasterEventStore _eventStore;
    private readonly ISecurityMasterSnapshotStore _snapshotStore;
    private readonly ISecurityMasterStore _store;
    private readonly SecurityMasterAggregateRebuilder _rebuilder;
    private readonly SecurityMasterOptions _options;
    private readonly ILogger<SecurityMasterService> _logger;
    private readonly ISecurityMasterConflictService? _conflictService;
    private readonly IPolygonCorporateActionFetcher? _corporateActionFetcher;
    private readonly SecurityMasterProjectionCache? _projectionCache;
    private readonly SecurityMasterCanonicalSymbolSeedService? _seedService;

    public SecurityMasterService(
        ISecurityMasterEventStore eventStore,
        ISecurityMasterSnapshotStore snapshotStore,
        ISecurityMasterStore store,
        SecurityMasterAggregateRebuilder rebuilder,
        SecurityMasterOptions options,
        ILogger<SecurityMasterService> logger,
        ISecurityMasterConflictService? conflictService = null,
        IPolygonCorporateActionFetcher? corporateActionFetcher = null,
        SecurityMasterProjectionCache? projectionCache = null,
        SecurityMasterCanonicalSymbolSeedService? seedService = null)
    {
        _eventStore = eventStore;
        _snapshotStore = snapshotStore;
        _store = store;
        _rebuilder = rebuilder;
        _options = options;
        _logger = logger;
        _conflictService = conflictService;
        _corporateActionFetcher = corporateActionFetcher;
        _projectionCache = projectionCache;
        _seedService = seedService;
    }

    public Task<SecurityDetailDto> CreateAsync(CreateSecurityRequest request, CancellationToken ct = default)
        => ExecuteCreateAsync(request, ct);

    public Task<SecurityDetailDto> AmendTermsAsync(AmendSecurityTermsRequest request, CancellationToken ct = default)
        => AmendTermsInternalAsync(request, eventType: "TermsAmended", ct);

    private async Task<SecurityDetailDto> AmendTermsInternalAsync(
        AmendSecurityTermsRequest request,
        string eventType,
        CancellationToken ct)
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
            eventType,
            request.UpdatedBy,
            request.SourceSystem,
            request.Reason,
            projection.Version);

        await _eventStore.AppendAsync(request.SecurityId, request.ExpectedVersion, [envelope], ct).ConfigureAwait(false);
        await _store.UpsertProjectionAsync(projection, ct).ConfigureAwait(false);
        await SaveSnapshotIfNeededAsync(economic, ct).ConfigureAwait(false);
        await TryRecordConflictsAsync(projection, request.SecurityId, ct).ConfigureAwait(false);

        // Enqueue a best-effort corporate action re-fetch so that updated identifiers
        // (e.g. ticker changes after a merger rename) are reflected in the backfill history.
        if (_corporateActionFetcher is not null)
        {
            var ticker = projection.PrimaryIdentifierValue;
            var securityId = projection.SecurityId;
            _ = Task.Run(async () =>
            {
                try
                {
                    await _corporateActionFetcher.FetchAndPersistAsync(ticker, securityId, CancellationToken.None)
                        .ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "Background corporate action sync failed after amendment for {Ticker} ({SecurityId})",
                        ticker, securityId);
                }
            });
        }

        // Keep the in-memory projection cache and canonical registry consistent with the DB write.
        _projectionCache?.Upsert(projection);
        TryReseedRegistryInBackground();

        return SecurityMasterMapping.ToDetail(projection);
    }

    public async Task<SecurityDetailDto> AmendPreferredEquityTermsAsync(Guid securityId, AmendPreferredEquityTermsRequest request, CancellationToken ct = default)
    {
        var currentProjection = await _store.GetProjectionAsync(securityId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Security '{securityId}' was not found.");

        var amendRequest = new AmendSecurityTermsRequest(
            SecurityId: securityId,
            ExpectedVersion: request.ExpectedVersion,
            CommonTerms: null,
            AssetSpecificTermsPatch: SecurityMasterMapping.BuildPreferredEquityTermsPatch(currentProjection, request),
            IdentifiersToAdd: Array.Empty<SecurityIdentifierDto>(),
            IdentifiersToExpire: Array.Empty<SecurityIdentifierDto>(),
            EffectiveFrom: request.EffectiveFrom,
            SourceSystem: request.SourceSystem,
            UpdatedBy: request.UpdatedBy,
            SourceRecordId: request.SourceRecordId,
            Reason: request.Reason);

        return await AmendTermsInternalAsync(amendRequest, eventType: "PreferredTermsAmended", ct).ConfigureAwait(false);
    }

    public async Task<SecurityDetailDto> AmendConvertibleEquityTermsAsync(Guid securityId, AmendConvertibleEquityTermsRequest request, CancellationToken ct = default)
    {
        var currentProjection = await _store.GetProjectionAsync(securityId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Security '{securityId}' was not found.");

        var amendRequest = new AmendSecurityTermsRequest(
            SecurityId: securityId,
            ExpectedVersion: request.ExpectedVersion,
            CommonTerms: null,
            AssetSpecificTermsPatch: SecurityMasterMapping.BuildConvertibleEquityTermsPatch(currentProjection, request),
            IdentifiersToAdd: Array.Empty<SecurityIdentifierDto>(),
            IdentifiersToExpire: Array.Empty<SecurityIdentifierDto>(),
            EffectiveFrom: request.EffectiveFrom,
            SourceSystem: request.SourceSystem,
            UpdatedBy: request.UpdatedBy,
            SourceRecordId: request.SourceRecordId,
            Reason: request.Reason);

        return await AmendTermsInternalAsync(amendRequest, eventType: "ConvertibleTermsAmended", ct).ConfigureAwait(false);
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

        await TryRecordConflictsAsync(projection, request.SecurityId, ct).ConfigureAwait(false);

        // Enqueue a best-effort corporate action backfill for the newly-created security so
        // that historical corp action data is available immediately for backtesting.
        if (_corporateActionFetcher is not null)
        {
            var ticker = projection.PrimaryIdentifierValue;
            var securityId = projection.SecurityId;
            _ = Task.Run(async () =>
            {
                try
                {
                    await _corporateActionFetcher.FetchAndPersistAsync(ticker, securityId, CancellationToken.None)
                        .ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "Background corporate action sync failed for new security {Ticker} ({SecurityId})",
                        ticker, securityId);
                }
            });
        }

        // Keep the in-memory projection cache and canonical registry consistent with the DB write.
        _projectionCache?.Upsert(projection);
        TryReseedRegistryInBackground();

        return SecurityMasterMapping.ToDetail(projection);
    }

    private void TryReseedRegistryInBackground()
    {
        if (_seedService is null)
            return;

        _ = Task.Run(async () =>
        {
            try
            {
                await _seedService.SeedAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Background canonical symbol registry re-seed failed.");
            }
        });
    }

    private async Task TryRecordConflictsAsync(SecurityProjectionRecord projection, Guid securityId, CancellationToken ct)
    {
        if (_conflictService is null)
            return;
        try
        {
            await _conflictService.RecordConflictsForProjectionAsync(projection, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Conflict detection failed for security {SecurityId}", securityId);
        }
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
