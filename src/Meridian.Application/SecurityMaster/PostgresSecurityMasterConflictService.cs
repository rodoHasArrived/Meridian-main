using System.Data.Common;
using System.Globalization;
using System.Text.Json;
using Meridian.Contracts.SecurityMaster;
using Meridian.Storage.SecurityMaster;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Meridian.Application.SecurityMaster;

/// <summary>
/// Durable, Postgres-backed golden-record conflict store. Conflict detection reuses the shared
/// <see cref="SecurityMasterConflictDetection"/> logic; detected conflicts and their resolution state
/// are persisted so a resolution and its chosen winner survive process recycles and are visible across
/// every instance — the audit guarantee the in-memory store could not provide.
/// </summary>
public sealed class PostgresSecurityMasterConflictService : ISecurityMasterConflictService
{
    private const string ConflictsTable = "security_master_conflicts";
    private const long IdentifierConflictAdvisoryLock = 0x534D_4944_4346;

    private readonly ISecurityMasterStore _store;
    private readonly SecurityMasterOptions _options;
    private readonly ILogger<PostgresSecurityMasterConflictService> _logger;

    public PostgresSecurityMasterConflictService(
        ISecurityMasterStore store,
        SecurityMasterOptions options,
        ILogger<PostgresSecurityMasterConflictService> logger,
        Meridian.ReferenceData.SecurityMaster.ISecurityAssetProfileCatalog? assetProfileCatalog = null)
    {
        _store = store;
        _options = options;
        _logger = logger;
        _assetProfileCatalog = assetProfileCatalog;
    }

    private readonly Meridian.ReferenceData.SecurityMaster.ISecurityAssetProfileCatalog? _assetProfileCatalog;

