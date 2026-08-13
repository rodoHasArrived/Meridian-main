using System.Text.Json;
using Meridian.Contracts.SecurityMaster;
using Meridian.FSharp.SecurityMasterInterop;
using Meridian.Infrastructure.Adapters.Polygon;
using Meridian.Storage.SecurityMaster;
using Microsoft.Extensions.Logging;

namespace Meridian.Application.SecurityMaster;

public sealed class SecurityMasterService : ISecurityMasterService, ISecurityMasterAmender, IDisposable
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
    private readonly Meridian.ReferenceData.SecurityMaster.ISecurityAssetProfileCatalog? _assetProfileCatalog;
    private readonly ISecurityFieldProvenanceStore? _fieldProvenance;

    // Owned lifetime token so background fire-and-forget tasks are cancelled on disposal.
    private readonly CancellationTokenSource _serviceCts = new();

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
        SecurityMasterCanonicalSymbolSeedService? seedService = null,
        Meridian.ReferenceData.SecurityMaster.ISecurityAssetProfileCatalog? assetProfileCatalog = null,
        ISecurityFieldProvenanceStore? fieldProvenance = null)
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
        _assetProfileCatalog = assetProfileCatalog;
        _fieldProvenance = fieldProvenance;
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
        EnsureAssetClassRoundTripsSafely(currentProjection, request.AssetSpecificTermsPatch);
        var currentRecord = SecurityMasterMapping.ToRecord(currentProjection);
        // The SUBMITTED envelope's profile decides which kind parses the patch: a record already
        // reclassified to a first-class kind can be repinned to a different profile, and parsing
        // the new envelope through the OLD class would demand the old class's fields (repinning
        // PrivateFundInterest to structured-credit-io-po must not require gpSponsor) — or, for an
        // unmapped profile, make the return to CustomAsset unreachable.
        var kindSourceProjection = currentProjection;
        if (request.AssetSpecificTermsPatch is JsonElement patchEnvelope
            && IsProfileBackedCustomAsset(currentProjection.AssetClass, patchEnvelope))
        {
            kindSourceProjection = currentProjection with
            {
                AssetClass = TryResolveProfileBackedAlternativeAssetClass(patchEnvelope, out var submittedClass)
                    ? submittedClass
                    : "CustomAsset"
            };
        }

        // The override guards are computed BEFORE the command mapping so a profile-backed record
        // amended without its envelope is refused with the envelope-specific guidance (submit the
        // pinned envelope or use the workbench field-edit route) rather than the generic
        // strict-mapping error the write-mode kind mapping would raise first.
        var assetClassOverride = GetProfileBackedAssetClassOverride(currentProjection, request.AssetSpecificTermsPatch);
        var assetTermsOverride = GetProfileBackedAssetSpecificTermsOverride(currentProjection, request.AssetSpecificTermsPatch);
        var result = SecurityMasterCommandFacade.Amend(currentRecord, SecurityMasterMapping.ToAmendCommand(request, kindSourceProjection));
        var projection = CreateProjectionFromResult(
            result,
            currentProjection.Aliases,
            assetClassOverride,
            assetTermsOverride);
        EnsureProfileBackedTermsAreCatalogValid(projection, request.EffectiveFrom);

        // The amend seam is the one place the pre-write golden copy and the incoming revision are
        // both in hand: record field-level cross-source conflicts (a different source disagreeing
        // on economic/common terms) BEFORE the amendment persists, and fail the amend if the
        // recording fails. Once the event and projection are written the previous source value is
        // overwritten and the disagreement cannot be reconstructed from current projections, so a
        // swallowed conflict-store failure would permanently remove the challenger from the
        // governed resolution workflow. Conflict ids are deterministic, so a retried amendment
        // reuses the same rows, and a conflict recorded for an amendment that subsequently fails
        // still describes a real cross-source disagreement.
        await RecordFieldConflictsBeforePersistAsync(currentProjection, projection, ct).ConfigureAwait(false);

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
        // A governed field that just changed invalidates any prior conflict-resolution attribution
        // for that path: the recorded winner no longer supplied the current value. Changed paths
        // are computed independently of cross-source conflict creation — the previous winner
        // amending its OWN value opens no conflict, yet still makes the old attribution stale.
        // Attribution runs IMMEDIATELY after the projection write (before snapshots and identifier
        // conflict detection) to minimize the window in which a concurrent reader sees the new
        // value with the old field attribution; true atomicity needs the shared-transaction seam
        // tracked as follow-up work.
        var changedGovernedFields = SecurityMasterConflictDetection.ChangedGovernedFieldPaths(
            currentProjection, projection, _assetProfileCatalog);
        await TryRetireStaleFieldResolutionProvenanceAsync(changedGovernedFields, request.SecurityId, ct).ConfigureAwait(false);
        await TryRecordCanonicalFieldAttributionAsync(changedGovernedFields, projection, ct).ConfigureAwait(false);
        // Open conflicts reconcile against the DURABLY persisted value only: superseding or
        // refreshing them before AppendAsync's ExpectedVersion check could mutate the governed
        // conflict queue for an amendment the event store then rejects. Best-effort — a failed
        // sweep leaves conflicts Open, where the resolve-time guard reconciles them lazily.
        await TryReconcileOpenFieldConflictsAsync(projection, ct).ConfigureAwait(false);
        await SaveSnapshotIfNeededAsync(economic, ct).ConfigureAwait(false);
        await TryRecordConflictsAsync(projection, request.SecurityId, ct).ConfigureAwait(false);

        // Enqueue a best-effort corporate action re-fetch so that updated identifiers
        // (e.g. ticker changes after a merger rename) are reflected in the backfill history.
        if (_corporateActionFetcher is not null)
        {
            var ticker = projection.PrimaryIdentifierValue;
            var securityId = projection.SecurityId;
            var ct2 = _serviceCts.Token;
            _ = Task.Run(async () =>
            {
                try
                {
                    await _corporateActionFetcher.FetchAndPersistAsync(ticker, securityId, ct2)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "Background corporate action sync failed after amendment for {Ticker} ({SecurityId})",
                        ticker, securityId);
                }
            }, ct2);
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
        EnsureAssetClassRoundTripsSafely(currentProjection);
        var currentRecord = SecurityMasterMapping.ToRecord(currentProjection);
        var result = SecurityMasterCommandFacade.Deactivate(currentRecord, SecurityMasterMapping.ToDeactivateCommand(request));
        var projection = CreateProjectionFromResult(
            result,
            currentProjection.Aliases,
            GetProfileBackedAssetClassOverride(currentProjection),
            GetProfileBackedAssetSpecificTermsOverride(currentProjection, assetSpecificTermsPatch: null));
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
        var projection = CreateProjectionFromResult(
            result,
            aliases: null,
            GetProfileBackedAssetClassOverride(request.AssetClass, request.AssetSpecificTerms),
            GetProfileBackedAssetSpecificTermsOverride(request.AssetSpecificTerms));
        EnsureProfileBackedTermsAreCatalogValid(projection, request.EffectiveFrom);
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
        // Creation is the first canonical write of every governed field the record supplies, so it
        // seeds the per-field attribution the same way an amend does — diffed against an empty
        // baseline instead of a previous revision, and immediately after the projection write to
        // minimize the unattributed window.
        var seededGovernedFields = SecurityMasterConflictDetection.ChangedGovernedFieldPaths(
            CreateEmptyGovernedBaseline(projection), projection, _assetProfileCatalog);
        await TryRecordCanonicalFieldAttributionAsync(seededGovernedFields, projection, ct).ConfigureAwait(false);
        await SaveSnapshotIfNeededAsync(economic, ct).ConfigureAwait(false);

        await TryRecordConflictsAsync(projection, request.SecurityId, ct).ConfigureAwait(false);

        // Enqueue a best-effort corporate action backfill for the newly-created security so
        // that historical corp action data is available immediately for backtesting.
        if (_corporateActionFetcher is not null)
        {
            var ticker = projection.PrimaryIdentifierValue;
            var securityId = projection.SecurityId;
            var ct2 = _serviceCts.Token;
            _ = Task.Run(async () =>
            {
                try
                {
                    await _corporateActionFetcher.FetchAndPersistAsync(ticker, securityId, ct2)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "Background corporate action sync failed for new security {Ticker} ({SecurityId})",
                        ticker, securityId);
                }
            }, ct2);
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

        var ct = _serviceCts.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                await _seedService.SeedAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Background canonical symbol registry re-seed failed.");
            }
        }, ct);
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

    /// <summary>
    /// Detects and durably records field-level cross-source conflicts BEFORE the amendment
    /// persists. Failures propagate — accepting the amendment while losing the conflict would
    /// silently drop the challenger from the governed resolution workflow, and the caller can
    /// safely retry because the amendment has not yet been written.
    /// </summary>
    private async Task RecordFieldConflictsBeforePersistAsync(
        SecurityProjectionRecord previous,
        SecurityProjectionRecord incoming,
        CancellationToken ct)
    {
        if (_conflictService is null)
            return;

        // The pre-check gate needs the same per-field attribution the durable store consults:
        // without it, record-level sources on both sides read the record's LAST writer, so source B
        // changing a field source A supplied would be filtered out here as same-source versioning
        // and never reach the conflict store at all.
        var (incumbentFieldSources, attributionIsAuthoritative) =
            await TryLoadIncumbentFieldSourcesAsync(previous.SecurityId, ct).ConfigureAwait(false);
        var candidates = SecurityMasterConflictDetection.DetectFieldConflicts(
            previous, incoming, DateTimeOffset.UtcNow, incumbentFieldSources, _assetProfileCatalog);
        // The zero-candidate shortcut is only safe when the attribution read was AUTHORITATIVE
        // (it succeeded, or no attribution store is wired so record-level provenance is all that
        // exists). After a failed read, "no candidates" may just mean the same-source filter hid a
        // real cross-source disagreement, so the durable conflict service — which performs its own
        // attribution read — must still run; if that read also fails, the amendment fails BEFORE
        // persisting, per this method's contract, instead of silently dropping the conflict.
        if (candidates.Count == 0 && attributionIsAuthoritative)
            return;

        await _conflictService.RecordFieldConflictsAsync(previous, incoming, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Post-persist reconciliation of OPEN field conflicts against the durably written projection
    /// (supersede when a third source replaced both candidates, refresh a candidate revising its
    /// own value). Best-effort: a failed sweep leaves conflicts Open — recoverable lazily at
    /// resolve time — whereas failing the amendment AFTER it durably persisted would report an
    /// error for a write that succeeded.
    /// </summary>
    private async Task TryReconcileOpenFieldConflictsAsync(SecurityProjectionRecord persisted, CancellationToken ct)
    {
        if (_conflictService is null)
            return;

        try
        {
            await _conflictService.ReconcileOpenFieldConflictsAsync(persisted, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(
                ex,
                "Reconciling open field conflicts after the amendment persisted failed for {SecurityId}; obsolete conflicts stay open until resolution or the next successful write.",
                persisted.SecurityId);
        }
    }

    /// <summary>
    /// Per-field incumbent attribution (field path → source system) from the durable provenance
    /// rows: canonical-write and conflict-resolution origins, newest recorded row winning per
    /// field. The <c>IsAuthoritative</c> flag distinguishes "record-level provenance is all that
    /// exists" (store unwired, or read succeeded) from "the read FAILED": after a failure the
    /// pre-check's same-source filter may hide a real cross-source disagreement, so callers must
    /// not treat a zero-candidate result as proof there is no conflict.
    /// </summary>
    private async Task<(IReadOnlyDictionary<string, string>? Sources, bool IsAuthoritative)> TryLoadIncumbentFieldSourcesAsync(
        Guid securityId,
        CancellationToken ct)
    {
        if (_fieldProvenance is null)
            return (null, true);

        try
        {
            var rows = await _fieldProvenance.GetAsync(securityId, ct).ConfigureAwait(false);
            var sources = rows
                .Where(static row => row.Origin is SecurityFieldProvenanceOrigins.ConflictResolution
                    or SecurityFieldProvenanceOrigins.CanonicalWrite)
                .GroupBy(static row => row.FieldPath, StringComparer.Ordinal)
                .ToDictionary(
                    static group => group.Key,
                    static group => group.OrderByDescending(static row => row.RecordedAt).First().SourceSystem,
                    StringComparer.Ordinal);
            return (sources, true);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(
                ex,
                "Loading per-field incumbent attribution for {SecurityId} failed; conflict pre-check falls back to record-level provenance and defers to the durable conflict service.",
                securityId);
            return (null, false);
        }
    }

    /// <summary>
    /// A persisted amendment that changed a conflicted field invalidates the field's prior
    /// ConflictResolution attribution — the recorded winner no longer supplied the current value,
    /// and if the new conflict is later dismissed the stale row would stay wrong indefinitely.
    /// Best-effort: the open conflict is the durable governance artifact; a removal failure is
    /// logged and the next resolution overwrites the row.
    /// </summary>
    private async Task TryRetireStaleFieldResolutionProvenanceAsync(
        IReadOnlyList<string> changedFieldPaths,
        Guid securityId,
        CancellationToken ct)
    {
        if (_fieldProvenance is null || changedFieldPaths.Count == 0)
            return;

        foreach (var fieldPath in changedFieldPaths)
        {
            try
            {
                await _fieldProvenance.RemoveAsync(
                    securityId,
                    fieldPath,
                    SecurityFieldProvenanceOrigins.ConflictResolution,
                    clearedAt: DateTimeOffset.UtcNow,
                    ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(
                    ex,
                    "Retiring stale conflict-resolution provenance for {SecurityId} field {FieldPath} failed; the row will be overwritten by the next resolution.",
                    securityId, fieldPath);
            }
        }
    }

    /// <summary>
    /// Records per-field CanonicalWrite attribution for the governed fields a persisted
    /// create/amend supplied. Record-level provenance flips on every amendment, so without these
    /// rows conflict detection can only name the record's LAST writer as a field's incumbent —
    /// which, when providers amend different fields in sequence, attributes a conflicted field to a
    /// source that never supplied it. The canonical projection stays the durable artifact, but a
    /// swallowed failure here does NOT degrade to the record-level fallback: any OLDER attribution
    /// row keeps naming a stale incumbent, which can open false cross-source conflicts against the
    /// writer's own later revisions. Each row is therefore retried once, and on final failure the
    /// stale attribution rows for that path are explicitly invalidated so the record-level
    /// fallback genuinely applies; only when THAT also fails does a stale incumbent remain,
    /// logged as an error.
    /// </summary>
    private async Task TryRecordCanonicalFieldAttributionAsync(
        IReadOnlyList<string> changedFieldPaths,
        SecurityProjectionRecord projection,
        CancellationToken ct)
    {
        if (_fieldProvenance is null || changedFieldPaths.Count == 0)
            return;

        var provenance = SecurityMasterProvenanceReader.Read(projection.Provenance);
        var recordedAt = DateTimeOffset.UtcNow;
        foreach (var fieldPath in changedFieldPaths)
        {
            var recorded = false;
            for (var attempt = 0; attempt < 2 && !recorded; attempt++)
            {
                try
                {
                    await _fieldProvenance.UpsertAsync(
                        new SecurityFieldProvenanceRecord(
                            projection.SecurityId,
                            fieldPath,
                            provenance.SourceSystem,
                            provenance.AsOf,
                            provenance.UpdatedBy,
                            Confidence: null,
                            SecurityFieldProvenanceOrigins.CanonicalWrite,
                            OriginReference: $"version:{projection.Version}",
                            recordedAt,
                            // Attribution is ordered by the amendment's COMMIT ORDER, not this
                            // callback's wall-clock time: a delayed v2 write arriving after v3's
                            // must not overwrite the newer incumbent.
                            SourceVersion: projection.Version),
                        ct).ConfigureAwait(false);
                    recorded = true;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    if (attempt == 0)
                    {
                        _logger.LogWarning(
                            ex,
                            "Recording canonical field attribution for {SecurityId} field {FieldPath} failed; retrying once.",
                            projection.SecurityId, fieldPath);
                        continue;
                    }

                    _logger.LogWarning(
                        ex,
                        "Recording canonical field attribution for {SecurityId} field {FieldPath} failed after retry; invalidating stale attribution so record-level provenance applies.",
                        projection.SecurityId, fieldPath);
                }
            }

            if (recorded)
            {
                continue;
            }

            // The durable record changed hands but its new attribution could not be written. An
            // older CanonicalWrite/ConflictResolution row would keep naming the PREVIOUS incumbent,
            // so the claimed record-level fallback would never engage — invalidate the stale rows
            // explicitly so absence (and therefore the record-level source) is what loaders see.
            try
            {
                var clearedAt = DateTimeOffset.UtcNow;
                await _fieldProvenance.RemoveAsync(
                    projection.SecurityId, fieldPath,
                    SecurityFieldProvenanceOrigins.CanonicalWrite, clearedAt, ct).ConfigureAwait(false);
                await _fieldProvenance.RemoveAsync(
                    projection.SecurityId, fieldPath,
                    SecurityFieldProvenanceOrigins.ConflictResolution, clearedAt, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(
                    ex,
                    "Invalidating stale field attribution for {SecurityId} field {FieldPath} also failed; a stale incumbent may misattribute this field until the next successful write or resolution.",
                    projection.SecurityId, fieldPath);
            }
        }
    }

    private static readonly JsonElement EmptyJsonObject = JsonDocument.Parse("{}").RootElement.Clone();

    /// <summary>
    /// A baseline with no governed term values, so <see
    /// cref="SecurityMasterConflictDetection.ChangedGovernedFieldPaths"/> reports every governed
    /// field a freshly created projection supplies (creation is the first canonical write of each).
    /// </summary>
    private static SecurityProjectionRecord CreateEmptyGovernedBaseline(SecurityProjectionRecord projection)
        => projection with
        {
            Currency = string.Empty,
            CommonTerms = EmptyJsonObject,
            AssetSpecificTerms = EmptyJsonObject,
        };

    public void Dispose()
    {
        _serviceCts.Cancel();
        _serviceCts.Dispose();
    }

    /// <summary>
    /// Read tolerance must not become write tolerance: a record whose stored asset class this node
    /// does not recognize deserializes through the OtherSecurity fallback, so re-serializing it on
    /// amend or deactivate would silently rewrite its asset class and drop its terms. Refuse the
    /// write instead — the record stays readable, and the change must come from a node that
    /// supports the class.
    /// </summary>
    private static void EnsureAssetClassRoundTripsSafely(
        SecurityProjectionRecord projection,
        JsonElement? assetSpecificTermsPatch = null)
    {
        if (!SecurityAssetClassCatalog.AssetClasses.Contains(projection.AssetClass, StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Security '{projection.SecurityId:D}' has asset class '{projection.AssetClass}', which this node does not recognize. " +
                "Amending or deactivating it here would re-serialize the record through the OtherSecurity fallback and rewrite its " +
                "asset class, so the write is refused. Apply the change from a node that supports this asset class.");
        }

        EnsureEquityClassificationRoundTripsSafely(projection);
        EnsureCustomAssetEnvelopeRoundTripsSafely(projection, assetSpecificTermsPatch);
    }

    /// <summary>
    /// Profile-backed records reference an approved profile that governs their dynamic fields, but
    /// the F# domain command cannot see the profile catalog — without this check an unknown, draft,
    /// or field-violating profile persists canonically and is only discovered by a later validation
    /// read. Runs the PROFILE validation only (existence, approval status, field conformance,
    /// identifier coverage) BEFORE the create/amend event is appended and refuses the write on
    /// Error-severity issues. Deliberately not the per-asset-class composite validators: a
    /// profile-backed record reclassified to its resolved asset class (e.g. PrivateFundInterest)
    /// must not be rejected by unrelated OtherSecurity field rules such as a required outer
    /// <c>category</c>. Validation runs at the WRITE'S effective time, not the wall clock: a
    /// future-dated create's identifiers are valid as of its EffectiveFrom, and evaluating them at
    /// "now" would refuse a legitimate forward-dated record (or accept one whose coverage lapses by
    /// its own effective date). Skipped when no catalog was supplied (harnesses that exercise
    /// storage mechanics without reference data).
    /// <para>Reclassified records (profile-backed but no longer CustomAsset) additionally re-run
    /// the resolved first-class kind's domain invariants: the F# create/amend validated the
    /// pre-override CustomAsset shape, so without this step a payload that violates the resolved
    /// kind's stricter rules (e.g. a non-positive PrivateFundInterest commitment) would persist
    /// under a class whose invariants it never satisfied.</para>
    /// </summary>
    private void EnsureProfileBackedTermsAreCatalogValid(SecurityProjectionRecord projection, DateTimeOffset effectiveAt)
    {
        if (_assetProfileCatalog is null)
        {
            return;
        }

        var isCustomAsset = string.Equals(projection.AssetClass, "CustomAsset", StringComparison.OrdinalIgnoreCase);
        var terms = projection.AssetSpecificTerms;
        var referencesProfile = terms.ValueKind == JsonValueKind.Object
            && terms.TryGetProperty("customProfileId", out var profileId)
            && profileId.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(profileId.GetString());
        if (!isCustomAsset && !referencesProfile)
        {
            return;
        }

        var validator = new Validation.SecurityAssetProfileAssetClassValidator(
            projection.AssetClass,
            _assetProfileCatalog,
            requireProfileReference: isCustomAsset,
            enforceWriteTimeGovernance: true);
        var issues = validator.Validate(new Validation.SecurityValidationContext(projection, effectiveAt));
        var errors = issues
            .Where(static issue => issue.Severity == SecurityValidationSeverityDto.Error)
            .ToArray();
        if (errors.Length > 0)
        {
            var summary = string.Join("; ", errors.Select(static issue => $"[{issue.Code}] {issue.Message}"));
            throw new InvalidOperationException(
                $"Security '{projection.SecurityId:D}' references an asset profile that fails catalog validation, " +
                $"so the write is refused: {summary}");
        }

        if (!isCustomAsset)
        {
            var kind = SecurityMasterMapping.ToRecord(projection).Kind;
            var invariantErrors = SecurityMasterCommandFacade.ValidateKindInvariants(kind);
            if (invariantErrors.Length > 0)
            {
                var summary = string.Join("; ", invariantErrors.Select(static e => $"[{e.Code}] {e.Message}"));
                throw new InvalidOperationException(
                    $"Security '{projection.SecurityId:D}' resolved to asset class '{projection.AssetClass}' but its " +
                    $"terms violate that class's domain invariants, so the write is refused: {summary}");
            }
        }
    }

    /// <summary>
    /// A legacy CustomAsset row that predates the profile envelope (no <c>customProfileId</c>)
    /// deserializes through the OtherSecurity salvage path even though "CustomAsset" is a
    /// catalog-recognized class, so an amend or deactivate would re-serialize the fallback as
    /// OtherSecurity and drop the record's unmodeled custom fields. The envelope, not the catalog
    /// name, is what makes the round-trip lossless — refuse the write when it is absent, EXCEPT
    /// for the one governed exit the refusal itself instructs: an amendment whose patch carries a
    /// complete profile envelope replaces the terms wholesale (the envelope override persists the
    /// submitted document verbatim), so the lossy fallback re-serialization is never written and
    /// the record migrates onto a profile.
    /// </summary>
    private static void EnsureCustomAssetEnvelopeRoundTripsSafely(
        SecurityProjectionRecord projection,
        JsonElement? assetSpecificTermsPatch)
    {
        if (!string.Equals(projection.AssetClass, "CustomAsset", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var terms = projection.AssetSpecificTerms;
        var hasEnvelope = terms.ValueKind == JsonValueKind.Object
            && terms.TryGetProperty("customProfileId", out var profileId)
            && profileId.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(profileId.GetString());
        if (hasEnvelope)
        {
            return;
        }

        var patchCarriesEnvelope = assetSpecificTermsPatch is JsonElement patch
            && patch.ValueKind == JsonValueKind.Object
            && patch.TryGetProperty("customProfileId", out var patchProfileId)
            && patchProfileId.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(patchProfileId.GetString());
        if (patchCarriesEnvelope)
        {
            return;
        }

        throw new InvalidOperationException(
            $"Security '{projection.SecurityId:D}' is a CustomAsset without a profile envelope (no customProfileId), " +
            "so it reads through the OtherSecurity salvage path. Amending or deactivating it here would re-serialize " +
            "that fallback and drop its custom fields, so the write is refused. Migrate the record by amending it with " +
            "a complete profile envelope (customProfileId, profileVersion, profileFields).");
    }

    /// <summary>
    /// Same read-tolerance-must-not-become-write-tolerance rule, one level down: an equity whose
    /// stored classification discriminant this node does not recognize deserializes as
    /// <c>EquityClassification.Other(raw)</c>, which re-serializes WITHOUT the record's
    /// <c>preferredTerms</c>/<c>convertibleTerms</c> blocks. When those blocks are present the write
    /// would silently delete structure this node did not understand, so it is refused; without them
    /// the Other round-trip is lossless (the raw label survives as <c>otherClassification</c>).
    /// </summary>
    private static void EnsureEquityClassificationRoundTripsSafely(SecurityProjectionRecord projection)
    {
        if (!string.Equals(projection.AssetClass, "Equity", StringComparison.OrdinalIgnoreCase)
            || projection.AssetSpecificTerms.ValueKind != JsonValueKind.Object
            || !projection.AssetSpecificTerms.TryGetProperty("classification", out var classification)
            || classification.ValueKind != JsonValueKind.String)
        {
            return;
        }

        // The serializer's discriminants are exact-case; anything else round-trips as Other.
        var raw = classification.GetString();
        var isKnownDiscriminant = raw is "Common" or "Preferred" or "Convertible" or "ConvertiblePreferred" or "Other";
        if (isKnownDiscriminant)
        {
            return;
        }

        var carriesNestedTerms =
            (projection.AssetSpecificTerms.TryGetProperty("preferredTerms", out var preferred)
                && preferred.ValueKind == JsonValueKind.Object)
            || (projection.AssetSpecificTerms.TryGetProperty("convertibleTerms", out var convertible)
                && convertible.ValueKind == JsonValueKind.Object);
        if (carriesNestedTerms)
        {
            throw new InvalidOperationException(
                $"Security '{projection.SecurityId:D}' is an equity with classification '{raw}', which this node does not " +
                "recognize, and it carries preferred/convertible term blocks tied to that classification. Re-serializing it " +
                "here would degrade the classification to 'Other' and drop those blocks, so the write is refused. Apply the " +
                "change from a node that supports this classification.");
        }
    }

    private static SecurityProjectionRecord CreateProjectionFromResult(
        SecurityMasterCommandResultWrapper result,
        IReadOnlyList<SecurityAliasDto>? aliases = null,
        string? assetClassOverride = null,
        JsonElement? assetSpecificTermsOverride = null)
    {
        if (!result.IsSuccess || result.Snapshot is null)
        {
            var errorText = string.Join("; ", result.ErrorDetails.Select(e => $"[{e.Code}] {e.Message}"));
            throw new InvalidOperationException(errorText);
        }

        var projection = SecurityMasterMapping.ToProjection(result.Snapshot, aliases);
        return projection with
        {
            AssetClass = assetClassOverride ?? projection.AssetClass,
            AssetSpecificTerms = assetSpecificTermsOverride?.Clone() ?? projection.AssetSpecificTerms
        };
    }

    private static string? GetProfileBackedAssetClassOverride(SecurityProjectionRecord projection)
        => IsProfileBackedCustomAsset(projection.AssetClass, projection.AssetSpecificTerms)
            ? GetProfileBackedAssetClassOverride(projection.AssetClass, projection.AssetSpecificTerms)
            : null;

    /// <summary>
    /// The SUBMITTED envelope decides an amendment's resolved class: repinning a record to a
    /// registered reclassifying profile must resolve exactly as the identical create would.
    /// Deriving only from the stored projection would persist the new envelope while silently
    /// keeping the old class — the record then skips the resolved class's validators and Asset
    /// Operations routing that a fresh create with the same payload receives. A patch without a
    /// profile envelope falls back to the stored projection's resolution.
    /// </summary>
    private static string? GetProfileBackedAssetClassOverride(
        SecurityProjectionRecord currentProjection,
        JsonElement? assetSpecificTermsPatch)
        => assetSpecificTermsPatch is JsonElement patch
            && IsProfileBackedCustomAsset(currentProjection.AssetClass, patch)
                ? (TryResolveProfileBackedAlternativeAssetClass(patch, out var submittedClass)
                    ? submittedClass
                    // An unmapped registered profile resolves to CustomAsset — explicitly, so a
                    // first-class record repinned to e.g. co-invest-spv RETURNS to CustomAsset
                    // instead of keeping the old class via the stored projection's resolution.
                    : "CustomAsset")
                : GetProfileBackedAssetClassOverride(currentProjection);

    private static string? GetProfileBackedAssetClassOverride(string assetClass, JsonElement assetSpecificTerms)
    {
        if (!IsProfileBackedCustomAsset(assetClass, assetSpecificTerms))
        {
            return null;
        }

        if (TryResolveProfileBackedAlternativeAssetClass(assetSpecificTerms, out var resolvedAssetClass))
        {
            return resolvedAssetClass;
        }

        return string.Equals(assetClass, "CustomAsset", StringComparison.OrdinalIgnoreCase)
            ? "CustomAsset"
            : null;
    }

    private static JsonElement? GetProfileBackedAssetSpecificTermsOverride(
        SecurityProjectionRecord currentProjection,
        JsonElement? assetSpecificTermsPatch)
    {
        if (assetSpecificTermsPatch is JsonElement patch && IsProfileBackedCustomAsset(currentProjection.AssetClass, patch))
        {
            return patch.Clone();
        }

        if (!IsProfileBackedCustomAsset(currentProjection.AssetClass, currentProjection.AssetSpecificTerms))
        {
            return null;
        }

        // A profile-backed record's asset terms ARE the profile envelope. A patch that does not
        // carry the envelope cannot be applied here — restoring the previous envelope instead
        // would append an event and advance the version while silently discarding every requested
        // value. Refuse the write; the caller must include the pinned envelope (with the updated
        // profileFields) or use the governed workbench field-edit route.
        if (assetSpecificTermsPatch is not null)
        {
            throw new InvalidOperationException(
                $"Security '{currentProjection.SecurityId:D}' is profile-backed: asset-specific term amendments must " +
                "carry the pinned profile envelope (customProfileId, profileVersion, profileFields). Submit the full " +
                "envelope with the updated profileFields, or edit individual fields through the workbench field-edit route.");
        }

        return currentProjection.AssetSpecificTerms.Clone();
    }

    private static JsonElement? GetProfileBackedAssetSpecificTermsOverride(JsonElement assetSpecificTerms)
        => IsProfileBackedCustomAsset(assetClass: null, assetSpecificTerms)
            ? assetSpecificTerms.Clone()
            : null;

    private static bool IsProfileBackedCustomAsset(string? assetClass, JsonElement assetSpecificTerms)
        => (string.IsNullOrWhiteSpace(assetClass)
            || string.Equals(assetClass, "CustomAsset", StringComparison.OrdinalIgnoreCase)
            || string.Equals(assetClass, "OtherSecurity", StringComparison.OrdinalIgnoreCase)
            || string.Equals(assetClass, "StructuredCredit", StringComparison.OrdinalIgnoreCase)
            || string.Equals(assetClass, "PrivateFundInterest", StringComparison.OrdinalIgnoreCase)
            || string.Equals(assetClass, "PrivateCompanyEquity", StringComparison.OrdinalIgnoreCase)
            || string.Equals(assetClass, "RealEstateHolding", StringComparison.OrdinalIgnoreCase)
            || string.Equals(assetClass, "CommitmentGuarantee", StringComparison.OrdinalIgnoreCase))
           && assetSpecificTerms.ValueKind == System.Text.Json.JsonValueKind.Object
           && assetSpecificTerms.TryGetProperty("customProfileId", out var customProfileId)
           && customProfileId.ValueKind == System.Text.Json.JsonValueKind.String
           && !string.IsNullOrWhiteSpace(customProfileId.GetString());

    /// <summary>
    /// The asset class a known profile id resolves to. The PROFILE is the identity of a
    /// profile-backed record, so reclassification derives from this map alone — caller-supplied
    /// category/subType/accountingClassification metadata may corroborate but never decide, or a
    /// mislabeled envelope would route a record into the wrong class's validators and projections.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> KnownProfileAssetClasses =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["structured-credit-io-po"] = "StructuredCredit",
            ["private-fund-interest"] = "PrivateFundInterest",
            ["private-company-equity"] = "PrivateCompanyEquity",
            ["real-estate-holding"] = "RealEstateHolding",
            ["commitment-guarantee"] = "CommitmentGuarantee",
        };

    /// <summary>Classification keywords each resolved class accepts in envelope metadata.</summary>
    private static readonly IReadOnlyDictionary<string, string[]> AssetClassMetadataKeywords =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["StructuredCredit"] = ["StructuredCredit", "MBS", "ABS", "CLO", "CMBS"],
            ["PrivateFundInterest"] = ["PrivateFunds", "PartnershipInterest", "PrivateFund", "PrivateFundInterest"],
            ["PrivateCompanyEquity"] = ["PrivateEquity", "PrivateCompanyEquity"],
            ["RealEstateHolding"] = ["RealEstate", "RealEstateInterest", "RealEstateHolding"],
            ["CommitmentGuarantee"] = ["CommitmentGuarantee", "UnfundedCommitment", "Guarantee"],
        };

    private static bool TryResolveProfileBackedAlternativeAssetClass(JsonElement assetSpecificTerms, out string assetClass)
    {
        var profileId = GetString(assetSpecificTerms, "customProfileId");

        // ONLY a registered reclassifying profile id changes the class. Field names and
        // classification metadata are caller-controlled, so any heuristic on them would let a
        // payload pinned to an unrelated profile (which validates against THAT profile's rules)
        // spoof its way into another class's validators and projection behavior. A profile id
        // outside this map keeps the record a CustomAsset.
        if (profileId is null || !KnownProfileAssetClasses.TryGetValue(profileId, out var resolvedClass))
        {
            assetClass = string.Empty;
            return false;
        }

        // Envelope metadata that names a DIFFERENT class's keyword is refused rather than
        // resolved: silently preferring the profile would hide the contradiction from the caller
        // who asserted it.
        var category = GetString(assetSpecificTerms, "category");
        var subType = GetString(assetSpecificTerms, "subType");
        var accountingClassification = GetString(assetSpecificTerms, "accountingClassification");
        foreach (var (candidateClass, keywords) in AssetClassMetadataKeywords)
        {
            if (string.Equals(candidateClass, resolvedClass, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var conflicting = keywords.FirstOrDefault(keyword =>
                string.Equals(category, keyword, StringComparison.OrdinalIgnoreCase)
                || string.Equals(subType, keyword, StringComparison.OrdinalIgnoreCase)
                || string.Equals(accountingClassification, keyword, StringComparison.OrdinalIgnoreCase));
            if (conflicting is not null)
            {
                throw new InvalidOperationException(
                    $"Profile '{profileId}' resolves to asset class '{resolvedClass}', but the envelope's " +
                    $"classification metadata ('{conflicting}') belongs to '{candidateClass}'. The profile is " +
                    "the identity of a profile-backed record — correct the category/subType/accountingClassification " +
                    "metadata (or the profile reference) so they agree before the write can proceed.");
            }
        }

        assetClass = resolvedClass;
        return true;
    }

    private static string? GetString(JsonElement json, string propertyName)
        => json.TryGetProperty(propertyName, out var value) && value.ValueKind == System.Text.Json.JsonValueKind.String
            ? value.GetString()
            : null;

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
