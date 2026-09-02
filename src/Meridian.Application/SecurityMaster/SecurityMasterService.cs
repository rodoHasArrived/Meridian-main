using System.Text.Json;
using Meridian.Contracts.SecurityMaster;
using Meridian.FSharp.SecurityMasterInterop;
using Meridian.Infrastructure.Adapters.Polygon;
using Meridian.Storage.SecurityMaster;
using Microsoft.Extensions.Logging;

namespace Meridian.Application.SecurityMaster;

public sealed class SecurityMasterService : ISecurityMasterService, ISecurityMasterAmender, IDisposable
{
    /// <summary>
    /// Per-security amendment serialization from the conflict pre-check (which reads per-field
    /// incumbent attribution) through the post-persist attribution write. Without it, amendment C
    /// can commit its projection and pause before its attribution lands, while amendment B reads
    /// the PREVIOUS incumbent's field row and records a conflict pairing the old source with C's
    /// value — a mispairing source-version ordering cannot repair once persisted. In-process only;
    /// the shared-transaction seam tracked as follow-up work is the durable multi-node answer.
    /// Entries are reference-counted and reclaimed on last release, so the pool does not grow
    /// with the security universe.
    /// </summary>
    private static readonly KeyedGatePool<Guid> AmendmentGates = new();

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
        // The stretch from the conflict pre-check to the attribution write serializes per
        // security: the pre-check reads per-field incumbent attribution, and a concurrent
        // amendment must not read incumbents while this one sits between its committed projection
        // and its not-yet-written attribution — it would pair the previous source with this
        // amendment's value in a durably recorded conflict.
        var economic = SecurityEconomicDefinitionAdapter.ToEconomicRecord(projection);
        var amendmentGate = await AmendmentGates.AcquireAsync(request.SecurityId, ct).ConfigureAwait(false);
        try
        {
            await RecordFieldConflictsBeforePersistAsync(currentProjection, projection, ct).ConfigureAwait(false);

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
            await TryRetireStaleFieldResolutionProvenanceAsync(
                changedGovernedFields, request.SecurityId, projection.Version, ct).ConfigureAwait(false);
            await TryRecordCanonicalFieldAttributionAsync(changedGovernedFields, projection, ct).ConfigureAwait(false);
        }
        finally
        {
            amendmentGate.Dispose();
        }
        // Open conflicts reconcile against the DURABLY persisted value only: superseding or
        // refreshing them before AppendAsync's ExpectedVersion check could mutate the governed
        // conflict queue for an amendment the event store then rejects. Best-effort — a failed
        // sweep leaves conflicts Open, where the resolve-time guard reconciles them lazily.
        await TryReconcileOpenFieldConflictsAsync(projection, ct).ConfigureAwait(false);
        // The snapshot and identifier-conflict steps are post-commit too: the event append and
        // projection upsert are durable, so a canceled request token must not surface a canceled
        // amendment (the retry would fail concurrency on the advanced version) nor skip the
        // cache/registry updates below.
        await SaveSnapshotIfNeededAsync(economic, CancellationToken.None).ConfigureAwait(false);
        await TryRecordConflictsAsync(projection, request.SecurityId, CancellationToken.None).ConfigureAwait(false);

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

