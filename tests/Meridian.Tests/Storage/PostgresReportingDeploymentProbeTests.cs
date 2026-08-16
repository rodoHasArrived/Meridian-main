using FluentAssertions;
using Meridian.Storage.Reporting;
using Xunit;

namespace Meridian.Tests.Storage;

public sealed class PostgresReportingDeploymentProbeTests
{
    [Fact]
    public void RequiredTriggerBindings_AreEnabledAndBoundToExpectedSchemaObjects()
    {
        var bindings = PostgresReportingDeploymentProbe.RequiredTriggerBindings;

        bindings.Should().NotBeEmpty();
        bindings.Select(static binding => binding.TriggerName)
            .Should()
            .OnlyHaveUniqueItems()
            .And.Equal(PostgresReportingDeploymentProbe.RequiredTriggers);
        bindings.Select(static binding => binding.TableName)
            .Should()
            .OnlyContain(static tableName =>
                PostgresReportingDeploymentProbe.RequiredTables.Contains(
                    tableName,
                    StringComparer.Ordinal));
        bindings.Should().OnlyContain(static binding =>
            !string.IsNullOrWhiteSpace(binding.FunctionName));
        var accessGrantGuard = bindings.Single(static binding =>
            binding.TriggerName
                == PostgresReportingDeploymentProbe
                    .AccessGrantArtifactConsumptionTriggerName);
        accessGrantGuard.FunctionName.Should().Be("guard_reporting_access_grant_mutation");
        accessGrantGuard.RequiredTypeMask.Should().Be(
            PostgresReportingDeploymentProbe.BeforeRowInsertUpdateDeleteTriggerTypeMask);
        accessGrantGuard.ForbiddenTypeMask.Should().Be(
            ReportingTriggerType.Truncate | ReportingTriggerType.Instead);
        accessGrantGuard.DefinitionFragment.Should().Be(
            PostgresReportingDeploymentProbe
                .AccessGrantInsertCompatibilityDefinitionFragment);
        accessGrantGuard.AdditionalDefinitionFragment.Should().Be(
            PostgresReportingDeploymentProbe
                .AccessGrantLegacyUseCompatibilityDefinitionFragment);
        accessGrantGuard.NormalizedDefinitionFragment.Should().Contain(
            "new.consumed_artifact_idsisnull");
        accessGrantGuard.NormalizedAdditionalDefinitionFragment.Should().Contain(
            "old.consumed_artifact_idsisnullandnew.consumed_artifact_idsisnull");
        accessGrantGuard.MatchesDefinition(
                """
                if tg_op = 'INSERT' and new.consumed_artifact_ids is null then
                    raise exception 'new writer required';
                end if;
                """)
            .Should().BeFalse(
                "the insert fence alone cannot prove retained legacy grants are protected");
        accessGrantGuard.MatchesDefinition(
                """
                if tg_op = 'INSERT' and new.consumed_artifact_ids is null then
                    raise exception 'new writer required';
                end if;
                if new.use_count = old.use_count + 1
                    and old.consumed_artifact_ids is null
                    and new.consumed_artifact_ids is null then
                    raise exception 'artifact identity required';
                end if;
                """)
            .Should().BeTrue();

        var statementAuthorityBindings = new[]
        {
            (
                TriggerName:
                    PostgresReportingDeploymentProbe.StatementDocumentGuardTriggerName,
                TableName: "reporting_statement_reconciliation_documents",
                FunctionName: "guard_reporting_statement_document_mutation",
                RequiredTypeMask:
                    PostgresReportingDeploymentProbe.BeforeRowUpdateDeleteTriggerTypeMask,
                ForbiddenTypeMask:
                    ReportingTriggerType.Insert
                    | ReportingTriggerType.Truncate
                    | ReportingTriggerType.Instead,
                DefinitionFragment: "old.is_immutable",
                AdditionalDefinitionFragment:
                    (string?)"new.document_version <> old.document_version + 1"),
            (
                TriggerName:
                    PostgresReportingDeploymentProbe
                        .StatementDocumentTruncateGuardTriggerName,
                TableName: "reporting_statement_reconciliation_documents",
                FunctionName: "reject_reporting_statement_document_truncate",
                RequiredTypeMask:
                    PostgresReportingDeploymentProbe.BeforeStatementTruncateTriggerTypeMask,
                ForbiddenTypeMask:
                    ReportingTriggerType.Row
                    | ReportingTriggerType.Insert
                    | ReportingTriggerType.Delete
                    | ReportingTriggerType.Update
                    | ReportingTriggerType.Instead,
                DefinitionFragment:
                    "statement reconciliation authority mappings cannot be truncated",
                AdditionalDefinitionFragment: (string?)null),
            (
                TriggerName:
                    PostgresReportingDeploymentProbe.StatementDocumentRevisionTriggerName,
                TableName: "reporting_statement_reconciliation_documents",
                FunctionName: "retain_reporting_statement_document_revision",
                RequiredTypeMask:
                    PostgresReportingDeploymentProbe.AfterRowInsertUpdateTriggerTypeMask,
                ForbiddenTypeMask:
                    ReportingTriggerType.Before
                    | ReportingTriggerType.Delete
                    | ReportingTriggerType.Truncate
                    | ReportingTriggerType.Instead,
                DefinitionFragment:
                    "reporting_statement_reconciliation_document_revisions",
                AdditionalDefinitionFragment: (string?)"new.document_version"),
            (
                TriggerName:
                    PostgresReportingDeploymentProbe.StatementRevisionAppendTriggerName,
                TableName: "reporting_statement_reconciliation_document_revisions",
                FunctionName: "validate_reporting_statement_revision_append",
                RequiredTypeMask:
                    PostgresReportingDeploymentProbe.BeforeRowInsertTriggerTypeMask,
                ForbiddenTypeMask:
                    ReportingTriggerType.Delete
                    | ReportingTriggerType.Update
                    | ReportingTriggerType.Truncate
                    | ReportingTriggerType.Instead,
                DefinitionFragment:
                    "new.document_version is distinct from current_mapping.document_version",
                AdditionalDefinitionFragment:
                    (string?)"new.previous_content_hash_sha256 is distinct from previous_revision.content_hash_sha256"),
            (
                TriggerName:
                    PostgresReportingDeploymentProbe.StatementRevisionGuardTriggerName,
                TableName: "reporting_statement_reconciliation_document_revisions",
                FunctionName: "guard_reporting_statement_revision_mutation",
                RequiredTypeMask:
                    PostgresReportingDeploymentProbe.BeforeRowUpdateDeleteTriggerTypeMask,
                ForbiddenTypeMask:
                    ReportingTriggerType.Insert
                    | ReportingTriggerType.Truncate
                    | ReportingTriggerType.Instead,
                DefinitionFragment:
                    "statement reconciliation document revisions are append-only",
                AdditionalDefinitionFragment: (string?)null),
            (
                TriggerName:
                    PostgresReportingDeploymentProbe
                        .StatementRevisionTruncateGuardTriggerName,
                TableName: "reporting_statement_reconciliation_document_revisions",
                FunctionName: "reject_reporting_statement_revision_truncate",
                RequiredTypeMask:
                    PostgresReportingDeploymentProbe.BeforeStatementTruncateTriggerTypeMask,
                ForbiddenTypeMask:
                    ReportingTriggerType.Row
                    | ReportingTriggerType.Insert
                    | ReportingTriggerType.Delete
                    | ReportingTriggerType.Update
                    | ReportingTriggerType.Instead,
                DefinitionFragment:
                    "statement reconciliation document revisions cannot be truncated",
                AdditionalDefinitionFragment: (string?)null)
        };
        foreach (var expected in statementAuthorityBindings)
        {
            var binding = bindings.Single(candidate =>
                candidate.TriggerName == expected.TriggerName);
            binding.TableName.Should().Be(expected.TableName);
            binding.FunctionName.Should().Be(expected.FunctionName);
            binding.RequiredTypeMask.Should().Be(expected.RequiredTypeMask);
            binding.ForbiddenTypeMask.Should().Be(expected.ForbiddenTypeMask);
            binding.DefinitionFragment.Should().Be(expected.DefinitionFragment);
            binding.AdditionalDefinitionFragment
                .Should().Be(expected.AdditionalDefinitionFragment);
            binding.MatchesDefinition(
                    $"{expected.DefinitionFragment}; "
                    + expected.AdditionalDefinitionFragment)
                .Should().BeTrue();
            if (expected.AdditionalDefinitionFragment is not null)
            {
                binding.MatchesDefinition(expected.DefinitionFragment)
                    .Should().BeFalse(
                        "both authority-function definition fragments are required");
            }
        }

        var allKnownTriggerTypeBits =
            ReportingTriggerType.Row
            | ReportingTriggerType.Before
            | ReportingTriggerType.Insert
            | ReportingTriggerType.Delete
            | ReportingTriggerType.Update
            | ReportingTriggerType.Truncate
            | ReportingTriggerType.Instead;
        statementAuthorityBindings
            .Select(expected => bindings.Single(binding =>
                binding.TriggerName == expected.TriggerName))
            .Append(accessGrantGuard)
            .Should()
            .OnlyContain(binding =>
                (binding.RequiredTypeMask & binding.ForbiddenTypeMask) == 0
                && (binding.RequiredTypeMask | binding.ForbiddenTypeMask)
                    == allKnownTriggerTypeBits,
                "authority triggers must exactly classify every known PostgreSQL trigger type bit");

        PostgresReportingDeploymentProbe.ProbeCommandText
            .Should().Contain("trigger_row.tgenabled in ('O', 'A')")
            .And.Contain("existing.table_name = required.table_name")
            .And.Contain("existing.function_name = required.function_name")
            .And.Contain("existing.trigger_type & required.required_type_mask")
            .And.Contain("existing.trigger_type & required.forbidden_type_mask")
            .And.Contain("pg_catalog.pg_get_functiondef")
            .And.Contain("required.normalized_definition_fragment")
            .And.Contain("required.normalized_additional_definition_fragment")
            .And.Contain("function_schema.nspname = @schema");
    }

