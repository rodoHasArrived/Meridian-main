using FluentAssertions;

namespace Meridian.Tests.Storage;

/// <summary>
/// SEC-005 slice 4c (read performance): the V_ledger_022 migration adds expression indexes on
/// <c>lower(trim(tenant_id))</c> so the tenant read predicate (which compares the same expression) is
/// index-friendly instead of forcing a sequential scan. Non-DB structural assertions on the migration
/// SQL — the indexes only affect query planning, so behavioral coverage is unnecessary; this verifies the
/// three indexes exist, match the predicate expression exactly, are partial, and are idempotent.
/// </summary>
public sealed class TenantLowerIndexMigrationTests
{
    private const string MigrationFile = "V_ledger_022__tenant_lower_indexes.sql";

    [Fact]
    public void Migration_AddsExpressionIndexesMatchingThePredicate_ForAllFundScopedTables()
    {
        var sql = ReadMigration();

        foreach (var table in new[] { "ledger_books", "accounting_periods", "operations_continuity_workflows" })
        {
            sql.Should().Contain($"on __SCHEMA__.{table} (lower(trim(tenant_id)))",
                $"the {table} expression index must match the read predicate's lower(trim(tenant_id))");
        }
    }

    [Fact]
    public void Migration_IndexesAreIdempotentAndPartial()
    {
        var sql = ReadMigration();

        // One idempotent `create index if not exists` and one partial filter per fund-scoped table.
        CountOf(sql, "create index if not exists").Should().Be(3);
        CountOf(sql, "where tenant_id is not null").Should().Be(3);
    }

    private static int CountOf(string haystack, string needle)
        => haystack.Split(needle, StringSplitOptions.None).Length - 1;

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
