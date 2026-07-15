using System.Collections.Immutable;
using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Meridian.Reporting;
using Npgsql;
using NpgsqlTypes;

namespace Meridian.Storage.Reporting;

/// <summary>
/// PostgreSQL implementation of the governed-reporting transaction boundary. Current aggregate
/// state is versioned optimistically while audit events are appended to a separately protected
/// hash chain in the same database transaction.
/// </summary>
public sealed class PostgresReportingGovernanceRepository : IReportingGovernanceRepository
{
    private readonly ReportingArtifactStoreOptions _options;
    private readonly string _runsTable;
    private readonly string _restatementTable;
    private readonly string _auditTable;

    public PostgresReportingGovernanceRepository(ReportingArtifactStoreOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        ArgumentException.ThrowIfNullOrWhiteSpace(_options.ConnectionString);
        ValidateIdentifier(_options.Schema, nameof(options.Schema));

        var schema = QuoteIdentifier(_options.Schema);
        _runsTable = $"{schema}.\"reporting_governed_runs\"";
        _restatementTable = $"{schema}.\"reporting_restatement_requests\"";
        _auditTable = $"{schema}.\"reporting_governance_audit\"";
    }

    public async ValueTask<TResult> ExecuteTransactionAsync<TResult>(
        Func<IReportingGovernanceTransaction, CancellationToken, ValueTask<TResult>> operation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);

        await using var connection = new NpgsqlConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection
            .BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
            .ConfigureAwait(false);

        var governanceTransaction = new PostgresReportingGovernanceTransaction(
            connection,
            transaction,
            _runsTable,
            _restatementTable,
            _auditTable);

        try
        {
            var result = await operation(governanceTransaction, cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return result;
        }
        catch (PostgresException exception) when (
            exception.SqlState is PostgresErrorCodes.SerializationFailure or PostgresErrorCodes.DeadlockDetected)
        {
            throw new ReportingGovernanceConcurrencyException(
                "The governed reporting transaction conflicted with another writer; reload the aggregate and retry.");
        }
    }

    private static string QuoteIdentifier(string value) => $"\"{value}\"";

