using System.Collections.Immutable;
using System.Data;
using System.Globalization;
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
        private const short LegacyFormatVersion = (short)ReportingGovernancePersistenceFormat.LegacyV1;
        private const short CurrentFormatVersion = (short)ReportingGovernancePersistenceFormat.CanonicalV2;

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

        public async ValueTask<IReadOnlyList<ReportingGovernancePersistenceStatus>> ListPersistenceStatusAsync(
            ReportingAuthorityScope authority,
            CancellationToken cancellationToken = default)
        {
            EnsurePersistenceEvidenceAuthority(authority);
            var tenantId = NormalizeKey(authority.TenantId, nameof(authority));
            var statuses = new List<ReportingGovernancePersistenceStatus>();

            var runRows = new List<PersistedRunRow>();
            await using (var command = CreateCommand())
            {
                command.CommandText =
                    $"""
                    select tenant_id,
                           run_id,
                           series_id,
                           organization_id,
                           company_id,
                           revision,
                           aggregate_version,
                           execution_state,
                           governance_state,
                           state_payload,
                           state_hash_sha256,
                           state_format_version
                    from {_runsTable}
                    where tenant_id = @tenant_id
                      and organization_id = @organization_id
                      and company_id is not distinct from @company_id
                    order by created_at_utc, run_id;
                    """;
                command.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Text, tenantId);
                command.Parameters.AddWithValue(
                    "organization_id",
                    NpgsqlDbType.Text,
                    authority.OrganizationId);
                command.Parameters.AddWithValue(
                    "company_id",
                    NpgsqlDbType.Text,
                    (object?)authority.CompanyId ?? DBNull.Value);
                await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    runRows.Add(ReadRunRow(reader));
                }
            }

            foreach (var row in runRows)
            {
                statuses.Add(await InspectRunStatusAsync(row, cancellationToken).ConfigureAwait(false));
            }

            var requestRows = new List<PersistedRestatementRow>();
            await using (var command = CreateCommand())
            {
                command.CommandText =
                    $"""
                    select request.tenant_id,
                           request.request_id,
                           request.series_id,
                           request.predecessor_run_id,
                           request.aggregate_version,
                           request.request_state,
                           request.state_payload,
                           request.state_hash_sha256,
                           request.state_format_version
                    from {_restatementTable} request
                    join {_runsTable} predecessor
                      on predecessor.tenant_id = request.tenant_id
                     and predecessor.run_id = request.predecessor_run_id
                    where request.tenant_id = @tenant_id
                      and predecessor.organization_id = @organization_id
                      and predecessor.company_id is not distinct from @company_id
                    order by request.created_at_utc, request.request_id;
                    """;
                command.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Text, tenantId);
                command.Parameters.AddWithValue(
                    "organization_id",
                    NpgsqlDbType.Text,
                    authority.OrganizationId);
                command.Parameters.AddWithValue(
                    "company_id",
                    NpgsqlDbType.Text,
                    (object?)authority.CompanyId ?? DBNull.Value);
                await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    requestRows.Add(ReadRestatementRow(reader));
                }
            }

            foreach (var row in requestRows)
            {
                statuses.Add(await InspectRestatementStatusAsync(row, cancellationToken).ConfigureAwait(false));
            }

            return statuses;
        }

        public async ValueTask<ReportingGovernancePersistenceExport?> ExportPersistenceRecordAsync(
            ReportingAuthorityScope authority,
            ReportingGovernanceAuditAggregateKind aggregateKind,
            string aggregateId,
            CancellationToken cancellationToken = default)
        {
            EnsurePersistenceEvidenceAuthority(authority);
            var tenantId = NormalizeKey(authority.TenantId, nameof(authority));
            aggregateId = NormalizeKey(aggregateId, nameof(aggregateId));

            string statePayload;
            ReportingGovernancePersistenceStatus status;
            switch (aggregateKind)
            {
                case ReportingGovernanceAuditAggregateKind.Run:
                    {
                        var row = await ReadRunRowAsync(tenantId, aggregateId, cancellationToken).ConfigureAwait(false);
                        if (row is null || !ScopeMatches(row, authority))
                        {
                            return null;
                        }
                        status = await RequireVerifiedRunStatusAsync(row, cancellationToken).ConfigureAwait(false);
                        statePayload = row.StatePayload;
                        break;
                    }
                case ReportingGovernanceAuditAggregateKind.RestatementRequest:
                    {
                        var row = await ReadRestatementRowAsync(tenantId, aggregateId, cancellationToken).ConfigureAwait(false);
                        if (row is null
                            || !await RestatementScopeMatchesAsync(
                                tenantId,
                                row.PredecessorRunId,
                                authority.OrganizationId,
                                authority.CompanyId,
                                cancellationToken).ConfigureAwait(false))
                        {
                            return null;
                        }
                        status = await RequireVerifiedRestatementStatusAsync(row, cancellationToken).ConfigureAwait(false);
                        statePayload = row.StatePayload;
                        break;
                    }
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(aggregateKind),
                        aggregateKind,
                        "Unsupported reporting governance aggregate kind.");
            }

            var auditRows = await ReadAuditRowsAsync(
                tenantId,
                aggregateKind,
                aggregateId,
                cancellationToken).ConfigureAwait(false);
            return new ReportingGovernancePersistenceExport(
                status,
                statePayload,
                auditRows.Select(static row => new ReportingGovernanceRawAuditEnvelope(
                        row.AggregateVersion,
                        row.EventId,
                        row.PreviousHash,
                        row.EventHash,
                        (ReportingGovernancePersistenceFormat)row.HashFormatVersion,
                        row.EventPayload,
                        row.PayloadHashSha256))
                    .ToImmutableArray());
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
                           organization_id,
                           company_id,
                           revision,
                           aggregate_version,
                           execution_state,
                           governance_state,
                           state_payload,
                           state_hash_sha256,
                           state_format_version
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
                        state_format_version,
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
                        @state_format_version,
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

            var currentRow = await ReadRunRowAsync(
                run.Scope.TenantId,
                run.RunId,
                cancellationToken).ConfigureAwait(false)
                ?? throw new ReportingGovernanceConcurrencyException(
                    $"Reporting run '{run.RunId}' version conflict: expected {expectedVersion}.");
            var current = await HydrateRunAsync(currentRow, cancellationToken).ConfigureAwait(false);
            if (current.Version != expectedVersion
                || !StringComparer.Ordinal.Equals(
                    ReportingGovernanceCanonicalValidation.ComputeImmutableRunFingerprint(current),
                    ReportingGovernanceCanonicalValidation.ComputeImmutableRunFingerprint(run)))
            {
                throw new ReportingGovernanceConcurrencyException(
                    $"Reporting run '{run.RunId}' replacement attempted to change its immutable creation binding or stale version {expectedVersion}.");
            }

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
                  and state_format_version = @state_format_version
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
                           state_hash_sha256,
                           state_format_version
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

        public async ValueTask<IReadOnlyList<ReportingRestatementRequest>> ListRestatementRequestsBySeriesAsync(
            string tenantId,
            string seriesId,
            CancellationToken cancellationToken = default)
        {
            tenantId = NormalizeKey(tenantId, nameof(tenantId));
            seriesId = NormalizeKey(seriesId, nameof(seriesId));

            var rows = new List<PersistedRestatementRow>();
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
                           state_hash_sha256,
                           state_format_version
                    from {_restatementTable}
                    where tenant_id = @tenant_id
                      and series_id = @series_id
                    order by created_at_utc, request_id;
                    """;
                command.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Text, tenantId);
                command.Parameters.AddWithValue("series_id", NpgsqlDbType.Text, seriesId);

                await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    rows.Add(ReadRestatementRow(reader));
                }
            }

            var requests = new List<ReportingRestatementRequest>(rows.Count);
            foreach (var row in rows)
            {
                requests.Add(await HydrateRestatementAsync(row, cancellationToken).ConfigureAwait(false));
            }

            return requests;
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
                        state_format_version,
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
                        @state_format_version,
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
                  and state_format_version = @state_format_version
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
                       organization_id,
                       company_id,
                       revision,
                       aggregate_version,
                       execution_state,
                       governance_state,
                       state_payload,
                       state_hash_sha256,
                       state_format_version
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

        private async Task<PersistedRestatementRow?> ReadRestatementRowAsync(
            string tenantId,
            string requestId,
            CancellationToken cancellationToken)
        {
            await using var command = CreateCommand();
            command.CommandText =
                $"""
                select tenant_id,
                       request_id,
                       series_id,
                       predecessor_run_id,
                       aggregate_version,
                       request_state,
                       state_payload,
                       state_hash_sha256,
                       state_format_version
                from {_restatementTable}
                where tenant_id = @tenant_id
                  and request_id = @request_id;
                """;
            command.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Text, tenantId);
            command.Parameters.AddWithValue("request_id", NpgsqlDbType.Text, requestId);

            await using var reader = await command.ExecuteReaderAsync(
                CommandBehavior.SingleRow,
                cancellationToken).ConfigureAwait(false);
            return await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
                ? ReadRestatementRow(reader)
                : null;
        }

        private async Task<GovernedReportingRun> HydrateRunAsync(
            PersistedRunRow row,
            CancellationToken cancellationToken,
            bool validateRestatementBinding = true)
        {
            if (row.StateFormatVersion == LegacyFormatVersion)
            {
                var status = await VerifyLegacyRunAsync(row, cancellationToken).ConfigureAwait(false);
                throw new ReportingGovernanceLegacyAggregateException(status);
            }
            if (row.StateFormatVersion != CurrentFormatVersion)
            {
                throw Integrity(
                    "reporting run",
                    row.RunId,
                    $"state format version {row.StateFormatVersion} is unsupported");
            }

            VerifyPayloadChecksum("reporting run", row.RunId, row.StatePayload, row.StateHashSha256);
            var run = DeserializeRun(row.StatePayload, row.RunId);
            if (!run.AuditTrail.IsDefaultOrEmpty)
            {
                throw Integrity("reporting run", row.RunId, "the retained state payload contains mutable audit history");
            }

            if (!StringComparer.Ordinal.Equals(run.Scope.TenantId, row.TenantId)
                || !StringComparer.Ordinal.Equals(run.RunId, row.RunId)
                || !StringComparer.Ordinal.Equals(run.SeriesId, row.SeriesId)
                || !StringComparer.Ordinal.Equals(run.Scope.OrganizationId, row.OrganizationId)
                || !StringComparer.Ordinal.Equals(run.Scope.CompanyId, row.CompanyId)
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
            if (validateRestatementBinding && run.Revision > 1)
            {
                await ValidateRunRestatementBindingAsync(
                    row.TenantId,
                    run,
                    cancellationToken).ConfigureAwait(false);
            }
            return run;
        }

        private async Task<ReportingRestatementRequest> HydrateRestatementAsync(
            PersistedRestatementRow row,
            CancellationToken cancellationToken,
            bool validateBinding = true)
        {
            if (row.StateFormatVersion == LegacyFormatVersion)
            {
                var status = await VerifyLegacyRestatementAsync(row, cancellationToken).ConfigureAwait(false);
                throw new ReportingGovernanceLegacyAggregateException(status);
            }
            if (row.StateFormatVersion != CurrentFormatVersion)
            {
                throw Integrity(
                    "reporting restatement request",
                    row.RequestId,
                    $"state format version {row.StateFormatVersion} is unsupported");
            }

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
            if (validateBinding)
            {
                await ValidateRestatementBindingAsync(
                    row.TenantId,
                    request,
                    cancellationToken).ConfigureAwait(false);
            }
            return request;
        }

        private async Task ValidateRunRestatementBindingAsync(
            string tenantId,
            GovernedReportingRun run,
            CancellationToken cancellationToken)
        {
            var requestRow = await ReadRestatementRowAsync(
                tenantId,
                run.RestatementRequestId!,
                cancellationToken).ConfigureAwait(false)
                ?? throw Integrity(
                    "reporting run",
                    run.RunId,
                    "the approved restatement request binding is missing");
            var request = await HydrateRestatementAsync(
                requestRow,
                cancellationToken,
                validateBinding: false).ConfigureAwait(false);
            var predecessorRow = await ReadRunRowAsync(
                tenantId,
                run.RestatementOfRunId!,
                cancellationToken).ConfigureAwait(false)
                ?? throw Integrity(
                    "reporting run",
                    run.RunId,
                    "the exact restatement predecessor is missing");
            var predecessor = await HydrateRunAsync(
                predecessorRow,
                cancellationToken).ConfigureAwait(false);

            try
            {
                ReportingGovernanceCanonicalValidation.ValidateRestatementBinding(
                    request,
                    predecessor,
                    run);
            }
            catch (Exception exception) when (
                exception is ReportingGovernanceException or ArgumentNullException)
            {
                throw Integrity(
                    "reporting run",
                    run.RunId,
                    $"canonical approved restatement binding is invalid: {exception.Message}");
            }
        }

        private async Task ValidateRestatementBindingAsync(
            string tenantId,
            ReportingRestatementRequest request,
            CancellationToken cancellationToken)
        {
            var predecessorRow = await ReadRunRowAsync(
                tenantId,
                request.PredecessorRunId,
                cancellationToken).ConfigureAwait(false)
                ?? throw Integrity(
                    "reporting restatement request",
                    request.RequestId,
                    "the exact predecessor run is missing");
            var predecessor = await HydrateRunAsync(
                predecessorRow,
                cancellationToken).ConfigureAwait(false);

            GovernedReportingRun? draftRun = null;
            if (request.State == ReportingRestatementRequestState.Approved)
            {
                var draftRow = await ReadRunRowAsync(
                    tenantId,
                    request.DraftRunId!,
                    cancellationToken).ConfigureAwait(false)
                    ?? throw Integrity(
                        "reporting restatement request",
                        request.RequestId,
                        "the approved draft run binding is missing");
                draftRun = await HydrateRunAsync(
                    draftRow,
                    cancellationToken,
                    validateRestatementBinding: false).ConfigureAwait(false);
            }

            try
            {
                ReportingGovernanceCanonicalValidation.ValidateRestatementBinding(
                    request,
                    predecessor,
                    draftRun);
            }
            catch (Exception exception) when (
                exception is ReportingGovernanceException or ArgumentNullException)
            {
                throw Integrity(
                    "reporting restatement request",
                    request.RequestId,
                    $"canonical predecessor or draft binding is invalid: {exception.Message}");
            }
        }

        private async Task<ImmutableArray<ReportingGovernanceAuditEntry>> ReadAuditAsync(
            string tenantId,
            ReportingGovernanceAuditAggregateKind aggregateKind,
            string aggregateId,
            CancellationToken cancellationToken)
        {
            var entries = ImmutableArray.CreateBuilder<ReportingGovernanceAuditEntry>();
            var rows = await ReadAuditRowsAsync(
                tenantId,
                aggregateKind,
                aggregateId,
                cancellationToken).ConfigureAwait(false);
            foreach (var row in rows)
            {
                if (row.HashFormatVersion != CurrentFormatVersion)
                {
                    throw Integrity(
                        "reporting governance audit event",
                        row.EventId,
                        $"canonical aggregate referenced hash format v{row.HashFormatVersion}");
                }

                VerifyPayloadChecksum(
                    "reporting governance audit event",
                    row.EventId,
                    row.EventPayload,
                    row.PayloadHashSha256);
                var entry = DeserializeAudit(row.EventPayload, row.EventId);
                ValidateAuditRowBinding(tenantId, aggregateKind, aggregateId, row, entry);
                entries.Add(entry);
            }

            var result = entries.DrainToImmutable();
            if (!ReportingGovernanceAuditChain.Verify(result))
            {
                throw Integrity(
                    "reporting governance audit chain",
                    aggregateId,
                    "the append-only version or SHA-256 chain is incomplete or invalid");
            }

            return result;
        }

        private async Task<ImmutableArray<PersistedAuditRow>> ReadAuditRowsAsync(
            string tenantId,
            ReportingGovernanceAuditAggregateKind aggregateKind,
            string aggregateId,
            CancellationToken cancellationToken)
        {
            var rows = ImmutableArray.CreateBuilder<PersistedAuditRow>();
            await using var command = CreateCommand();
            command.CommandText =
                $"""
                select aggregate_version,
                       event_id,
                       previous_hash,
                       event_hash,
                       event_payload,
                       payload_hash_sha256,
                       hash_format_version
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
                rows.Add(new PersistedAuditRow(
                    reader.GetInt64(0),
                    reader.GetString(1),
                    reader.IsDBNull(2) ? null : reader.GetString(2),
                    reader.GetString(3),
                    reader.GetString(4),
                    reader.GetString(5),
                    reader.GetInt16(6)));
            }

            return rows.DrainToImmutable();
        }

        private async Task<ReportingGovernancePersistenceStatus> VerifyLegacyRunAsync(
            PersistedRunRow row,
            CancellationToken cancellationToken)
        {
            VerifyPayloadChecksum("legacy reporting run", row.RunId, row.StatePayload, row.StateHashSha256);
            var run = DeserializeLegacyRun(row.StatePayload, row.RunId);
            if (!run.AuditTrail.IsDefaultOrEmpty)
            {
                throw Integrity(
                    "legacy reporting run",
                    row.RunId,
                    "the retained state payload contains mutable audit history");
            }
            if (!StringComparer.Ordinal.Equals(run.Scope.TenantId, row.TenantId)
                || !StringComparer.Ordinal.Equals(run.RunId, row.RunId)
                || !StringComparer.Ordinal.Equals(run.SeriesId, row.SeriesId)
                || !StringComparer.Ordinal.Equals(run.Scope.OrganizationId, row.OrganizationId)
                || !StringComparer.Ordinal.Equals(run.Scope.CompanyId, row.CompanyId)
                || run.Revision != row.Revision
                || run.Version != row.AggregateVersion
                || (short)run.ExecutionState != row.ExecutionState
                || (short)run.GovernanceState != row.GovernanceState)
            {
                throw Integrity(
                    "legacy reporting run",
                    row.RunId,
                    "indexed columns do not match the retained v1 state payload");
            }

            ValidateLegacyRunShape(run);
            var auditRows = await VerifyLegacyAuditAsync(
                row.TenantId,
                ReportingGovernanceAuditAggregateKind.Run,
                row.RunId,
                row.AggregateVersion,
                cancellationToken).ConfigureAwait(false);
            return BuildVerifiedLegacyStatus(
                row.TenantId,
                ReportingGovernanceAuditAggregateKind.Run,
                row.RunId,
                row.AggregateVersion,
                row.StateHashSha256,
                auditRows);
        }

        private async Task<ReportingGovernancePersistenceStatus> InspectRunStatusAsync(
            PersistedRunRow row,
            CancellationToken cancellationToken)
        {
            try
            {
                return await RequireVerifiedRunStatusAsync(row, cancellationToken).ConfigureAwait(false);
            }
            catch (ReportingGovernanceException exception)
            {
                return BuildIntegrityFailureStatus(
                    row.TenantId,
                    ReportingGovernanceAuditAggregateKind.Run,
                    row.RunId,
                    row.AggregateVersion,
                    row.StateFormatVersion,
                    row.StateHashSha256,
                    StringComparer.Ordinal.Equals(ComputeSha256(row.StatePayload), row.StateHashSha256),
                    exception.Message);
            }
        }

        private async Task<ReportingGovernancePersistenceStatus> RequireVerifiedRunStatusAsync(
            PersistedRunRow row,
            CancellationToken cancellationToken)
        {
            if (row.StateFormatVersion == LegacyFormatVersion)
            {
                return await VerifyLegacyRunAsync(row, cancellationToken).ConfigureAwait(false);
            }
            if (row.StateFormatVersion != CurrentFormatVersion)
            {
                throw Integrity("reporting run", row.RunId, $"unsupported state format v{row.StateFormatVersion}");
            }

            _ = await HydrateRunAsync(row, cancellationToken).ConfigureAwait(false);
            var auditRows = await ReadAuditRowsAsync(
                row.TenantId,
                ReportingGovernanceAuditAggregateKind.Run,
                row.RunId,
                cancellationToken).ConfigureAwait(false);
            return BuildVerifiedCurrentStatus(
                row.TenantId,
                ReportingGovernanceAuditAggregateKind.Run,
                row.RunId,
                row.AggregateVersion,
                row.StateHashSha256,
                auditRows);
        }

        private async Task<ReportingGovernancePersistenceStatus> VerifyLegacyRestatementAsync(
            PersistedRestatementRow row,
            CancellationToken cancellationToken)
        {
            VerifyPayloadChecksum(
                "legacy reporting restatement request",
                row.RequestId,
                row.StatePayload,
                row.StateHashSha256);
            var request = DeserializeLegacyRestatement(row.StatePayload, row.RequestId);
            if (!request.AuditTrail.IsDefaultOrEmpty)
            {
                throw Integrity(
                    "legacy reporting restatement request",
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
                    "legacy reporting restatement request",
                    row.RequestId,
                    "indexed columns do not match the retained v1 state payload");
            }

            ValidateLegacyRestatementShape(row.TenantId, request);
            var auditRows = await VerifyLegacyAuditAsync(
                row.TenantId,
                ReportingGovernanceAuditAggregateKind.RestatementRequest,
                row.RequestId,
                row.AggregateVersion,
                cancellationToken).ConfigureAwait(false);
            return BuildVerifiedLegacyStatus(
                row.TenantId,
                ReportingGovernanceAuditAggregateKind.RestatementRequest,
                row.RequestId,
                row.AggregateVersion,
                row.StateHashSha256,
                auditRows);
        }

        private async Task<ReportingGovernancePersistenceStatus> InspectRestatementStatusAsync(
            PersistedRestatementRow row,
            CancellationToken cancellationToken)
        {
            try
            {
                return await RequireVerifiedRestatementStatusAsync(row, cancellationToken).ConfigureAwait(false);
            }
            catch (ReportingGovernanceException exception)
            {
                return BuildIntegrityFailureStatus(
                    row.TenantId,
                    ReportingGovernanceAuditAggregateKind.RestatementRequest,
                    row.RequestId,
                    row.AggregateVersion,
                    row.StateFormatVersion,
                    row.StateHashSha256,
                    StringComparer.Ordinal.Equals(ComputeSha256(row.StatePayload), row.StateHashSha256),
                    exception.Message);
            }
        }

        private async Task<ReportingGovernancePersistenceStatus> RequireVerifiedRestatementStatusAsync(
            PersistedRestatementRow row,
            CancellationToken cancellationToken)
        {
            if (row.StateFormatVersion == LegacyFormatVersion)
            {
                return await VerifyLegacyRestatementAsync(row, cancellationToken).ConfigureAwait(false);
            }
            if (row.StateFormatVersion != CurrentFormatVersion)
            {
                throw Integrity(
                    "reporting restatement request",
                    row.RequestId,
                    $"unsupported state format v{row.StateFormatVersion}");
            }

            _ = await HydrateRestatementAsync(row, cancellationToken).ConfigureAwait(false);
            var auditRows = await ReadAuditRowsAsync(
                row.TenantId,
                ReportingGovernanceAuditAggregateKind.RestatementRequest,
                row.RequestId,
                cancellationToken).ConfigureAwait(false);
            return BuildVerifiedCurrentStatus(
                row.TenantId,
                ReportingGovernanceAuditAggregateKind.RestatementRequest,
                row.RequestId,
                row.AggregateVersion,
                row.StateHashSha256,
                auditRows);
        }

        private async Task<ImmutableArray<PersistedAuditRow>> VerifyLegacyAuditAsync(
            string tenantId,
            ReportingGovernanceAuditAggregateKind aggregateKind,
            string aggregateId,
            long aggregateVersion,
            CancellationToken cancellationToken)
        {
            var rows = await ReadAuditRowsAsync(
                tenantId,
                aggregateKind,
                aggregateId,
                cancellationToken).ConfigureAwait(false);
            if (rows.Length != aggregateVersion || rows.IsDefaultOrEmpty)
            {
                throw Integrity(
                    "legacy reporting governance audit chain",
                    aggregateId,
                    "the v1 audit chain is incomplete");
            }

            string? previousHash = null;
            for (var index = 0; index < rows.Length; index++)
            {
                var row = rows[index];
                if (row.HashFormatVersion != LegacyFormatVersion)
                {
                    throw Integrity(
                        "legacy reporting governance audit event",
                        row.EventId,
                        $"declared hash format v{row.HashFormatVersion} instead of v1");
                }

                VerifyPayloadChecksum(
                    "legacy reporting governance audit event",
                    row.EventId,
                    row.EventPayload,
                    row.PayloadHashSha256);
                var entry = DeserializeAudit(row.EventPayload, row.EventId);
                ValidateAuditRowBinding(tenantId, aggregateKind, aggregateId, row, entry);
                if (entry.AggregateVersion != index + 1L
                    || !StringComparer.Ordinal.Equals(entry.PreviousHash, previousHash)
                    || !StringComparer.Ordinal.Equals(entry.Hash, ComputeLegacyAuditHash(entry)))
                {
                    throw Integrity(
                        "legacy reporting governance audit chain",
                        aggregateId,
                        "the append-only v1 version or SHA-256 chain is invalid");
                }
                previousHash = entry.Hash;
            }

            return rows;
        }

        private static void ValidateAuditRowBinding(
            string tenantId,
            ReportingGovernanceAuditAggregateKind aggregateKind,
            string aggregateId,
            PersistedAuditRow row,
            ReportingGovernanceAuditEntry entry)
        {
            if (entry.AggregateKind != aggregateKind
                || !StringComparer.Ordinal.Equals(entry.AggregateId, aggregateId)
                || entry.AggregateVersion != row.AggregateVersion
                || !StringComparer.Ordinal.Equals(entry.EventId, row.EventId)
                || !StringComparer.Ordinal.Equals(entry.PreviousHash, row.PreviousHash)
                || !StringComparer.Ordinal.Equals(entry.Hash, row.EventHash)
                || !StringComparer.Ordinal.Equals(entry.Authority.TenantId, tenantId))
            {
                throw Integrity(
                    "reporting governance audit event",
                    row.EventId,
                    "indexed columns do not match the retained event payload");
            }
        }

        private static ReportingGovernancePersistenceStatus BuildVerifiedLegacyStatus(
            string tenantId,
            ReportingGovernanceAuditAggregateKind aggregateKind,
            string aggregateId,
            long aggregateVersion,
            string statePayloadHash,
            ImmutableArray<PersistedAuditRow> auditRows) =>
            new(
                tenantId,
                aggregateKind,
                aggregateId,
                aggregateVersion,
                ReportingGovernancePersistenceFormat.LegacyV1,
                statePayloadHash,
                StateChecksumVerified: true,
                auditRows.Length,
                auditRows.Select(static row =>
                        (ReportingGovernancePersistenceFormat)row.HashFormatVersion)
                    .ToImmutableArray(),
                AuditChainVerified: true,
                ReportingGovernancePersistenceDisposition.LegacyReadOnlyRecertificationRequired,
                "The committed v1 checksum and audit chain are valid, but typed principals and exact certification hashes are absent; export the immutable history and create a freshly certified v2 run.");

        private static ReportingGovernancePersistenceStatus BuildVerifiedCurrentStatus(
            string tenantId,
            ReportingGovernanceAuditAggregateKind aggregateKind,
            string aggregateId,
            long aggregateVersion,
            string statePayloadHash,
            ImmutableArray<PersistedAuditRow> auditRows) =>
            new(
                tenantId,
                aggregateKind,
                aggregateId,
                aggregateVersion,
                ReportingGovernancePersistenceFormat.CanonicalV2,
                statePayloadHash,
                StateChecksumVerified: true,
                auditRows.Length,
                auditRows.Select(static row =>
                        (ReportingGovernancePersistenceFormat)row.HashFormatVersion)
                    .ToImmutableArray(),
                AuditChainVerified: true,
                ReportingGovernancePersistenceDisposition.Current,
                "Canonical v2 state and audit evidence verified.");

        private static ReportingGovernancePersistenceStatus BuildIntegrityFailureStatus(
            string tenantId,
            ReportingGovernanceAuditAggregateKind aggregateKind,
            string aggregateId,
            long aggregateVersion,
            short stateFormatVersion,
            string statePayloadHash,
            bool stateChecksumVerified,
            string reason) =>
            new(
                tenantId,
                aggregateKind,
                aggregateId,
                aggregateVersion,
                (ReportingGovernancePersistenceFormat)stateFormatVersion,
                statePayloadHash,
                stateChecksumVerified,
                AuditEventCount: 0,
                AuditHashFormats: [],
                AuditChainVerified: false,
                ReportingGovernancePersistenceDisposition.IntegrityFailure,
                reason);

        private static void EnsurePersistenceEvidenceAuthority(ReportingAuthorityScope authority)
        {
            ArgumentNullException.ThrowIfNull(authority);
            RequireKey(authority.ActorId, nameof(authority.ActorId));
            RequireKey(authority.TenantId, nameof(authority.TenantId));
            RequireKey(authority.OrganizationId, nameof(authority.OrganizationId));
            RequireKey(authority.CorrelationId, nameof(authority.CorrelationId));
            if (authority.Origin != ReportingCommandOrigin.HumanOperator
                || !authority.HasPermission(ReportingGovernancePermission.ExportPersistenceEvidence))
            {
                throw new ReportingGovernanceAuthorizationException(
                    "Reporting governance persistence inventory and raw export require an explicit human ExportPersistenceEvidence capability.");
            }
        }

        private static bool ScopeMatches(
            PersistedRunRow row,
            ReportingAuthorityScope authority) =>
            StringComparer.Ordinal.Equals(row.OrganizationId, authority.OrganizationId)
            && StringComparer.Ordinal.Equals(row.CompanyId, authority.CompanyId);

        private async Task<bool> RestatementScopeMatchesAsync(
            string tenantId,
            string predecessorRunId,
            string authorityOrganizationId,
            string? authorityCompanyId,
            CancellationToken cancellationToken)
        {
            await using var command = CreateCommand();
            command.CommandText =
                $"""
                select organization_id, company_id
                from {_runsTable}
                where tenant_id = @tenant_id
                  and run_id = @run_id;
                """;
            command.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Text, tenantId);
            command.Parameters.AddWithValue("run_id", NpgsqlDbType.Text, predecessorRunId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                return false;
            }

            var retainedOrganizationId = reader.IsDBNull(0) ? null : reader.GetString(0);
            var retainedCompanyId = reader.IsDBNull(1) ? null : reader.GetString(1);
            return StringComparer.Ordinal.Equals(retainedOrganizationId, authorityOrganizationId)
                && StringComparer.Ordinal.Equals(retainedCompanyId, authorityCompanyId);
        }

        private static string ComputeLegacyAuditHash(ReportingGovernanceAuditEntry entry)
        {
            var canonical = new StringBuilder();
            AppendLegacyCanonical(canonical, entry.EventId);
            AppendLegacyCanonical(canonical, (int)entry.AggregateKind);
            AppendLegacyCanonical(canonical, entry.AggregateId);
            AppendLegacyCanonical(canonical, entry.AggregateVersion);
            AppendLegacyCanonical(
                canonical,
                entry.OccurredAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
            AppendLegacyCanonical(canonical, (int)entry.Action);
            AppendLegacyCanonical(canonical, entry.Authority.ActorId);
            AppendLegacyCanonical(canonical, entry.Authority.TenantId);
            AppendLegacyCanonical(canonical, entry.Authority.OrganizationId);
            AppendLegacyCanonical(canonical, entry.Authority.CompanyId);
            foreach (var permission in entry.Authority.Permissions.OrderBy(static permission => permission))
            {
                AppendLegacyCanonical(canonical, (int)permission);
            }
            AppendLegacyCanonical(canonical, (int)entry.Authority.Origin);
            AppendLegacyCanonical(canonical, entry.Authority.CorrelationId);
            AppendLegacyCanonical(canonical, (int)entry.PermissionUsed);
            AppendLegacyCanonical(canonical, entry.FromExecutionState is null ? null : (int)entry.FromExecutionState.Value);
            AppendLegacyCanonical(canonical, entry.ToExecutionState is null ? null : (int)entry.ToExecutionState.Value);
            AppendLegacyCanonical(canonical, entry.FromGovernanceState is null ? null : (int)entry.FromGovernanceState.Value);
            AppendLegacyCanonical(canonical, entry.ToGovernanceState is null ? null : (int)entry.ToGovernanceState.Value);
            AppendLegacyCanonical(canonical, entry.FromRestatementState is null ? null : (int)entry.FromRestatementState.Value);
            AppendLegacyCanonical(canonical, entry.ToRestatementState is null ? null : (int)entry.ToRestatementState.Value);
            AppendLegacyCanonical(canonical, entry.Note);
            AppendLegacyCanonical(canonical, entry.PreviousHash);
            return ComputeSha256(canonical.ToString());
        }

        private static void AppendLegacyCanonical(StringBuilder target, object? value)
        {
            var text = value switch
            {
                null => null,
                IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
                _ => value.ToString()
            };
            if (text is null)
            {
                target.Append("-1:");
                return;
            }
            target.Append(Encoding.UTF8.GetByteCount(text).ToString(CultureInfo.InvariantCulture));
            target.Append(':');
            target.Append(text);
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
                    payload_hash_sha256,
                    hash_format_version)
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
                    @payload_hash_sha256,
                    @hash_format_version);
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
            command.Parameters.AddWithValue("hash_format_version", NpgsqlDbType.Smallint, CurrentFormatVersion);
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
            command.Parameters.AddWithValue("state_format_version", NpgsqlDbType.Smallint, CurrentFormatVersion);
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
            command.Parameters.AddWithValue("state_format_version", NpgsqlDbType.Smallint, CurrentFormatVersion);
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

            try
            {
                ReportingGovernanceCanonicalValidation.ValidateGovernedRun(run, requireAudit);
            }
            catch (Exception exception) when (
                exception is ReportingGovernanceException or ArgumentNullException)
            {
                throw Integrity(
                    "reporting run",
                    run.RunId,
                    $"canonical immutable state is invalid: {exception.Message}");
            }

            RequireKey(run.Scope.TenantId, nameof(run.Scope.TenantId));
            RequireKey(run.Scope.OrganizationId, nameof(run.Scope.OrganizationId));
            RequireKey(run.Scope.CompanyId, nameof(run.Scope.CompanyId));
            RequireKey(run.Scope.FundId, nameof(run.Scope.FundId));
            RequireKey(run.Scope.BookId, nameof(run.Scope.BookId));
            RequireKey(run.Scope.PeriodId, nameof(run.Scope.PeriodId));
            if (run.Revision <= 0 || run.Version <= 0
                || !Enum.IsDefined(run.ExecutionState)
                || !Enum.IsDefined(run.GovernanceState))
            {
                throw Integrity("reporting run", run.RunId, "revision, version, or lifecycle state is invalid");
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
                || !StringComparer.Ordinal.Equals(request.RequestedBy.TenantId, tenantId))
            {
                throw Integrity("reporting restatement request", request.RequestId, "the requester escaped the indexed tenant");
            }

            try
            {
                ReportingGovernanceCanonicalValidation.ValidateRestatementRequest(request, requireAudit);
            }
            catch (Exception exception) when (
                exception is ReportingGovernanceException or ArgumentNullException)
            {
                throw Integrity(
                    "reporting restatement request",
                    request.RequestId,
                    $"canonical lifecycle state is invalid: {exception.Message}");
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

        private static void ValidateLegacyRunShape(GovernedReportingRunV1 run)
        {
            RequireKey(run.RunId, nameof(run.RunId));
            RequireKey(run.SeriesId, nameof(run.SeriesId));
            RequireKey(run.TemplateId, nameof(run.TemplateId));
            RequireKey(run.TemplateVersion, nameof(run.TemplateVersion));
            if (run.Scope is null || run.Access is null || run.Snapshot is null || run.CreationAuthority is null)
            {
                throw Integrity("legacy reporting run", run.RunId, "required v1 immutable scope data is missing");
            }

            RequireKey(run.Scope.TenantId, nameof(run.Scope.TenantId));
            RequireKey(run.Scope.OrganizationId, nameof(run.Scope.OrganizationId));
            RequireKey(run.Scope.BookId, nameof(run.Scope.BookId));
            RequireKey(run.Scope.PeriodId, nameof(run.Scope.PeriodId));
            ValidateLegacyAccess(run.Access, run.RunId);
            RequireKey(run.Snapshot.SnapshotId, nameof(run.Snapshot.SnapshotId));
            RequireKey(run.Snapshot.SnapshotHash, nameof(run.Snapshot.SnapshotHash));
            RequireKey(run.Snapshot.ReconciliationCheckpointId, nameof(run.Snapshot.ReconciliationCheckpointId));

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
                throw Integrity(
                    "legacy reporting run",
                    run.RunId,
                    "the v1 snapshot or creation authority escaped its retained operational scope");
            }

            if (run.Revision <= 0 || run.Version <= 0
                || !Enum.IsDefined(run.ExecutionState)
                || !Enum.IsDefined(run.GovernanceState)
                || ((run.GovernanceState == GovernedReportingState.Released) != (run.Release is not null))
                || (run.GovernanceState >= GovernedReportingState.Approved && run.Approval is null)
                || (run.GovernanceState >= GovernedReportingState.Validated && run.Readiness is null))
            {
                throw Integrity(
                    "legacy reporting run",
                    run.RunId,
                    "the v1 revision, lifecycle state, or receipt shape is invalid");
            }

            if (run.Readiness is not null)
            {
                ValidateLegacyReadiness(run.Readiness, run);
            }
        }

        private static void ValidateLegacyAccess(ReportingAccessScopeV1 access, string runId)
        {
            RequireKey(access.PolicyId, nameof(access.PolicyId));
            RequireKey(access.PolicyVersion, nameof(access.PolicyVersion));
            RequireKey(access.PolicyHash, nameof(access.PolicyHash));
            if (!Enum.IsDefined(access.Mode)
                || (access.Mode == ReportingGovernanceAccessMode.Private
                    && string.IsNullOrWhiteSpace(access.OwnerPrincipalId))
                || (access.Mode == ReportingGovernanceAccessMode.Restricted
                    && access.PrincipalIds.IsDefaultOrEmpty)
                || (!access.PrincipalIds.IsDefaultOrEmpty
                    && (access.PrincipalIds.Any(string.IsNullOrWhiteSpace)
                        || access.PrincipalIds.Distinct(StringComparer.Ordinal).Count()
                            != access.PrincipalIds.Length)))
            {
                throw Integrity(
                    "legacy reporting run",
                    runId,
                    "the retained v1 access policy shape is invalid");
            }
        }

        private static void ValidateLegacyReadiness(
            ReportingReadinessReceiptV1 readiness,
            GovernedReportingRunV1 run)
        {
            RequireKey(readiness.ReceiptId, nameof(readiness.ReceiptId));
            RequireKey(readiness.ReceiptHash, nameof(readiness.ReceiptHash));
            if (!StringComparer.Ordinal.Equals(readiness.RunId, run.RunId)
                || !StringComparer.Ordinal.Equals(readiness.TenantId, run.Scope.TenantId)
                || !StringComparer.Ordinal.Equals(readiness.SnapshotId, run.Snapshot.SnapshotId)
                || !StringComparer.Ordinal.Equals(readiness.SnapshotHash, run.Snapshot.SnapshotHash)
                || readiness.Checks.IsDefaultOrEmpty
                || readiness.Checks.Any(static check =>
                    string.IsNullOrWhiteSpace(check.CheckId)
                    || !check.Passed
                    || check.EvidenceIds.IsDefaultOrEmpty
                    || check.EvidenceIds.Any(string.IsNullOrWhiteSpace)
                    || check.EvidenceIds.Distinct(StringComparer.Ordinal).Count()
                        != check.EvidenceIds.Length)
                || readiness.Checks.Select(static check => check.CheckId)
                    .Distinct(StringComparer.Ordinal).Count() != readiness.Checks.Length)
            {
                throw Integrity(
                    "legacy reporting run",
                    run.RunId,
                    "the retained v1 readiness receipt is invalid");
            }
        }

        private static void ValidateLegacyRestatementShape(
            string tenantId,
            ReportingRestatementRequestV1 request)
        {
            RequireKey(request.RequestId, nameof(request.RequestId));
            RequireKey(request.PredecessorRunId, nameof(request.PredecessorRunId));
            RequireKey(request.SeriesId, nameof(request.SeriesId));
            RequireKey(request.Reason, nameof(request.Reason));
            if (request.RequestedBy is null
                || !StringComparer.Ordinal.Equals(request.RequestedBy.TenantId, tenantId)
                || request.Version <= 0
                || request.PredecessorRevision <= 0
                || request.PredecessorVersion <= 0
                || !Enum.IsDefined(request.State)
                || request.ChangedLines.IsDefaultOrEmpty
                || request.ChangedLines.Any(static line =>
                    string.IsNullOrWhiteSpace(line.LineKey)
                    || StringComparer.Ordinal.Equals(line.PreviousValue, line.CurrentValue)
                    || line.EvidenceIds.IsDefaultOrEmpty
                    || line.EvidenceIds.Any(string.IsNullOrWhiteSpace))
                || request.ChangedLines.Select(static line => line.LineKey)
                    .Distinct(StringComparer.Ordinal).Count() != request.ChangedLines.Length)
            {
                throw Integrity(
                    "legacy reporting restatement request",
                    request.RequestId,
                    "the retained v1 request shape is invalid");
            }

            if ((request.State == ReportingRestatementRequestState.Approved)
                    != (request.ApprovedBy is not null
                        && request.ApprovedAtUtc is not null
                        && request.DraftRunId is not null)
                || (request.ApprovedBy is not null
                    && !StringComparer.Ordinal.Equals(request.ApprovedBy.TenantId, tenantId)))
            {
                throw Integrity(
                    "legacy reporting restatement request",
                    request.RequestId,
                    "the retained v1 approval fields are invalid");
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

        private static GovernedReportingRunV1 DeserializeLegacyRun(string payload, string id)
        {
            try
            {
                return JsonSerializer.Deserialize(
                        payload,
                        ReportingGovernanceJsonContext.Default.GovernedReportingRunV1)
                    ?? throw Integrity("legacy reporting run", id, "the retained v1 state payload is null");
            }
            catch (JsonException exception)
            {
                throw Integrity(
                    "legacy reporting run",
                    id,
                    "the retained v1 state payload is invalid JSON",
                    exception);
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

        private static ReportingRestatementRequestV1 DeserializeLegacyRestatement(string payload, string id)
        {
            try
            {
                return JsonSerializer.Deserialize(
                        payload,
                        ReportingGovernanceJsonContext.Default.ReportingRestatementRequestV1)
                    ?? throw Integrity(
                        "legacy reporting restatement request",
                        id,
                        "the retained v1 state payload is null");
            }
            catch (JsonException exception)
            {
                throw Integrity(
                    "legacy reporting restatement request",
                    id,
                    "the retained v1 state payload is invalid JSON",
                    exception);
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
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.GetInt32(5),
                reader.GetInt64(6),
                reader.GetInt16(7),
                reader.GetInt16(8),
                reader.GetString(9),
                reader.GetString(10),
                reader.GetInt16(11));

        private static PersistedRestatementRow ReadRestatementRow(NpgsqlDataReader reader) =>
            new(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetInt64(4),
                reader.GetInt16(5),
                reader.GetString(6),
                reader.GetString(7),
                reader.GetInt16(8));

        private sealed record PersistedRunRow(
            string TenantId,
            string RunId,
            string SeriesId,
            string? OrganizationId,
            string? CompanyId,
            int Revision,
            long AggregateVersion,
            short ExecutionState,
            short GovernanceState,
            string StatePayload,
            string StateHashSha256,
            short StateFormatVersion);

        private sealed record PersistedRestatementRow(
            string TenantId,
            string RequestId,
            string SeriesId,
            string PredecessorRunId,
            long AggregateVersion,
            short RequestState,
            string StatePayload,
            string StateHashSha256,
            short StateFormatVersion);

        private sealed record PersistedAuditRow(
            long AggregateVersion,
            string EventId,
            string? PreviousHash,
            string EventHash,
            string EventPayload,
            string PayloadHashSha256,
            short HashFormatVersion);
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

/// <summary>
/// Raised after a v1 aggregate and its original audit chain have been verified successfully. The
/// aggregate remains read-only because current typed access and certification facts cannot be
/// reconstructed from the committed v1 payload without inference.
/// </summary>
public sealed class ReportingGovernanceLegacyAggregateException : ReportingGovernanceException
{
    public ReportingGovernanceLegacyAggregateException(ReportingGovernancePersistenceStatus status)
        : base(
            $"Retained {status.AggregateKind} '{status.AggregateId}' is verified legacy governance format v1 and is read-only; export its immutable history and create a freshly certified v2 run.")
    {
        Status = status;
    }

    public ReportingGovernancePersistenceStatus Status { get; }
}
