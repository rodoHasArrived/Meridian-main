using System.Security.Cryptography;
using System.Text;
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
    public const string AccessGrantArtifactConsumptionCompatibilityMarker =
        "reporting-access-grant-artifact-consumption:v1";

    public const string AccessGrantArtifactConsumptionTriggerName =
        "trg_reporting_access_grants_guard_v012";

    public const string StatementReconciliationAuthorityCompatibilityMarker =
        "reporting-statement-reconciliation-authority:v1";

    public const string StatementDocumentGuardTriggerName =
        "trg_reporting_statement_document_guard";

    public const string StatementDocumentRevisionTriggerName =
        "trg_reporting_statement_document_revision";

    public const string StatementDocumentTruncateGuardTriggerName =
        "trg_reporting_statement_document_truncate_guard";

    public const string StatementRevisionAppendTriggerName =
        "trg_reporting_statement_revision_append";

    public const string StatementRevisionGuardTriggerName =
        "trg_reporting_statement_revision_guard";

    public const string StatementRevisionTruncateGuardTriggerName =
        "trg_reporting_statement_revision_truncate_guard";

    public const string SchemaIncompleteFailureCode =
        "REPORTING_SCHEMA_INCOMPLETE";

    public const string BinaryMigrationAssetUnavailableFailureCode =
        "REPORTING_BINARY_MIGRATION_ASSET_UNAVAILABLE";

    internal const string AccessGrantArtifactConsumptionMigrationFileName =
        "012_reporting_access_grant_artifact_consumption.sql";

    internal const string StatementReconciliationAuthorityMigrationFileName =
        "013_reporting_statement_reconciliation_authority.sql";

    internal const int BeforeRowInsertUpdateDeleteTriggerTypeMask =
        ReportingTriggerType.Row
        | ReportingTriggerType.Before
        | ReportingTriggerType.Insert
        | ReportingTriggerType.Delete
        | ReportingTriggerType.Update;

    internal const int BeforeRowUpdateDeleteTriggerTypeMask =
        ReportingTriggerType.Row
        | ReportingTriggerType.Before
        | ReportingTriggerType.Delete
        | ReportingTriggerType.Update;

    internal const int AfterRowInsertUpdateTriggerTypeMask =
        ReportingTriggerType.Row
        | ReportingTriggerType.Insert
        | ReportingTriggerType.Update;

    internal const int BeforeRowInsertTriggerTypeMask =
        ReportingTriggerType.Row
        | ReportingTriggerType.Before
        | ReportingTriggerType.Insert;

    internal const int BeforeStatementTruncateTriggerTypeMask =
        ReportingTriggerType.Before
        | ReportingTriggerType.Truncate;

    internal const string ConsumedArtifactConstraintDefinitionFragment =
        "consumed_artifact_ids <@ artifact_ids "
        + "and cardinality(consumed_artifact_ids) <= use_count "
        + "and (use_count = 0 "
        + "or cardinality(artifact_ids) = 0 "
        + "or cardinality(consumed_artifact_ids) > 0)";

    internal const string AccessGrantInsertCompatibilityDefinitionFragment =
        "tg_op = 'INSERT' and new.consumed_artifact_ids is null";

    internal const string AccessGrantLegacyUseCompatibilityDefinitionFragment =
        "new.use_count = old.use_count + 1 "
        + "and old.consumed_artifact_ids is null "
        + "and new.consumed_artifact_ids is null";

    internal const string StatementIdentityUtf8ByteBudgetDefinitionFragment =
        "octet_length(tenant_id) + octet_length(company_id) "
        + "+ octet_length(workflow_id) + octet_length(document_key) <= 2048";

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
        "reporting_delivery_receipts",
        "reporting_statement_reconciliation_documents",
        "reporting_statement_reconciliation_document_revisions"
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
            AccessGrantArtifactConsumptionTriggerName,
            "reporting_access_grants",
            "guard_reporting_access_grant_mutation",
            RequiredTypeMask: BeforeRowInsertUpdateDeleteTriggerTypeMask,
            ForbiddenTypeMask:
                ReportingTriggerType.Truncate
                | ReportingTriggerType.Instead,
            DefinitionFragment: AccessGrantInsertCompatibilityDefinitionFragment,
            AdditionalDefinitionFragment:
                AccessGrantLegacyUseCompatibilityDefinitionFragment),
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
            "guard_reporting_reconciliation_evidence_v2_append"),
        new(
            StatementDocumentGuardTriggerName,
            "reporting_statement_reconciliation_documents",
            "guard_reporting_statement_document_mutation",
            RequiredTypeMask: BeforeRowUpdateDeleteTriggerTypeMask,
            ForbiddenTypeMask:
                ReportingTriggerType.Insert
                | ReportingTriggerType.Truncate
                | ReportingTriggerType.Instead,
            DefinitionFragment: "old.is_immutable",
            AdditionalDefinitionFragment:
                "new.document_version <> old.document_version + 1"),
        new(
            StatementDocumentTruncateGuardTriggerName,
            "reporting_statement_reconciliation_documents",
            "reject_reporting_statement_document_truncate",
            RequiredTypeMask: BeforeStatementTruncateTriggerTypeMask,
            ForbiddenTypeMask:
                ReportingTriggerType.Row
                | ReportingTriggerType.Insert
                | ReportingTriggerType.Delete
                | ReportingTriggerType.Update
                | ReportingTriggerType.Instead,
            DefinitionFragment:
                "statement reconciliation authority mappings cannot be truncated"),
        new(
            StatementDocumentRevisionTriggerName,
            "reporting_statement_reconciliation_documents",
            "retain_reporting_statement_document_revision",
            RequiredTypeMask: AfterRowInsertUpdateTriggerTypeMask,
            ForbiddenTypeMask:
                ReportingTriggerType.Before
                | ReportingTriggerType.Delete
                | ReportingTriggerType.Truncate
                | ReportingTriggerType.Instead,
            DefinitionFragment:
                "reporting_statement_reconciliation_document_revisions",
            AdditionalDefinitionFragment: "new.document_version"),
        new(
            StatementRevisionAppendTriggerName,
            "reporting_statement_reconciliation_document_revisions",
            "validate_reporting_statement_revision_append",
            RequiredTypeMask: BeforeRowInsertTriggerTypeMask,
            ForbiddenTypeMask:
                ReportingTriggerType.Delete
                | ReportingTriggerType.Update
                | ReportingTriggerType.Truncate
                | ReportingTriggerType.Instead,
            DefinitionFragment:
                "new.document_version is distinct from current_mapping.document_version",
            AdditionalDefinitionFragment:
                "new.previous_content_hash_sha256 is distinct from previous_revision.content_hash_sha256"),
        new(
            StatementRevisionGuardTriggerName,
            "reporting_statement_reconciliation_document_revisions",
            "guard_reporting_statement_revision_mutation",
            RequiredTypeMask: BeforeRowUpdateDeleteTriggerTypeMask,
            ForbiddenTypeMask:
                ReportingTriggerType.Insert
                | ReportingTriggerType.Truncate
                | ReportingTriggerType.Instead,
            DefinitionFragment:
                "statement reconciliation document revisions are append-only"),
        new(
            StatementRevisionTruncateGuardTriggerName,
            "reporting_statement_reconciliation_document_revisions",
            "reject_reporting_statement_revision_truncate",
            RequiredTypeMask: BeforeStatementTruncateTriggerTypeMask,
            ForbiddenTypeMask:
                ReportingTriggerType.Row
                | ReportingTriggerType.Insert
                | ReportingTriggerType.Delete
                | ReportingTriggerType.Update
                | ReportingTriggerType.Instead,
            DefinitionFragment:
                "statement reconciliation document revisions cannot be truncated")
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
        new(
            "reporting_access_grants",
            "consumed_artifact_ids",
            MustHaveNoDefault: true),
        new("reporting_schedule_snapshots", "due_at_utc"),
        new("reporting_schedule_snapshots", "lease_owner"),
        new("reporting_schedule_snapshots", "lease_expires_at_utc"),
        new("reporting_schedule_snapshots", "lease_version"),
        new("reporting_statement_reconciliation_documents", "tenant_id", MustBeNotNull: true),
        new("reporting_statement_reconciliation_documents", "company_id", MustBeNotNull: true),
        new("reporting_statement_reconciliation_documents", "workflow_id", MustBeNotNull: true),
        new("reporting_statement_reconciliation_documents", "document_key", MustBeNotNull: true),
        new("reporting_statement_reconciliation_documents", "content_hash_sha256", MustBeNotNull: true),
        new("reporting_statement_reconciliation_documents", "byte_size", MustBeNotNull: true),
        new("reporting_statement_reconciliation_documents", "is_immutable", MustBeNotNull: true),
        new("reporting_statement_reconciliation_documents", "document_version", MustBeNotNull: true),
        new("reporting_statement_reconciliation_documents", "stored_at_utc", MustBeNotNull: true),
        new("reporting_statement_reconciliation_documents", "updated_at_utc", MustBeNotNull: true),
        new("reporting_statement_reconciliation_document_revisions", "tenant_id", MustBeNotNull: true),
        new("reporting_statement_reconciliation_document_revisions", "company_id", MustBeNotNull: true),
        new("reporting_statement_reconciliation_document_revisions", "workflow_id", MustBeNotNull: true),
        new("reporting_statement_reconciliation_document_revisions", "document_key", MustBeNotNull: true),
        new("reporting_statement_reconciliation_document_revisions", "document_version", MustBeNotNull: true),
        new("reporting_statement_reconciliation_document_revisions", "previous_content_hash_sha256"),
        new("reporting_statement_reconciliation_document_revisions", "previous_byte_size"),
        new("reporting_statement_reconciliation_document_revisions", "previous_updated_at_utc"),
        new("reporting_statement_reconciliation_document_revisions", "content_hash_sha256", MustBeNotNull: true),
        new("reporting_statement_reconciliation_document_revisions", "byte_size", MustBeNotNull: true),
        new("reporting_statement_reconciliation_document_revisions", "is_immutable", MustBeNotNull: true),
        new("reporting_statement_reconciliation_document_revisions", "mapping_stored_at_utc", MustBeNotNull: true),
        new("reporting_statement_reconciliation_document_revisions", "mapping_updated_at_utc", MustBeNotNull: true),
        new("reporting_statement_reconciliation_document_revisions", "recorded_at_utc", MustBeNotNull: true)
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
        new("reporting_delivery_receipts", ["job_id", "receipt_id"]),
        new(
            "reporting_statement_reconciliation_documents",
            ["tenant_id", "company_id", "workflow_id", "document_key"]),
        new(
            "reporting_statement_reconciliation_document_revisions",
            ["tenant_id", "company_id", "workflow_id", "document_key", "document_version"])
    ];

    internal static readonly ReportingConstraintBinding[] RequiredConstraintBindings =
    [
        new(
            "reporting_access_grants",
            "ck_reporting_access_grant_consumed_artifacts",
            ConstraintType: "c",
            DefinitionFragment: ConsumedArtifactConstraintDefinitionFragment),
        new(
            "reporting_statement_reconciliation_documents",
            "fk_reporting_statement_document_blob",
            ConstraintType: "f",
            DefinitionFragment: "FOREIGN KEY (tenant_id, content_hash_sha256)"),
        new(
            "reporting_statement_reconciliation_documents",
            "ck_reporting_statement_document_key",
            ConstraintType: "c",
            DefinitionFragment: "document_key = btrim(document_key)"),
        new(
            "reporting_statement_reconciliation_documents",
            "ck_reporting_statement_document_identity_utf8_bytes",
            ConstraintType: "c",
            DefinitionFragment:
                StatementIdentityUtf8ByteBudgetDefinitionFragment),
        new(
            "reporting_statement_reconciliation_document_revisions",
            "fk_reporting_statement_revision_blob",
            ConstraintType: "f",
            DefinitionFragment: "FOREIGN KEY (tenant_id, content_hash_sha256)"),
        new(
            "reporting_statement_reconciliation_document_revisions",
            "fk_reporting_statement_revision_previous_blob",
            ConstraintType: "f",
            DefinitionFragment:
                "FOREIGN KEY (tenant_id, previous_content_hash_sha256)"),
        new(
            "reporting_statement_reconciliation_document_revisions",
            "ck_reporting_statement_revision_chain",
            ConstraintType: "c",
            DefinitionFragment:
                "document_version = 1 and previous_content_hash_sha256 is null"),
        new(
            "reporting_statement_reconciliation_document_revisions",
            "ck_reporting_statement_revision_identity_utf8_bytes",
            ConstraintType: "c",
            DefinitionFragment:
                StatementIdentityUtf8ByteBudgetDefinitionFragment)
    ];

    internal static readonly ReportingApplicationCompatibilityBinding[]
        RequiredApplicationCompatibilityBindings =
        [
            new(
                AccessGrantArtifactConsumptionCompatibilityMarker,
                AccessGrantArtifactConsumptionMigrationFileName,
                RequiredColumns:
                [
                    "reporting_access_grants.consumed_artifact_ids"
                ],
                RequiredTriggers:
                [
                    AccessGrantArtifactConsumptionTriggerName
                ],
                RequiredConstraints:
                [
                    "reporting_access_grants.ck_reporting_access_grant_consumed_artifacts"
                ]),
            new(
                StatementReconciliationAuthorityCompatibilityMarker,
                StatementReconciliationAuthorityMigrationFileName,
                RequiredColumns:
                [
                    "reporting_statement_reconciliation_documents.tenant_id",
                    "reporting_statement_reconciliation_documents.company_id",
                    "reporting_statement_reconciliation_documents.workflow_id",
                    "reporting_statement_reconciliation_documents.document_key",
                    "reporting_statement_reconciliation_documents.content_hash_sha256",
                    "reporting_statement_reconciliation_documents.byte_size",
                    "reporting_statement_reconciliation_documents.is_immutable",
                    "reporting_statement_reconciliation_documents.document_version",
                    "reporting_statement_reconciliation_documents.stored_at_utc",
                    "reporting_statement_reconciliation_documents.updated_at_utc",
                    "reporting_statement_reconciliation_document_revisions.document_version",
                    "reporting_statement_reconciliation_document_revisions.previous_content_hash_sha256",
                    "reporting_statement_reconciliation_document_revisions.content_hash_sha256",
                    "reporting_statement_reconciliation_document_revisions.recorded_at_utc"
                ],
                RequiredTriggers:
                [
                    StatementDocumentGuardTriggerName,
                    StatementDocumentTruncateGuardTriggerName,
                    StatementDocumentRevisionTriggerName,
                    StatementRevisionAppendTriggerName,
                    StatementRevisionGuardTriggerName,
                    StatementRevisionTruncateGuardTriggerName
                ],
                RequiredConstraints:
                [
                    "reporting_statement_reconciliation_documents.fk_reporting_statement_document_blob",
                    "reporting_statement_reconciliation_documents.ck_reporting_statement_document_key",
                    "reporting_statement_reconciliation_documents.ck_reporting_statement_document_identity_utf8_bytes",
                    "reporting_statement_reconciliation_document_revisions.fk_reporting_statement_revision_blob",
                    "reporting_statement_reconciliation_document_revisions.fk_reporting_statement_revision_previous_blob",
                    "reporting_statement_reconciliation_document_revisions.ck_reporting_statement_revision_chain",
                    "reporting_statement_reconciliation_document_revisions.ck_reporting_statement_revision_identity_utf8_bytes"
                ])
        ];

    private static readonly Lazy<string[]> RequiredApplicationCompatibilityMigrationChecksums =
        new(() =>
            RequiredApplicationCompatibilityBindings
                .Select(static binding =>
                    ComputeMigrationChecksum(ReadMigration(binding.MigrationFileName)))
                .ToArray());

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
            @required_trigger_functions::text[],
            @required_trigger_type_masks::integer[],
            @required_trigger_forbidden_type_masks::integer[],
            @required_trigger_definition_fragments::text[],
            @required_trigger_additional_definition_fragments::text[])
            as required(
                trigger_name,
                table_name,
                function_name,
                required_type_mask,
                forbidden_type_mask,
                normalized_definition_fragment,
                normalized_additional_definition_fragment)
        left join (
            select
                trigger_row.tgname as trigger_name,
                target_table.relname as table_name,
                trigger_function.proname as function_name,
                trigger_row.tgtype::integer as trigger_type,
                regexp_replace(
                    lower(pg_catalog.pg_get_functiondef(trigger_function.oid)),
                    '[[:space:]()]',
                    '',
                    'g') as normalized_definition
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
         and (required.required_type_mask = 0
             or (existing.trigger_type & required.required_type_mask)
                = required.required_type_mask)
         and (required.forbidden_type_mask = 0
             or (existing.trigger_type & required.forbidden_type_mask) = 0)
         and (required.normalized_definition_fragment = ''
              or position(
                  required.normalized_definition_fragment
                  in existing.normalized_definition) > 0)
         and (required.normalized_additional_definition_fragment = ''
              or position(
                  required.normalized_additional_definition_fragment
                  in existing.normalized_definition) > 0)
        where existing.trigger_name is null
        order by required.trigger_name;

        select required.table_name || '.' || required.column_name
        from unnest(
            @required_column_tables::text[],
            @required_columns::text[],
            @required_column_not_null::boolean[],
            @required_column_no_default::boolean[])
            as required(table_name, column_name, must_be_not_null, must_have_no_default)
        left join (
            select
                target_table.relname as table_name,
                column_row.attname as column_name,
                column_row.attnotnull as is_not_null,
                column_row.atthasdef as has_default
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
           or (required.must_have_no_default and existing.has_default)
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

        select required.signature
        from unnest(
            @required_constraint_tables::text[],
            @required_constraints::text[],
            @required_constraint_types::text[],
            @required_constraint_definitions::text[],
            @required_constraint_signatures::text[])
            as required(
                table_name,
                constraint_name,
                constraint_type,
                normalized_definition_fragment,
                signature)
        left join (
            select
                target_table.relname as table_name,
                constraint_row.conname as constraint_name,
                constraint_row.contype::text as constraint_type,
                constraint_row.convalidated as is_validated,
                regexp_replace(
                    lower(pg_catalog.pg_get_constraintdef(constraint_row.oid)),
                    '[[:space:]()]',
                    '',
                    'g') as normalized_definition
            from pg_catalog.pg_constraint constraint_row
            join pg_catalog.pg_class target_table
              on target_table.oid = constraint_row.conrelid
            join pg_catalog.pg_namespace target_schema
              on target_schema.oid = target_table.relnamespace
            where target_schema.nspname = @schema
        ) existing
          on existing.table_name = required.table_name
         and existing.constraint_name = required.constraint_name
         and existing.constraint_type = required.constraint_type
         and existing.is_validated
         and position(
             required.normalized_definition_fragment
             in existing.normalized_definition) > 0
        where existing.constraint_name is null
        order by required.signature;

        select required.compatibility_marker
        from unnest(
            @required_compatibility_markers::text[],
            @required_compatibility_migration_files::text[],
            @required_compatibility_migration_checksums::text[])
            as required(
                compatibility_marker,
                migration_file,
                migration_checksum)
        left join __SCHEMA__.reporting_schema_migrations existing
          on existing.filename = required.migration_file
         and existing.checksum = required.migration_checksum
        where existing.filename is null
        order by required.compatibility_marker;
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
            command.CommandText = ProbeCommandText.Replace(
                "__SCHEMA__",
                $"\"{_schema}\"",
                StringComparison.Ordinal);
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
                "required_trigger_type_masks",
                NpgsqlDbType.Array | NpgsqlDbType.Integer,
                RequiredTriggerBindings.Select(static binding => binding.RequiredTypeMask).ToArray());
            command.Parameters.AddWithValue(
                "required_trigger_forbidden_type_masks",
                NpgsqlDbType.Array | NpgsqlDbType.Integer,
                RequiredTriggerBindings.Select(static binding => binding.ForbiddenTypeMask).ToArray());
            command.Parameters.AddWithValue(
                "required_trigger_definition_fragments",
                NpgsqlDbType.Array | NpgsqlDbType.Text,
                RequiredTriggerBindings
                    .Select(static binding => binding.NormalizedDefinitionFragment)
                    .ToArray());
            command.Parameters.AddWithValue(
                "required_trigger_additional_definition_fragments",
                NpgsqlDbType.Array | NpgsqlDbType.Text,
                RequiredTriggerBindings
                    .Select(static binding => binding.NormalizedAdditionalDefinitionFragment)
                    .ToArray());
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
                "required_column_no_default",
                NpgsqlDbType.Array | NpgsqlDbType.Boolean,
                RequiredColumnBindings.Select(static binding => binding.MustHaveNoDefault).ToArray());
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
            command.Parameters.AddWithValue(
                "required_constraint_tables",
                NpgsqlDbType.Array | NpgsqlDbType.Text,
                RequiredConstraintBindings.Select(static binding => binding.TableName).ToArray());
            command.Parameters.AddWithValue(
                "required_constraints",
                NpgsqlDbType.Array | NpgsqlDbType.Text,
                RequiredConstraintBindings.Select(static binding => binding.ConstraintName).ToArray());
            command.Parameters.AddWithValue(
                "required_constraint_types",
                NpgsqlDbType.Array | NpgsqlDbType.Text,
                RequiredConstraintBindings.Select(static binding => binding.ConstraintType).ToArray());
            command.Parameters.AddWithValue(
                "required_constraint_definitions",
                NpgsqlDbType.Array | NpgsqlDbType.Text,
                RequiredConstraintBindings
                    .Select(static binding => binding.NormalizedDefinitionFragment)
                    .ToArray());
            command.Parameters.AddWithValue(
                "required_constraint_signatures",
                NpgsqlDbType.Array | NpgsqlDbType.Text,
                RequiredConstraintBindings.Select(static binding => binding.Signature).ToArray());
            command.Parameters.AddWithValue(
                "required_compatibility_markers",
                NpgsqlDbType.Array | NpgsqlDbType.Text,
                RequiredApplicationCompatibilityBindings
                    .Select(static binding => binding.CompatibilityMarker)
                    .ToArray());
            command.Parameters.AddWithValue(
                "required_compatibility_migration_files",
                NpgsqlDbType.Array | NpgsqlDbType.Text,
                RequiredApplicationCompatibilityBindings
                    .Select(static binding => binding.MigrationFileName)
                    .ToArray());
            command.Parameters.AddWithValue(
                "required_compatibility_migration_checksums",
                NpgsqlDbType.Array | NpgsqlDbType.Text,
                RequiredApplicationCompatibilityMigrationChecksums.Value);
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

            var missingConstraints = new List<string>();
            if (!reader.NextResult())
            {
                throw new InvalidDataException(
                    "The reporting deployment probe did not return its constraint verification result.");
            }
            while (reader.Read())
            {
                missingConstraints.Add(reader.GetString(0));
            }

            var missingCompatibilityMarkers = new List<string>();
            if (!reader.NextResult())
            {
                throw new InvalidDataException(
                    "The reporting deployment probe did not return its application-compatibility verification result.");
            }
            while (reader.Read())
            {
                missingCompatibilityMarkers.Add(reader.GetString(0));
            }

            missingCompatibilityMarkers = ResolveMissingCompatibilityMarkers(
                    missingCompatibilityMarkers,
                    missingColumns,
                    missingTriggers,
                    missingConstraints)
                .ToList();
            var verifiedCompatibilityMarkers =
                RequiredApplicationCompatibilityBindings
                    .Select(static binding => binding.CompatibilityMarker)
                    .Except(missingCompatibilityMarkers, StringComparer.Ordinal)
                    .Order(StringComparer.Ordinal)
                    .ToArray();

            return new ReportingDeploymentProbeResult(
                IsReachable: true,
                MissingTables: missingTables,
                MissingTriggers: missingTriggers,
                FailureCode: null)
            {
                MissingColumns = missingColumns,
                MissingUniqueKeys = missingUniqueKeys,
                MissingConstraints = missingConstraints,
                MissingCompatibilityMarkers = missingCompatibilityMarkers,
                VerifiedCompatibilityMarkers = verifiedCompatibilityMarkers
            };
        }
        catch (PostgresException exception)
            when (IsIncompleteSchemaError(exception.SqlState))
        {
            return CreateFailureResult(
                isReachable: true,
                SchemaIncompleteFailureCode);
        }
        catch (Exception exception) when (exception is NpgsqlException
            or TimeoutException
            or InvalidOperationException
            or InvalidDataException
            or IOException
            or UnauthorizedAccessException)
        {
            var compatibilityAssetUnavailable =
                exception is IOException or UnauthorizedAccessException;
            return CreateFailureResult(
                isReachable: false,
                compatibilityAssetUnavailable
                    ? BinaryMigrationAssetUnavailableFailureCode
                    : "REPORTING_POSTGRES_UNREACHABLE");
        }
    }

    internal static bool IsIncompleteSchemaError(string? sqlState) =>
        sqlState is "42P01" // undefined_table
            or "42703"; // undefined_column

    internal static ReportingDeploymentProbeResult CreateFailureResult(
        bool isReachable,
        string failureCode) =>
        new(
            IsReachable: isReachable,
            MissingTables: RequiredTables,
            MissingTriggers: RequiredTriggers,
            FailureCode: failureCode)
        {
            MissingColumns = RequiredColumnBindings
                .Select(static binding => binding.QualifiedName)
                .ToArray(),
            MissingUniqueKeys = RequiredUniqueKeyBindings
                .Select(static binding => binding.Signature)
                .ToArray(),
            MissingConstraints = RequiredConstraintBindings
                .Select(static binding => binding.Signature)
                .ToArray(),
            MissingCompatibilityMarkers = RequiredApplicationCompatibilityBindings
                .Select(static binding => binding.CompatibilityMarker)
                .ToArray()
        };

    internal static string ComputeMigrationChecksum(string sql)
    {
        ArgumentNullException.ThrowIfNull(sql);
        return Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(sql)))
            .ToLowerInvariant();
    }

    internal static IReadOnlyList<string> ResolveMissingCompatibilityMarkers(
        IReadOnlyCollection<string> missingMigrationMarkers,
        IReadOnlyCollection<string> missingColumns,
        IReadOnlyCollection<string> missingTriggers,
        IReadOnlyCollection<string> missingConstraints)
    {
        ArgumentNullException.ThrowIfNull(missingMigrationMarkers);
        ArgumentNullException.ThrowIfNull(missingColumns);
        ArgumentNullException.ThrowIfNull(missingTriggers);
        ArgumentNullException.ThrowIfNull(missingConstraints);

        var missing = new HashSet<string>(
            missingMigrationMarkers,
            StringComparer.Ordinal);
        foreach (var binding in RequiredApplicationCompatibilityBindings)
        {
            if (binding.RequiredColumns.Any(missingColumns.Contains)
                || binding.RequiredTriggers.Any(missingTriggers.Contains)
                || binding.RequiredConstraints.Any(missingConstraints.Contains))
            {
                missing.Add(binding.CompatibilityMarker);
            }
        }

        return missing.Order(StringComparer.Ordinal).ToArray();
    }

    private static string ReadMigration(string fileName) =>
        File.ReadAllText(
            Path.Combine(
                AppContext.BaseDirectory,
                "Reporting",
                "Migrations",
                fileName));
}

