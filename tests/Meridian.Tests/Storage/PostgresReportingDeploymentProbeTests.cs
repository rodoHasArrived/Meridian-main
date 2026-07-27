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

        PostgresReportingDeploymentProbe.ProbeCommandText
            .Should().Contain("trigger_row.tgenabled in ('O', 'A')")
            .And.Contain("existing.table_name = required.table_name")
            .And.Contain("existing.function_name = required.function_name")
            .And.Contain("existing.trigger_type & required.required_type_mask")
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
                "reporting_schedule_snapshots.lease_version"
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
    }

    [Fact]
    public void RequiredConstraintBindings_CoverConsumedArtifactAuthority()
    {
        var bindings = PostgresReportingDeploymentProbe.RequiredConstraintBindings;

        bindings.Should().ContainSingle();
        var consumedArtifacts = bindings.Single();
        consumedArtifacts.Signature.Should().Be(
            "reporting_access_grants.ck_reporting_access_grant_consumed_artifacts");
        consumedArtifacts.ConstraintType.Should().Be("c");
        consumedArtifacts.DefinitionFragment.Should().Be(
            PostgresReportingDeploymentProbe.ConsumedArtifactConstraintDefinitionFragment);
        consumedArtifacts.NormalizedDefinitionFragment.Should().Contain(
            "use_count=0orcardinalityartifact_ids=0orcardinalityconsumed_artifact_ids>0");
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

        bindings.Should().ContainSingle();
        var accessGrantConsumption = bindings.Single();
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
        var marker =
            PostgresReportingDeploymentProbe
                .AccessGrantArtifactConsumptionCompatibilityMarker;
        var scenarios = new[]
        {
            PostgresReportingDeploymentProbe.ResolveMissingCompatibilityMarkers(
                [marker],
                [],
                [],
                []),
            PostgresReportingDeploymentProbe.ResolveMissingCompatibilityMarkers(
                [],
                ["reporting_access_grants.consumed_artifact_ids"],
                [],
                []),
            PostgresReportingDeploymentProbe.ResolveMissingCompatibilityMarkers(
                [],
                [],
                [
                    PostgresReportingDeploymentProbe
                        .AccessGrantArtifactConsumptionTriggerName
                ],
                []),
            PostgresReportingDeploymentProbe.ResolveMissingCompatibilityMarkers(
                [],
                [],
                [],
                ["reporting_access_grants.ck_reporting_access_grant_consumed_artifacts"])
        };

        scenarios.Should().OnlyContain(result =>
            result.SequenceEqual([marker], StringComparer.Ordinal));
    }

    [Fact]
    public void ReportingDeploymentProbeResult_UnverifiedCompatibilityMarker_ShouldNotBeComplete()
    {
        var result = new ReportingDeploymentProbeResult(
            IsReachable: true,
            MissingTables: [],
            MissingTriggers: [],
            FailureCode: null);

        result.HasCompatibilityMarker(
                PostgresReportingDeploymentProbe
                    .AccessGrantArtifactConsumptionCompatibilityMarker)
            .Should().BeFalse();
        result.IsComplete.Should().BeFalse();
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
            PostgresReportingDeploymentProbe
                .AccessGrantArtifactConsumptionCompatibilityMarker);
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
            PostgresReportingDeploymentProbe
                .AccessGrantArtifactConsumptionCompatibilityMarker);
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
                "reporting_delivery_receipts(job_id,receipt_id)"
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