        // Keep the in-memory projection cache coherent with the durable write, matching the
        // create/amend paths — without this a deactivated security kept reading Active from the
        // warm cache until the next full re-warm.
        _projectionCache?.Upsert(projection);
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
            // Proposed creation stamp. It is applied only when this upsert inserts a new alias; for an
            // edit the store keeps the alias's original created_at/created_by and returns those.
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
            // POST-COMMIT: the event append and projection upsert are already durable, so this
            // best-effort sweep runs on a detached token and absorbs cancellation like the other
            // post-persist steps — a canceled request token must not surface a canceled amendment
            // whose canonical version advanced (the retry would fail concurrency), nor skip the
            // snapshot and identifier-conflict steps that follow.
            await _conflictService.ReconcileOpenFieldConflictsAsync(persisted, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
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
            // Cross-origin precedence follows the projection VERSION the row attributes, not the
            // callback's wall-clock recording time: a delayed low-version canonical write can be
            // recorded after a resolution that validated a higher version, and wall-clock order
            // would resurrect the older incumbent — making that source's next amendment look like
            // same-source versioning and suppressing the real cross-source conflict. Time orders
            // only unversioned (legacy) and tied rows.
            var sources = rows
                .Where(static row => row.Origin is SecurityFieldProvenanceOrigins.ConflictResolution
                    or SecurityFieldProvenanceOrigins.CanonicalWrite)
                .GroupBy(static row => row.FieldPath, StringComparer.Ordinal)
                .ToDictionary(
                    static group => group.Key,
                    static group => group
                        .OrderByDescending(static row => row.SourceVersion is not null)
                        .ThenByDescending(static row => row.SourceVersion ?? long.MinValue)
                        .ThenByDescending(static row => row.RecordedAt)
                        .First().SourceSystem,
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
        long maxSourceVersion,
        CancellationToken ct)
    {
        if (_fieldProvenance is null || changedFieldPaths.Count == 0)
            return;

        foreach (var fieldPath in changedFieldPaths)
        {
            try
            {
                // POST-COMMIT best-effort: detached token + catch-all, matching the other
                // post-persist lineage steps — cancellation must not escape after the canonical
                // write durably advanced. The removal acts on behalf of THIS amendment, so it is
                // bounded by this amendment's projection version: a resolution recorded against a
                // NEWER version (a later amendment committed while this cleanup was delayed) is
                // that version's incumbent evidence and must survive.
                await _fieldProvenance.RemoveAsync(
                    securityId,
                    fieldPath,
                    SecurityFieldProvenanceOrigins.ConflictResolution,
                    clearedAt: DateTimeOffset.UtcNow,
                    maxSourceVersion: maxSourceVersion,
                    ct: CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
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
                    // The canonical event append and projection upsert have already COMMITTED by
                    // the time attribution runs, so this best-effort lineage step uses a detached
                    // token and absorbs cancellation like any other post-persist failure: a
                    // canceled request token must not surface a canceled create/amend whose
                    // canonical version durably advanced — the caller would retry with the
                    // original expected version, fail concurrency, and be unable to repair the
                    // lineage that the invalidation fallback below already handles.
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
                        CancellationToken.None).ConfigureAwait(false);
                    recorded = true;
                }
                catch (Exception ex)
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
                    SecurityFieldProvenanceOrigins.CanonicalWrite, clearedAt,
                    // The invalidation acts on behalf of THIS amendment: attribution a newer
                    // amendment already committed (higher source version) must survive it.
                    maxSourceVersion: projection.Version, ct: CancellationToken.None).ConfigureAwait(false);
                await _fieldProvenance.RemoveAsync(
                    projection.SecurityId, fieldPath,
                    SecurityFieldProvenanceOrigins.ConflictResolution, clearedAt,
                    maxSourceVersion: projection.Version, ct: CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
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

        EnsureDeclaredVocabulariesRoundTripSafely(projection, assetSpecificTermsPatch);
        EnsureBondCouponPayloadRoundTripsSafely(projection, assetSpecificTermsPatch);
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
    /// Same read-tolerance-must-not-become-write-tolerance rule, one level down from the asset class:
    /// a stored DISCRIMINANT value this node does not recognize still deserializes — that is the
    /// point of read tolerance — but it deserializes into something else, and re-serializing it on
    /// amend or deactivate writes that something else back. This walks the vocabularies
    /// <see cref="SecurityAssetTermsSchema"/> declares for the record's asset class rather than
    /// hard-coding one check per field, so a vocabulary added to the table is guarded here the day
    /// it is declared.
    /// <para>Whether an unrecognized value is actually lossy is the schema's
    /// <see cref="SecurityAssetTermField.CarriesUndeclaredValueVerbatim"/>/
    /// <see cref="SecurityAssetTermField.Escape"/> trichotomy:</para>
    /// <list type="bullet">
    /// <item>Carried verbatim — an option's <c>putCall</c> re-serializes intact. Never refused here;
    /// the vocabulary constrains what a write may assert, not what an old row may keep.</item>
    /// <item>An escape — an unknown equity <c>classification</c> becomes <c>Other(raw)</c> and the
    /// label survives as <c>otherClassification</c>, so the round-trip is lossless UNLESS the record
    /// carries the escape's dependent blocks (<c>preferredTerms</c>/<c>convertibleTerms</c>), which
    /// the escape decode has nowhere to reattach and would silently delete.</item>
    /// <item>Neither — an unknown <c>couponType</c> collapses to <c>Fixed</c> and the label is gone
    /// from the row. Always refused.</item>
    /// </list>
    /// <para>A stored token of the wrong JSON KIND sits outside that trichotomy and is refused on
    /// its own terms: the discriminant readers are all <c>GetOptionalString</c>/
    /// <c>GetRequiredString</c>, so a number or a boolean is not decoded badly, it is not decoded at
    /// all — neither the verbatim carry nor the escape can re-emit a string the codec never read.
    /// The one exception is a REQUIRED discriminant, whose reader throws: the record fails loudly on
    /// read rather than being silently rewritten, and nothing here could repair it anyway.</para>
    /// <para>Every refusal here has one governed exit, the same one
    /// <see cref="EnsureCustomAssetEnvelopeRoundTripsSafely"/> instructs: an amendment whose patch
    /// settles the offending field itself — naming a DECLARED value, or explicitly nulling one the
    /// codec clears rather than substitutes (see <see cref="ClearingIsARepairFor"/>). The patch
    /// replaces the kind wholesale (the
    /// F# amend binds <c>Kind = defaultArg command.Kind current.Kind</c>), so the undecodable stored
    /// value is never re-serialized and that amendment is what repairs the record. Without the exit
    /// the refusal is a dead end for the dropped-value fields: "apply the change from a node that
    /// supports this value" is sound advice for an unrecognized ASSET CLASS, where a newer node
    /// plausibly has the deserializer, but an undeclared <c>couponType</c> names no node at all —
    /// it is bad data, so the only node that can fix it is this one, and a row carrying one would
    /// otherwise be permanently unamendable AND undeactivatable.</para>
    /// </summary>
    private static void EnsureDeclaredVocabulariesRoundTripSafely(
        SecurityProjectionRecord projection,
        JsonElement? assetSpecificTermsPatch)
    {
        var terms = projection.AssetSpecificTerms;
        if (terms.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        foreach (var field in SecurityAssetTermsSchema.DiscriminantFields(projection.AssetClass))
        {
            if (!terms.TryGetProperty(field.Key, out var value)
                || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            {
                continue;
            }

            // A token of the wrong JSON KIND is undeclared in the strongest sense. Every
            // discriminant reader is GetOptionalString/GetRequiredString, so a number, a boolean or
            // an object is not a value this codec reads badly — it is one the codec cannot see at
            // all: an OPTIONAL discriminant decodes to its missing-key result and the token is gone
            // on the next write, exactly as an undeclared string would be. Neither exemption below
            // rescues it, because both re-emit a STRING the codec never read. A REQUIRED
            // discriminant is deliberately left alone — GetRequiredString THROWS, so the record
            // fails loudly on read instead of being silently rewritten, and no patch could repair
            // it here anyway: the amendment builds its current record from the STORED terms.
            var unreadableToken = value.ValueKind != JsonValueKind.String;
            if (unreadableToken && field.Required)
            {
                continue;
            }

            // The serializer's discriminants are exact-case, so SecurityAssetTermField.Allows is too.
            if (!unreadableToken && field.Allows(value.GetString()))
            {
                continue;
            }

            // A value the codec carries verbatim re-serializes intact, so the vocabulary constrains
            // what a WRITE may assert (see the mapping and the field-edit validator) without making
            // an odd value already in the row a reason to refuse amending or deactivating it.
            if (!unreadableToken && field.CarriesUndeclaredValueVerbatim)
            {
                continue;
            }

            // The governed exit: this amendment settles the value itself, so the stored one is
            // replaced rather than re-serialized and nothing is lost. Checked before both throws —
            // it is the repair route each of their messages points at.
            if (PatchRepairsValueFor(assetSpecificTermsPatch, field))
            {
                continue;
            }

            var raw = unreadableToken ? value.GetRawText() : value.GetString();
            var clearHint = ClearingIsARepairFor(field)
                ? $", or an explicit null {field.Key} to clear it"
                : string.Empty;
            var repairHint =
                " To repair the record here, amend it with a COMPLETE asset-terms document naming a declared " +
                $"{field.Key} ({string.Join(", ", field.AllowedValues)}, matched case-sensitively){clearHint} — the " +
                "amendment replaces the terms wholesale, so any field the patch omits is dropped with them — and note " +
                "that a deactivation cannot carry a patch, so repair it first.";

            // An escape absorbs an undeclared STRING by re-emitting the raw label under the
            // canonical escape member. An unreadable token gives it nothing to absorb — the codec
            // read no string at all — so it falls through to the unconditional refusal.
            SecurityAssetTermVocabularyEscape? escape = unreadableToken ? null : field.Escape;
            if (escape is null)
            {
                throw new InvalidOperationException(
                    $"Security '{projection.SecurityId:D}' has {field.Key} '{raw}', which this node does not recognize and " +
                    $"cannot carry — the declared values are {string.Join(", ", field.AllowedValues)}. Re-serializing it here " +
                    "would rewrite it as one of those and drop the terms that depend on it, so the write is refused." +
                    repairHint);
            }

            var dependentKeys = escape.DependentKeys
                .Where(key => terms.TryGetProperty(key, out var dependent)
                    && dependent.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
                .ToArray();
            if (dependentKeys.Length > 0)
            {
                throw new InvalidOperationException(
                    $"Security '{projection.SecurityId:D}' has {field.Key} '{raw}', which this node does not recognize, and " +
                    $"it carries {string.Join(" and ", dependentKeys)} tied to that value. Re-serializing it here would " +
                    $"degrade {field.Key} to '{escape.Value}' and drop those blocks, so the write is refused. Apply the " +
                    "change from a node that supports this value." + repairHint);
            }
        }
    }

    /// <summary>
    /// The vocabulary walk inspects the FLAT discriminant keys the canonical serializer writes. A
    /// bond carrying the legacy nested <c>coupon</c> object instead has no flat <c>couponType</c>
    /// for that walk to see, yet it is lossy in exactly the same way: <c>ToBondTerms</c> has no
    /// nested-coupon fallback, so the record reads as <c>Fixed(couponRate ?? 0)</c> and
    /// re-serializing flattens it to <c>couponType: "Fixed"</c>, dropping the nested index, spread,
    /// and day count. Nothing in this repo writes that shape, but the projection store reads it
    /// deliberately — <c>PostgresSecurityMasterStore.TryBuildBondProjection</c> falls back to
    /// <c>coupon.kind</c>/<c>rate</c>/<c>index</c> "for externally-authored payloads that still use
    /// it", with a regression test pinning the behaviour — so such rows are expected to exist.
    /// <para>The same hole is open one step wider, and a present <c>couponType</c> does not close
    /// it: the codec reads the flat companions ONE ARM AT A TIME, so a document with no
    /// <c>couponType</c> and a populated <c>floatingIndex</c> orphans it on the Fixed default, and
    /// a document declaring <c>couponType: "Fixed"</c> beside that same <c>floatingIndex</c> orphans
    /// it just as completely — the Fixed arm never reads it and the serializer writes it back null.
    /// The projection store reads those columns independently of the discriminant, so the value is
    /// visible to operators right up until the amendment that deletes it. All of these are one
    /// defect — coupon structure the selected arm does not read — so all of them are refused here.</para>
    /// <para>What keeps this free of false positives is the serializer's shape rather than any
    /// gate: <c>Interop.SecurityMaster.assetSpecificTermsJson</c> emits the fields of the arm it is
    /// writing and <see langword="null"/>/<c>[]</c> for every other arm's, so a canonically written
    /// row only ever populates keys its own arm reads and none of them can trip this.</para>
    /// </summary>
    private static void EnsureBondCouponPayloadRoundTripsSafely(
        SecurityProjectionRecord projection,
        JsonElement? assetSpecificTermsPatch)
    {
        var terms = projection.AssetSpecificTerms;
        if (!string.Equals(projection.AssetClass, "Bond", StringComparison.OrdinalIgnoreCase)
            || terms.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        var couponType = SecurityAssetTermsSchema.Field("Bond", "couponType");
        var declared = string.Join(", ", couponType?.AllowedValues ?? Array.Empty<string>());

        // The SUBMITTED document is held to the same standard as the stored one, and INDEPENDENTLY
        // of it. A patch replaces the terms wholesale, so coupon structure its own discriminant does
        // not name is dropped on the way in — the operator's spreadBps never reaches the record.
        // Checking this only while the stored terms were themselves orphaned left the hole open on
        // every other path, the repair route this guard advertises included: a record refused for an
        // undeclared couponType would be "repaired" by a patch that quietly lost its nested spread.
        // The loss is not self-correcting either — by the time the next amendment refuses the new
        // record the value is already gone.
        if (assetSpecificTermsPatch is JsonElement patch && patch.ValueKind == JsonValueKind.Object)
        {
            var submitted = OrphanedCouponStructure(patch);
            if (submitted.Length > 0)
            {
                throw new InvalidOperationException(
                    $"The amendment for security '{projection.SecurityId:D}' submits a bond document whose " +
                    $"{string.Join(" and ", submitted)} the canonical codec does not read for the coupon type the " +
                    "document declares, so the amendment would persist the record without them. The write is " +
                    $"refused. Re-submit it with a declared couponType ({declared}) naming the structure and its " +
                    "values in the flat keys the codec reads.");
            }
        }

        var orphaned = OrphanedCouponStructure(terms);
        if (orphaned.Length == 0)
        {
            return;
        }

        // The governed exit, with the repair document already validated above: naming a declared
        // couponType is not enough on its own, because the submitted document can carry an orphan of
        // its own and the amendment would persist it having dropped that value first.
        if (couponType is not null && PatchRepairsValueFor(assetSpecificTermsPatch, couponType))
        {
            return;
        }

        throw new InvalidOperationException(
            $"Security '{projection.SecurityId:D}' is a bond carrying {string.Join(" and ", orphaned)} that the " +
            "canonical codec does not read for the coupon structure the record resolves to, so re-serializing it " +
            "here would drop that structure. The write is refused. To repair the record here, amend it with a " +
            $"COMPLETE asset-terms document naming a declared couponType ({declared}) and carrying those values in " +
            "the flat keys — the amendment replaces the terms wholesale, so any field the patch omits is dropped " +
            "with them — and note that a deactivation cannot carry a patch, so repair it first.");
    }

    /// <summary>
    /// Every flat key that belongs to a coupon STRUCTURE rather than to the bond, in the order a
    /// refusal names them. Which of them survives a round trip is not a property of the key but of
    /// the arm <c>ToBondTerms</c> selects — see <see cref="CouponArmReads"/> — so the whole set is
    /// checked and the arm decides.
    /// </summary>
    private static readonly string[] FlatCouponStructureKeys =
    [
        "couponRate", "floatingIndex", "spreadBps", "capRate", "floorRate",
        "stepSchedule", "inflationIndex", "inflationBaseIndexValue", "inflationIndexRatio", "dayCount"
    ];

    /// <summary>
    /// The arm <c>ToBondTerms</c> selects for this document. Anything the codec cannot read as a
    /// discriminant — absent, null, a case variant, a non-string token — lands on the same Fixed
    /// default the reader applies (<c>GetOptionalString(json, "couponType") ?? "Fixed"</c>), and so
    /// does an undeclared string, which the read-tolerant arm also decodes as Fixed.
    /// </summary>
    private static string SelectedCouponArm(JsonElement terms)
        => CodecCanRead(terms, "couponType") && terms.TryGetProperty("couponType", out var couponType)
            ? couponType.GetString() ?? "Fixed"
            : "Fixed";

    /// <summary>
    /// True when <paramref name="arm"/> reads <paramref name="key"/>, mirroring the arms of
    /// <c>ToBondTerms</c> exactly. A key the selected arm does not read is not merely ignored: the
    /// F# serializer emits its OWN arm's fields and nulls the rest
    /// (<c>Interop.SecurityMaster.assetSpecificTermsJson</c>), so re-serializing deletes it — a
    /// <c>floatingIndex</c> beside <c>couponType: "Fixed"</c> is as gone as one with no discriminant
    /// at all, even though the projection store reads and shows it.
    /// <para>That same emit-per-arm shape is what keeps this free of false positives: a canonically
    /// written row only ever populates the keys its own arm reads, so no canonical bond can trip it.</para>
    /// </summary>
    private static bool CouponArmReads(string arm, string key) => arm switch
    {
        "Floating" => key is "floatingIndex" or "spreadBps" or "capRate" or "floorRate" or "dayCount",
        "ZeroCoupon" => false,
        "Step" => key is "stepSchedule" or "dayCount",
        "InflationLinked" =>
            key is "couponRate" or "inflationIndex" or "inflationBaseIndexValue" or "inflationIndexRatio" or "dayCount",
        // "Fixed", and the read-tolerant fallback every unreadable or undeclared discriminant lands on.
        _ => key is "couponRate" or "dayCount",
    };

    /// <summary>
    /// The legacy nested <c>coupon</c> members and the flat key each corresponds to, mirroring the
    /// per-field fallback in <c>PostgresSecurityMasterStore.TryBuildBondProjection</c>. That
    /// per-field shape is why the nested object cannot be judged present-or-absent: a row can carry
    /// its discriminant flat and one value only nested.
    /// </summary>
    private static readonly (string Nested, string Flat)[] NestedCouponMembers =
    [
        ("kind", "couponType"), ("rate", "couponRate"), ("index", "floatingIndex"),
        ("spreadBps", "spreadBps"), ("dayCountConvention", "dayCount")
    ];

    /// <summary>
    /// The coupon structure this record carries that the canonical codec would drop on
    /// re-serialization, named for the refusal message. Two independent sources:
    /// <list type="bullet">
    /// <item>flat companions the arm the codec selects does not read, or reads and cannot decode —
    /// see <see cref="OrphansFlatCompanion"/>. The arm is the whole question: the same
    /// <c>spreadBps</c> is economics under <c>couponType: "Floating"</c> and a deleted value under
    /// <c>"Fixed"</c>.</item>
    /// <item>nested <c>coupon</c> members whose flat counterpart is missing — the codec reads only
    /// the flat key, so the nested value is invisible to it and gone on the next write. Judged per
    /// member rather than by the object's presence, so a vestigial duplicate of the flat payload
    /// (and an empty <c>coupon: {}</c>) is correctly ignored, while a mixed row that keeps one value
    /// only nested is correctly refused.</item>
    /// </list>
    /// Only a POPULATED value counts in either case: the canonical serializer emits the companions
    /// its arm does not own as <see langword="null"/>/<c>[]</c>, so a presence-only test would
    /// refuse every ordinary bond.
    /// </summary>
    private static string[] OrphanedCouponStructure(JsonElement terms)
    {
        var orphaned = new List<string>();
        var arm = SelectedCouponArm(terms);

        orphaned.AddRange(FlatCouponStructureKeys.Where(key => OrphansFlatCompanion(terms, arm, key)));

        if (!CodecCanRead(terms, "couponType"))
        {
            // A case-variant spelling is not the discriminant: every read of it is ordinal
            // (JsonElement.TryGetProperty, and the codec's own GetOptionalString), so the codec
            // cannot see it, the record still loads as a fixed coupon, and re-serializing writes a
            // fresh document without the stray key — the value is gone with no trace. Stored
            // documents really do carry case-variant keys; ApprovedFieldEditCanonicalMergeHandler
            // .RemoveKeyVariants exists to strip them, matching OrdinalIgnoreCase. Only VARIANT
            // spellings are named here — the exact key, present but undecodable, is the declared
            // vocabulary's business and EnsureDeclaredVocabulariesRoundTripSafely refuses it there.
            orphaned.AddRange(terms
                .EnumerateObject()
                .Where(property => !string.Equals(property.Name, "couponType", StringComparison.Ordinal)
                    && string.Equals(property.Name, "couponType", StringComparison.OrdinalIgnoreCase)
                    && CarriesValue(terms, property.Name))
                .Select(property => property.Name));
        }

        if (terms.TryGetProperty("coupon", out var nested) && nested.ValueKind == JsonValueKind.Object)
        {
            // The flat counterpart only REPRESENTS the nested value if the codec can decode it.
            // CarriesValue is the wrong question here: the projection store's readers are
            // type-aware and fall through to the nested value when the flat one does not parse
            // (spreadBps: "N/A"), so treating any populated flat value as a representation would
            // discard exactly the nested value the store is reading.
            orphaned.AddRange(NestedCouponMembers
                .Where(member => CarriesValue(nested, member.Nested)
                    && !CodecCanRead(terms, member.Flat)
                    && !MatchesTheMissingKeyDefault(nested, member))
                .Select(member => $"coupon.{member.Nested}"));
        }

        return orphaned.ToArray();
    }

    /// <summary>
    /// True when <paramref name="key"/> holds coupon structure this document would lose. Both halves
    /// of the question matter: <see cref="CouponArmReads"/> decides whether the selected arm reads
    /// the key at all, and <see cref="CodecCanRead"/> decides whether reading it yields anything —
    /// <c>spreadBps: "N/A"</c> IS read by the Floating arm and still lands as null.
    /// <para>The one populated value a non-reading arm may drop safely is a numeric zero, because
    /// zero is exactly what the codec substitutes for an absent scalar
    /// (<c>GetOptionalDecimal(json, "couponRate") ?? 0m</c>). A vendor payload spelling "no fixed
    /// rate" as <c>couponRate: 0</c> beside a floating coupon states nothing the structure has not
    /// already said, so refusing it would freeze an ordinary row to protect a zero — while 4.25 in
    /// that same slot is a number the record states and the write would delete.</para>
    /// </summary>
    private static bool OrphansFlatCompanion(JsonElement terms, string arm, string key)
    {
        if (!CarriesValue(terms, key))
        {
            return false;
        }

        return CouponArmReads(arm, key)
            ? !CodecCanRead(terms, key)
            : !IsNumericZero(terms, key);
    }

    /// <summary>True when <paramref name="key"/> holds a JSON number the codec would decode as zero.</summary>
    private static bool IsNumericZero(JsonElement document, string key)
        => document.TryGetProperty(key, out var value)
            && value.ValueKind == JsonValueKind.Number
            && value.TryGetDecimal(out var number)
            && number == 0m;

    /// <summary>
    /// True when the nested member says exactly what the codec's documented default for the absent
    /// flat key already produces, so re-serializing canonicalizes the JSON shape and loses nothing.
    /// <para>Only <c>coupon.kind = "Fixed"</c> qualifies, and it qualifies through
    /// <see cref="MissingKeyDefaultFor"/> rather than a spelling of its own: <c>ToBondTerms</c>
    /// defaults an absent <c>couponType</c> to <c>Fixed</c> and the projection store reads the
    /// nested kind as <c>Fixed</c>, so both codecs already agree and refusing would freeze a valid
    /// legacy record to protect nothing. Any other nested kind names a structure the default does
    /// not produce, and the remaining members have no substituted counterpart at all — an absent
    /// <c>couponRate</c> reads as <c>0</c>, which is a different number, not the same one spelled
    /// differently.</para>
    /// </summary>
    private static bool MatchesTheMissingKeyDefault(JsonElement nested, (string Nested, string Flat) member)
        => SecurityAssetTermsSchema.Field("Bond", member.Flat) is { } field
            && MissingKeyDefaultFor(field) is { } substituted
            && nested.TryGetProperty(member.Nested, out var value)
            && value.ValueKind == JsonValueKind.String
            && string.Equals(value.GetString(), substituted, StringComparison.Ordinal);

    /// <summary>
    /// True when the canonical codec can actually DECODE <paramref name="key"/> — present,
    /// populated, AND of the JSON kind its declared schema type requires. Presence is not the same
    /// question as readability, and conflating them loses data twice over: <c>couponType: null</c>
    /// is a present property that <c>ToBondTerms</c> treats as the missing-value default, and
    /// <c>spreadBps: "N/A"</c> is a populated value that <c>GetOptionalDecimal</c> cannot parse.
    /// Both are invisible to the codec, so neither can stand in for the value it would drop.
    /// </summary>
    private static bool CodecCanRead(JsonElement document, string key)
    {
        if (!CarriesValue(document, key) || !document.TryGetProperty(key, out var value))
        {
            return false;
        }

        // Each arm mirrors the corresponding reader in SecurityMasterMapping, including its PARSE
        // and not merely its JSON kind. The kind alone is a proxy, and the proxy is wrong in
        // exactly the cases that matter here: 1e100 is a JSON number that TryGetDecimal rejects,
        // and "not-a-date" is a JSON string that DateOnly.TryParse rejects. The projection store's
        // readers parse too, so it falls through to the nested value while a kind-only check would
        // call the flat one a representation and let the nested one be discarded.
        return SecurityAssetTermsSchema.Field("Bond", key)?.Type switch
        {
            SecurityAssetTermFieldType.Decimal =>
                value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out _),
            SecurityAssetTermFieldType.Integer =>
                value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out _),
            SecurityAssetTermFieldType.Boolean => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
            SecurityAssetTermFieldType.Date =>
                value.ValueKind == JsonValueKind.String && DateOnly.TryParse(value.GetString(), out _),
            SecurityAssetTermFieldType.Guid =>
                value.ValueKind == JsonValueKind.String && Guid.TryParse(value.GetString(), out _),
            SecurityAssetTermFieldType.Array => value.ValueKind == JsonValueKind.Array,
            SecurityAssetTermFieldType.Object => value.ValueKind == JsonValueKind.Object,
            // String, and any undeclared key, decode straight from a non-blank JSON string.
            _ => value.ValueKind == JsonValueKind.String,
        };
    }

    /// <summary>True when <paramref name="key"/> holds a value, not null, blank, or an empty array.</summary>
    private static bool CarriesValue(JsonElement document, string key)
        => document.TryGetProperty(key, out var value)
            && value.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined)
            && (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() > 0)
            && (value.ValueKind != JsonValueKind.String || !string.IsNullOrWhiteSpace(value.GetString()));

    /// <summary>
    /// True when the amendment's patch settles <paramref name="field"/> itself, so the stored value
    /// is replaced rather than re-serialized. Two forms qualify, and both are POSITIVE instructions:
    /// <list type="bullet">
    /// <item>naming a declared value — the ordinary repair;</item>
    /// <item>an explicit <see langword="null"/>, where <see cref="ClearingIsARepairFor"/> says the
    /// codec decodes an absent key as a genuine absence. Clearing is the honest repair for a value
    /// this node has no declared equivalent for (an <c>exerciseStyle</c> of <c>"Asian"</c>): the
    /// write mapper reads null as None and the serializer emits null, so the amendment persists
    /// exactly what the operator asked for. Refusing it would force them to assert a style the
    /// option does not have, which corrupts the record more than the value it replaces.</item>
    /// </list>
    /// A patch that OMITS the key, blanks it, or repeats an undeclared value is deliberately not an
    /// exit: the write-mode mapping would decode it to the same fallback and complete the very
    /// rewrite the refusal exists to stop.
    /// </summary>
    private static bool PatchRepairsValueFor(JsonElement? assetSpecificTermsPatch, SecurityAssetTermField field)
    {
        if (assetSpecificTermsPatch is not JsonElement patch
            || patch.ValueKind != JsonValueKind.Object
            || !patch.TryGetProperty(field.Key, out var patched))
        {
            return false;
        }

        if (patched.ValueKind == JsonValueKind.String)
        {
            return field.Allows(patched.GetString());
        }

        // A cleared discriminant cannot hold the blocks that only its IN-vocabulary cases own, so a
        // patch still carrying them would lose them on the way in — the same defect the escape's
        // dependent-key check refuses on the stored side.
        return patched.ValueKind == JsonValueKind.Null
            && ClearingIsARepairFor(field)
            && !CarriesEscapeDependentKeys(patch, field);
    }

    /// <summary>
    /// True when an absent or null <paramref name="field"/> round-trips as a genuine absence, so
    /// clearing it is lossless rather than a silent re-typing.
    /// <para>The distinction is the codec's, not the schema's: an optional discriminant whose
    /// missing-key read is <c>None</c> (<c>ParseExerciseStyle</c>, <c>ToEquityClassificationOption</c>)
    /// serializes back as null, while one the codec SUBSTITUTES a declared value for is not cleared
    /// at all — <c>ToBondTerms</c> reads a null <c>couponType</c> as <c>"Fixed"</c>, which is
    /// precisely the rewrite these guards exist to stop. A required field is never clearable: its
    /// reader throws.</para>
    /// </summary>
    private static bool ClearingIsARepairFor(SecurityAssetTermField field)
        => !field.Required && MissingKeyDefaultFor(field) is null;

    /// <summary>
    /// The declared value the canonical codec SUBSTITUTES when a discriminant key is absent or null,
    /// for the one field that substitutes anything: <c>ToBondTerms</c> reads
    /// <c>GetOptionalString(json, "couponType") ?? "Fixed"</c>. This mirrors the codec rather than
    /// the schema — the schema declares what a value may BE, not what the reader invents when the
    /// key is gone — so it lives beside the guards that have to reason about the missing-key read.
    /// </summary>
    private static string? MissingKeyDefaultFor(SecurityAssetTermField field)
        => string.Equals(field.Key, "couponType", StringComparison.Ordinal) ? "Fixed" : null;

    /// <summary>
    /// True when <paramref name="document"/> carries any of the blocks that hang off
    /// <paramref name="field"/>'s in-vocabulary cases (an equity's <c>preferredTerms</c> under
    /// <c>classification</c>), which a decode that does not land on those cases has nowhere to
    /// reattach.
    /// </summary>
    private static bool CarriesEscapeDependentKeys(JsonElement document, SecurityAssetTermField field)
        => field.Escape is { } escape
            && escape.DependentKeys.Any(key => document.TryGetProperty(key, out var dependent)
                && dependent.ValueKind is JsonValueKind.Object or JsonValueKind.Array);

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
        // The store retains the original created_at/created_by when the alias already exists, so the
        // persisted row is authoritative: returning the locally stamped `alias` would report a
        // corrected identifier as created at the moment of the correction. Fall back to the proposed
        // value only when the store cannot read the row back.
        var persisted = await _store.UpsertAliasAsync(alias, ct).ConfigureAwait(false);
        return persisted ?? alias;
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