internal sealed record ReportingTriggerBinding(
    string TriggerName,
    string TableName,
    string FunctionName,
    int RequiredTypeMask = 0,
    int ForbiddenTypeMask = 0,
    string? DefinitionFragment = null,
    string? AdditionalDefinitionFragment = null)
{
    internal string NormalizedDefinitionFragment =>
        NormalizeSqlFragment(DefinitionFragment);

    internal string NormalizedAdditionalDefinitionFragment =>
        NormalizeSqlFragment(AdditionalDefinitionFragment);

    internal bool MatchesDefinition(string functionDefinition)
    {
        var normalizedDefinition = NormalizeSqlFragment(functionDefinition);
        return ContainsFragment(normalizedDefinition, NormalizedDefinitionFragment)
            && ContainsFragment(
                normalizedDefinition,
                NormalizedAdditionalDefinitionFragment);
    }

    private static bool ContainsFragment(
        string normalizedDefinition,
        string normalizedFragment) =>
        normalizedFragment.Length == 0
        || normalizedDefinition.Contains(normalizedFragment, StringComparison.Ordinal);

    private static string NormalizeSqlFragment(string? value) =>
        string.Concat((value ?? string.Empty)
            .Where(static character =>
                !char.IsWhiteSpace(character)
                && character is not '('
                && character is not ')'))
            .ToLowerInvariant();
}