    [Fact]
    public void RequiredColumnBindings_CoverOperationalClaimsAndFences()
    {
        var bindings = PostgresReportingDeploymentProbe.RequiredColumnBindings;

        bindings.Select(static binding => binding.QualifiedName)
            .Should()
            .OnlyHaveUniqueItems()
            .And.Contain(
            new[]
            {
                "reporting_schema_migrations.filename",
                "reporting_schema_migrations.checksum",
                "reporting_run_create_claims.tenant_id",
                "reporting_run_create_claims.run_id",
                "reporting_run_create_claims.run_id_key",
                "reporting_run_create_claims.lease_owner",
                "reporting_run_create_claims.claimed_at_utc",
                "reporting_run_create_claims.lease_expires_at_utc",
                "reporting_run_create_claims.lease_version",
                "reporting_access_grants.consumed_artifact_ids",
                "reporting_schedule_snapshots.due_at_utc",
                "reporting_schedule_snapshots.lease_owner",
                "reporting_schedule_snapshots.lease_expires_at_utc",
                "reporting_schedule_snapshots.lease_version",
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
                "reporting_statement_reconciliation_document_revisions.tenant_id",
                "reporting_statement_reconciliation_document_revisions.company_id",
                "reporting_statement_reconciliation_document_revisions.workflow_id",
                "reporting_statement_reconciliation_document_revisions.document_key",
                "reporting_statement_reconciliation_document_revisions.document_version",
                "reporting_statement_reconciliation_document_revisions.previous_content_hash_sha256",
                "reporting_statement_reconciliation_document_revisions.previous_byte_size",
                "reporting_statement_reconciliation_document_revisions.previous_updated_at_utc",
                "reporting_statement_reconciliation_document_revisions.content_hash_sha256",
                "reporting_statement_reconciliation_document_revisions.byte_size",
                "reporting_statement_reconciliation_document_revisions.is_immutable",
                "reporting_statement_reconciliation_document_revisions.mapping_stored_at_utc",
                "reporting_statement_reconciliation_document_revisions.mapping_updated_at_utc",
                "reporting_statement_reconciliation_document_revisions.recorded_at_utc"
            });
        bindings.Select(static binding => binding.TableName)
            .Should()
            .OnlyContain(static tableName =>
                PostgresReportingDeploymentProbe.RequiredTables.Contains(
                    tableName,
                    StringComparer.Ordinal));
        PostgresReportingDeploymentProbe.ProbeCommandText
            .Should().Contain("pg_catalog.pg_attribute")
            .And.Contain("existing.column_name = required.column_name")
            .And.Contain("required.must_be_not_null and not existing.is_not_null")
            .And.Contain("required.must_have_no_default and existing.has_default");

        bindings.Single(static binding =>
                binding.QualifiedName == "reporting_schema_migrations.filename")
            .MustBeNotNull.Should().BeTrue();
        bindings.Single(static binding =>
                binding.QualifiedName == "reporting_schema_migrations.checksum")
            .MustBeNotNull.Should().BeTrue();
        bindings.Single(static binding =>
                binding.QualifiedName == "reporting_access_grants.consumed_artifact_ids")
            .MustHaveNoDefault.Should().BeTrue();

        var nullableStatementColumns = new HashSet<string>(StringComparer.Ordinal)
        {
            "reporting_statement_reconciliation_document_revisions.previous_content_hash_sha256",
            "reporting_statement_reconciliation_document_revisions.previous_byte_size",
            "reporting_statement_reconciliation_document_revisions.previous_updated_at_utc"
        };
        var statementColumns = bindings
            .Where(static binding =>
                binding.TableName.StartsWith(
                    "reporting_statement_reconciliation_document",
                    StringComparison.Ordinal))
            .ToArray();
        statementColumns.Should().HaveCount(24);
        statementColumns.Should().OnlyContain(binding =>
            binding.MustBeNotNull
                == !nullableStatementColumns.Contains(binding.QualifiedName));
    }

