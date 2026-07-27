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

        PostgresReportingDeploymentProbe.ProbeCommandText
            .Should().Contain("trigger_row.tgenabled in ('O', 'A')")
            .And.Contain("existing.table_name = required.table_name")
            .And.Contain("existing.function_name = required.function_name")
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
            .And.Contain("required.must_be_not_null and not existing.is_not_null");

        bindings.Single(static binding =>
                binding.QualifiedName == "reporting_schema_migrations.filename")
            .MustBeNotNull.Should().BeTrue();
        bindings.Single(static binding =>
                binding.QualifiedName == "reporting_schema_migrations.checksum")
            .MustBeNotNull.Should().BeTrue();
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