internal sealed record ReportingColumnBinding(
    string TableName,
    string ColumnName,
    bool MustBeNotNull = false,
    bool MustHaveNoDefault = false)
{
    internal string QualifiedName => $"{TableName}.{ColumnName}";
}

internal sealed record ReportingConstraintBinding(
    string TableName,
    string ConstraintName,
    string ConstraintType,
    string DefinitionFragment)
{
    internal string Signature => $"{TableName}.{ConstraintName}";

    internal string NormalizedDefinitionFragment =>
        string.Concat(DefinitionFragment
            .Where(static character =>
                !char.IsWhiteSpace(character)
                && character is not '('
                && character is not ')'))
            .ToLowerInvariant();
}

internal sealed record ReportingApplicationCompatibilityBinding(
    string CompatibilityMarker,
    string MigrationFileName,
    IReadOnlyList<string> RequiredColumns,
    IReadOnlyList<string> RequiredTriggers,
    IReadOnlyList<string> RequiredConstraints);

internal static class ReportingTriggerType
{
    internal const int Row = 1;
    internal const int Before = 2;
    internal const int Insert = 4;
    internal const int Delete = 8;
    internal const int Update = 16;
    internal const int Truncate = 32;
    internal const int Instead = 64;
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

    public IReadOnlyList<string> MissingConstraints { get; init; } = [];

