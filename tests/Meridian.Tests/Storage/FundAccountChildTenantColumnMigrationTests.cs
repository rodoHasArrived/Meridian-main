using FluentAssertions;

namespace Meridian.Tests.Storage;

/// <summary>
/// SEC-005 slice 4c (fund-account child partitioning): the 004 migration extends the nullable
/// <c>tenant_id</c> partition column from <c>account_definition</c> (003) to every child table reached
/// by <c>account_id</c> / run id, so the alternate-identifier read/write routes are uniformly scoped.
/// Non-DB structural assertions on the migration SQL — behavioral coverage lives in the DB-gated suite
/// (skipped without a database) — verifying the column-add and both indexes are idempotent per table,
/// and that the expression index matches the read predicate (<c>lower(trim(tenant_id))</c>).
/// </summary>
public sealed class FundAccountChildTenantColumnMigrationTests
{
    private const string MigrationFile = "004_fund_account_child_tenant_columns.sql";

    private static readonly string[] ChildTables =
    [
        "account_balance_snapshot",
        "custodian_statement_batch",
        "custodian_position_line",
        "bank_statement_batch",
        "bank_statement_line",
        "account_reconciliation_run",
        "account_reconciliation_breaks",
        "account_sync_history",
        "account_margin_snapshot",
    ];

    [Fact]
    public void Migration_AddsNullableTenantColumn_ToEveryChildTable_Idempotently()
    {
        var sql = ReadMigration();

        foreach (var table in ChildTables)
        {
            sql.Should().Contain($"alter table __SCHEMA__.{table}", $"table {table} must be partitioned");
        }

        CountOf(sql, "add column if not exists tenant_id text null")
            .Should().Be(ChildTables.Length, "each child table gets exactly one idempotent tenant column-add");
    }

    [Fact]
    public void Migration_CreatesPartialAndExpressionIndexes_PerTable_Idempotently()
    {
        var sql = ReadMigration();

        foreach (var table in ChildTables)
        {
            // The expression index must match the read predicate expression exactly so it stays index-served.
            sql.Should().Contain($"on __SCHEMA__.{table} (lower(trim(tenant_id)))",
                $"table {table} needs the lower(trim()) expression index that matches the predicate");
        }

        CountOf(sql, "lower(trim(tenant_id))")
            .Should().Be(ChildTables.Length, "one expression index per child table");
        CountOf(sql, "where tenant_id is not null")
            .Should().Be(ChildTables.Length * 2, "both the plain and expression tenant indexes are partial, per table");
        CountOf(sql, "create index if not exists")
            .Should().Be(ChildTables.Length * 2, "two idempotent tenant indexes per child table");
    }

    private static int CountOf(string haystack, string needle)
        => haystack.Split(needle, StringSplitOptions.None).Length - 1;

    private static string ReadMigration()
    {
        var root = FindRepoRoot();
        var path = Path.Combine(root, "src", "Meridian.Storage", "FundAccounts", "Migrations", MigrationFile);
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
