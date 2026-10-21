using System.Text.Json;
using Meridian.Contracts.SecurityMaster;
using Meridian.Core.Serialization;
using Npgsql;
using NpgsqlTypes;

namespace Meridian.Storage.SecurityMaster;

public sealed partial class PostgresSecurityMasterStore : ISecurityMasterStore
{
    private const string OptionContractProjectionTable = "option_contract_projection";
    private const string OptionSeriesProjectionTable = "option_series_projection";
    private const string OptionLifecycleProjectionTable = "option_lifecycle_projection";
    private const string OptionAliasProjectionTable = "option_alias_projection";

    /// <summary>
    /// A registered per-asset-class relational projection writer: the asset class it owns and the
    /// upsert-or-delete operation the store fans out to for every persisted record.
    /// </summary>
    private sealed record AssetProjectionWriter(
        string AssetClass,
        Func<PostgresSecurityMasterStore, NpgsqlConnection, NpgsqlTransaction, SecurityProjectionRecord, CancellationToken, Task> WriteAsync);

    /// <summary>
    /// The projection writers that predate <see cref="SecurityTermsProjectionRegistry"/> and have
    /// not moved onto it. Every other class — including Bond, whose three sibling tables are now
    /// registry satellites — is a descriptor. Option stays here because its tables are keyed by
    /// contract symbol rather than security, its alias table fans out from the record's aliases, its
    /// lifecycle row has an insert-only <c>listed_at</c>, and
    /// <see cref="PostgresOptionReferenceProjectionStore"/> writes the same tables from quotes.
    /// </summary>
    private static readonly IReadOnlyList<AssetProjectionWriter> HandWrittenProjectionWriters =
    [
        new("Option", static (store, c, t, r, ct) => store.UpsertOptionProjectionTablesAsync(c, t, r, ct)),
    ];

    /// <summary>
    /// The ordered registry of per-asset-class projection writers. Replaces the previous hard-coded
    /// fan-out block so adding a projected asset class is a single additive registration here, and the
    /// projected set is enumerable for the catalog-vs-projection coverage guard.
    /// <para>
    /// Anything a class can state declaratively — a table, its columns, and the declared terms they
    /// read — is a descriptor in <see cref="SecurityTermsProjectionRegistry"/> instead of another
    /// copy of the same gate/read/upsert/delete method, and is appended here.
    /// </para>
    /// </summary>
    private static readonly IReadOnlyList<AssetProjectionWriter> ProjectionWriters =
    [
        .. HandWrittenProjectionWriters,
        .. SecurityTermsProjectionRegistry.Descriptors.Select(static descriptor =>
            new AssetProjectionWriter(
                descriptor.AssetClass,
                (store, c, t, r, ct) => store.WriteTermsProjectionAsync(descriptor, c, t, r, ct)))
    ];

    /// <summary>
    /// The asset classes projected by a specialized writer. Locked to the classes the asset-class
    /// registry declares <see cref="SecurityTermsProjectionKind.Specialized"/>.
    /// </summary>
    internal static IReadOnlyList<string> SpecializedProjectionAssetClasses { get; } =
        HandWrittenProjectionWriters.Select(static writer => writer.AssetClass).ToArray();

    /// <summary>The asset classes that have a dedicated relational projection, in fan-out order.</summary>
    internal static IReadOnlyList<string> ProjectedAssetClasses { get; } =
        ProjectionWriters.Select(static writer => writer.AssetClass).ToArray();

    /// <summary>
    /// The writers an upsert runs, by the asset class the <c>securities</c> row held before the
    /// write and the class it holds now. A projection row for class X can only exist while the
    /// security's stored class is X (every write path goes through <see cref="UpsertProjectionCoreAsync"/>,
    /// which clears X's rows the moment the class moves on), so a write has to visit at most two
    /// writers: the current class's, which upserts or clears its own rows depending on the gates,
    /// and the prior class's when the class changed, which clears the rows it left behind. Every
    /// other writer would delete rows that cannot be there. Returned in registry order, so a class
    /// change clears the old projection before the new one is published when the old writer is
    /// registered first.
    /// </summary>
    internal static IReadOnlyList<string> ProjectionWritersFor(string? priorAssetClass, string currentAssetClass)
    {
        ArgumentNullException.ThrowIfNull(currentAssetClass);

        var classChanged = priorAssetClass is not null
            && !string.Equals(priorAssetClass, currentAssetClass, StringComparison.OrdinalIgnoreCase);

        return ProjectionWriters
            .Where(writer =>
                string.Equals(writer.AssetClass, currentAssetClass, StringComparison.OrdinalIgnoreCase)
                || (classChanged && string.Equals(writer.AssetClass, priorAssetClass, StringComparison.OrdinalIgnoreCase)))
            .Select(static writer => writer.AssetClass)
            .ToArray();
    }

    private static readonly IReadOnlyDictionary<string, AssetProjectionWriter> ProjectionWritersByAssetClass =
        ProjectionWriters.ToDictionary(static writer => writer.AssetClass, StringComparer.OrdinalIgnoreCase);

    private readonly SecurityMasterOptions _options;
    private readonly SecurityMasterNodeIdentity _nodeIdentity;

    public PostgresSecurityMasterStore(SecurityMasterOptions options, SecurityMasterNodeIdentity? nodeIdentity = null)
    {
        _options = options;
        _nodeIdentity = nodeIdentity ?? SecurityMasterNodeIdentity.CreateNew();
    }

