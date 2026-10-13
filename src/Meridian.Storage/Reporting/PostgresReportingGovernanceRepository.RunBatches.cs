using System.Collections.Immutable;
using Meridian.Reporting;
using NpgsqlTypes;

namespace Meridian.Storage.Reporting;

public sealed partial class PostgresReportingGovernanceRepository
{
    private sealed partial class PostgresReportingGovernanceTransaction
    {
        public async ValueTask<IReadOnlyList<GovernedReportingRun>> GetRunsAsync(
            string tenantId,
            IReadOnlyCollection<string> runIds,
            CancellationToken cancellationToken = default)
        {
            tenantId = NormalizeKey(tenantId, nameof(tenantId));
            ArgumentNullException.ThrowIfNull(runIds);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(runIds.Count, IReportingGovernanceTransaction.MaximumRunReadBatchSize);
            cancellationToken.ThrowIfCancellationRequested();
            var identities = runIds.Select(runId => NormalizeKey(runId, nameof(runIds)))
                .Distinct(StringComparer.Ordinal).ToArray();
            if (identities.Length == 0)
                return [];

            var rows = new List<PersistedRunRow>(identities.Length);
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
                      and run_id = any(@run_ids)
                    order by run_id;
                    """;
                command.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Text, tenantId);
                command.Parameters.AddWithValue("run_ids", NpgsqlDbType.Array | NpgsqlDbType.Text, identities);
                await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                    rows.Add(ReadRunRow(reader));
            }

            var auditRows = await ReadRunAuditBatchAsync(tenantId, rows.Select(row => row.RunId).ToArray(), cancellationToken)
                .ConfigureAwait(false);
            var runs = new List<GovernedReportingRun>(rows.Count);
            foreach (var row in rows)
            {
                // Legacy records retain their existing fail-closed migration path. Canonical
                // records use the exact same row binding and audit-chain verification as GetRun.
                ImmutableArray<ReportingGovernanceAuditEntry>? audit = row.StateFormatVersion == CurrentFormatVersion
                    ? VerifyAuditRows(tenantId, ReportingGovernanceAuditAggregateKind.Run, row.RunId,
                        auditRows.GetValueOrDefault(row.RunId) ?? [])
                    : null;
                runs.Add(await HydrateRunAsync(row, cancellationToken, retainedAudit: audit).ConfigureAwait(false));
            }
            return runs;
        }

        private async Task<Dictionary<string, List<PersistedAuditRow>>> ReadRunAuditBatchAsync(
            string tenantId,
            string[] runIds,
            CancellationToken cancellationToken)
        {
            var rows = new Dictionary<string, List<PersistedAuditRow>>(StringComparer.Ordinal);
            if (runIds.Length == 0)
                return rows;
            await using var command = CreateCommand();
            command.CommandText =
                $"""
                select aggregate_id,
                       aggregate_version,
                       event_id,
                       previous_hash,
                       event_hash,
                       event_payload,
                       payload_hash_sha256,
                       hash_format_version
                from {_auditTable}
                where tenant_id = @tenant_id
                  and aggregate_kind = @aggregate_kind
                  and aggregate_id = any(@run_ids)
                order by aggregate_id, aggregate_version;
                """;
            command.Parameters.AddWithValue("tenant_id", NpgsqlDbType.Text, tenantId);
            command.Parameters.AddWithValue("aggregate_kind", NpgsqlDbType.Smallint, (short)ReportingGovernanceAuditAggregateKind.Run);
            command.Parameters.AddWithValue("run_ids", NpgsqlDbType.Array | NpgsqlDbType.Text, runIds);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var runId = reader.GetString(0);
                if (!rows.TryGetValue(runId, out var audit))
                {
                    audit = [];
                    rows.Add(runId, audit);
                }
                audit.Add(new PersistedAuditRow(
                    reader.GetInt64(1), reader.GetString(2), reader.IsDBNull(3) ? null : reader.GetString(3),
                    reader.GetString(4), reader.GetString(5), reader.GetString(6), reader.GetInt16(7)));
            }
            return rows;
        }
    }
}
