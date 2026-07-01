using FluentAssertions;

namespace Meridian.Tests.Storage;

/// <summary>
/// SEC-005 slice 4c (store extension): the V_ledger_021 migration adds a nullable <c>tenant_id</c> partition
/// column to operations_continuity_workflows and backfills it transitively from the registry-stamped
/// ledger_books via the workflow's JSON ledgerBookId. These are non-DB structural assertions on the
/// migration SQL — the behavioral Postgres coverage lives in the integration suite (skipped without a
/// database) — verifying the migration is idempotent and re-runnable (the runner replays every script).
/// </summary>
public sealed class OperationsContinuityTenantColumnMigrationTests
{
    private const string MigrationFile = "V_ledger_021__operations_continuity_tenant_column.sql";

    [Fact]
    public void Migration_AddsNullableTenantColumn_Idempotently()
    {
        var sql = ReadMigration();

        sql.Should().Contain("alter table __SCHEMA__.operations_continuity_workflows");
        sql.Should().Contain("add column if not exists tenant_id text null");
    }

    [Fact]
    public void Migration_BackfillsTenantFromLedgerBookViaWorkflowJson_AndIsRepeatable()
    {
        var sql = ReadMigration();

        // The workflow inherits its owning tenant from its ledger book (stamped by V_ledger_020, which runs
        // first), resolved through the JSON ledgerBookId. Only fills rows still null so a re-run is a no-op.
        sql.Should().Contain("from __SCHEMA__.ledger_books b");
        sql.Should().Contain("b.ledger_book_id = (w.workflow_json ->> 'ledgerBookId')::uuid");
        sql.Should().Contain("and b.tenant_id is not null");
        sql.Should().Contain("and w.tenant_id is null");
    }

    [Fact]
    public void Migration_CreatesPartialTenantIndex_Idempotently()
    {
        var sql = ReadMigration();

        sql.Should().Contain("create index if not exists ix_operations_continuity_workflows_tenant");
        sql.Should().Contain("where tenant_id is not null");
    }

    private static string ReadMigration()
    {
        var root = FindRepoRoot();
        var path = Path.Combine(root, "src", "Meridian.Storage", "Ledger", "Migrations", MigrationFile);
        return File.ReadAllText(path);
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Meridian.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Unable to locate Meridian repository root.");
    }
}