    private static void ValidateIdentifier(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value)
            || !(char.IsAsciiLetter(value[0]) || value[0] == '_')
            || !value.All(static character => char.IsAsciiLetterOrDigit(character) || character == '_'))
        {
            throw new ArgumentException(
                $"PostgreSQL identifier '{value}' is not supported. Use letters, digits, and underscores, and start with a letter or underscore.",
                parameterName);
        }
    }

    private sealed class PostgresReportingGovernanceTransaction : IReportingGovernanceTransaction
    {
        private const int MaximumKeyLength = 256;

        private readonly NpgsqlConnection _connection;
        private readonly NpgsqlTransaction _transaction;
        private readonly string _runsTable;
        private readonly string _restatementTable;
        private readonly string _auditTable;

        public PostgresReportingGovernanceTransaction(
            NpgsqlConnection connection,
            NpgsqlTransaction transaction,
            string runsTable,
            string restatementTable,
            string auditTable)
        {
            _connection = connection;
            _transaction = transaction;
            _runsTable = runsTable;
            _restatementTable = restatementTable;
            _auditTable = auditTable;
        }

        public async ValueTask<GovernedReportingRun?> GetRunAsync(
            string tenantId,
            string runId,
            CancellationToken cancellationToken = default)
        {
            tenantId = NormalizeKey(tenantId, nameof(tenantId));
            runId = NormalizeKey(runId, nameof(runId));

            var row = await ReadRunRowAsync(tenantId, runId, cancellationToken).ConfigureAwait(false);
            return row is null
                ? null
                : await HydrateRunAsync(row, cancellationToken).ConfigureAwait(false);
        }

        public async ValueTask<IReadOnlyList<GovernedReportingRun>> ListRunsBySeriesAsync(
            string tenantId,
            string seriesId,
            CancellationToken cancellationToken = default)
        {
            tenantId = NormalizeKey(tenantId, nameof(tenantId));
            seriesId = NormalizeKey(seriesId, nameof(seriesId));

            var rows = new List<PersistedRunRow>();
            await using (var command = CreateCommand())
            {
                command.CommandText =
                    $"""
                    select tenant_id,
                           run_id,
                           series_id,
                           revision,
                           aggregate_version,
                           execution_state,
                           governance_state,
                           state_payload,
                           state_hash_sha256
                    from {_runsTable}
                    where tenant_id = @tenant_id
                      and series_id = @series_id
                    order by revision;
                    """;
                command.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Text, tenantId);
                command.Parameters.AddWithValue("series_id", NpgsqlDbType.Text, seriesId);

                await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    rows.Add(ReadRunRow(reader));
                }
            }

            var runs = new List<GovernedReportingRun>(rows.Count);
            foreach (var row in rows)
            {
                runs.Add(await HydrateRunAsync(row, cancellationToken).ConfigureAwait(false));
            }

            return runs;
        }

        public async ValueTask AddRunAsync(
            GovernedReportingRun run,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(run);
            ValidateRunForWrite(run, expectedVersion: null);

            var payload = SerializeRun(run with { AuditTrail = [] });
            var payloadHash = ComputeSha256(payload);

            try
            {
                await using var command = CreateCommand();
                command.CommandText =
                    $"""
                    insert into {_runsTable} (
                        tenant_id,
                        run_id,
                        series_id,
                        revision,
                        organization_id,
                        company_id,
                        aggregate_version,
                        execution_state,
                        governance_state,
                        state_payload,
                        state_hash_sha256,
                        created_at_utc,
                        updated_at_utc)
                    values (
                        @tenant_id,
                        @run_id,
                        @series_id,
                        @revision,
                        @organization_id,
                        @company_id,
                        @aggregate_version,
                        @execution_state,
                        @governance_state,
                        @state_payload,
                        @state_hash_sha256,
                        @created_at_utc,
                        @created_at_utc);
                    """;
                AddRunParameters(command, run, payload, payloadHash);
                await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UniqueViolation)
            {
                throw new ReportingGovernanceConcurrencyException(
                    $"Reporting run '{run.RunId}' or series revision '{run.SeriesId}/{run.Revision}' already exists.");
            }

            await AppendAuditAsync(run.Scope.TenantId, run.AuditTrail[^1], cancellationToken).ConfigureAwait(false);
        }

        public async ValueTask ReplaceRunAsync(
            GovernedReportingRun run,
            long expectedVersion,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(run);
            ValidateRunForWrite(run, expectedVersion);

            var payload = SerializeRun(run with { AuditTrail = [] });
            var payloadHash = ComputeSha256(payload);
            await using var command = CreateCommand();
            command.CommandText =
                $"""
                update {_runsTable}
                set aggregate_version = @aggregate_version,
                    execution_state = @execution_state,
                    governance_state = @governance_state,
                    state_payload = @state_payload,
                    state_hash_sha256 = @state_hash_sha256,
                    updated_at_utc = now()
                where tenant_id = @tenant_id
                  and run_id = @run_id
                  and aggregate_version = @expected_version;
                """;
            AddRunParameters(command, run, payload, payloadHash);
            command.Parameters.AddWithValue("expected_version", NpgsqlDbType.Bigint, expectedVersion);

            if (await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
            {
                throw new ReportingGovernanceConcurrencyException(
                    $"Reporting run '{run.RunId}' version conflict: expected {expectedVersion}.");
            }

            await AppendAuditAsync(run.Scope.TenantId, run.AuditTrail[^1], cancellationToken).ConfigureAwait(false);
        }

        public async ValueTask<ReportingRestatementRequest?> GetRestatementRequestAsync(
            string tenantId,
            string requestId,
            CancellationToken cancellationToken = default)
        {
            tenantId = NormalizeKey(tenantId, nameof(tenantId));
            requestId = NormalizeKey(requestId, nameof(requestId));

            PersistedRestatementRow? row;
            await using (var command = CreateCommand())
            {
                command.CommandText =
                    $"""
                    select tenant_id,
                           request_id,
                           series_id,
                           predecessor_run_id,
                           aggregate_version,
                           request_state,
                           state_payload,
                           state_hash_sha256
                    from {_restatementTable}
                    where tenant_id = @tenant_id
                      and request_id = @request_id;
                    """;
                command.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Text, tenantId);
                command.Parameters.AddWithValue("request_id", NpgsqlDbType.Text, requestId);

                await using var reader = await command.ExecuteReaderAsync(
                    CommandBehavior.SingleRow,
                    cancellationToken).ConfigureAwait(false);
                row = await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
                    ? ReadRestatementRow(reader)
                    : null;
            }

            return row is null
                ? null
                : await HydrateRestatementAsync(row, cancellationToken).ConfigureAwait(false);
        }

        public async ValueTask AddRestatementRequestAsync(
            string tenantId,
            ReportingRestatementRequest request,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            tenantId = NormalizeKey(tenantId, nameof(tenantId));
            ValidateRestatementForWrite(tenantId, request, expectedVersion: null);

            var payload = SerializeRestatement(request with { AuditTrail = [] });
            var payloadHash = ComputeSha256(payload);

            try
            {
                await using var command = CreateCommand();
                command.CommandText =
                    $"""
                    insert into {_restatementTable} (
                        tenant_id,
                        request_id,
                        series_id,
                        predecessor_run_id,
                        aggregate_version,
                        request_state,
                        state_payload,
                        state_hash_sha256,
                        created_at_utc,
                        updated_at_utc)
                    values (
                        @tenant_id,
                        @request_id,
                        @series_id,
                        @predecessor_run_id,
                        @aggregate_version,
                        @request_state,
                        @state_payload,
                        @state_hash_sha256,
                        @created_at_utc,
                        @created_at_utc);
                    """;
                AddRestatementParameters(command, tenantId, request, payload, payloadHash);
                await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UniqueViolation)
            {
                throw new ReportingGovernanceConcurrencyException(
                    $"Reporting restatement request '{request.RequestId}' already exists.");
            }

            await AppendAuditAsync(tenantId, request.AuditTrail[^1], cancellationToken).ConfigureAwait(false);
        }

        public async ValueTask ReplaceRestatementRequestAsync(
            string tenantId,
            ReportingRestatementRequest request,
            long expectedVersion,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            tenantId = NormalizeKey(tenantId, nameof(tenantId));
            ValidateRestatementForWrite(tenantId, request, expectedVersion);

            var payload = SerializeRestatement(request with { AuditTrail = [] });
            var payloadHash = ComputeSha256(payload);
            await using var command = CreateCommand();
            command.CommandText =
                $"""
                update {_restatementTable}
                set aggregate_version = @aggregate_version,
                    request_state = @request_state,
                    state_payload = @state_payload,
                    state_hash_sha256 = @state_hash_sha256,
                    updated_at_utc = now()
                where tenant_id = @tenant_id
                  and request_id = @request_id
                  and aggregate_version = @expected_version;
                """;
            AddRestatementParameters(command, tenantId, request, payload, payloadHash);
            command.Parameters.AddWithValue("expected_version", NpgsqlDbType.Bigint, expectedVersion);

            if (await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
            {
                throw new ReportingGovernanceConcurrencyException(
                    $"Reporting restatement request '{request.RequestId}' version conflict: expected {expectedVersion}.");
            }

            await AppendAuditAsync(tenantId, request.AuditTrail[^1], cancellationToken).ConfigureAwait(false);
        }

        private async Task<PersistedRunRow?> ReadRunRowAsync(
            string tenantId,
            string runId,
            CancellationToken cancellationToken)
        {
            await using var command = CreateCommand();
            command.CommandText =
                $"""
                select tenant_id,
                       run_id,
                       series_id,
                       revision,
                       aggregate_version,
                       execution_state,
                       governance_state,
                       state_payload,
                       state_hash_sha256
                from {_runsTable}
                where tenant_id = @tenant_id
                  and run_id = @run_id;
                """;
            command.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Text, tenantId);
            command.Parameters.AddWithValue("run_id", NpgsqlDbType.Text, runId);

            await using var reader = await command.ExecuteReaderAsync(
                CommandBehavior.SingleRow,
                cancellationToken).ConfigureAwait(false);
            return await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
                ? ReadRunRow(reader)
                : null;
        }

        private async Task<GovernedReportingRun> HydrateRunAsync(
            PersistedRunRow row,
            CancellationToken cancellationToken)
        {
            VerifyPayloadChecksum("reporting run", row.RunId, row.StatePayload, row.StateHashSha256);
            var run = DeserializeRun(row.StatePayload, row.RunId);
            if (!run.AuditTrail.IsDefaultOrEmpty)
            {
                throw Integrity("reporting run", row.RunId, "the retained state payload contains mutable audit history");
            }

            if (!StringComparer.Ordinal.Equals(run.Scope.TenantId, row.TenantId)
                || !StringComparer.Ordinal.Equals(run.RunId, row.RunId)
                || !StringComparer.Ordinal.Equals(run.SeriesId, row.SeriesId)
                || run.Revision != row.Revision
                || run.Version != row.AggregateVersion
                || (short)run.ExecutionState != row.ExecutionState
                || (short)run.GovernanceState != row.GovernanceState)
            {
                throw Integrity("reporting run", row.RunId, "identity or version columns do not match the retained state payload");
            }

            var audit = await ReadAuditAsync(
                row.TenantId,
                ReportingGovernanceAuditAggregateKind.Run,
                row.RunId,
                cancellationToken).ConfigureAwait(false);
            run = run with { AuditTrail = audit };
            ValidateRunShape(run, requireAudit: true);
            return run;
        }

        private async Task<ReportingRestatementRequest> HydrateRestatementAsync(
            PersistedRestatementRow row,
            CancellationToken cancellationToken)
        {
            VerifyPayloadChecksum("reporting restatement request", row.RequestId, row.StatePayload, row.StateHashSha256);
            var request = DeserializeRestatement(row.StatePayload, row.RequestId);
            if (!request.AuditTrail.IsDefaultOrEmpty)
            {
                throw Integrity(
                    "reporting restatement request",
                    row.RequestId,
                    "the retained state payload contains mutable audit history");
            }

            if (!StringComparer.Ordinal.Equals(request.RequestedBy.TenantId, row.TenantId)
                || !StringComparer.Ordinal.Equals(request.RequestId, row.RequestId)
                || !StringComparer.Ordinal.Equals(request.SeriesId, row.SeriesId)
                || !StringComparer.Ordinal.Equals(request.PredecessorRunId, row.PredecessorRunId)
                || request.Version != row.AggregateVersion
                || (short)request.State != row.RequestState)
            {
                throw Integrity(
                    "reporting restatement request",
                    row.RequestId,
                    "identity or version columns do not match the retained state payload");
            }

            var audit = await ReadAuditAsync(
                row.TenantId,
                ReportingGovernanceAuditAggregateKind.RestatementRequest,
                row.RequestId,
                cancellationToken).ConfigureAwait(false);
            request = request with { AuditTrail = audit };
            ValidateRestatementShape(row.TenantId, request, requireAudit: true);
            return request;
        }

        private async Task<ImmutableArray<ReportingGovernanceAuditEntry>> ReadAuditAsync(
            string tenantId,
            ReportingGovernanceAuditAggregateKind aggregateKind,
            string aggregateId,
            CancellationToken cancellationToken)
        {
            var entries = ImmutableArray.CreateBuilder<ReportingGovernanceAuditEntry>();
            await using var command = CreateCommand();
            command.CommandText =
                $"""
                select aggregate_version,
                       event_id,
                       previous_hash,
                       event_hash,
                       event_payload,
                       payload_hash_sha256
                from {_auditTable}
                where tenant_id = @tenant_id
                  and aggregate_kind = @aggregate_kind
                  and aggregate_id = @aggregate_id
                order by aggregate_version;
                """;
            command.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Text, tenantId);
            command.Parameters.AddWithValue("aggregate_kind", NpgsqlDbType.Smallint, (short)aggregateKind);
            command.Parameters.AddWithValue("aggregate_id", NpgsqlDbType.Text, aggregateId);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var version = reader.GetInt64(0);
                var eventId = reader.GetString(1);
                var previousHash = reader.IsDBNull(2) ? null : reader.GetString(2);
                var eventHash = reader.GetString(3);
                var payload = reader.GetString(4);
                var payloadHash = reader.GetString(5);
                VerifyPayloadChecksum("reporting governance audit event", eventId, payload, payloadHash);

                var entry = DeserializeAudit(payload, eventId);
                if (entry.AggregateKind != aggregateKind
                    || !StringComparer.Ordinal.Equals(entry.AggregateId, aggregateId)
                    || entry.AggregateVersion != version
                    || !StringComparer.Ordinal.Equals(entry.EventId, eventId)
                    || !StringComparer.Ordinal.Equals(entry.PreviousHash, previousHash)
                    || !StringComparer.Ordinal.Equals(entry.Hash, eventHash)
                    || !StringComparer.Ordinal.Equals(entry.Authority.TenantId, tenantId))
                {
                    throw Integrity(
                        "reporting governance audit event",
                        eventId,
                        "indexed columns do not match the retained event payload");
                }

                entries.Add(entry);
            }

            var result = entries.MoveToImmutable();
            if (!ReportingGovernanceAuditChain.Verify(result))
            {
                throw Integrity(
                    "reporting governance audit chain",
                    aggregateId,
                    "the append-only version or SHA-256 chain is incomplete or invalid");
            }

            return result;
        }

        private async Task AppendAuditAsync(
            string tenantId,
            ReportingGovernanceAuditEntry entry,
            CancellationToken cancellationToken)
        {
            var payload = SerializeAudit(entry);
            await using var command = CreateCommand();
            command.CommandText =
                $"""
                insert into {_auditTable} (
                    tenant_id,
                    aggregate_kind,
                    aggregate_id,
                    aggregate_version,
                    event_id,
                    occurred_at_utc,
                    previous_hash,
                    event_hash,
                    event_payload,
                    payload_hash_sha256)
                values (
                    @tenant_id,
                    @aggregate_kind,
                    @aggregate_id,
                    @aggregate_version,
                    @event_id,
                    @occurred_at_utc,
                    @previous_hash,
                    @event_hash,
                    @event_payload,
                    @payload_hash_sha256);
                """;
            command.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Text, tenantId);
            command.Parameters.AddWithValue("aggregate_kind", NpgsqlDbType.Smallint, (short)entry.AggregateKind);
            command.Parameters.AddWithValue("aggregate_id", NpgsqlDbType.Text, entry.AggregateId);
            command.Parameters.AddWithValue("aggregate_version", NpgsqlDbType.Bigint, entry.AggregateVersion);
            command.Parameters.AddWithValue("event_id", NpgsqlDbType.Text, entry.EventId);
            command.Parameters.AddWithValue("occurred_at_utc", NpgsqlDbType.TimestampTz, entry.OccurredAtUtc);
            command.Parameters.AddWithValue(
                "previous_hash",
                NpgsqlDbType.Text,
                (object?)entry.PreviousHash ?? DBNull.Value);
            command.Parameters.AddWithValue("event_hash", NpgsqlDbType.Text, entry.Hash);
            command.Parameters.AddWithValue("event_payload", NpgsqlDbType.Text, payload);
            command.Parameters.AddWithValue("payload_hash_sha256", NpgsqlDbType.Text, ComputeSha256(payload));
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        private NpgsqlCommand CreateCommand()
        {
            var command = _connection.CreateCommand();
            command.Transaction = _transaction;
            return command;
        }

        private static void AddRunParameters(
            NpgsqlCommand command,
            GovernedReportingRun run,
            string payload,
            string payloadHash)
        {
            command.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Text, run.Scope.TenantId);
            command.Parameters.AddWithValue("run_id", NpgsqlDbType.Text, run.RunId);
            command.Parameters.AddWithValue("series_id", NpgsqlDbType.Text, run.SeriesId);
            command.Parameters.AddWithValue("revision", NpgsqlDbType.Integer, run.Revision);
            command.Parameters.AddWithValue("organization_id", NpgsqlDbType.Text, run.Scope.OrganizationId);
            command.Parameters.AddWithValue(
                "company_id",
                NpgsqlDbType.Text,
                (object?)run.Scope.CompanyId ?? DBNull.Value);
            command.Parameters.AddWithValue("aggregate_version", NpgsqlDbType.Bigint, run.Version);
            command.Parameters.AddWithValue("execution_state", NpgsqlDbType.Smallint, (short)run.ExecutionState);
            command.Parameters.AddWithValue("governance_state", NpgsqlDbType.Smallint, (short)run.GovernanceState);
            command.Parameters.AddWithValue("state_payload", NpgsqlDbType.Text, payload);
            command.Parameters.AddWithValue("state_hash_sha256", NpgsqlDbType.Text, payloadHash);
            command.Parameters.AddWithValue("created_at_utc", NpgsqlDbType.TimestampTz, run.CreatedAtUtc);
        }

        private static void AddRestatementParameters(
            NpgsqlCommand command,
            string tenantId,
            ReportingRestatementRequest request,
            string payload,
            string payloadHash)
        {
            command.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Text, tenantId);
            command.Parameters.AddWithValue("request_id", NpgsqlDbType.Text, request.RequestId);
            command.Parameters.AddWithValue("series_id", NpgsqlDbType.Text, request.SeriesId);
            command.Parameters.AddWithValue("predecessor_run_id", NpgsqlDbType.Text, request.PredecessorRunId);
            command.Parameters.AddWithValue("aggregate_version", NpgsqlDbType.Bigint, request.Version);
            command.Parameters.AddWithValue("request_state", NpgsqlDbType.Smallint, (short)request.State);
            command.Parameters.AddWithValue("state_payload", NpgsqlDbType.Text, payload);
            command.Parameters.AddWithValue("state_hash_sha256", NpgsqlDbType.Text, payloadHash);
            command.Parameters.AddWithValue("created_at_utc", NpgsqlDbType.TimestampTz, request.RequestedAtUtc);
        }

        private static void ValidateRunForWrite(GovernedReportingRun run, long? expectedVersion)
        {
            ValidateRunShape(run, requireAudit: true);
            if (expectedVersion is null)
            {
                if (run.Version != 1 || run.Revision < 1)
                {
                    throw new ReportingGovernanceException(
                        "A newly persisted reporting revision must start at aggregate version 1.");
                }
            }
            else if (expectedVersion <= 0 || run.Version != expectedVersion + 1)
            {
                throw new ReportingGovernanceConcurrencyException(
                    $"Reporting run '{run.RunId}' replacement must advance expected version {expectedVersion} by exactly one.");
            }
        }

        private static void ValidateRunShape(GovernedReportingRun run, bool requireAudit)
        {
            RequireKey(run.RunId, nameof(run.RunId));
            RequireKey(run.SeriesId, nameof(run.SeriesId));
            RequireKey(run.TemplateId, nameof(run.TemplateId));
            RequireKey(run.TemplateVersion, nameof(run.TemplateVersion));
            if (run.Scope is null || run.Access is null || run.Snapshot is null || run.CreationAuthority is null)
            {
                throw Integrity("reporting run", run.RunId, "required immutable scope data is missing");
            }

            RequireKey(run.Scope.TenantId, nameof(run.Scope.TenantId));
            RequireKey(run.Scope.OrganizationId, nameof(run.Scope.OrganizationId));
            RequireKey(run.Scope.BookId, nameof(run.Scope.BookId));
            RequireKey(run.Scope.PeriodId, nameof(run.Scope.PeriodId));
            if (run.Revision <= 0 || run.Version <= 0
                || !Enum.IsDefined(run.ExecutionState)
                || !Enum.IsDefined(run.GovernanceState))
            {
                throw Integrity("reporting run", run.RunId, "revision, version, or lifecycle state is invalid");
            }

            if (!StringComparer.Ordinal.Equals(run.Snapshot.TenantId, run.Scope.TenantId)
                || !StringComparer.Ordinal.Equals(run.Snapshot.OrganizationId, run.Scope.OrganizationId)
                || !StringComparer.Ordinal.Equals(run.Snapshot.CompanyId, run.Scope.CompanyId)
                || !StringComparer.Ordinal.Equals(run.Snapshot.FundId, run.Scope.FundId)
                || !StringComparer.Ordinal.Equals(run.Snapshot.BookId, run.Scope.BookId)
                || !StringComparer.Ordinal.Equals(run.Snapshot.PeriodId, run.Scope.PeriodId)
                || !StringComparer.Ordinal.Equals(run.CreationAuthority.TenantId, run.Scope.TenantId)
                || !StringComparer.Ordinal.Equals(run.CreationAuthority.OrganizationId, run.Scope.OrganizationId)
                || !StringComparer.Ordinal.Equals(run.CreationAuthority.CompanyId, run.Scope.CompanyId))
            {
                throw Integrity("reporting run", run.RunId, "snapshot or creation authority escaped the immutable operational scope");
            }

            if ((run.GovernanceState == GovernedReportingState.Released) != (run.Release is not null)
                || (run.GovernanceState >= GovernedReportingState.Approved && run.Approval is null)
                || (run.GovernanceState >= GovernedReportingState.Validated && run.Readiness is null))
            {
                throw Integrity("reporting run", run.RunId, "lifecycle receipts do not match the retained governance state");
            }

            if (requireAudit)
            {
                ValidateAudit(run.Scope.TenantId, ReportingGovernanceAuditAggregateKind.Run, run.RunId, run.Version, run.AuditTrail);
            }
        }

        private static void ValidateRestatementForWrite(
            string tenantId,
            ReportingRestatementRequest request,
            long? expectedVersion)
        {
            ValidateRestatementShape(tenantId, request, requireAudit: true);
            if (expectedVersion is null)
            {
                if (request.Version != 1)
                {
                    throw new ReportingGovernanceException(
                        "A newly persisted reporting restatement request must start at aggregate version 1.");
                }
            }
            else if (expectedVersion <= 0 || request.Version != expectedVersion + 1)
            {
                throw new ReportingGovernanceConcurrencyException(
                    $"Reporting restatement request '{request.RequestId}' replacement must advance expected version {expectedVersion} by exactly one.");
            }
        }

        private static void ValidateRestatementShape(
            string tenantId,
            ReportingRestatementRequest request,
            bool requireAudit)
        {
            RequireKey(request.RequestId, nameof(request.RequestId));
            RequireKey(request.PredecessorRunId, nameof(request.PredecessorRunId));
            RequireKey(request.SeriesId, nameof(request.SeriesId));
            if (request.RequestedBy is null
                || !StringComparer.Ordinal.Equals(request.RequestedBy.TenantId, tenantId)
                || request.Version <= 0
                || request.PredecessorRevision <= 0
                || request.PredecessorVersion <= 0
                || !Enum.IsDefined(request.State))
            {
                throw Integrity("reporting restatement request", request.RequestId, "tenant, predecessor, version, or state is invalid");
            }

            if ((request.State == ReportingRestatementRequestState.Approved)
                != (request.ApprovedBy is not null && request.ApprovedAtUtc is not null && request.DraftRunId is not null))
            {
                throw Integrity("reporting restatement request", request.RequestId, "approval fields do not match the retained request state");
            }

            if (request.ApprovedBy is not null
                && !StringComparer.Ordinal.Equals(request.ApprovedBy.TenantId, tenantId))
            {
                throw Integrity("reporting restatement request", request.RequestId, "approval authority escaped the request tenant");
            }

            if (requireAudit)
            {
                ValidateAudit(
                    tenantId,
                    ReportingGovernanceAuditAggregateKind.RestatementRequest,
                    request.RequestId,
                    request.Version,
                    request.AuditTrail);
            }
        }

        private static void ValidateAudit(
            string tenantId,
            ReportingGovernanceAuditAggregateKind aggregateKind,
            string aggregateId,
            long aggregateVersion,
            ImmutableArray<ReportingGovernanceAuditEntry> audit)
        {
            if (audit.IsDefaultOrEmpty
                || audit.Length != aggregateVersion
                || !ReportingGovernanceAuditChain.Verify(audit)
                || audit.Any(entry =>
                    entry.AggregateKind != aggregateKind
                    || !StringComparer.Ordinal.Equals(entry.AggregateId, aggregateId)
                    || !StringComparer.Ordinal.Equals(entry.Authority.TenantId, tenantId)))
            {
                throw Integrity("reporting governance audit chain", aggregateId, "the aggregate audit chain is incomplete, unbound, or invalid");
            }
        }

        private static void VerifyPayloadChecksum(string kind, string id, string payload, string expectedHash)
        {
            var actualHash = ComputeSha256(payload);
            if (!StringComparer.Ordinal.Equals(actualHash, expectedHash))
            {
                throw Integrity(kind, id, $"payload SHA-256 {actualHash} does not match retained checksum {expectedHash}");
            }
        }

        private static string SerializeRun(GovernedReportingRun run) =>
            JsonSerializer.Serialize(run, ReportingGovernanceJsonContext.Default.GovernedReportingRun);

        private static GovernedReportingRun DeserializeRun(string payload, string id)
        {
            try
            {
                return JsonSerializer.Deserialize(payload, ReportingGovernanceJsonContext.Default.GovernedReportingRun)
                    ?? throw Integrity("reporting run", id, "the retained state payload is null");
            }
            catch (JsonException exception)
            {
                throw Integrity("reporting run", id, "the retained state payload is invalid JSON", exception);
            }
        }

        private static string SerializeRestatement(ReportingRestatementRequest request) =>
            JsonSerializer.Serialize(request, ReportingGovernanceJsonContext.Default.ReportingRestatementRequest);

        private static ReportingRestatementRequest DeserializeRestatement(string payload, string id)
        {
            try
            {
                return JsonSerializer.Deserialize(
                        payload,
                        ReportingGovernanceJsonContext.Default.ReportingRestatementRequest)
                    ?? throw Integrity("reporting restatement request", id, "the retained state payload is null");
            }
            catch (JsonException exception)
            {
                throw Integrity("reporting restatement request", id, "the retained state payload is invalid JSON", exception);
            }
        }

        private static string SerializeAudit(ReportingGovernanceAuditEntry entry) =>
            JsonSerializer.Serialize(entry, ReportingGovernanceJsonContext.Default.ReportingGovernanceAuditEntry);

        private static ReportingGovernanceAuditEntry DeserializeAudit(string payload, string id)
        {
            try
            {
                return JsonSerializer.Deserialize(
                        payload,
                        ReportingGovernanceJsonContext.Default.ReportingGovernanceAuditEntry)
                    ?? throw Integrity("reporting governance audit event", id, "the retained event payload is null");
            }
            catch (JsonException exception)
            {
                throw Integrity("reporting governance audit event", id, "the retained event payload is invalid JSON", exception);
            }
        }

        private static string ComputeSha256(string value) =>
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

        private static string NormalizeKey(string value, string parameterName)
        {
            RequireKey(value, parameterName);
            return value.Trim();
        }

        private static void RequireKey(string? value, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length > MaximumKeyLength)
            {
                throw new ArgumentException(
                    $"Reporting governance keys must contain between 1 and {MaximumKeyLength} characters.",
                    parameterName);
            }
        }

        private static ReportingGovernancePersistenceException Integrity(
            string kind,
            string id,
            string reason,
            Exception? innerException = null) =>
            new($"Retained {kind} '{id}' failed integrity validation: {reason}.", innerException);

        private static PersistedRunRow ReadRunRow(NpgsqlDataReader reader) =>
            new(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetInt32(3),
                reader.GetInt64(4),
                reader.GetInt16(5),
                reader.GetInt16(6),
                reader.GetString(7),
                reader.GetString(8));

        private static PersistedRestatementRow ReadRestatementRow(NpgsqlDataReader reader) =>
            new(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetInt64(4),
                reader.GetInt16(5),
                reader.GetString(6),
                reader.GetString(7));

        private sealed record PersistedRunRow(
            string TenantId,
            string RunId,
            string SeriesId,
            int Revision,
            long AggregateVersion,
            short ExecutionState,
            short GovernanceState,
            string StatePayload,
            string StateHashSha256);

        private sealed record PersistedRestatementRow(
            string TenantId,
            string RequestId,
            string SeriesId,
            string PredecessorRunId,
            long AggregateVersion,
            short RequestState,
            string StatePayload,
            string StateHashSha256);
    }
}

/// <summary>Raised when retained reporting governance state or audit evidence cannot be trusted.</summary>
public sealed class ReportingGovernancePersistenceException : ReportingGovernanceException
{
    public ReportingGovernancePersistenceException(string message, Exception? innerException = null)
        : base(message)
    {
        if (innerException is not null)
        {
            Data[nameof(innerException)] = innerException.ToString();
        }
    }
}