    public async Task UpsertProjectionAsync(SecurityProjectionRecord record, CancellationToken ct = default)
    {
        // Inside a canonical write scope the projection joins the shared transaction with the event
        // append and field attribution; its change notification is delivered when that commits.
        if (PostgresSecurityMasterMutation.IsActiveFor(_options, record.SecurityId))
        {
            await using var lease = await PostgresSecurityMasterMutation.OpenConnectionAsync(_options, ct).ConfigureAwait(false);
            if (await UpsertProjectionCoreAsync(lease.Connection, lease.Transaction!, record, ct).ConfigureAwait(false))
            {
                await NotifyProjectionChangedAsync(
                    lease.Connection,
                    lease.Transaction!,
                    SecurityProjectionChangeNotification.ForSecurity(_nodeIdentity.NodeId, record.SecurityId, record.Version),
                    ct).ConfigureAwait(false);
            }

            return;
        }

        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);
        if (await UpsertProjectionCoreAsync(connection, transaction, record, ct).ConfigureAwait(false))
        {
            await NotifyProjectionChangedAsync(
                connection,
                transaction,
                SecurityProjectionChangeNotification.ForSecurity(_nodeIdentity.NodeId, record.SecurityId, record.Version),
                ct).ConfigureAwait(false);
        }
        await transaction.CommitAsync(ct).ConfigureAwait(false);
    }

    public async Task PersistProjectionBatchAsync(
        string projectionName,
        long lastGlobalSequence,
        IReadOnlyList<SecurityProjectionRecord> records,
        CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);

        var acceptedRecords = new List<SecurityProjectionRecord>(records.Count);
        // Consistent row-lock ordering also keeps overlapping rebuild batches from deadlocking.
        foreach (var record in records.OrderBy(record => record.SecurityId).ThenBy(record => record.Version))
        {
            if (await UpsertProjectionCoreAsync(connection, transaction, record, ct).ConfigureAwait(false))
                acceptedRecords.Add(record);
        }

        if (acceptedRecords.Count > SecurityProjectionChangeNotification.MaxPerSecurityNotificationsPerBatch)
        {
            await NotifyProjectionChangedAsync(
                connection,
                transaction,
                SecurityProjectionChangeNotification.ForResync(_nodeIdentity.NodeId),
                ct).ConfigureAwait(false);
        }
        else
        {
            foreach (var record in acceptedRecords)
            {
                await NotifyProjectionChangedAsync(
                    connection,
                    transaction,
                    SecurityProjectionChangeNotification.ForSecurity(_nodeIdentity.NodeId, record.SecurityId, record.Version),
                    ct).ConfigureAwait(false);
            }
        }

        if (acceptedRecords.Count > 0)
        {
            // A rebuild rewrites governed projections from the event log; record that it happened,
            // and which records it wrote, in the append-only governance log in the same transaction.
            await PostgresSecurityGovernanceEventLog.AppendAsync(
                connection,
                transaction,
                _options.Schema,
                securityId: null,
                SecurityGovernanceStreams.ProjectionRebuild,
                "ProjectionBatchRebuilt",
                "security-master.rebuild",
                DateTimeOffset.UtcNow,
                new
                {
                    projectionName,
                    lastGlobalSequence,
                    records = acceptedRecords.Select(static record => new { record.SecurityId, record.Version }).ToArray()
                },
                ct).ConfigureAwait(false);
        }

        await SaveCheckpointCoreAsync(connection, transaction, projectionName, lastGlobalSequence, ct).ConfigureAwait(false);
        await transaction.CommitAsync(ct).ConfigureAwait(false);
    }

    public async Task DeactivateProjectionAsync(Guid securityId, DateTimeOffset effectiveTo, long version, CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);
        int updated;
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText =
                $"""
                update {Qualified("securities")}
                set status = 'Inactive',
                    effective_to = @effective_to,
                    version = @version
                where security_id = @security_id and version <= @version;
                """;
            command.Parameters.AddWithValue("security_id", securityId);
            command.Parameters.AddWithValue("effective_to", effectiveTo.UtcDateTime);
            command.Parameters.AddWithValue("version", version);
            updated = await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        if (updated > 0)
        {
            await NotifyProjectionChangedAsync(
                connection,
                transaction,
                SecurityProjectionChangeNotification.ForSecurity(_nodeIdentity.NodeId, securityId, version),
                ct).ConfigureAwait(false);
        }

        await transaction.CommitAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Queues a projection-change notification on the writing transaction. PostgreSQL delivers a
    /// <c>NOTIFY</c> issued inside a transaction only when that transaction commits (and drops it on
    /// rollback), so other nodes can never observe a change the durable store does not hold.
    /// </summary>
    private async Task NotifyProjectionChangedAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        SecurityProjectionChangeNotification notification,
        CancellationToken ct)
    {
        if (!_options.ProjectionCacheNotificationsEnabled)
        {
            return;
        }

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "select pg_notify(@channel, @payload);";
        command.Parameters.AddWithValue("channel", SecurityProjectionChangeNotification.ChannelFor(_options.Schema));
        command.Parameters.AddWithValue("payload", notification.ToPayload());
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<SecurityDetailDto?> GetDetailAsync(Guid securityId, CancellationToken ct = default)
    {
        var projection = await GetProjectionAsync(securityId, ct).ConfigureAwait(false);
        return projection is null ? null : SecurityMasterDbMapper.ToDetail(projection);
    }

    public async Task<SecurityProjectionRecord?> GetProjectionAsync(Guid securityId, CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        return await GetProjectionCoreAsync(connection, securityId, ct).ConfigureAwait(false);
    }

    public async Task<SecurityProjectionRecord?> GetByIdentifierAsync(SecurityIdentifierKind kind, string value, string? provider, DateTimeOffset asOfUtc, bool includeInactive, CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        var securityId = await ResolveSecurityIdAsync(connection, kind, value, provider, asOfUtc, includeInactive, ct).ConfigureAwait(false);
        return securityId is null ? null : await GetProjectionCoreAsync(connection, securityId.Value, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<SecuritySummaryDto>> SearchAsync(SecuritySearchRequest request, CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();

        var trimmedQuery = request.Query.Trim();
        var useFts = trimmedQuery.Length >= 2;

        // Use full-text search when the query is long enough for meaningful tokenisation.
        // The ILIKE fallback catches short identifiers (e.g. "GE", "T") that tsvector skips.
        command.CommandText = useFts
            ? $"""
               select security_id,
                      ts_rank(search_vector, plainto_tsquery('simple', @query)) as rank
               from {Qualified("securities")}
               where (
                   search_vector @@ plainto_tsquery('simple', @query)
                   or display_name ilike @pattern
                   or primary_identifier_value ilike @pattern)
                 and (@active_only = false or status = 'Active')
               order by rank desc, display_name, security_id
               limit @take offset @skip;
               """
            : $"""
               select security_id, 0::float4 as rank
               from {Qualified("securities")}
               where (
                   display_name ilike @pattern
                   or primary_identifier_value ilike @pattern)
                 and (@active_only = false or status = 'Active')
               order by display_name, security_id
               limit @take offset @skip;
               """;

        command.Parameters.AddWithValue("query", trimmedQuery);
        command.Parameters.AddWithValue("pattern", $"%{trimmedQuery}%");
        command.Parameters.AddWithValue("active_only", request.ActiveOnly);
        command.Parameters.AddWithValue("take", request.Take);
        command.Parameters.AddWithValue("skip", request.Skip);

        var ids = new List<Guid>();
        await using (var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                ids.Add(reader.GetGuid(0));
            }
        }

        var results = new List<SecuritySummaryDto>(ids.Count);
        foreach (var id in ids)
        {
            var projection = await GetProjectionCoreAsync(connection, id, ct).ConfigureAwait(false);
            if (projection is not null)
            {
                results.Add(SecurityMasterDbMapper.ToSummary(projection));
            }
        }

        return results;
    }

    public Task<IReadOnlyList<SecurityProjectionRecord>> LoadAllAsync(CancellationToken ct = default)
        => LoadByStatusAsync(activeOnly: false, ct);

    public Task<IReadOnlyList<SecurityProjectionRecord>> LoadActiveAsync(CancellationToken ct = default)
        => LoadByStatusAsync(activeOnly: true, ct);

    private async Task<IReadOnlyList<SecurityProjectionRecord>> LoadByStatusAsync(
        bool activeOnly, CancellationToken ct)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            select security_id from {Qualified("securities")}
            where (@active_only = false or status = 'Active')
            order by display_name;
            """;
        command.Parameters.AddWithValue("active_only", activeOnly);

        var ids = new List<Guid>();
        await using (var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                ids.Add(reader.GetGuid(0));
            }
        }

        var results = new List<SecurityProjectionRecord>(ids.Count);
        foreach (var id in ids)
        {
            var projection = await GetProjectionCoreAsync(connection, id, ct).ConfigureAwait(false);
            if (projection is not null)
            {
                results.Add(projection);
            }
        }

        return results;
    }

    public async Task<long?> GetCheckpointAsync(string projectionName, CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            select last_global_sequence
            from {Qualified("projection_checkpoint")}
            where projection_name = @projection_name;
            """;
        command.Parameters.AddWithValue("projection_name", projectionName);
        var result = await command.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return result is null or DBNull ? null : (long)result;
    }

    public async Task SaveCheckpointAsync(string projectionName, long lastGlobalSequence, CancellationToken ct = default)
    {
        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await SaveCheckpointCoreAsync(connection, transaction: null, projectionName, lastGlobalSequence, ct).ConfigureAwait(false);
    }

    private async Task<bool> UpsertProjectionCoreAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        SecurityProjectionRecord record,
        CancellationToken ct)
    {
        // The class the row held before this write decides which stale projection, if any, has to
        // be cleared below. Read under a per-security advisory lock plus the row lock the upsert is
        // about to take anyway, so a competing writer cannot change the class — or create the row
        // — between this read and the upsert's own ON CONFLICT recheck.
        var priorAssetClass = await ReadAssetClassForUpdateAsync(connection, transaction, record.SecurityId, ct).ConfigureAwait(false);

        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText =
                $"""
                insert into {Qualified("securities")} as retained (
                    security_id, asset_class, status, display_name, currency, country_of_risk, issuer_name,
                    exchange_code, listing_mic, lot_size, tick_size, primary_identifier_kind, primary_identifier_value,
                    normalized_primary_identifier_value,
                    common_terms, asset_specific_terms, provenance, version, schema_version, terms_format,
                    effective_from, effective_to)
                values (
                    @security_id, @asset_class, @status, @display_name, @currency, @country_of_risk, @issuer_name,
                    @exchange_code, @listing_mic, @lot_size, @tick_size, @primary_identifier_kind, @primary_identifier_value,
                    @normalized_primary_identifier_value,
                    @common_terms::jsonb, @asset_specific_terms::jsonb, @provenance::jsonb, @version, @schema_version,
                    @terms_format, @effective_from, @effective_to)
                on conflict (security_id) do update set
                    asset_class = excluded.asset_class,
                    status = excluded.status,
                    display_name = excluded.display_name,
                    currency = excluded.currency,
                    country_of_risk = excluded.country_of_risk,
                    issuer_name = excluded.issuer_name,
                    exchange_code = excluded.exchange_code,
                    listing_mic = excluded.listing_mic,
                    lot_size = excluded.lot_size,
                    tick_size = excluded.tick_size,
                    primary_identifier_kind = excluded.primary_identifier_kind,
                    primary_identifier_value = excluded.primary_identifier_value,
                    normalized_primary_identifier_value = excluded.normalized_primary_identifier_value,
                    common_terms = excluded.common_terms,
                    asset_specific_terms = excluded.asset_specific_terms,
                    provenance = excluded.provenance,
                    version = excluded.version,
                    schema_version = excluded.schema_version,
                    terms_format = excluded.terms_format,
                    effective_from = excluded.effective_from,
                    effective_to = excluded.effective_to
                where retained.version <= excluded.version;
                """;

            // Promote the asset-specific-terms schema version into the queryable schema_version column.
            // The column has ONE definition, shared with migration 024's backfill: the version stamped
            // on the STORED blob (a payload with no explicit schemaVersion resolves to the legacy
            // default). It is deliberately not the post-upcast version — the stored payload is
            // written unchanged so no read path observes an altered blob, and a cross-family
            // economic-terms document sitting in this slot must be selectable by
            // `where schema_version = 2` for the audit and compatibility queries the column exists
            // for. Readability stays the mapping guard's decision, which resolves the version from
            // the blob through the upcaster chain and never consults this column.
            var schemaVersion = SecurityAssetSpecificTermsV0ToCurrentUpcaster.ResolveSchemaVersion(record.AssetSpecificTerms);
            var termsFormat = ResolveStorableTermsFormat(record);

            command.Parameters.AddWithValue("security_id", record.SecurityId);
            command.Parameters.AddWithValue("asset_class", record.AssetClass);
            command.Parameters.AddWithValue("status", record.Status.ToString());
            command.Parameters.AddWithValue("display_name", record.DisplayName);
            command.Parameters.AddWithValue("currency", record.Currency);
            command.Parameters.AddWithValue("country_of_risk", (object?)GetOptionalString(record.CommonTerms, "countryOfRisk") ?? DBNull.Value);
            command.Parameters.AddWithValue("issuer_name", (object?)GetOptionalString(record.CommonTerms, "issuerName") ?? DBNull.Value);
            command.Parameters.AddWithValue("exchange_code", (object?)GetOptionalString(record.CommonTerms, "exchange") ?? DBNull.Value);
            command.Parameters.AddWithValue("listing_mic", (object?)ResolveListingMic(record.CommonTerms) ?? DBNull.Value);
            command.Parameters.AddWithValue("lot_size", (object?)GetOptionalDecimal(record.CommonTerms, "lotSize") ?? DBNull.Value);
            command.Parameters.AddWithValue("tick_size", (object?)GetOptionalDecimal(record.CommonTerms, "tickSize") ?? DBNull.Value);
            command.Parameters.AddWithValue("primary_identifier_kind", record.PrimaryIdentifierKind);
            command.Parameters.AddWithValue("primary_identifier_value", record.PrimaryIdentifierValue);
            command.Parameters.AddWithValue("normalized_primary_identifier_value", NormalizePrimaryIdentifierValue(record));
            command.Parameters.AddWithValue("common_terms", record.CommonTerms.GetRawText());
            command.Parameters.AddWithValue("asset_specific_terms", record.AssetSpecificTerms.GetRawText());
            command.Parameters.AddWithValue("provenance", record.Provenance.GetRawText());
            command.Parameters.AddWithValue("version", record.Version);
            command.Parameters.AddWithValue("schema_version", schemaVersion);
            command.Parameters.AddWithValue("terms_format", termsFormat);
            command.Parameters.AddWithValue("effective_from", record.EffectiveFrom.UtcDateTime);
            command.Parameters.AddWithValue("effective_to", (object?)record.EffectiveTo?.UtcDateTime ?? DBNull.Value);
            // ON CONFLICT locks and rechecks the retained row after a competing writer commits.
            // A skipped parent must skip every child replacement/deletion and its notification;
            // checking the version in application code before this statement would still race.
            if (await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false) == 0)
                return false;
        }

        await ReplaceIdentifiersAsync(connection, transaction, record.SecurityId, record.Identifiers, ct).ConfigureAwait(false);
        // The securities upsert above holds the parent row lock that alias writes also take first,
        // so the recorded alias history cannot move between this stamp check and the replacement.
        // A record carrying aliases read before a later alias write is an echo of superseded state:
        // writing it back would durably undo that correction, so the current rows are kept instead.
        if (record.Aliases is SecurityAliasSnapshot snapshot
            && await ReadAliasRevisionStampAsync(connection, record.SecurityId, ct).ConfigureAwait(false) != snapshot.RevisionStamp)
        {
            record = record with
            {
                Aliases = await LoadAliasesAsync(connection, record.SecurityId, ct).ConfigureAwait(false)
            };
        }
        else
        {
            await ReplaceAliasesAsync(connection, transaction, record.SecurityId, record.Aliases, ct).ConfigureAwait(false);
        }

        // Fan out to the per-asset-class projection writers this write can affect: the current
        // class's writer upserts its projection (or clears it when a gate fails), and when the class
        // changed, the prior class's writer runs against the non-matching record and clears the rows
        // it left behind. Running every registered writer here used to cost one delete per table
        // for rows that cannot exist (N6 in the extensibility review); ProjectionWritersFor states
        // the invariant that makes the narrower visit sufficient. Adding a projected asset class is
        // still a single additive registration in ProjectionWriters.
        foreach (var assetClass in ProjectionWritersFor(priorAssetClass, record.AssetClass))
        {
            await ProjectionWritersByAssetClass[assetClass].WriteAsync(this, connection, transaction, record, ct).ConfigureAwait(false);
        }

        return true;
    }

    /// <summary>
    /// The asset class the <c>securities</c> row currently holds, locked for the rest of the
    /// transaction, or <see langword="null"/> when the security has never been projected.
    /// <para>
    /// A row lock cannot serialize an absent key: two transactions creating the same security for
    /// the first time would both read "no row", both skip the prior-class cleanup, and the one that
    /// loses the ON CONFLICT race would leave its projection beside the winner's. The per-security
    /// advisory lock taken first serializes those first writes too, so a null here means the row
    /// did not exist and no concurrent writer can create it before this transaction's upsert.
    /// The lock is transaction-scoped and released at commit.
    /// </para>
    /// </summary>
    private async Task<string?> ReadAssetClassForUpdateAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid securityId,
        CancellationToken ct)
    {
        await using (var lockCommand = connection.CreateCommand())
        {
            lockCommand.Transaction = transaction;
            lockCommand.CommandText = "select pg_advisory_xact_lock(hashtextextended(@lock_scope, 0));";
            lockCommand.Parameters.AddWithValue("lock_scope", $"{_options.Schema}.securities:{securityId:N}");
            await lockCommand.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"select asset_class from {Qualified("securities")} where security_id = @security_id for update;";
        command.Parameters.AddWithValue("security_id", securityId);
        var result = await command.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return result is string assetClass ? assetClass : null;
    }

    private async Task UpsertOptionProjectionTablesAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        SecurityProjectionRecord record,
        CancellationToken ct)
    {
        if (SecurityAssetClassCatalog.GetOrDefault(record.AssetClass).ProjectionKind != SecurityTermsProjectionKind.Specialized ||
            !TryBuildOptionProjection(record, out var projection))
        {
            await DeleteOptionProjectionTablesAsync(connection, transaction, record.SecurityId, ct).ConfigureAwait(false);
            return;
        }

        await UpsertOptionContractProjectionAsync(connection, transaction, projection, ct).ConfigureAwait(false);
        await UpsertOptionSeriesProjectionAsync(connection, transaction, projection, ct).ConfigureAwait(false);
        await UpsertOptionLifecycleProjectionAsync(connection, transaction, projection, ct).ConfigureAwait(false);
        await UpsertOptionAliasProjectionAsync(connection, transaction, projection, record.Aliases, ct).ConfigureAwait(false);
    }

    private async Task ReplaceIdentifiersAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid securityId,
        IReadOnlyList<SecurityIdentifierDto> identifiers,
        CancellationToken ct)
    {
        await using (var delete = connection.CreateCommand())
        {
            delete.Transaction = transaction;
            delete.CommandText = $"delete from {Qualified("security_identifiers")} where security_id = @security_id;";
            delete.Parameters.AddWithValue("security_id", securityId);
            await delete.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        foreach (var identifier in identifiers)
        {
            await using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText =
                $"""
                insert into {Qualified("security_identifiers")} (
                    security_id, identifier_kind, identifier_value, normalized_identifier_value,
                    provider, normalized_provider, is_primary,
                    valid_from, valid_to, source, confidence, manual_override)
                values (
                    @security_id, @identifier_kind, @identifier_value, @normalized_identifier_value,
                    @provider, @normalized_provider, @is_primary,
                    @valid_from, @valid_to, @source, @confidence, @manual_override);
                """;
            insert.Parameters.AddWithValue("security_id", securityId);
            insert.Parameters.AddWithValue("identifier_kind", identifier.Kind.ToString());
            insert.Parameters.AddWithValue("identifier_value", identifier.Value);
            insert.Parameters.AddWithValue("normalized_identifier_value", SecurityIdentifierNormalizer.GetOrComputeNormalizedValue(identifier));
            insert.Parameters.AddWithValue("provider", (object?)identifier.Provider ?? DBNull.Value);
            insert.Parameters.AddWithValue("normalized_provider", ToDbNullable(SecurityIdentifierNormalizer.GetOrComputeNormalizedProvider(identifier)));
            insert.Parameters.AddWithValue("is_primary", identifier.IsPrimary);
            insert.Parameters.AddWithValue("valid_from", identifier.ValidFrom.UtcDateTime);
            insert.Parameters.AddWithValue("valid_to", (object?)identifier.ValidTo?.UtcDateTime ?? DBNull.Value);
            insert.Parameters.AddWithValue("source", "SecurityMaster");
            insert.Parameters.AddWithValue("confidence", DBNull.Value);
            insert.Parameters.AddWithValue("manual_override", false);
            await insert.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
    }

    private async Task SaveCheckpointCoreAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        string projectionName,
        long lastGlobalSequence,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"""
            insert into {Qualified("projection_checkpoint")} as retained (
                projection_name, last_global_sequence, updated_at)
            values (
                @projection_name, @last_global_sequence, @updated_at)
            on conflict (projection_name) do update set
                last_global_sequence = greatest(retained.last_global_sequence, excluded.last_global_sequence),
                updated_at = excluded.updated_at;
            """;
        command.Parameters.AddWithValue("projection_name", projectionName);
        command.Parameters.AddWithValue("last_global_sequence", lastGlobalSequence);
        command.Parameters.AddWithValue("updated_at", DateTime.UtcNow);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private async Task DeleteOptionProjectionTablesAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid securityId,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"delete from {Qualified(OptionContractProjectionTable)} where security_id = @security_id;";
        command.Parameters.AddWithValue("security_id", securityId);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private async Task<SecurityProjectionRecord?> GetProjectionCoreAsync(NpgsqlConnection connection, Guid securityId, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            select security_id,
                   asset_class,
                   status,
                   display_name,
                   currency,
                   primary_identifier_kind,
                   primary_identifier_value,
                   common_terms::text,
                   asset_specific_terms::text,
                   provenance::text,
                   version,
                   effective_from,
                   effective_to
            from {Qualified("securities")}
            where security_id = @security_id;
            """;
        command.Parameters.AddWithValue("security_id", securityId);

        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            return null;
        }

        var projection = new SecurityProjectionRecord(
            reader.GetGuid(0),
            reader.GetString(1),
            SecurityMasterEnumReads.ParseOrFallback(reader.GetString(2), SecurityStatusDto.Unknown),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetString(5),
            reader.GetString(6),
            JsonDocument.Parse(reader.GetString(7)).RootElement.Clone(),
            JsonDocument.Parse(reader.GetString(8)).RootElement.Clone(),
            JsonDocument.Parse(reader.GetString(9)).RootElement.Clone(),
            reader.GetInt64(10),
            new DateTimeOffset(reader.GetDateTime(11), TimeSpan.Zero),
            reader.IsDBNull(12) ? null : new DateTimeOffset(reader.GetDateTime(12), TimeSpan.Zero),
            Array.Empty<SecurityIdentifierDto>(),
            Array.Empty<SecurityAliasDto>());

        await reader.CloseAsync().ConfigureAwait(false);

        var identifiers = await LoadIdentifiersAsync(connection, securityId, ct).ConfigureAwait(false);
        var aliases = await LoadAliasesAsync(connection, securityId, ct).ConfigureAwait(false);
        return projection with { Identifiers = identifiers, Aliases = aliases };
    }

    private async Task<IReadOnlyList<SecurityIdentifierDto>> LoadIdentifiersAsync(NpgsqlConnection connection, Guid securityId, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            select identifier_kind, identifier_value, is_primary, valid_from, valid_to, provider,
                   normalized_identifier_value, normalized_provider
            from {Qualified("security_identifiers")}
            where security_id = @security_id
            order by is_primary desc, identifier_kind, identifier_value;
            """;
        command.Parameters.AddWithValue("security_id", securityId);

        var results = new List<SecurityIdentifierDto>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            // Read-tolerant: an identifier kind written by a newer node degrades to Unknown so the
            // row (and every other identifier on the security) stays readable.
            var kind = SecurityMasterEnumReads.ParseOrFallback(reader.GetString(0), SecurityIdentifierKind.Unknown);
            var value = reader.GetString(1);
            var provider = reader.IsDBNull(5) ? null : reader.GetString(5);
            var normalizedValue = reader.IsDBNull(6)
                ? SecurityIdentifierNormalizer.NormalizeValue(kind, value)
                : reader.GetString(6);
            var normalizedProvider = reader.IsDBNull(7)
                ? SecurityIdentifierNormalizer.NormalizeProvider(provider)
                : reader.GetString(7);
            results.Add(new SecurityIdentifierDto(
                kind,
                value,
                reader.GetBoolean(2),
                new DateTimeOffset(reader.GetDateTime(3), TimeSpan.Zero),
                reader.IsDBNull(4) ? null : new DateTimeOffset(reader.GetDateTime(4), TimeSpan.Zero),
                provider,
                normalizedValue,
                normalizedProvider));
        }

        return results;
    }

    private async Task<long> ReadAliasRevisionStampAsync(NpgsqlConnection connection, Guid securityId, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"select count(*) from {Qualified("security_alias_revisions")} where security_id = @security_id;";
        command.Parameters.AddWithValue("security_id", securityId);
        return (long)(await command.ExecuteScalarAsync(ct).ConfigureAwait(false))!;
    }

    /// <summary>
    /// Reads the current aliases as a <see cref="SecurityAliasSnapshot"/>. The revision stamp is read
    /// first: an alias write committing between the two reads leaves the stamp older than the rows,
    /// which a later projection replacement treats as stale (it keeps the rows) rather than current.
    /// </summary>
    private async Task<SecurityAliasSnapshot> LoadAliasesAsync(NpgsqlConnection connection, Guid securityId, CancellationToken ct)
    {
        var revisionStamp = await ReadAliasRevisionStampAsync(connection, securityId, ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            select alias_id, alias_kind, alias_value, provider, scope, reason,
                   created_by, created_at, valid_from, valid_to, is_enabled
            from {Qualified("security_aliases")}
            where security_id = @security_id
            order by alias_kind, alias_value;
            """;
        command.Parameters.AddWithValue("security_id", securityId);

        var results = new List<SecurityAliasDto>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            results.Add(new SecurityAliasDto(
                reader.GetGuid(0),
                securityId,
                reader.GetString(1),
                reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                SecurityMasterEnumReads.ParseOrFallback(reader.GetString(4), SecurityAliasScope.Unknown),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.GetString(6),
                new DateTimeOffset(reader.GetDateTime(7), TimeSpan.Zero),
                new DateTimeOffset(reader.GetDateTime(8), TimeSpan.Zero),
                reader.IsDBNull(9) ? null : new DateTimeOffset(reader.GetDateTime(9), TimeSpan.Zero),
                reader.GetBoolean(10)));
        }

        return new SecurityAliasSnapshot(results, revisionStamp);
    }

    private async Task<Guid?> ResolveSecurityIdAsync(
        NpgsqlConnection connection,
        SecurityIdentifierKind kind,
        string value,
        string? provider,
        DateTimeOffset asOfUtc,
        bool includeInactive,
        CancellationToken ct)
    {
        var identifierKind = kind.ToString();
        var normalizedIdentifierValue = SecurityIdentifierNormalizer.NormalizeValue(kind, value);
        var normalizedProvider = ToNullableString(SecurityIdentifierNormalizer.NormalizeProvider(provider));
        if (normalizedIdentifierValue.Length == 0)
        {
            return null;
        }

        foreach (var sql in new[]
        {
            $"""
            select s.security_id
            from {Qualified("security_identifiers")} i
            join {Qualified("securities")} s on s.security_id = i.security_id
            where i.identifier_kind = @identifier_kind
              and i.normalized_identifier_value = @normalized_identifier_value
              and ((@normalized_provider is null and i.normalized_provider is null) or i.normalized_provider = @normalized_provider)
              and i.valid_from <= @as_of
              and (i.valid_to is null or i.valid_to > @as_of)
              and (@include_inactive = true or s.status = 'Active')
            order by i.is_primary desc
            limit 1;
            """,
            $"""
            select s.security_id
            from {Qualified("security_aliases")} a
            join {Qualified("securities")} s on s.security_id = a.security_id
            where a.alias_kind = @identifier_kind
              and a.normalized_alias_value = @normalized_identifier_value
              and ((@normalized_provider is null and a.normalized_provider is null) or a.normalized_provider = @normalized_provider)
              and a.valid_from <= @as_of
              and (a.valid_to is null or a.valid_to > @as_of)
              and a.is_enabled = true
              and (@include_inactive = true or s.status = 'Active')
            limit 1;
            """,
            $"""
            select s.security_id
            from {Qualified("securities")} s
            where s.primary_identifier_kind = @identifier_kind
              and s.normalized_primary_identifier_value = @normalized_identifier_value
              and @normalized_provider is null
              and not exists (
                  select 1
                  from {Qualified("security_identifiers")} i
                  where i.security_id = s.security_id
                    and i.identifier_kind = @identifier_kind
                    and i.normalized_identifier_value = @normalized_identifier_value)
              and (@include_inactive = true or s.status = 'Active')
            limit 1;
            """
        })
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.Parameters.AddWithValue("identifier_kind", identifierKind);
            command.Parameters.AddWithValue("normalized_identifier_value", normalizedIdentifierValue);
            command.Parameters.Add(new NpgsqlParameter("normalized_provider", NpgsqlTypes.NpgsqlDbType.Text) { Value = (object?)normalizedProvider ?? DBNull.Value });
            command.Parameters.AddWithValue("as_of", asOfUtc.UtcDateTime);
            command.Parameters.AddWithValue("include_inactive", includeInactive);

            var result = await command.ExecuteScalarAsync(ct).ConfigureAwait(false);
            if (result is Guid guid)
            {
                return guid;
            }
        }

        return null;
    }

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

    private async Task UpsertOptionContractProjectionAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        OptionProjectionWriteModel projection,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"""
            insert into {Qualified(OptionContractProjectionTable)} (
                contract_symbol, security_id, option_chain_id, underlying_symbol, underlying_security_id,
                put_call, strike, expiry_date, multiplier, is_adjusted, last_trading_dt, lifecycle_stat, updated_at, version)
            values (
                @contract_symbol, @security_id, @option_chain_id, @underlying_symbol, @underlying_security_id,
                @put_call, @strike, @expiry_date, @multiplier, @is_adjusted, @last_trading_dt, @lifecycle_stat, @updated_at, @version)
            on conflict (contract_symbol) do update set
                security_id = excluded.security_id,
                option_chain_id = excluded.option_chain_id,
                underlying_symbol = excluded.underlying_symbol,
                underlying_security_id = excluded.underlying_security_id,
                put_call = excluded.put_call,
                strike = excluded.strike,
                expiry_date = excluded.expiry_date,
                multiplier = excluded.multiplier,
                is_adjusted = excluded.is_adjusted,
                last_trading_dt = excluded.last_trading_dt,
                lifecycle_stat = excluded.lifecycle_stat,
                updated_at = excluded.updated_at,
                version = excluded.version;
            """;
        AddOptionProjectionParameters(command, projection);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private async Task UpsertOptionSeriesProjectionAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        OptionProjectionWriteModel projection,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"""
            insert into {Qualified(OptionSeriesProjectionTable)} (
                option_chain_id, contract_symbol, underlying_symbol, expiry_date, strike, put_call)
            values (
                @option_chain_id, @contract_symbol, @underlying_symbol, @expiry_date, @strike, @put_call)
            on conflict (option_chain_id, contract_symbol) do update set
                underlying_symbol = excluded.underlying_symbol,
                expiry_date = excluded.expiry_date,
                strike = excluded.strike,
                put_call = excluded.put_call;
            """;
        command.Parameters.AddWithValue("option_chain_id", projection.OptionChainId);
        command.Parameters.AddWithValue("contract_symbol", projection.ContractSymbol);
        command.Parameters.AddWithValue("underlying_symbol", projection.UnderlyingSymbol);
        command.Parameters.AddWithValue("expiry_date", projection.ExpiryDate.ToDateTime(TimeOnly.MinValue));
        command.Parameters.AddWithValue("strike", projection.Strike);
        command.Parameters.AddWithValue("put_call", projection.PutCall);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private async Task UpsertOptionLifecycleProjectionAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        OptionProjectionWriteModel projection,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"""
            insert into {Qualified(OptionLifecycleProjectionTable)} (
                contract_symbol, lifecycle_stat, listed_at, last_trading_dt, expiry_date, version)
            values (
                @contract_symbol, @lifecycle_stat, @listed_at, @last_trading_dt, @expiry_date, @version)
            on conflict (contract_symbol) do update set
                lifecycle_stat = excluded.lifecycle_stat,
                last_trading_dt = excluded.last_trading_dt,
                expiry_date = excluded.expiry_date,
                version = excluded.version;
            """;
        command.Parameters.AddWithValue("contract_symbol", projection.ContractSymbol);
        command.Parameters.AddWithValue("lifecycle_stat", projection.LifecycleStat);
        command.Parameters.AddWithValue("listed_at", projection.ListedAt.UtcDateTime);
        command.Parameters.AddWithValue("last_trading_dt", (object?)projection.LastTradingDate?.ToDateTime(TimeOnly.MinValue) ?? DBNull.Value);
        command.Parameters.AddWithValue("expiry_date", projection.ExpiryDate.ToDateTime(TimeOnly.MinValue));
        command.Parameters.AddWithValue("version", projection.Version);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private async Task UpsertOptionAliasProjectionAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        OptionProjectionWriteModel projection,
        IReadOnlyList<SecurityAliasDto> aliases,
        CancellationToken ct)
    {
        await using (var delete = connection.CreateCommand())
        {
            delete.Transaction = transaction;
            delete.CommandText = $"delete from {Qualified(OptionAliasProjectionTable)} where contract_symbol = @contract_symbol;";
            delete.Parameters.AddWithValue("contract_symbol", projection.ContractSymbol);
            await delete.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        foreach (var alias in aliases)
        {
            await using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText =
                $"""
                insert into {Qualified(OptionAliasProjectionTable)} (
                    contract_symbol, alias_kind, alias_value, provider, normalized_alias)
                values (
                    @contract_symbol, @alias_kind, @alias_value, @provider, @normalized_alias);
                """;
            insert.Parameters.AddWithValue("contract_symbol", projection.ContractSymbol);
            insert.Parameters.AddWithValue("alias_kind", alias.AliasKind);
            insert.Parameters.AddWithValue("alias_value", alias.AliasValue);
            insert.Parameters.AddWithValue("provider", alias.Provider ?? string.Empty);
            insert.Parameters.AddWithValue("normalized_alias", alias.AliasValue.Trim().ToUpperInvariant());
            await insert.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
    }

    private static void AddOptionProjectionParameters(NpgsqlCommand command, OptionProjectionWriteModel projection)
    {
        command.Parameters.AddWithValue("contract_symbol", projection.ContractSymbol);
        command.Parameters.AddWithValue("security_id", projection.SecurityId);
        command.Parameters.AddWithValue("option_chain_id", projection.OptionChainId);
        command.Parameters.AddWithValue("underlying_symbol", projection.UnderlyingSymbol);
        command.Parameters.AddWithValue("underlying_security_id", (object?)projection.UnderlyingSecurityId ?? DBNull.Value);
        command.Parameters.AddWithValue("put_call", projection.PutCall);
        command.Parameters.AddWithValue("strike", projection.Strike);
        command.Parameters.AddWithValue("expiry_date", projection.ExpiryDate.ToDateTime(TimeOnly.MinValue));
        command.Parameters.AddWithValue("multiplier", projection.Multiplier);
        command.Parameters.AddWithValue("is_adjusted", projection.IsAdjusted);
        command.Parameters.AddWithValue("last_trading_dt", (object?)projection.LastTradingDate?.ToDateTime(TimeOnly.MinValue) ?? DBNull.Value);
        command.Parameters.AddWithValue("lifecycle_stat", projection.LifecycleStat);
        command.Parameters.AddWithValue("updated_at", projection.ListedAt.UtcDateTime);
        command.Parameters.AddWithValue("version", projection.Version);
    }

    private static bool TryBuildOptionProjection(SecurityProjectionRecord record, out OptionProjectionWriteModel projection)
    {
        if (!TryGetOptionalDateOnly(record.AssetSpecificTerms, "expiry", out var expiryDate) ||
            expiryDate is null)
        {
            projection = default!;
            return false;
        }

        var contractSymbol = record.PrimaryIdentifierValue.Trim().ToUpperInvariant();
        var underlyingSymbol = GetOptionalString(record.AssetSpecificTerms, "underlyingSymbol")
            ?? GetOptionalString(record.CommonTerms, "underlyingSymbol")
            ?? "UNKNOWN";

        var putCall = GetOptionalString(record.AssetSpecificTerms, "putCall") ?? "Call";
        var strike = GetOptionalDecimal(record.AssetSpecificTerms, "strike");
        var multiplier = GetOptionalDecimal(record.AssetSpecificTerms, "multiplier");

        if (strike is null || multiplier is null)
        {
            projection = default!;
            return false;
        }

        Guid? underlyingSecurityId = null;
        if (record.AssetSpecificTerms.TryGetProperty("underlyingId", out var underlyingIdEl) &&
            underlyingIdEl.ValueKind == JsonValueKind.String &&
            Guid.TryParse(underlyingIdEl.GetString(), out var parsed))
        {
            underlyingSecurityId = parsed;
        }

        var optionChainId = GetOptionalString(record.AssetSpecificTerms, "optChainId");
        if (string.IsNullOrWhiteSpace(optionChainId))
        {
            optionChainId = BuildOptionChainId(underlyingSymbol, expiryDate.Value);
        }

        var lastTradingDate = GetOptionalDateOnly(record.AssetSpecificTerms, "lastTradingDt") ?? expiryDate;
        var lifecycleStat =
            record.EffectiveTo.HasValue ? "Retired" :
            expiryDate.Value < DateOnly.FromDateTime(DateTime.UtcNow) ? "Expired" :
            "Active";

        projection = new OptionProjectionWriteModel(
            contractSymbol,
            record.SecurityId,
            optionChainId!,
            underlyingSymbol.Trim().ToUpperInvariant(),
            underlyingSecurityId,
            putCall,
            strike.Value,
            expiryDate.Value,
            multiplier.Value,
            GetOptionalBool(record.AssetSpecificTerms, "isAdjusted") ?? false,
            lastTradingDate,
            lifecycleStat,
            record.EffectiveFrom,
            record.Version);
        return true;
    }

    private static string BuildOptionChainId(string underlyingSymbol, DateOnly expiryDate)
        => $"{underlyingSymbol.Trim().ToUpperInvariant()}-{expiryDate:yyyyMMdd}";







    /// <summary>
    /// Resolves the queryable <c>swap_type</c> from the swap terms. The serializer emits swap economics
    /// as <c>legs</c> (per the Swap entry in <see cref="SecurityAssetTermsSchema"/>) and never a
    /// <c>swapType</c> field, so reading only <c>swapType</c> left the column null for every canonically
    /// serialized swap; derive it from the leg types instead, honouring an explicit <c>swapType</c> when a
    /// non-canonical payload supplies one.
    /// </summary>
    internal static string? DeriveSwapType(JsonElement assetSpecificTerms)
    {
        var explicitType = GetOptionalString(assetSpecificTerms, "swapType");
        if (!string.IsNullOrWhiteSpace(explicitType))
        {
            return explicitType;
        }

        if (!assetSpecificTerms.TryGetProperty("legs", out var legs) || legs.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var legTypes = new List<string>();
        var hasFixed = false;
        var hasFloating = false;
        foreach (var leg in legs.EnumerateArray())
        {
            var legType = GetOptionalString(leg, "legType");
            if (string.IsNullOrWhiteSpace(legType))
            {
                continue;
            }

            legType = legType.Trim();
            var alreadySeen = false;
            foreach (var seen in legTypes)
            {
                if (string.Equals(seen, legType, StringComparison.OrdinalIgnoreCase))
                {
                    alreadySeen = true;
                    break;
                }
            }

            if (!alreadySeen)
            {
                legTypes.Add(legType);
            }

            if (string.Equals(legType, "Fixed", StringComparison.OrdinalIgnoreCase))
            {
                hasFixed = true;
            }
            else if (string.Equals(legType, "Floating", StringComparison.OrdinalIgnoreCase))
            {
                hasFloating = true;
            }
        }

        if (legTypes.Count == 0)
        {
            return null;
        }

        if (hasFixed && hasFloating)
        {
            return "FixedFloat";
        }

        if (hasFloating && !hasFixed && legTypes.Count == 1)
        {
            return "BasisFloat";
        }

        return string.Join("/", legTypes);
    }


    /// <summary>
    /// The ISO 10383 MIC of the primary listing: a well-formed <c>primaryListingMic</c> wins,
    /// otherwise <c>exchange</c> resolves through <see cref="Meridian.Contracts.Domain.MicReference"/>.
    /// Migration 048 backfills existing rows with the same rule.
    /// </summary>
    internal static string? ResolveListingMic(JsonElement commonTerms)
    {
        if (Meridian.Contracts.Domain.Mic.TryParse(GetOptionalString(commonTerms, "primaryListingMic"), out var primary))
        {
            return primary.Value;
        }

        return Meridian.Contracts.Domain.MicReference.TryResolve(GetOptionalString(commonTerms, "exchange"), out var resolved)
            ? resolved.Value
            : null;
    }

    internal static string? GetOptionalString(JsonElement json, string propertyName)
        => json.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    /// <summary>
    /// The terms-payload family written to the <c>terms_format</c> column (migration 042). A declared
    /// <see cref="SecurityTermsEnvelope.FormatKey"/> wins; a payload that predates the tag is
    /// classified by the same shape rule the migration backfill applied, so the column has one
    /// definition whether the row was migrated or written. The stored payload is never mutated to add
    /// the tag — writers own stamping. A declared format this node does not know is refused here with
    /// a precise reason rather than by the column's check constraint.
    /// </summary>
    internal static string ResolveStorableTermsFormat(SecurityProjectionRecord record)
    {
        var format = SecurityTermsEnvelope.ResolveFormat(record.AssetSpecificTerms);
        if (!SecurityTermsFormats.IsKnown(format))
        {
            throw new InvalidOperationException(
                $"Security '{record.SecurityId}' ({record.AssetClass}) declares {SecurityTermsEnvelope.FormatKey} '{format}', " +
                $"which is not a storable terms format; expected one of {string.Join(", ", SecurityTermsFormats.All)}.");
        }

        return format;
    }

    private static string NormalizePrimaryIdentifierValue(SecurityProjectionRecord record)
        => Enum.TryParse<SecurityIdentifierKind>(record.PrimaryIdentifierKind, ignoreCase: true, out var kind)
            ? SecurityIdentifierNormalizer.NormalizeValue(kind, record.PrimaryIdentifierValue)
            : SecurityIdentifierNormalizer.NormalizeAliasValue(record.PrimaryIdentifierKind, record.PrimaryIdentifierValue);

    private static object ToDbNullable(string? value)
        => string.IsNullOrWhiteSpace(value) ? DBNull.Value : value;

    private static string? ToNullableString(string value)
        => string.IsNullOrWhiteSpace(value) ? null : value;

    // The value kind is checked BEFORE the value is read, matching the sibling readers above and
    // below and the registry-driven DecodeTerm in PostgresSecurityMasterStore.TermsProjection.cs.
    // JsonElement.TryGetDecimal / TryGetInt32 throw InvalidOperationException on a non-number
    // element — including the JSON null the canonical F# serializer writes for every optional term
    // the record does not carry (a security with no lotSize is an ordinary record) — and these two
    // readers serve the core `securities` upsert and every hand-written projection writer, so an
    // unchecked read could abort the whole projection transaction on a legitimate row.
    internal static decimal? GetOptionalDecimal(JsonElement json, string propertyName)
        => json.TryGetProperty(propertyName, out var value)
           && value.ValueKind == JsonValueKind.Number
           && value.TryGetDecimal(out var decimalValue)
            ? decimalValue
            : null;

    internal static int? GetOptionalInt(JsonElement json, string propertyName)
        => json.TryGetProperty(propertyName, out var value)
           && value.ValueKind == JsonValueKind.Number
           && value.TryGetInt32(out var intValue)
            ? intValue
            : null;

    internal static bool? GetOptionalBool(JsonElement json, string propertyName)
        => json.TryGetProperty(propertyName, out var value) &&
            value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : null;

    internal static JsonElement? GetOptionalObject(JsonElement json, string propertyName)
        => json.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.Object
            ? value
            : null;

    internal static DateOnly? GetOptionalDateOnly(JsonElement json, string propertyName)
        => TryGetOptionalDateOnly(json, propertyName, out var value) ? value : null;

    private static bool TryGetOptionalDateOnly(JsonElement json, string propertyName, out DateOnly? value)
    {
        if (json.TryGetProperty(propertyName, out var element) &&
            element.ValueKind == JsonValueKind.String &&
            DateOnly.TryParse(element.GetString(), out var parsed))
        {
            value = parsed;
            return true;
        }

        value = null;
        return false;
    }

    private sealed record OptionProjectionWriteModel(
        string ContractSymbol,
        Guid SecurityId,
        string OptionChainId,
        string UnderlyingSymbol,
        Guid? UnderlyingSecurityId,
        string PutCall,
        decimal Strike,
        DateOnly ExpiryDate,
        decimal Multiplier,
        bool IsAdjusted,
        DateOnly? LastTradingDate,
        string LifecycleStat,
        DateTimeOffset ListedAt,
        long Version);
}