    [Fact]
    public void RequiredConstraintBindings_CoverDeliveryAndStatementAuthorities()
    {
        var bindings = PostgresReportingDeploymentProbe.RequiredConstraintBindings;

        var consumedArtifacts = bindings.Single(static binding =>
            binding.Signature
                == "reporting_access_grants.ck_reporting_access_grant_consumed_artifacts");
        consumedArtifacts.Signature.Should().Be(
            "reporting_access_grants.ck_reporting_access_grant_consumed_artifacts");
        consumedArtifacts.ConstraintType.Should().Be("c");
        consumedArtifacts.DefinitionFragment.Should().Be(
            PostgresReportingDeploymentProbe.ConsumedArtifactConstraintDefinitionFragment);
        consumedArtifacts.NormalizedDefinitionFragment.Should().Contain(
            "use_count=0orcardinalityartifact_ids=0orcardinalityconsumed_artifact_ids>0");
        PostgresReportingDeploymentProbe.StatementIdentityUtf8ByteBudgetDefinitionFragment
            .Should().Contain(
                $"<= {PostgresStatementReconciliationReportAuthorityStore.MaximumCompositeIdentityUtf8Bytes}");

        var expectedStatementConstraints = new[]
        {
            (
                Signature:
                    "reporting_statement_reconciliation_documents.fk_reporting_statement_document_blob",
                ConstraintType: "f",
                DefinitionFragment: "FOREIGN KEY (tenant_id, content_hash_sha256)"),
            (
                Signature:
                    "reporting_statement_reconciliation_documents.ck_reporting_statement_document_key",
                ConstraintType: "c",
                DefinitionFragment: "document_key = btrim(document_key)"),
            (
                Signature:
                    "reporting_statement_reconciliation_documents.ck_reporting_statement_document_identity_utf8_bytes",
                ConstraintType: "c",
                DefinitionFragment:
                    PostgresReportingDeploymentProbe
                        .StatementIdentityUtf8ByteBudgetDefinitionFragment),
            (
                Signature:
                    "reporting_statement_reconciliation_document_revisions.fk_reporting_statement_revision_blob",
                ConstraintType: "f",
                DefinitionFragment: "FOREIGN KEY (tenant_id, content_hash_sha256)"),
            (
                Signature:
                    "reporting_statement_reconciliation_document_revisions.fk_reporting_statement_revision_previous_blob",
                ConstraintType: "f",
                DefinitionFragment:
                    "FOREIGN KEY (tenant_id, previous_content_hash_sha256)"),
            (
                Signature:
                    "reporting_statement_reconciliation_document_revisions.ck_reporting_statement_revision_chain",
                ConstraintType: "c",
                DefinitionFragment:
                    "document_version = 1 and previous_content_hash_sha256 is null"),
            (
                Signature:
                    "reporting_statement_reconciliation_document_revisions.ck_reporting_statement_revision_identity_utf8_bytes",
                ConstraintType: "c",
                DefinitionFragment:
                    PostgresReportingDeploymentProbe
                        .StatementIdentityUtf8ByteBudgetDefinitionFragment)
        };
        foreach (var expected in expectedStatementConstraints)
        {
            var binding = bindings.Single(candidate =>
                candidate.Signature == expected.Signature);
            binding.ConstraintType.Should().Be(expected.ConstraintType);
            binding.DefinitionFragment.Should().Be(expected.DefinitionFragment);
            binding.NormalizedDefinitionFragment.Should().NotBeEmpty();
        }

        PostgresReportingDeploymentProbe.ProbeCommandText
            .Should().Contain("pg_catalog.pg_constraint")
            .And.Contain("existing.constraint_type = required.constraint_type")
            .And.Contain("existing.is_validated")
            .And.Contain("pg_catalog.pg_get_constraintdef")
            .And.Contain("required.normalized_definition_fragment");
    }

