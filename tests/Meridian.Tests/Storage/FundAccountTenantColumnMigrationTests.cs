using FluentAssertions;

namespace Meridian.Tests.Storage;

/// <summary>
/// SEC-005 slice 4c (fund-account defense-in-depth): the 003 migration adds a nullable <c>tenant_id</c>
/// partition column to <c>account_definition</c> (a separate database with no in-DB registry linkage, so
/// the column is stamped trust-on-first-use from the writing caller). Non-DB structural assertions on the
/// migration SQL — behavioral coverage lives in the DB-gated suite (skipped without a database) —
/// verifying the column-add and indexes are idempotent, and the expression index matches the read
/// predicate (<c>lower(trim(tenant_id))</c>).
/// </summary>
public sealed class FundAccountTenantColumnMigrationTests
{
    private const string MigrationFile = "003_fund_account_tenant_column.sql";

    [Fact]
    public void Migration_AddsNullableTenantColumn_Idempotently()
    {
        var sql = ReadMigration();

        sql.Should().Contain("alter table __SCHEMA__.account_definition");
        sql.Should().Contain("add column if not exists tenant_id text null");
    }

    [Fact]
    public void Migration_CreatesPartialAndExpressionIndexes_Idempotently()
    {
        var sql = ReadMigration();

        sql.Should().Contain("create index if not exists ix_account_definition_tenant");
        // The expression index must match the read predicate expression exactly so it stays index-served.
        sql.Should().Contain("on __SCHEMA__.account_definition (lower(trim(tenant_id)))");
        CountOf(sql, "where tenant_id is not null").Should().Be(2, "both tenant indexes must be partial");
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
