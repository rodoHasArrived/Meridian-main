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
        "reporting_run_create_claims",
        "reporting_schedule_snapshots",
        "reporting_access_grants",
        "reporting_delivery_jobs",
        "reporting_delivery_receipts"
    ];

    internal static readonly ReportingTriggerBinding[] RequiredTriggerBindings =
    [
        new(
            "trg_reporting_artifact_blobs_immutable",
            "reporting_artifact_blobs",
            "reject_reporting_artifact_blob_mutation"),
        new(
            "trg_reporting_governed_runs_guard",
            "reporting_governed_runs",
            "guard_reporting_governed_run_mutation"),
        new(
            "trg_reporting_restatement_requests_guard",
            "reporting_restatement_requests",
            "guard_reporting_restatement_mutation"),
        new(
            "trg_reporting_governance_audit_append",
            "reporting_governance_audit",
            "validate_reporting_governance_audit_append"),
        new(
            "trg_reporting_governance_audit_immutable",
            "reporting_governance_audit",
            "reject_reporting_governance_audit_mutation"),
        new(
            "trg_reporting_access_grants_guard",
            "reporting_access_grants",
            "guard_reporting_access_grant_mutation"),
        new(
            "trg_reporting_delivery_jobs_guard",
            "reporting_delivery_jobs",
            "guard_reporting_delivery_job_mutation"),
        new(
            "trg_reporting_delivery_receipts_immutable",
            "reporting_delivery_receipts",
            "reject_reporting_delivery_receipt_mutation"),
        new(
            "trg_reporting_artifact_audit_append_guard",
            "reporting_artifact_audit",
            "enforce_reporting_artifact_audit_append"),
        new(
            "trg_reporting_artifact_packages_immutable",
            "reporting_artifact_packages",
            "reject_reporting_artifact_metadata_mutation"),
        new(
            "trg_reporting_artifact_catalog_immutable",
            "reporting_artifact_catalog",
            "reject_reporting_artifact_metadata_mutation"),
        new(
            "trg_reporting_artifact_audit_immutable",
            "reporting_artifact_audit",
            "reject_reporting_artifact_metadata_mutation"),
        new(
            "reporting_reconciliation_evidence_immutable",
            "reporting_reconciliation_evidence",
            "guard_reporting_reconciliation_evidence_immutable"),
        new(
            "reporting_reconciliation_evidence_v2_append",
            "reporting_reconciliation_evidence_v2",
            "guard_reporting_reconciliation_evidence_v2_append")
    ];

    internal static readonly string[] RequiredTriggers =
        RequiredTriggerBindings.Select(static binding => binding.TriggerName).ToArray();

    internal static readonly ReportingColumnBinding[] RequiredColumnBindings =
    [
        new("reporting_schema_migrations", "filename", MustBeNotNull: true),
        new("reporting_schema_migrations", "checksum", MustBeNotNull: true),
        new("reporting_run_create_claims", "tenant_id"),
        new("reporting_run_create_claims", "run_id"),
        new("reporting_run_create_claims", "run_id_key"),
        new("reporting_run_create_claims", "lease_owner"),
        new("reporting_run_create_claims", "claimed_at_utc"),
        new("reporting_run_create_claims", "lease_expires_at_utc"),
        new("reporting_run_create_claims", "lease_version"),
        new("reporting_schedule_snapshots", "due_at_utc"),
        new("reporting_schedule_snapshots", "lease_owner"),
        new("reporting_schedule_snapshots", "lease_expires_at_utc"),
        new("reporting_schedule_snapshots", "lease_version")
    ];

    internal static readonly ReportingUniqueKeyBinding[] RequiredUniqueKeyBindings =
    [
        new("reporting_schema_migrations", ["filename"]),
        new("reporting_governed_runs", ["tenant_id", "run_id"]),
        new("reporting_governed_runs", ["tenant_id", "series_id", "revision"]),
        new("reporting_restatement_requests", ["tenant_id", "request_id"]),
        new(
            "reporting_governance_audit",
            ["tenant_id", "aggregate_kind", "aggregate_id", "aggregate_version"]),
        new("reporting_governance_audit", ["tenant_id", "event_id"]),
        new("reporting_artifact_blobs", ["tenant_id", "content_hash_sha256"]),
        new("reporting_artifact_packages", ["tenant_id", "package_id"]),
        new("reporting_artifact_catalog", ["tenant_id", "package_id", "artifact_id"]),
        new("reporting_artifact_audit_chain_head", ["chain_id"]),
        new("reporting_artifact_audit", ["sequence"]),
        new("reporting_artifact_audit", ["event_id"]),
        new("reporting_reconciliation_evidence", ["tenant_id", "receipt_key_sha256"]),
        new(
            "reporting_reconciliation_evidence",
            ["tenant_id", "reconciliation_checkpoint_id", "reconciliation_checkpoint_hash"]),
        new("reporting_reconciliation_evidence_v2", ["tenant_id", "receipt_key_sha256"]),
        new(
            "reporting_reconciliation_evidence_v2",
            ["tenant_id", "reconciliation_checkpoint_id", "reconciliation_checkpoint_hash"]),
        new("reporting_run_snapshots", ["tenant_id", "run_id_key"]),
        new("reporting_run_create_claims", ["tenant_id", "run_id_key"]),
        new(
            "reporting_schedule_snapshots",
            ["tenant_id", "company_id", "schedule_id_key"]),
        new("reporting_access_grants", ["grant_id"]),
        new("reporting_delivery_jobs", ["job_id"]),
        new("reporting_delivery_jobs", ["idempotency_key"]),
        new("reporting_delivery_jobs", ["job_id", "tenant_id"]),
        new(
            "reporting_delivery_jobs",
            ["access_grant_id"],
            "access_grant_id IS NOT NULL"),
        new("reporting_delivery_receipts", ["job_id", "receipt_id"])
    ];

    internal const string ProbeCommandText =
        """
        select required.table_name
        from unnest(@required_tables::text[]) as required(table_name)
        left join pg_catalog.pg_tables existing
          on existing.schemaname = @schema
         and existing.tablename = required.table_name
        where existing.tablename is null
        order by required.table_name;

        select required.trigger_name
        from unnest(
            @required_triggers::text[],
            @required_trigger_tables::text[],
            @required_trigger_functions::text[])
            as required(trigger_name, table_name, function_name)
        left join (
            select
                trigger_row.tgname as trigger_name,
                target_table.relname as table_name,
                trigger_function.proname as function_name
            from pg_catalog.pg_trigger trigger_row
            join pg_catalog.pg_class target_table
              on target_table.oid = trigger_row.tgrelid
            join pg_catalog.pg_namespace target_schema
              on target_schema.oid = target_table.relnamespace
            join pg_catalog.pg_proc trigger_function
              on trigger_function.oid = trigger_row.tgfoid
            join pg_catalog.pg_namespace function_schema
              on function_schema.oid = trigger_function.pronamespace
            where target_schema.nspname = @schema
              and function_schema.nspname = @schema
              and not trigger_row.tgisinternal
              and trigger_row.tgenabled in ('O', 'A')
        ) existing
          on existing.trigger_name = required.trigger_name
         and existing.table_name = required.table_name
         and existing.function_name = required.function_name
        where existing.trigger_name is null
        order by required.trigger_name;

        select required.table_name || '.' || required.column_name
        from unnest(
            @required_column_tables::text[],
            @required_columns::text[],
            @required_column_not_null::boolean[])
            as required(table_name, column_name, must_be_not_null)
        left join (
            select
                target_table.relname as table_name,
                column_row.attname as column_name,
                column_row.attnotnull as is_not_null
            from pg_catalog.pg_attribute column_row
            join pg_catalog.pg_class target_table
              on target_table.oid = column_row.attrelid
            join pg_catalog.pg_namespace target_schema
              on target_schema.oid = target_table.relnamespace
            where target_schema.nspname = @schema
              and column_row.attnum > 0
              and not column_row.attisdropped
        ) existing
          on existing.table_name = required.table_name
         and existing.column_name = required.column_name
        where existing.column_name is null
           or (required.must_be_not_null and not existing.is_not_null)
        order by required.table_name, required.column_name;

        select required.signature
        from unnest(
            @required_unique_key_tables::text[],
            @required_unique_key_columns::text[],
            @required_unique_key_predicates::text[],
            @required_unique_key_signatures::text[])
            as required(table_name, column_names, predicate, signature)
        left join (
            select
                target_table.relname as table_name,
                string_agg(
                    target_column.attname,
                    ','
                    order by target_column.attname) as column_names,
                regexp_replace(
                    lower(coalesce(
                        max(pg_catalog.pg_get_expr(
                            unique_index.indpred,
                            unique_index.indrelid)),
                        '')),
                    '[[:space:]()]',
                    '',
                    'g') as predicate
            from pg_catalog.pg_index unique_index
            join pg_catalog.pg_class target_table
              on target_table.oid = unique_index.indrelid
            join pg_catalog.pg_namespace target_schema
              on target_schema.oid = target_table.relnamespace
            cross join lateral unnest(unique_index.indkey)
              with ordinality as key_column(attribute_number, ordinality)
            left join pg_catalog.pg_attribute target_column
              on target_column.attrelid = target_table.oid
             and target_column.attnum = key_column.attribute_number
            where target_schema.nspname = @schema
              and unique_index.indisunique
              and unique_index.indisvalid
              and unique_index.indisready
              and unique_index.indimmediate
              and key_column.ordinality <= unique_index.indnkeyatts
            group by
                target_table.relname,
                unique_index.indexrelid,
                unique_index.indnkeyatts
            having count(*) = unique_index.indnkeyatts
               and bool_and(key_column.attribute_number > 0)
        ) existing
          on existing.table_name = required.table_name
         and existing.column_names = required.column_names
         and existing.predicate = required.predicate
        where existing.table_name is null
        order by required.signature;
        """;

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
            command.CommandText = ProbeCommandText;
            command.Parameters.AddWithValue(
                "required_tables",
                NpgsqlDbType.Array | NpgsqlDbType.Text,
                RequiredTables);
            command.Parameters.AddWithValue(
                "required_triggers",
                NpgsqlDbType.Array | NpgsqlDbType.Text,
                RequiredTriggers);
            command.Parameters.AddWithValue(
                "required_trigger_tables",
                NpgsqlDbType.Array | NpgsqlDbType.Text,
                RequiredTriggerBindings.Select(static binding => binding.TableName).ToArray());
            command.Parameters.AddWithValue(
                "required_trigger_functions",
                NpgsqlDbType.Array | NpgsqlDbType.Text,
                RequiredTriggerBindings.Select(static binding => binding.FunctionName).ToArray());
            command.Parameters.AddWithValue(
                "required_column_tables",
                NpgsqlDbType.Array | NpgsqlDbType.Text,
                RequiredColumnBindings.Select(static binding => binding.TableName).ToArray());
            command.Parameters.AddWithValue(
                "required_columns",
                NpgsqlDbType.Array | NpgsqlDbType.Text,
                RequiredColumnBindings.Select(static binding => binding.ColumnName).ToArray());
            command.Parameters.AddWithValue(
                "required_column_not_null",
                NpgsqlDbType.Array | NpgsqlDbType.Boolean,
                RequiredColumnBindings.Select(static binding => binding.MustBeNotNull).ToArray());
            command.Parameters.AddWithValue(
                "required_unique_key_tables",
                NpgsqlDbType.Array | NpgsqlDbType.Text,
                RequiredUniqueKeyBindings.Select(static binding => binding.TableName).ToArray());
            command.Parameters.AddWithValue(
                "required_unique_key_columns",
                NpgsqlDbType.Array | NpgsqlDbType.Text,
                RequiredUniqueKeyBindings.Select(static binding => binding.CanonicalColumnNames).ToArray());
            command.Parameters.AddWithValue(
                "required_unique_key_predicates",
                NpgsqlDbType.Array | NpgsqlDbType.Text,
                RequiredUniqueKeyBindings.Select(static binding => binding.NormalizedPredicate).ToArray());
            command.Parameters.AddWithValue(
                "required_unique_key_signatures",
                NpgsqlDbType.Array | NpgsqlDbType.Text,
                RequiredUniqueKeyBindings.Select(static binding => binding.Signature).ToArray());
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

            var missingColumns = new List<string>();
            if (!reader.NextResult())
            {
                throw new InvalidDataException(
                    "The reporting deployment probe did not return its column verification result.");
            }
            while (reader.Read())
            {
                missingColumns.Add(reader.GetString(0));
            }

            var missingUniqueKeys = new List<string>();
            if (!reader.NextResult())
            {
                throw new InvalidDataException(
                    "The reporting deployment probe did not return its unique-key verification result.");
            }
            while (reader.Read())
            {
                missingUniqueKeys.Add(reader.GetString(0));
            }

            return new ReportingDeploymentProbeResult(
                IsReachable: true,
                MissingTables: missingTables,
                MissingTriggers: missingTriggers,
                FailureCode: null)
            {
                MissingColumns = missingColumns,
                MissingUniqueKeys = missingUniqueKeys
            };
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
                FailureCode: "REPORTING_POSTGRES_UNREACHABLE")
            {
                MissingColumns = RequiredColumnBindings
                    .Select(static binding => binding.QualifiedName)
                    .ToArray(),
                MissingUniqueKeys = RequiredUniqueKeyBindings
                    .Select(static binding => binding.Signature)
                    .ToArray()
            };
        }
    }
}