    [Fact]
    public void RequiredApplicationCompatibilityBindings_RequireExactMigrationAndSchemaCapability()
    {
        var bindings =
            PostgresReportingDeploymentProbe.RequiredApplicationCompatibilityBindings;

        bindings.Select(static binding => binding.CompatibilityMarker)
            .Should()
            .Equal(
                PostgresReportingDeploymentProbe
                    .AccessGrantArtifactConsumptionCompatibilityMarker,
                PostgresReportingDeploymentProbe
                    .StatementReconciliationAuthorityCompatibilityMarker);
        var accessGrantConsumption = bindings.Single(static binding =>
            binding.CompatibilityMarker
                == PostgresReportingDeploymentProbe
                    .AccessGrantArtifactConsumptionCompatibilityMarker);
        accessGrantConsumption.CompatibilityMarker.Should().Be(
            PostgresReportingDeploymentProbe
                .AccessGrantArtifactConsumptionCompatibilityMarker);
        accessGrantConsumption.MigrationFileName.Should().Be(
            PostgresReportingDeploymentProbe
                .AccessGrantArtifactConsumptionMigrationFileName);
        accessGrantConsumption.RequiredColumns.Should().Contain(
            "reporting_access_grants.consumed_artifact_ids");
        accessGrantConsumption.RequiredTriggers.Should().Contain(
            PostgresReportingDeploymentProbe
                .AccessGrantArtifactConsumptionTriggerName);
        accessGrantConsumption.RequiredConstraints.Should().Contain(
            "reporting_access_grants.ck_reporting_access_grant_consumed_artifacts");

        var statementAuthority = bindings.Single(static binding =>
            binding.CompatibilityMarker
                == PostgresReportingDeploymentProbe
                    .StatementReconciliationAuthorityCompatibilityMarker);
        statementAuthority.MigrationFileName.Should().Be(
            PostgresReportingDeploymentProbe
                .StatementReconciliationAuthorityMigrationFileName);
        statementAuthority.RequiredColumns.Should().Equal(
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
            "reporting_statement_reconciliation_document_revisions.recorded_at_utc");
        statementAuthority.RequiredTriggers.Should().Equal(
            PostgresReportingDeploymentProbe.StatementDocumentGuardTriggerName,
            PostgresReportingDeploymentProbe.StatementDocumentTruncateGuardTriggerName,
            PostgresReportingDeploymentProbe.StatementDocumentRevisionTriggerName,
            PostgresReportingDeploymentProbe.StatementRevisionAppendTriggerName,
            PostgresReportingDeploymentProbe.StatementRevisionGuardTriggerName,
            PostgresReportingDeploymentProbe.StatementRevisionTruncateGuardTriggerName);
        statementAuthority.RequiredConstraints.Should().Equal(
            "reporting_statement_reconciliation_documents.fk_reporting_statement_document_blob",
            "reporting_statement_reconciliation_documents.ck_reporting_statement_document_key",
            "reporting_statement_reconciliation_documents.ck_reporting_statement_document_identity_utf8_bytes",
            "reporting_statement_reconciliation_document_revisions.fk_reporting_statement_revision_blob",
            "reporting_statement_reconciliation_document_revisions.fk_reporting_statement_revision_previous_blob",
            "reporting_statement_reconciliation_document_revisions.ck_reporting_statement_revision_chain",
            "reporting_statement_reconciliation_document_revisions.ck_reporting_statement_revision_identity_utf8_bytes");
        PostgresReportingDeploymentProbe.ProbeCommandText
            .Should().Contain("__SCHEMA__.reporting_schema_migrations")
            .And.Contain("existing.filename = required.migration_file")
            .And.Contain("existing.checksum = required.migration_checksum");
        PostgresReportingDeploymentProbe.ComputeMigrationChecksum("select 1;")
            .Should().MatchRegex("^[0-9a-f]{64}$");
    }