    public IReadOnlyList<string> MissingCompatibilityMarkers { get; init; } = [];

    public IReadOnlyList<string> VerifiedCompatibilityMarkers { get; init; } = [];

    public bool IsComplete =>
        IsReachable
        && MissingTables.Count == 0
        && MissingTriggers.Count == 0
        && MissingColumns.Count == 0
        && MissingUniqueKeys.Count == 0
        && MissingConstraints.Count == 0
        && MissingCompatibilityMarkers.Count == 0
        && PostgresReportingDeploymentProbe.RequiredApplicationCompatibilityBindings
            .All(binding => HasCompatibilityMarker(binding.CompatibilityMarker));

    public bool HasTable(string tableName) =>
        IsReachable
        && PostgresReportingDeploymentProbe.RequiredTables.Contains(
            tableName,
            StringComparer.Ordinal)
        && !MissingTables.Contains(tableName, StringComparer.Ordinal);

    public bool HasTrigger(string triggerName) =>
        IsReachable
        && PostgresReportingDeploymentProbe.RequiredTriggers.Contains(
            triggerName,
            StringComparer.Ordinal)
        && !MissingTriggers.Contains(triggerName, StringComparer.Ordinal);

    public bool HasColumn(string tableName, string columnName) =>
        IsReachable
        && PostgresReportingDeploymentProbe.RequiredColumnBindings.Any(binding =>
            string.Equals(binding.TableName, tableName, StringComparison.Ordinal)
            && string.Equals(binding.ColumnName, columnName, StringComparison.Ordinal))
        && !MissingColumns.Contains(
            $"{tableName}.{columnName}",
            StringComparer.Ordinal);

    public bool HasUniqueKey(string signature) =>
        IsReachable
        && PostgresReportingDeploymentProbe.RequiredUniqueKeyBindings.Any(binding =>
            string.Equals(binding.Signature, signature, StringComparison.Ordinal))
        && !MissingUniqueKeys.Contains(signature, StringComparer.Ordinal);

    public bool HasConstraint(string signature) =>
        IsReachable
        && PostgresReportingDeploymentProbe.RequiredConstraintBindings.Any(binding =>
            string.Equals(binding.Signature, signature, StringComparison.Ordinal))
        && !MissingConstraints.Contains(signature, StringComparer.Ordinal);

    public bool HasCompatibilityMarker(string compatibilityMarker) =>
        IsReachable &&
        VerifiedCompatibilityMarkers.Contains(
            compatibilityMarker,
            StringComparer.Ordinal)
        && !MissingCompatibilityMarkers.Contains(
            compatibilityMarker,
            StringComparer.Ordinal);
}
