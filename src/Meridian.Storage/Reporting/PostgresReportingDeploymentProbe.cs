using Npgsql;
using NpgsqlTypes;

namespace Meridian.Storage.Reporting;

public interface IReportingDeploymentProbe
{
    ReportingDeploymentProbeResult Probe();
}

/// <summary>
/// Read-only liveness and schema probe for the canonical PostgreSQL reporting authority.
/// It exposes only stable component identifiers and never returns connection details.
/// </summary>
public sealed class PostgresReportingDeploymentProbe : IReportingDeploymentProbe
{
    internal static readonly string[] RequiredTables =
    [
        "reporting_schema_migrations",
        "reporting_governed_runs",
        "reporting_restatement_requests",
        "reporting_governance_audit",
        "reporting_artifact_blobs",
        "reporting_artifact_packages",
        "reporting_artifact_catalog",
        "reporting_artifact_audit_chain_head",
        "reporting_artifact_audit",
        "reporting_reconciliation_evidence",
        "reporting_reconciliation_evidence_v2",
        "reporting_run_snapshots",
        "reporting_schedule_snapshots",
        "reporting_access_grants",
        "reporting_delivery_jobs",
        "reporting_delivery_receipts"
    ];

    internal static readonly string[] RequiredTriggers =
    [
        "trg_reporting_artifact_blobs_immutable",
        "trg_reporting_governed_runs_guard",
        "trg_reporting_restatement_requests_guard",
        "trg_reporting_governance_audit_append",
        "trg_reporting_governance_audit_immutable",
        "trg_reporting_access_grants_guard",
        "trg_reporting_delivery_jobs_guard",
        "trg_reporting_delivery_receipts_immutable",
        "trg_reporting_artifact_audit_append_guard",
        "trg_reporting_artifact_packages_immutable",
        "trg_reporting_artifact_catalog_immutable",
        "trg_reporting_artifact_audit_immutable",
        "reporting_reconciliation_evidence_immutable",
        "reporting_reconciliation_evidence_v2_append"
    ];

    private readonly string _connectionString;
    private readonly string _schema;

    public PostgresReportingDeploymentProbe(ReportingArtifactStoreOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.ConnectionString);
        ReportingDistributionStoreGuard.ValidateIdentifier(options.Schema, nameof(options.Schema));

        var builder = new NpgsqlConnectionStringBuilder(options.ConnectionString)
        {
            Timeout = Math.Clamp(
                new NpgsqlConnectionStringBuilder(options.ConnectionString).Timeout,
                1,
                2),
            CommandTimeout = 2
        };
        _connectionString = builder.ConnectionString;
        _schema = options.Schema;
    }

    public ReportingDeploymentProbeResult Probe()
    {
        try
        {
            using var connection = new NpgsqlConnection(_connectionString);
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText =
                """
                select required.table_name
                from unnest(@required_tables::text[]) as required(table_name)
                left join pg_catalog.pg_tables existing
                  on existing.schemaname = @schema
                 and existing.tablename = required.table_name
                where existing.tablename is null
                order by required.table_name;

                select required.trigger_name
                from unnest(@required_triggers::text[]) as required(trigger_name)
                left join (
                    select trigger_row.tgname as trigger_name
                    from pg_catalog.pg_trigger trigger_row
                    join pg_catalog.pg_class target_table
                      on target_table.oid = trigger_row.tgrelid
                    join pg_catalog.pg_namespace target_schema
                      on target_schema.oid = target_table.relnamespace
                    where target_schema.nspname = @schema
                      and not trigger_row.tgisinternal
                ) existing
                  on existing.trigger_name = required.trigger_name
                where existing.trigger_name is null
                order by required.trigger_name;
                """;
            command.Parameters.AddWithValue(
                "required_tables",
                NpgsqlDbType.Array | NpgsqlDbType.Text,
                RequiredTables);
            command.Parameters.AddWithValue(
                "required_triggers",
                NpgsqlDbType.Array | NpgsqlDbType.Text,
                RequiredTriggers);
            command.Parameters.AddWithValue("schema", NpgsqlDbType.Text, _schema);

            var missingTables = new List<string>();
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                missingTables.Add(reader.GetString(0));
            }

            var missingTriggers = new List<string>();
            if (!reader.NextResult())
            {
                throw new InvalidDataException(
                    "The reporting deployment probe did not return its trigger verification result.");
            }
            while (reader.Read())
            {
                missingTriggers.Add(reader.GetString(0));
            }

            return new ReportingDeploymentProbeResult(
                IsReachable: true,
                MissingTables: missingTables,
                MissingTriggers: missingTriggers,
                FailureCode: null);
        }
        catch (Exception exception) when (exception is NpgsqlException
            or TimeoutException
            or InvalidOperationException
            or InvalidDataException)
        {
            return new ReportingDeploymentProbeResult(
                IsReachable: false,
                MissingTables: RequiredTables,
                MissingTriggers: RequiredTriggers,
                FailureCode: "REPORTING_POSTGRES_UNREACHABLE");
        }
    }
}

public sealed record ReportingDeploymentProbeResult(
    bool IsReachable,
    IReadOnlyList<string> MissingTables,
    IReadOnlyList<string> MissingTriggers,
    string? FailureCode)
{
    public bool IsComplete =>
        IsReachable
        && MissingTables.Count == 0
        && MissingTriggers.Count == 0;

    public bool HasTable(string tableName) =>
        IsReachable &&
        !MissingTables.Contains(tableName, StringComparer.Ordinal);

    public bool HasTrigger(string triggerName) =>
        IsReachable &&
        !MissingTriggers.Contains(triggerName, StringComparer.Ordinal);
}