    [Fact]
    public void ResolveMissingCompatibilityMarkers_AnyMigrationOrSchemaMismatch_ShouldFailClosed()
    {
        foreach (var binding in
                 PostgresReportingDeploymentProbe.RequiredApplicationCompatibilityBindings)
        {
            var scenarios = new[]
            {
                PostgresReportingDeploymentProbe.ResolveMissingCompatibilityMarkers(
                    [binding.CompatibilityMarker],
                    [],
                    [],
                    []),
                PostgresReportingDeploymentProbe.ResolveMissingCompatibilityMarkers(
                    [],
                    [binding.RequiredColumns[0]],
                    [],
                    []),
                PostgresReportingDeploymentProbe.ResolveMissingCompatibilityMarkers(
                    [],
                    [],
                    [binding.RequiredTriggers[0]],
                    []),
                PostgresReportingDeploymentProbe.ResolveMissingCompatibilityMarkers(
                    [],
                    [],
                    [],
                    [binding.RequiredConstraints[0]])
            };

            scenarios.Should().OnlyContain(result =>
                result.SequenceEqual(
                    new[] { binding.CompatibilityMarker },
                    StringComparer.Ordinal));
        }
    }

    [Fact]
    public void ReportingDeploymentProbeResult_RequiresEveryCompatibilityMarkerToBeComplete()
    {
        var result = new ReportingDeploymentProbeResult(
            IsReachable: true,
            MissingTables: [],
            MissingTriggers: [],
            FailureCode: null)
        {
            VerifiedCompatibilityMarkers =
            [
                PostgresReportingDeploymentProbe
                    .AccessGrantArtifactConsumptionCompatibilityMarker
            ]
        };

        result.HasCompatibilityMarker(
                PostgresReportingDeploymentProbe
                    .AccessGrantArtifactConsumptionCompatibilityMarker)
            .Should().BeTrue();
        result.HasCompatibilityMarker(
                PostgresReportingDeploymentProbe
                    .StatementReconciliationAuthorityCompatibilityMarker)
            .Should().BeFalse();
        result.IsComplete.Should().BeFalse();

        var complete = result with
        {
            VerifiedCompatibilityMarkers =
            [
                PostgresReportingDeploymentProbe
                    .AccessGrantArtifactConsumptionCompatibilityMarker,
                PostgresReportingDeploymentProbe
                    .StatementReconciliationAuthorityCompatibilityMarker
            ]
        };
        complete.IsComplete.Should().BeTrue();
    }