internal sealed record ReportingTriggerBinding(
    string TriggerName,
    string TableName,
    string FunctionName);

internal sealed record ReportingColumnBinding(
    string TableName,
    string ColumnName,
    bool MustBeNotNull = false)
{
    internal string QualifiedName => $"{TableName}.{ColumnName}";
}

internal sealed record ReportingUniqueKeyBinding(
    string TableName,
    IReadOnlyList<string> Columns,
    string? Predicate = null)
{
    internal string ColumnNames => string.Join(",", Columns);

    internal string CanonicalColumnNames =>
        string.Join(",", Columns.Order(StringComparer.Ordinal));

    internal string NormalizedPredicate =>
        string.Concat((Predicate ?? string.Empty)
            .Where(static character =>
                !char.IsWhiteSpace(character)
                && character is not '('
                && character is not ')'))
            .ToLowerInvariant();

    internal string Signature =>
        string.IsNullOrWhiteSpace(Predicate)
            ? $"{TableName}({ColumnNames})"
            : $"{TableName}({ColumnNames}) where {Predicate}";
}

public sealed record ReportingDeploymentProbeResult(
    bool IsReachable,
    IReadOnlyList<string> MissingTables,
    IReadOnlyList<string> MissingTriggers,
    string? FailureCode)
{
    public IReadOnlyList<string> MissingColumns { get; init; } = [];

    public IReadOnlyList<string> MissingUniqueKeys { get; init; } = [];

    public bool IsComplete =>
        IsReachable
        && MissingTables.Count == 0
        && MissingTriggers.Count == 0
        && MissingColumns.Count == 0
        && MissingUniqueKeys.Count == 0;

    public bool HasTable(string tableName) =>
        IsReachable &&
        !MissingTables.Contains(tableName, StringComparer.Ordinal);

    public bool HasTrigger(string triggerName) =>
        IsReachable &&
        !MissingTriggers.Contains(triggerName, StringComparer.Ordinal);

    public bool HasColumn(string tableName, string columnName) =>
        IsReachable &&
        !MissingColumns.Contains(
            $"{tableName}.{columnName}",
            StringComparer.Ordinal);

    public bool HasUniqueKey(string signature) =>
        IsReachable &&
        !MissingUniqueKeys.Contains(signature, StringComparer.Ordinal);
}