    public async Task<IReadOnlyList<SecurityMasterConflict>> GetOpenConflictsAsync(CancellationToken ct)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);
        await AcquireIdentifierConflictLockAsync(connection, transaction, ct).ConfigureAwait(false);

        // Load only after taking the conflict-reconciliation lock. An ingest that has persisted a
        // projection but not its conflict row is allowed to finish before this snapshot; an ingest
        // that starts later blocks here and cannot be incorrectly superseded by a stale refresh.
        var all = await _store.LoadAllAsync(ct).ConfigureAwait(false);
        var detected = SecurityMasterConflictDetection.DetectAll(all, DateTimeOffset.UtcNow);

        foreach (var conflict in detected)
        {
            await ReconcileLegacyIdentifierConflictIdAsync(
                connection,
                transaction,
                conflict,
                FindLegacyIdentifierConflictIds(conflict, all),
                ct).ConfigureAwait(false);
            await UpsertDetectedIdentifierConflictAsync(connection, transaction, conflict, ct).ConfigureAwait(false);
        }

        if (detected.Count > 0)
        {
            _logger.LogInformation("Detected {Count} identifier conflicts in Security Master", detected.Count);
        }

        // A full refresh is authoritative for identifier ambiguity. Close only still-open
        // detector-owned rows that disappeared because the claim windows no longer overlap;
        // operator resolutions and field conflicts are never rewritten here.
        await using (var supersede = connection.CreateCommand())
        {
            supersede.Transaction = transaction;
            supersede.CommandText =
                $"""
                update {Qualified(ConflictsTable)}
                set status = 'Superseded',
                    resolved_reason = @resolved_reason,
                    resolved_at = @resolved_at
                where status = 'Open'
                  and conflict_kind = @conflict_kind
                  and not (conflict_id = any(@detected_ids));
                """;
            supersede.Parameters.AddWithValue(
                "resolved_reason",
                SecurityMasterConflictService.IdentifierNoLongerDetectedReason);
            supersede.Parameters.AddWithValue("resolved_at", DateTimeOffset.UtcNow.UtcDateTime);
            supersede.Parameters.AddWithValue("conflict_kind", SecurityMasterConflictKinds.IdentifierAmbiguity);
            supersede.Parameters.AddWithValue(
                "detected_ids",
                NpgsqlTypes.NpgsqlDbType.Array | NpgsqlTypes.NpgsqlDbType.Uuid,
                detected.Select(static conflict => conflict.ConflictId).ToArray());
            await supersede.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"""
            select {ConflictColumns}
            from {Qualified(ConflictsTable)}
            where status = 'Open'
            order by detected_at;
            """;

        var results = new List<SecurityMasterConflict>();
        await using (var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                results.Add(MapConflict(reader));
            }
        }

        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return results;
    }

    public async Task<SecurityMasterConflict?> GetConflictAsync(Guid conflictId, CancellationToken ct)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            select {ConflictColumns}
            from {Qualified(ConflictsTable)}
            where conflict_id = @conflict_id;
            """;
        command.Parameters.AddWithValue("conflict_id", conflictId);

        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        return await reader.ReadAsync(ct).ConfigureAwait(false) ? MapConflict(reader) : null;
    }

    public async Task<SecurityMasterConflict?> ResolveAsync(ResolveConflictRequest request, CancellationToken ct)
    {
        var newStatus = "Dismiss".Equals(request.Resolution, StringComparison.OrdinalIgnoreCase)
            ? "Dismissed"
            : "Resolved";

        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        // The close and the winner's truthful field-provenance write-back commit together. The
        // current persisted value is locked and checked before any field conflict can close.
        await using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);

        SecurityMasterConflict? openConflict;
        await using (var select = connection.CreateCommand())
        {
            select.Transaction = transaction;
            select.CommandText =
                $"""
                select {ConflictColumns}
                from {Qualified(ConflictsTable)}
                where conflict_id = @conflict_id and status = 'Open'
                for update;
                """;
            select.Parameters.AddWithValue("conflict_id", request.ConflictId);
            await using var reader = await select.ExecuteReaderAsync(ct).ConfigureAwait(false);
            openConflict = await reader.ReadAsync(ct).ConfigureAwait(false) ? MapConflict(reader) : null;
        }

        if (openConflict is null)
        {
            return null;
        }

        var fieldConflict = IsFieldLevelConflict(openConflict.ConflictKind);
        long? persistedVersion = null;
        var resolvingField = newStatus == "Resolved" && fieldConflict;
        var selectedSource = resolvingField
            ? ResolveSelectedSource(openConflict, request.ChosenWinnerSource, request.Resolution)
            : request.ChosenWinnerSource?.Trim();
        var selectedValue = resolvingField
            ? ResolveSelectedValue(openConflict, selectedSource!)
            : string.Empty;
        if (fieldConflict)
        {
            // DISMISSALS revalidate the persisted value too, not only resolutions: a dismissal
            // asserts the recorded candidates are equivalent, which is only meaningful while the
            // persisted value still matches one of them. If an amendment slipped in between the
            // workbench's version check and this transaction's conflict lock, the persisted value
            // may match NEITHER candidate — closing the stale assessment as Dismissed would erase
            // a live disagreement that post-write reconciliation then skips (it ignores closed
            // rows). The obsolete handling below refreshes a self-revising candidate or
            // supersedes a third-party replacement instead.
            var (persistedValue, recordSourceSystem, lockedVersion, declaredFieldType) = await ReadPersistedFieldValueAsync(
                connection,
                transaction,
                openConflict,
                ct).ConfigureAwait(false);
            persistedVersion = lockedVersion;
            var persistedContradictsDecision = resolvingField
                ? !FieldValuesMatch(openConflict.FieldPath, persistedValue, selectedValue, declaredFieldType)
                : SecurityMasterConflictDetection.FieldConflictIsObsolete(openConflict, persistedValue, declaredFieldType);
            if (persistedContradictsDecision)
            {
                // When the persisted value matches NEITHER candidate, a later canonical write has
                // replaced both sources' asserted values and this guard would reject either
                // choice, turning the queue row into a dead end. WHO wrote that value decides the
                // outcome: the field's provenance-attributed source when recorded, else the
                // record-level source of the persisted row. A CANDIDATE author is revising its own
                // value — the disagreement is still live, so its candidate refreshes and the
                // conflict stays open; a third-party author replaced both candidates, so the
                // conflict closes as Superseded in the same governed transaction.
                if (SecurityMasterConflictDetection.FieldConflictIsObsolete(openConflict, persistedValue, declaredFieldType))
                {
                    var fieldSources = await LoadConflictResolutionFieldSourcesAsync(
                        connection, openConflict.SecurityId, ct).ConfigureAwait(false);
                    var authoringSource = fieldSources.TryGetValue(openConflict.FieldPath, out var attributed)
                        ? attributed
                        : recordSourceSystem;
                    if (SecurityMasterConflictDetection.TryMatchCandidateProvider(openConflict, authoringSource, out var revisesProviderA))
                    {
                        await RefreshConflictCandidateValueAsync(
                            connection, transaction, openConflict, revisesProviderA, persistedValue!, ct).ConfigureAwait(false);
                        await transaction.CommitAsync(ct).ConfigureAwait(false);
                        throw new InvalidOperationException(
                            $"Conflict '{openConflict.ConflictId:D}' cannot resolve yet: source '{authoringSource}' has " +
                            $"revised its value for '{openConflict.FieldPath}' since the conflict was recorded. Its " +
                            "candidate has been refreshed to the persisted value — review the updated disagreement and " +
                            "retry the resolution.");
                    }

                    await SupersedeConflictAsync(
                        connection, transaction, openConflict,
                        BuildReplacedBothCandidatesReason(openConflict, persistedValue), ct).ConfigureAwait(false);
                    await transaction.CommitAsync(ct).ConfigureAwait(false);
                    throw new InvalidOperationException(
                        $"Conflict '{openConflict.ConflictId:D}' was superseded: a later canonical write persisted a value " +
                        $"for '{openConflict.FieldPath}' that matches neither recorded candidate, so no resolution to " +
                        "either source can apply. The conflict has been closed as Superseded; no operator action is required.");
                }

                throw new InvalidOperationException(
                    $"Conflict '{openConflict.ConflictId:D}' cannot resolve to source '{selectedSource}' because " +
                    $"the persisted value for '{openConflict.FieldPath}' does not match that source's selected value. " +
                    "Apply the selected value through a governed amendment, then retry the resolution.");
            }
        }

        SecurityMasterConflict updated;
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;

            // Atomic compare-and-set: only an Open conflict transitions, and the winner, resolver, and
            // reason are captured in the SAME write as the close. A concurrent resolver that lost the race
            // matches zero rows and observes null instead of overwriting the first operator's decision.
            command.CommandText =
                $"""
                update {Qualified(ConflictsTable)}
                set status = @status,
                    resolved_winner_source = @resolved_winner_source,
                    resolved_by = @resolved_by,
                    resolved_reason = @resolved_reason,
                    resolved_at = @resolved_at
                where conflict_id = @conflict_id and status = 'Open'
                returning {ConflictColumns};
                """;
            command.Parameters.AddWithValue("status", newStatus);
            // Dismissals persist the acknowledged source too when one was selected: the workbench's
            // DismissAsEquivalent flow requires a candidate and reports it as ChosenWinnerSource, so
            // storing NULL here would expose the same decision WITH a winner through the workbench
            // response but WITHOUT one through the authoritative conflict row. Field provenance is
            // still only written for Resolved — a dismissal asserts equivalence, not attribution.
            command.Parameters.AddWithValue(
                "resolved_winner_source",
                newStatus == "Resolved"
                    ? (object?)selectedSource ?? DBNull.Value
                    : (object?)(string.IsNullOrWhiteSpace(request.ChosenWinnerSource) ? null : request.ChosenWinnerSource.Trim())
                        ?? DBNull.Value);
            command.Parameters.AddWithValue("resolved_by", request.ResolvedBy);
            command.Parameters.AddWithValue("resolved_reason", (object?)request.Reason ?? DBNull.Value);
            command.Parameters.AddWithValue("resolved_at", DateTimeOffset.UtcNow.UtcDateTime);
            command.Parameters.AddWithValue("conflict_id", request.ConflictId);

            await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
            if (!await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                return null;
            }

            updated = MapConflict(reader);
        }

        if (updated.Status == "Resolved"
            && !string.IsNullOrWhiteSpace(updated.ResolvedWinnerSource)
            && IsFieldLevelConflict(updated.ConflictKind))
        {
            var resolutionTime = updated.ResolvedAt
                ?? throw new InvalidOperationException("A resolved conflict must retain its resolution timestamp.");
            await PostgresSecurityFieldProvenanceSql.UpsertAsync(
                connection,
                transaction,
                _options.Schema,
                new SecurityFieldProvenanceRecord(
                    updated.SecurityId,
                    updated.FieldPath,
                    updated.ResolvedWinnerSource,
                    // AsOf is WHEN THE SOURCE ASSERTED the value. The conflict row does not retain
                    // each candidate's source as-of, so it is unknown here — and per the
                    // SecurityFieldProvenance contract an unknown as-of is null, never fabricated.
                    // Writing the resolution time would misdate January vendor data resolved in
                    // August as an August assertion. RecordedAt carries the resolution time.
                    AsOf: null,
                    UpdatedBy: updated.ResolvedBy,
                    Confidence: null,
                    Origin: SecurityFieldProvenanceOrigins.ConflictResolution,
                    OriginReference: updated.ConflictId.ToString("D"),
                    RecordedAt: resolutionTime,
                    // The row is ordered by the projection version the resolution validated
                    // against: the persisted-value check above ran on the securities row locked at
                    // this version in this same transaction. Without it (null), the version-bounded
                    // invalidation an OLDER amendment's retry issues would treat the row as
                    // always-eligible and delete a resolution recorded against a newer version.
                    SourceVersion: persistedVersion),
                ct).ConfigureAwait(false);
        }

        await transaction.CommitAsync(ct).ConfigureAwait(false);

        // ResolvedBy is request-supplied text; flatten line endings so it cannot forge log entries.
        _logger.LogInformation(
            "Conflict {ConflictId} for security {SecurityId} {Status} by {ResolvedBy}",
            updated.ConflictId, updated.SecurityId, newStatus, request.ResolvedBy?.ReplaceLineEndings(" "));
        return updated;
    }

    /// <summary>
    /// Field-level conflict kinds carry a real term path whose winning source is meaningful field
    /// provenance. Identifier-ambiguity conflicts resolve ownership between two securities, not a
    /// field's source, so they do not write field lineage.
    /// </summary>
    private static bool IsFieldLevelConflict(string conflictKind)
        => string.Equals(conflictKind, SecurityMasterConflictKinds.EconomicTermMismatch, StringComparison.Ordinal)
           || string.Equals(conflictKind, SecurityMasterConflictKinds.CommonTermMismatch, StringComparison.Ordinal);

    private static string ResolveSelectedSource(
        SecurityMasterConflict conflict,
        string? requestedSource,
        string resolution)
    {
        if (string.IsNullOrWhiteSpace(requestedSource))
        {
            if (string.Equals(resolution, "AcceptA", StringComparison.OrdinalIgnoreCase))
            {
                return conflict.ProviderA;
            }

            if (string.Equals(resolution, "AcceptB", StringComparison.OrdinalIgnoreCase))
            {
                return conflict.ProviderB;
            }

            throw new InvalidOperationException(
                $"Field conflict '{conflict.ConflictId:D}' requires a selected candidate source.");
        }

        if (string.Equals(requestedSource.Trim(), conflict.ProviderA.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return conflict.ProviderA;
        }

        if (string.Equals(requestedSource.Trim(), conflict.ProviderB.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return conflict.ProviderB;
        }

        return requestedSource.Trim();
    }

    private static string ResolveSelectedValue(SecurityMasterConflict conflict, string selectedSource)
    {
        if (string.Equals(selectedSource, conflict.ProviderA, StringComparison.OrdinalIgnoreCase))
        {
            return conflict.ValueA;
        }

        if (string.Equals(selectedSource, conflict.ProviderB, StringComparison.OrdinalIgnoreCase))
        {
            return conflict.ValueB;
        }

        throw new InvalidOperationException(
            $"Conflict '{conflict.ConflictId:D}' can only resolve to '{conflict.ProviderA}' or '{conflict.ProviderB}'.");
    }

    private async Task<(string? Value,
        string RecordSourceSystem,
        long? Version,
        Meridian.Contracts.SecurityMaster.SecurityAssetProfileFieldTypeDto? DeclaredFieldType)> ReadPersistedFieldValueAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        SecurityMasterConflict conflict,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"""
            select currency, common_terms::text, asset_specific_terms::text, effective_from, provenance::text, version
            from {Qualified("securities")}
            where security_id = @security_id
            for update;
            """;
        command.Parameters.AddWithValue("security_id", conflict.SecurityId);

        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            return (null, SecurityMasterProvenanceReader.UnknownSource, null, null);
        }

        var recordSourceSystem = SecurityMasterProvenanceReader
            .Read(JsonDocument.Parse(reader.GetString(4)).RootElement)
            .SourceSystem;

        using var commonTerms = JsonDocument.Parse(reader.GetString(1));
        using var assetTerms = JsonDocument.Parse(reader.GetString(2));

        // Deliberately no record-level provenance comparison here: record-level source flips on
        // every amendment (including one from an unrelated third source touching a different
        // field), so it cannot establish who supplied an unchanged individual field — that is what
        // field-level provenance exists for. The guard that matters is the VALUE comparison below:
        // a field conflict may only close when the persisted field value equals the value the
        // selected source asserted.
        var detail = new SecurityDetailDto(
            conflict.SecurityId,
            "",
            SecurityStatusDto.Active,
            "",
            reader.GetString(0),
            commonTerms.RootElement.Clone(),
            assetTerms.RootElement.Clone(),
            [],
            [],
            0,
            new DateTimeOffset(reader.GetDateTime(3), TimeSpan.Zero),
            null);
        return (
            SecurityMasterConflictDetection.ReadComparableFieldValue(detail, conflict.FieldPath, _assetProfileCatalog),
            recordSourceSystem,
            reader.GetInt64(5),
            // The declared type drives the value guards' equality contract (a Text profile field
            // is a case-sensitive code), resolved against the same locked row the value came from.
            SecurityMasterConflictDetection.ResolveDeclaredFieldTypeForPath(detail, conflict.FieldPath, _assetProfileCatalog));
    }

    /// <summary>
    /// Closes an OPEN conflict as <c>Superseded</c> with the compare-and-set pattern every other
    /// transition uses: a later canonical write replaced both candidate values, so the row records
    /// WHY it closed (the persisted value that obsoleted it) without fabricating a winner — no
    /// field provenance is written, because neither source supplied the persisted value.
    /// </summary>
    private async Task<bool> SupersedeConflictAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        SecurityMasterConflict conflict,
        string reason,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"""
            update {Qualified(ConflictsTable)}
            set status = 'Superseded',
                resolved_by = @resolved_by,
                resolved_reason = @resolved_reason,
                resolved_at = @resolved_at
            where conflict_id = @conflict_id and status = 'Open';
            """;
        command.Parameters.AddWithValue("resolved_by", "system:canonical-write");
        command.Parameters.AddWithValue("resolved_reason", reason);
        command.Parameters.AddWithValue("resolved_at", DateTimeOffset.UtcNow.UtcDateTime);
        command.Parameters.AddWithValue("conflict_id", conflict.ConflictId);
        return await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false) > 0;
    }

    private static string BuildReplacedBothCandidatesReason(SecurityMasterConflict conflict, string? persistedValue)
        => $"A later canonical write persisted '{persistedValue}' for '{conflict.FieldPath}', which matches neither " +
           $"recorded candidate ('{conflict.ProviderA}'='{conflict.ValueA}', '{conflict.ProviderB}'='{conflict.ValueB}').";

    private async Task<IReadOnlyDictionary<string, string>> LoadConflictResolutionFieldSourcesAsync(
        NpgsqlConnection connection,
        Guid securityId,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        // Both canonical attribution origins name a field's true incumbent: ConflictResolution rows
        // record a resolved winner, CanonicalWrite rows record which source supplied the field
        // through an ordinary create/amend. Precedence follows the attributed projection VERSION,
        // not recording wall-clock: a delayed low-version canonical row recorded after a resolution
        // that validated a higher version must not resurrect the older incumbent. Recording time
        // orders only unversioned (legacy) and tied rows.
        command.CommandText =
            $"""
            select distinct on (field_path) field_path, source_system
            from {Qualified(PostgresSecurityFieldProvenanceSql.Table)}
            where security_id = @security_id and origin in (@resolution_origin, @canonical_origin)
            order by field_path, (source_version is not null) desc, source_version desc nulls last, recorded_at desc;
            """;
        command.Parameters.AddWithValue("security_id", securityId);
        command.Parameters.AddWithValue("resolution_origin", SecurityFieldProvenanceOrigins.ConflictResolution);
        command.Parameters.AddWithValue("canonical_origin", SecurityFieldProvenanceOrigins.CanonicalWrite);

        var sources = new Dictionary<string, string>(StringComparer.Ordinal);
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            sources[reader.GetString(0)] = reader.GetString(1);
        }

        return sources;
    }

    private static bool FieldValuesMatch(
        string fieldPath,
        string? persisted,
        string selected,
        Meridian.Contracts.SecurityMaster.SecurityAssetProfileFieldTypeDto? declaredProfileFieldType = null)
        => SecurityMasterConflictDetection.FieldValuesMatch(fieldPath, persisted, selected, declaredProfileFieldType);

    public async Task RecordConflictsForProjectionAsync(SecurityProjectionRecord projection, CancellationToken ct)
        => await RecordConflictsForProjectionsAsync([projection], ct).ConfigureAwait(false);

    public async Task RecordConflictsForProjectionsAsync(
        IReadOnlyList<SecurityProjectionRecord> projections,
        CancellationToken ct)
    {
        if (projections.Count == 0)
        {
            return;
        }

        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);
        await AcquireIdentifierConflictLockAsync(connection, transaction, ct).ConfigureAwait(false);

        var identifiers = projections.SelectMany(static projection => projection.Identifiers).ToArray();
        var excludedSecurityIds = projections.Select(static projection => projection.SecurityId).ToArray();
        var existingCandidates = await _store
            .FindIdentifierCandidatesAsync(identifiers, excludedSecurityIds, ct)
            .ConfigureAwait(false);
        var candidates = SecurityMasterConflictDetection.DetectForProjections(
            projections,
            existingCandidates,
            DateTimeOffset.UtcNow);
        var candidateUniverse = projections.Concat(existingCandidates).ToArray();

        int newConflicts = 0;
        foreach (var conflict in candidates)
        {
            await ReconcileLegacyIdentifierConflictIdAsync(
                connection,
                transaction,
                conflict,
                FindLegacyIdentifierConflictIds(conflict, candidateUniverse),
                ct).ConfigureAwait(false);
            var opened = await UpsertDetectedIdentifierConflictAsync(connection, transaction, conflict, ct).ConfigureAwait(false);
            if (opened)
            {
                newConflicts++;
                _logger.LogWarning(
                    "Ingest-time conflict detected: {FieldPath} already assigned to security {ExistingId} (new: {NewId})",
                    conflict.FieldPath, conflict.ValueB, conflict.SecurityId);
            }
        }

        // The projection write commits BEFORE this lock is taken, so a full refresh that loaded
        // the pre-write universe can have upserted or retained a conflict the subjects' amended
        // claims no longer produce. This scan holds the subjects' current claims and enumerates
        // every pair touching a subject, so it is authoritative for them: still-open detector
        // rows referencing a subject that the scan did not re-detect are superseded here rather
        // than lingering until the next full refresh.
        await using (var supersede = connection.CreateCommand())
        {
            supersede.Transaction = transaction;
            supersede.CommandText =
                $"""
                update {Qualified(ConflictsTable)}
                set status = 'Superseded',
                    resolved_reason = @resolved_reason,
                    resolved_at = @resolved_at
                where status = 'Open'
                  and conflict_kind = @conflict_kind
                  and (value_a = any(@subject_ids) or value_b = any(@subject_ids))
                  and not (conflict_id = any(@detected_ids));
                """;
            supersede.Parameters.AddWithValue(
                "resolved_reason",
                SecurityMasterConflictService.IdentifierNoLongerDetectedReason);
            supersede.Parameters.AddWithValue("resolved_at", DateTimeOffset.UtcNow.UtcDateTime);
            supersede.Parameters.AddWithValue("conflict_kind", SecurityMasterConflictKinds.IdentifierAmbiguity);
            supersede.Parameters.AddWithValue(
                "subject_ids",
                NpgsqlTypes.NpgsqlDbType.Array | NpgsqlTypes.NpgsqlDbType.Text,
                excludedSecurityIds.Select(static id => id.ToString()).ToArray());
            supersede.Parameters.AddWithValue(
                "detected_ids",
                NpgsqlTypes.NpgsqlDbType.Array | NpgsqlTypes.NpgsqlDbType.Uuid,
                candidates.Select(static conflict => conflict.ConflictId).ToArray());
            await supersede.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        await transaction.CommitAsync(ct).ConfigureAwait(false);

        if (newConflicts > 0)
        {
            _logger.LogInformation(
                "Recorded {Count} new identifier conflict(s) for {SecurityCount} security projection(s)",
                newConflicts, projections.Count);
        }
    }

    public async Task RecordFieldConflictsAsync(SecurityProjectionRecord previous, SecurityProjectionRecord incoming, CancellationToken ct)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);

        // Per-field attribution names the true incumbent: record-level provenance flips on every
        // amendment, so when providers amend DIFFERENT fields in sequence it can name a source
        // that never supplied the conflicted field, letting the authority policy persist false
        // field provenance. Recorded conflict-resolution rows carry each field's real source; the
        // record provenance stays the fallback for fields never resolved.
        var incumbentFieldSources = await LoadConflictResolutionFieldSourcesAsync(
            connection, previous.SecurityId, ct).ConfigureAwait(false);
        var candidates = SecurityMasterConflictDetection.DetectFieldConflicts(
            previous, incoming, DateTimeOffset.UtcNow, incumbentFieldSources, _assetProfileCatalog);

        if (candidates.Count == 0)
        {
            return;
        }

        await using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);

        int newConflicts = 0;
        foreach (var conflict in candidates)
        {
            // Insert-if-absent preserves operator resolution state on re-detection.
            var inserted = await InsertIfAbsentAsync(connection, transaction, conflict, ct).ConfigureAwait(false);
            if (inserted)
            {
                newConflicts++;
                _logger.LogWarning(
                    "Cross-source field conflict on {FieldPath} for security {SecurityId}: {SourceA}='{ValueA}' vs {SourceB}='{ValueB}'",
                    conflict.FieldPath, conflict.SecurityId, conflict.ProviderA, conflict.ValueA, conflict.ProviderB, conflict.ValueB);
            }
        }

        await transaction.CommitAsync(ct).ConfigureAwait(false);

        if (newConflicts > 0)
        {
            _logger.LogInformation(
                "Recorded {Count} new field conflict(s) for security {SecurityId}",
                newConflicts, incoming.SecurityId);
        }
    }

    public async Task ReconcileOpenFieldConflictsAsync(SecurityProjectionRecord persisted, CancellationToken ct)
    {
        // Runs strictly AFTER the canonical write commits: retiring or refreshing conflicts from
        // a value the event store might still reject (a stale ExpectedVersion) would mutate the
        // governed conflict queue for an amendment that never happened.
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);
        await SupersedeObsoleteFieldConflictsAsync(connection, transaction, persisted, ct).ConfigureAwait(false);
        await transaction.CommitAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Closes every OPEN field-level conflict on <paramref name="incoming"/>'s security whose BOTH
    /// candidate values the newly persisted record no longer matches. Rows are locked before the
    /// comparison so a concurrent resolution and this sweep serialize; conflicts whose persisted
    /// value still matches a candidate stay open (that candidate remains a legal resolution), and
    /// an unreadable persisted value retires nothing.
    /// </summary>
    private async Task SupersedeObsoleteFieldConflictsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        SecurityProjectionRecord incoming,
        CancellationToken ct)
    {
        var openConflicts = new List<SecurityMasterConflict>();
        await using (var select = connection.CreateCommand())
        {
            select.Transaction = transaction;
            select.CommandText =
                $"""
                select {ConflictColumns}
                from {Qualified(ConflictsTable)}
                where security_id = @security_id
                  and status = 'Open'
                  and conflict_kind in (@economic_kind, @common_kind)
                for update;
                """;
            select.Parameters.AddWithValue("security_id", incoming.SecurityId);
            select.Parameters.AddWithValue("economic_kind", SecurityMasterConflictKinds.EconomicTermMismatch);
            select.Parameters.AddWithValue("common_kind", SecurityMasterConflictKinds.CommonTermMismatch);
            await using var reader = await select.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                openConflicts.Add(MapConflict(reader));
            }
        }

        var incomingSource = SecurityMasterProvenanceReader.Read(incoming.Provenance).SourceSystem;
        foreach (var conflict in openConflicts)
        {
            var persistedValue = SecurityMasterConflictDetection.ReadComparableFieldValue(incoming, conflict.FieldPath, _assetProfileCatalog);
            var declaredFieldType = SecurityMasterConflictDetection.ResolveDeclaredFieldTypeForPath(incoming, conflict.FieldPath, _assetProfileCatalog);
            if (!SecurityMasterConflictDetection.FieldConflictIsObsolete(conflict, persistedValue, declaredFieldType))
            {
                continue;
            }

            // A write AUTHORED BY one of the conflict's own candidates is not a third-source
            // replacement: the providers still disagree — the author simply asserts a new value
            // now (same-source detection records no replacement candidate for it). Refresh that
            // candidate's recorded value so the row shows the LIVE disagreement and the author
            // stays a legally resolvable winner, instead of retiring a dispute that is still real.
            if (SecurityMasterConflictDetection.TryMatchCandidateProvider(conflict, incomingSource, out var revisesProviderA))
            {
                // COALESCE before refreshing: pre-persist detection may already have opened a
                // newer conflict for this field and provider pair carrying the live values (its
                // deterministic id hashes the current values; this row's id still hashes the
                // originals). Refreshing this row too would surface TWO independently resolvable
                // queue entries for one disagreement — so the older row closes into the newer one.
                var newerDuplicate = openConflicts.FirstOrDefault(other =>
                    other.ConflictId != conflict.ConflictId
                    && string.Equals(other.FieldPath, conflict.FieldPath, StringComparison.Ordinal)
                    && SecurityMasterConflictDetection.SameProviderPair(other, conflict));
                if (newerDuplicate is not null)
                {
                    if (await SupersedeConflictAsync(
                            connection, transaction, conflict,
                            $"Coalesced into conflict '{newerDuplicate.ConflictId:D}': the same providers dispute " +
                            $"'{conflict.FieldPath}' with refreshed candidate values recorded there.",
                            ct).ConfigureAwait(false))
                    {
                        _logger.LogInformation(
                            "Coalesced open field conflict {ConflictId} into {DuplicateId} ({FieldPath}) for security {SecurityId}.",
                            conflict.ConflictId, newerDuplicate.ConflictId, conflict.FieldPath, conflict.SecurityId);
                    }

                    continue;
                }

                if (await RefreshConflictCandidateValueAsync(
                        connection, transaction, conflict, revisesProviderA, persistedValue!, ct).ConfigureAwait(false))
                {
                    _logger.LogInformation(
                        "Refreshed candidate {Provider} on open field conflict {ConflictId} ({FieldPath}) for security {SecurityId}: the candidate revised its own value.",
                        revisesProviderA ? conflict.ProviderA : conflict.ProviderB,
                        conflict.ConflictId, conflict.FieldPath, conflict.SecurityId);
                }

                continue;
            }

            // An UNKNOWN author must never retire a real disagreement on guesswork.
            if (string.Equals(incomingSource, SecurityMasterProvenanceReader.UnknownSource, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (await SupersedeConflictAsync(
                    connection, transaction, conflict,
                    BuildReplacedBothCandidatesReason(conflict, persistedValue), ct).ConfigureAwait(false))
            {
                _logger.LogInformation(
                    "Superseded obsolete field conflict {ConflictId} on {FieldPath} for security {SecurityId}: canonical write replaced both candidates.",
                    conflict.ConflictId, conflict.FieldPath, conflict.SecurityId);
            }
        }
    }

    /// <summary>
    /// Replaces one candidate's recorded value on an OPEN conflict with the value that candidate's
    /// own later canonical write persisted, keeping the conflict open: the disagreement with the
    /// other provider is unchanged, and the refreshed value is what the resolution guard will
    /// accept if an operator picks this candidate.
    /// </summary>
    private async Task<bool> RefreshConflictCandidateValueAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        SecurityMasterConflict conflict,
        bool refreshProviderA,
        string persistedValue,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        var column = refreshProviderA ? "value_a" : "value_b";
        command.CommandText =
            $"""
            update {Qualified(ConflictsTable)}
            set {column} = @value
            where conflict_id = @conflict_id and status = 'Open';
            """;
        command.Parameters.AddWithValue("value", persistedValue);
        command.Parameters.AddWithValue("conflict_id", conflict.ConflictId);
        return await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false) > 0;
    }

    private static IReadOnlyList<Guid> FindLegacyIdentifierConflictIds(
        SecurityMasterConflict conflict,
        IReadOnlyList<SecurityProjectionRecord> universe)
    {
        const string prefix = "Identifiers.";
        if (!conflict.FieldPath.StartsWith(prefix, StringComparison.Ordinal)
            || !Enum.TryParse<SecurityIdentifierKind>(conflict.FieldPath[prefix.Length..], out var kind)
            || !Guid.TryParse(conflict.ValueA, out var firstSecurityId)
            || !Guid.TryParse(conflict.ValueB, out var secondSecurityId))
        {
            return Array.Empty<Guid>();
        }

        var first = universe.FirstOrDefault(record => record.SecurityId == firstSecurityId);
        var second = universe.FirstOrDefault(record => record.SecurityId == secondSecurityId);
        if (first is null || second is null)
        {
            return Array.Empty<Guid>();
        }

        // Before normalized conflict IDs, the detector grouped raw values case-insensitively and
        // hashed whichever raw spelling it encountered first. Enumerate both spellings so upgrade
        // reconciliation is independent of projection load order; punctuation variants did not
        // conflict in that detector and therefore have no legacy row to migrate.
        var legacyIds = new HashSet<Guid>();
        foreach (var left in first.Identifiers.Where(identifier => identifier.Kind == kind))
        {
            foreach (var right in second.Identifiers.Where(identifier => identifier.Kind == kind))
            {
                if (!string.Equals(left.Value, right.Value, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                legacyIds.Add(SecurityMasterConflictDetection.DeterministicConflictId(
                    kind.ToString(), left.Value, firstSecurityId, secondSecurityId));
                legacyIds.Add(SecurityMasterConflictDetection.DeterministicConflictId(
                    kind.ToString(), right.Value, firstSecurityId, secondSecurityId));
            }
        }

        legacyIds.Remove(conflict.ConflictId);
        return legacyIds.Order().ToArray();
    }

    private async Task ReconcileLegacyIdentifierConflictIdAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        SecurityMasterConflict detected,
        IReadOnlyList<Guid> legacyIds,
        CancellationToken ct)
    {
        if (legacyIds.Count == 0)
        {
            return;
        }

        var rows = new List<SecurityMasterConflict>();
        await using (var select = connection.CreateCommand())
        {
            select.Transaction = transaction;
            select.CommandText =
                $"""
                select {ConflictColumns}
                from {Qualified(ConflictsTable)}
                where conflict_id = @canonical_id
                   or conflict_id = any(@legacy_ids)
                order by conflict_id
                for update;
                """;
            select.Parameters.AddWithValue("canonical_id", detected.ConflictId);
            select.Parameters.AddWithValue(
                "legacy_ids",
                NpgsqlTypes.NpgsqlDbType.Array | NpgsqlTypes.NpgsqlDbType.Uuid,
                legacyIds.ToArray());
            await using var reader = await select.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                rows.Add(MapConflict(reader));
            }
        }

        var legacyRows = rows.Where(row => row.ConflictId != detected.ConflictId).ToArray();
        if (legacyRows.Length == 0)
        {
            return;
        }

        var canonical = rows.FirstOrDefault(row => row.ConflictId == detected.ConflictId);
        var authoritativeLegacy = legacyRows
            .OrderByDescending(IsOperatorDecision)
            .ThenByDescending(row => string.Equals(row.Status, "Open", StringComparison.Ordinal))
            .ThenByDescending(row => row.ResolvedAt ?? row.DetectedAt)
            .ThenBy(row => row.ConflictId)
            .First();

        if (canonical is null)
        {
            await using var migrate = connection.CreateCommand();
            migrate.Transaction = transaction;
            migrate.CommandText =
                $"""
                update {Qualified(ConflictsTable)}
                set conflict_id = @canonical_id,
                    security_id = @security_id,
                    conflict_kind = @conflict_kind,
                    field_path = @field_path,
                    provider_a = @provider_a,
                    value_a = @value_a,
                    provider_b = @provider_b,
                    value_b = @value_b
                where conflict_id = @legacy_id;
                """;
            AddConflictIdentityParameters(migrate, detected);
            migrate.Parameters.AddWithValue("legacy_id", authoritativeLegacy.ConflictId);
            await migrate.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            canonical = authoritativeLegacy with { ConflictId = detected.ConflictId };
        }
        else if (IsOperatorDecision(authoritativeLegacy) && !IsOperatorDecision(canonical))
        {
            await using var inherit = connection.CreateCommand();
            inherit.Transaction = transaction;
            inherit.CommandText =
                $"""
                update {Qualified(ConflictsTable)}
                set status = @status,
                    resolved_winner_source = @winner,
                    resolved_by = @resolved_by,
                    resolved_reason = @resolved_reason,
                    resolved_at = @resolved_at
                where conflict_id = @canonical_id;
                """;
            inherit.Parameters.AddWithValue("canonical_id", detected.ConflictId);
            inherit.Parameters.AddWithValue("status", authoritativeLegacy.Status);
            inherit.Parameters.AddWithValue("winner", (object?)authoritativeLegacy.ResolvedWinnerSource ?? DBNull.Value);
            inherit.Parameters.AddWithValue("resolved_by", (object?)authoritativeLegacy.ResolvedBy ?? DBNull.Value);
            inherit.Parameters.AddWithValue("resolved_reason", (object?)authoritativeLegacy.ResolvedReason ?? DBNull.Value);
            inherit.Parameters.AddWithValue("resolved_at", (object?)authoritativeLegacy.ResolvedAt?.UtcDateTime ?? DBNull.Value);
            await inherit.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        var remainingLegacyIds = legacyRows
            .Where(row => row.ConflictId != authoritativeLegacy.ConflictId || rows.Any(existing => existing.ConflictId == detected.ConflictId))
            .Where(row => string.Equals(row.Status, "Open", StringComparison.Ordinal))
            .Select(row => row.ConflictId)
            .ToArray();
        if (remainingLegacyIds.Length > 0)
        {
            await using var supersede = connection.CreateCommand();
            supersede.Transaction = transaction;
            supersede.CommandText =
                $"""
                update {Qualified(ConflictsTable)}
                set status = 'Superseded',
                    resolved_reason = @reason,
                    resolved_at = @resolved_at
                where conflict_id = any(@legacy_ids)
                  and status = 'Open';
                """;
            supersede.Parameters.AddWithValue(
                "reason",
                $"Reconciled to normalized identifier conflict '{detected.ConflictId:D}'.");
            supersede.Parameters.AddWithValue("resolved_at", DateTimeOffset.UtcNow.UtcDateTime);
            supersede.Parameters.AddWithValue(
                "legacy_ids",
                NpgsqlTypes.NpgsqlDbType.Array | NpgsqlTypes.NpgsqlDbType.Uuid,
                remainingLegacyIds);
            await supersede.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
    }

    private static bool IsOperatorDecision(SecurityMasterConflict conflict)
        => string.Equals(conflict.Status, "Resolved", StringComparison.Ordinal)
           || string.Equals(conflict.Status, "Dismissed", StringComparison.Ordinal);

    private async Task AcquireIdentifierConflictLockAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "select pg_advisory_xact_lock(@lock_id);";
        command.Parameters.AddWithValue("lock_id", IdentifierConflictAdvisoryLock);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private async Task<bool> UpsertDetectedIdentifierConflictAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        SecurityMasterConflict conflict,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"""
            insert into {Qualified(ConflictsTable)} (
                conflict_id, security_id, conflict_kind, field_path,
                provider_a, value_a, provider_b, value_b, detected_at, status,
                resolved_winner_source, resolved_by, resolved_reason, resolved_at)
            values (
                @canonical_id, @security_id, @conflict_kind, @field_path,
                @provider_a, @value_a, @provider_b, @value_b, @detected_at, 'Open',
                null, null, null, null)
            on conflict (conflict_id) do update
            set security_id = excluded.security_id,
                field_path = excluded.field_path,
                provider_a = excluded.provider_a,
                value_a = excluded.value_a,
                provider_b = excluded.provider_b,
                value_b = excluded.value_b,
                detected_at = excluded.detected_at,
                status = 'Open',
                resolved_winner_source = null,
                resolved_by = null,
                resolved_reason = null,
                resolved_at = null
            where {Qualified(ConflictsTable)}.status = 'Superseded'
              and {Qualified(ConflictsTable)}.conflict_kind = @conflict_kind
              and {Qualified(ConflictsTable)}.resolved_reason = @detector_reason
              and {Qualified(ConflictsTable)}.resolved_by is null
              and {Qualified(ConflictsTable)}.resolved_winner_source is null;
            """;
        AddConflictIdentityParameters(command, conflict);
        command.Parameters.AddWithValue("detected_at", conflict.DetectedAt.UtcDateTime);
        command.Parameters.AddWithValue(
            "detector_reason",
            SecurityMasterConflictService.IdentifierNoLongerDetectedReason);
        return await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false) > 0;
    }

    private static void AddConflictIdentityParameters(NpgsqlCommand command, SecurityMasterConflict conflict)
    {
        command.Parameters.AddWithValue("canonical_id", conflict.ConflictId);
        command.Parameters.AddWithValue("security_id", conflict.SecurityId);
        command.Parameters.AddWithValue("conflict_kind", conflict.ConflictKind);
        command.Parameters.AddWithValue("field_path", conflict.FieldPath);
        command.Parameters.AddWithValue("provider_a", conflict.ProviderA);
        command.Parameters.AddWithValue("value_a", conflict.ValueA);
        command.Parameters.AddWithValue("provider_b", conflict.ProviderB);
        command.Parameters.AddWithValue("value_b", conflict.ValueB);
    }

    private async Task<bool> InsertIfAbsentAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        SecurityMasterConflict conflict,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"""
            insert into {Qualified(ConflictsTable)} (
                conflict_id, security_id, conflict_kind, field_path,
                provider_a, value_a, provider_b, value_b, detected_at, status,
                resolved_winner_source, resolved_by, resolved_reason, resolved_at)
            values (
                @conflict_id, @security_id, @conflict_kind, @field_path,
                @provider_a, @value_a, @provider_b, @value_b, @detected_at, @status,
                @resolved_winner_source, @resolved_by, @resolved_reason, @resolved_at)
            on conflict (conflict_id) do nothing;
            """;
        command.Parameters.AddWithValue("conflict_id", conflict.ConflictId);
        command.Parameters.AddWithValue("security_id", conflict.SecurityId);
        command.Parameters.AddWithValue("conflict_kind", conflict.ConflictKind);
        command.Parameters.AddWithValue("field_path", conflict.FieldPath);
        command.Parameters.AddWithValue("provider_a", conflict.ProviderA);
        command.Parameters.AddWithValue("value_a", conflict.ValueA);
        command.Parameters.AddWithValue("provider_b", conflict.ProviderB);
        command.Parameters.AddWithValue("value_b", conflict.ValueB);
        command.Parameters.AddWithValue("detected_at", conflict.DetectedAt.UtcDateTime);
        command.Parameters.AddWithValue("status", conflict.Status);
        command.Parameters.AddWithValue("resolved_winner_source", (object?)conflict.ResolvedWinnerSource ?? DBNull.Value);
        command.Parameters.AddWithValue("resolved_by", (object?)conflict.ResolvedBy ?? DBNull.Value);
        command.Parameters.AddWithValue("resolved_reason", (object?)conflict.ResolvedReason ?? DBNull.Value);
        command.Parameters.AddWithValue("resolved_at", (object?)conflict.ResolvedAt?.UtcDateTime ?? DBNull.Value);

        var affected = await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        return affected > 0;
    }

    private const string ConflictColumns =
        "conflict_id, security_id, conflict_kind, field_path, provider_a, value_a, provider_b, value_b, " +
        "detected_at, status, resolved_winner_source, resolved_by, resolved_reason, resolved_at";

    private static SecurityMasterConflict MapConflict(DbDataReader reader) => new(
        ConflictId: reader.GetGuid(0),
        SecurityId: reader.GetGuid(1),
        ConflictKind: reader.GetString(2),
        FieldPath: reader.GetString(3),
        ProviderA: reader.GetString(4),
        ValueA: reader.GetString(5),
        ProviderB: reader.GetString(6),
        ValueB: reader.GetString(7),
        DetectedAt: new DateTimeOffset(reader.GetDateTime(8), TimeSpan.Zero),
        Status: reader.GetString(9))
    {
        ResolvedWinnerSource = reader.IsDBNull(10) ? null : reader.GetString(10),
        ResolvedBy = reader.IsDBNull(11) ? null : reader.GetString(11),
        ResolvedReason = reader.IsDBNull(12) ? null : reader.GetString(12),
        ResolvedAt = reader.IsDBNull(13) ? null : new DateTimeOffset(reader.GetDateTime(13), TimeSpan.Zero),
    };

    private async Task<NpgsqlConnection> OpenConnectionAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_options.ConnectionString))
        {
            throw new InvalidOperationException("SecurityMasterOptions.ConnectionString is not configured.");
        }

        var connection = new NpgsqlConnection(_options.ConnectionString);
        await connection.OpenAsync(ct).ConfigureAwait(false);
        return connection;
    }

    private string Qualified(string table) => $"{_options.Schema}.{table}";
}