    [Fact]
    public void ReportingDeploymentProbeResult_UnprobedSchemaObject_ShouldFailClosed()
    {
        var result = new ReportingDeploymentProbeResult(
            IsReachable: true,
            MissingTables: [],
            MissingTriggers: [],
            FailureCode: null);

        result.HasTable("unprobed_table").Should().BeFalse();
        result.HasTrigger("unprobed_trigger").Should().BeFalse();
        result.HasColumn("unprobed_table", "unprobed_column").Should().BeFalse();
        result.HasUniqueKey("unprobed_table(unprobed_column)").Should().BeFalse();
        result.HasConstraint("unprobed_table.unprobed_constraint").Should().BeFalse();
    }

    [Fact]
    public void CreateFailureResult_MissingMigrationLedger_ShouldRemainReachableAndFailClosed()
    {
        var result = PostgresReportingDeploymentProbe.CreateFailureResult(
            isReachable: true,
            PostgresReportingDeploymentProbe.SchemaIncompleteFailureCode);

        result.IsReachable.Should().BeTrue(
            "an absent migration ledger is schema incompleteness, not a database liveness failure");
        result.FailureCode.Should().Be(
            PostgresReportingDeploymentProbe.SchemaIncompleteFailureCode);
        result.MissingTables.Should().Contain("reporting_schema_migrations");
        result.MissingCompatibilityMarkers.Should().Contain(
        [
            PostgresReportingDeploymentProbe
                .AccessGrantArtifactConsumptionCompatibilityMarker,
            PostgresReportingDeploymentProbe
                .StatementReconciliationAuthorityCompatibilityMarker
        ]);
        result.IsComplete.Should().BeFalse();
    }

