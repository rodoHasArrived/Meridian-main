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

    private readonly ISecurityMasterStore _store;
    private readonly SecurityMasterOptions _options;
    private readonly ILogger<PostgresSecurityMasterConflictService> _logger;

    public PostgresSecurityMasterConflictService(
        ISecurityMasterStore store,
        SecurityMasterOptions options,
        ILogger<PostgresSecurityMasterConflictService> logger)
    {
        _store = store;
        _options = options;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SecurityMasterConflict>> GetOpenConflictsAsync(CancellationToken ct)
    {
        var all = await _store.LoadAllAsync(ct).ConfigureAwait(false);
        var detected = SecurityMasterConflictDetection.DetectAll(all, DateTimeOffset.UtcNow);

        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);

        // Record newly detected conflicts in one transaction (one commit instead of one per conflict);
        // ON CONFLICT DO NOTHING preserves any existing resolution state so a re-detected,
        // already-resolved conflict is never re-opened.
        if (detected.Count > 0)
        {
            await using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);
            foreach (var conflict in detected)
            {
                await InsertIfAbsentAsync(connection, transaction, conflict, ct).ConfigureAwait(false);
            }

            await transaction.CommitAsync(ct).ConfigureAwait(false);
        }

        if (detected.Count > 0)
        {
            _logger.LogInformation("Detected {Count} identifier conflicts in Security Master", detected.Count);
        }

        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            select {ConflictColumns}
            from {Qualified(ConflictsTable)}
            where status = 'Open'
            order by detected_at;
            """;

        var results = new List<SecurityMasterConflict>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            results.Add(MapConflict(reader));
        }

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

        var resolvingField = newStatus == "Resolved" && IsFieldLevelConflict(openConflict.ConflictKind);
        var selectedSource = resolvingField
            ? ResolveSelectedSource(openConflict, request.ChosenWinnerSource, request.Resolution)
            : request.ChosenWinnerSource?.Trim();
        var selectedValue = resolvingField
            ? ResolveSelectedValue(openConflict, selectedSource!)
            : string.Empty;
        if (resolvingField)
        {
            var persistedValue = await ReadPersistedFieldValueAsync(
                connection,
                transaction,
                openConflict,
                selectedSource!,
                ct).ConfigureAwait(false);
            if (!FieldValuesMatch(openConflict.FieldPath, persistedValue, selectedValue))
            {
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
            command.Parameters.AddWithValue(
                "resolved_winner_source",
                newStatus == "Resolved" ? (object?)selectedSource ?? DBNull.Value : DBNull.Value);
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
                    AsOf: resolutionTime,
                    UpdatedBy: updated.ResolvedBy,
                    Confidence: null,
                    Origin: SecurityFieldProvenanceOrigins.ConflictResolution,
                    OriginReference: updated.ConflictId.ToString("D"),
                    RecordedAt: resolutionTime),
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

    private async Task<string?> ReadPersistedFieldValueAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        SecurityMasterConflict conflict,
        string selectedSource,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"""
            select currency, common_terms::text, asset_specific_terms::text, provenance::text, effective_from
            from {Qualified("securities")}
            where security_id = @security_id
            for update;
            """;
        command.Parameters.AddWithValue("security_id", conflict.SecurityId);

        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            return null;
        }

        using var commonTerms = JsonDocument.Parse(reader.GetString(1));
        using var assetTerms = JsonDocument.Parse(reader.GetString(2));
        using var provenance = JsonDocument.Parse(reader.GetString(3));
        var currentSource = SecurityMasterProvenanceReader.Read(provenance.RootElement).SourceSystem;
        if (!string.Equals(currentSource, selectedSource, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

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
            new DateTimeOffset(reader.GetDateTime(4), TimeSpan.Zero),
            null);
        var terms = StructuredCashFlowTermsResolver.Resolve(detail);
        return conflict.FieldPath switch
        {
            "EconomicTerms.maturityDate" => terms.MaturityDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            "EconomicTerms.issueDate" => terms.IssueDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            "EconomicTerms.couponRate" => terms.CouponRate?.ToString(CultureInfo.InvariantCulture),
            "EconomicTerms.principalFace" => terms.PrincipalFace?.ToString(CultureInfo.InvariantCulture),
            "EconomicTerms.paymentFrequency" => terms.PaymentFrequency,
            "EconomicTerms.dayCountConvention" => terms.DayCountConvention,
            "CommonTerms.currency" => detail.Currency,
            "CommonTerms.countryOfRisk" => SecurityTermReader.ReadString(detail.CommonTerms, "countryOfRisk"),
            _ => null,
        };
    }

    private static bool FieldValuesMatch(string fieldPath, string? persisted, string selected)
    {
        if (string.IsNullOrWhiteSpace(persisted))
        {
            return false;
        }

        if (string.Equals(fieldPath, "EconomicTerms.dayCountConvention", StringComparison.Ordinal))
        {
            var persistedConvention = DayCountConventions.Parse(persisted);
            var selectedConvention = DayCountConventions.Parse(selected);
            if (persistedConvention != DayCountConvention.Unknown || selectedConvention != DayCountConvention.Unknown)
            {
                return persistedConvention == selectedConvention;
            }
        }

        return string.Equals(persisted.Trim(), selected.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    public async Task RecordConflictsForProjectionAsync(SecurityProjectionRecord projection, CancellationToken ct)
    {
        var all = await _store.LoadAllAsync(ct).ConfigureAwait(false);
        var candidates = SecurityMasterConflictDetection.DetectForProjection(projection, all, DateTimeOffset.UtcNow);
        if (candidates.Count == 0)
        {
            return;
        }

        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);

        int newConflicts = 0;
        foreach (var conflict in candidates)
        {
            var inserted = await InsertIfAbsentAsync(connection, transaction, conflict, ct).ConfigureAwait(false);
            if (inserted)
            {
                newConflicts++;
                _logger.LogWarning(
                    "Ingest-time conflict detected: {FieldPath} already assigned to security {ExistingId} (new: {NewId})",
                    conflict.FieldPath, conflict.ValueB, projection.SecurityId);
            }
        }

        await transaction.CommitAsync(ct).ConfigureAwait(false);

        if (newConflicts > 0)
        {
            _logger.LogInformation(
                "Recorded {Count} new identifier conflict(s) for security {SecurityId}",
                newConflicts, projection.SecurityId);
        }
    }

    public async Task RecordFieldConflictsAsync(SecurityProjectionRecord previous, SecurityProjectionRecord incoming, CancellationToken ct)
    {
        var candidates = SecurityMasterConflictDetection.DetectFieldConflicts(previous, incoming, DateTimeOffset.UtcNow);
        if (candidates.Count == 0)
        {
            return;
        }

        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
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