    [Theory]
    [InlineData("42P01")]
    [InlineData("42703")]
    public void IncompleteMigrationLedgerShape_ShouldRemainReachableSchemaFailure(
        string sqlState)
    {
        PostgresReportingDeploymentProbe.IsIncompleteSchemaError(sqlState)
            .Should().BeTrue();
        var result = PostgresReportingDeploymentProbe.CreateFailureResult(
            isReachable: true,
            PostgresReportingDeploymentProbe.SchemaIncompleteFailureCode);

        result.IsReachable.Should().BeTrue();
        result.FailureCode.Should().Be(
            PostgresReportingDeploymentProbe.SchemaIncompleteFailureCode);
        result.MissingTables.Should().Contain("reporting_schema_migrations");
        result.MissingCompatibilityMarkers.Should().Contain(
        [
            PostgresReportingDeploymentProbe
                .AccessGrantArtifactConsumptionCompatibilityMarker,
            PostgresReportingDeploymentProbe
                .StatementReconciliationAuthorityCompatibilityMarker
        ]);
    }

    [Fact]
    public void RequiredUniqueKeyBindings_CoverConflictAndIdempotencyAuthorities()
    {
        var bindings = PostgresReportingDeploymentProbe.RequiredUniqueKeyBindings;

        bindings.Select(static binding => binding.Signature)
            .Should()
            .OnlyHaveUniqueItems()
            .And.Contain(
            [
                "reporting_schema_migrations(filename)",
                "reporting_run_snapshots(tenant_id,run_id_key)",
                "reporting_run_create_claims(tenant_id,run_id_key)",
                "reporting_schedule_snapshots(tenant_id,company_id,schedule_id_key)",
                "reporting_delivery_jobs(idempotency_key)",
                "reporting_delivery_jobs(access_grant_id) where access_grant_id IS NOT NULL",
                "reporting_delivery_receipts(job_id,receipt_id)",
                "reporting_statement_reconciliation_documents(tenant_id,company_id,workflow_id,document_key)",
                "reporting_statement_reconciliation_document_revisions(tenant_id,company_id,workflow_id,document_key,document_version)"
            ]);
        bindings.Select(static binding => binding.TableName)
            .Should()
            .OnlyContain(static tableName =>
                PostgresReportingDeploymentProbe.RequiredTables.Contains(
                    tableName,
                    StringComparer.Ordinal));
        PostgresReportingDeploymentProbe.ProbeCommandText
            .Should().Contain("pg_catalog.pg_index")
            .And.Contain("unique_index.indisunique")
            .And.Contain("unique_index.indisvalid")
            .And.Contain("unique_index.indisready")
            .And.Contain("unique_index.indimmediate")
            .And.Contain("pg_catalog.pg_get_expr")
            .And.Contain("bool_and(key_column.attribute_number > 0)")
            .And.Contain("key_column.ordinality <= unique_index.indnkeyatts")
            .And.Contain("existing.column_names = required.column_names")
            .And.Contain("existing.predicate = required.predicate");

        var partialKey = bindings.Single(static binding =>
            binding.TableName == "reporting_delivery_jobs"
            && binding.ColumnNames == "access_grant_id");
        partialKey.NormalizedPredicate.Should().Be("access_grant_idisnotnull");

        new ReportingUniqueKeyBinding("test", ["second", "first"])
            .CanonicalColumnNames.Should().Be("first,second");
    }
}
