using FluentAssertions;

namespace Meridian.Tests.Storage;

/// <summary>
/// SEC-005 slice 4c-i: the V_ledger_020 migration adds a nullable <c>tenant_id</c> partition column to the
/// fund-scoped ledger stores (ledger_books, accounting_periods) and backfills it from the authoritative
/// fund_profile_tenancy registry. These are non-DB structural assertions on the migration SQL — the
/// behavioral Postgres coverage lives in the integration suite (skipped without a database) — verifying the
/// migration is idempotent and re-runnable (the runner replays every script on each startup).
/// </summary>
public sealed class FundScopeTenantColumnMigrationTests
{
    private const string MigrationFile = "V_ledger_020__fund_scope_tenant_columns.sql";

    [Fact]
    public void Migration_AddsNullableTenantColumns_Idempotently()
    {
        var sql = ReadMigration();

        sql.Should().Contain("alter table __SCHEMA__.ledger_books");
        sql.Should().Contain("alter table __SCHEMA__.accounting_periods");
        // Both tables must add the column, and each add must be guarded so replaying the script cannot fail
        // on an existing column — assert the exact count so an omitted/renamed second add is caught.
        CountOf(sql, "add column if not exists tenant_id text null").Should().Be(
            2, "both ledger_books and accounting_periods must idempotently add the tenant_id column");
    }

    [Fact]
    public void Migration_BackfillsTenantFromRegistry_AndIsRepeatable()
    {
        var sql = ReadMigration();

        // ledger_books inherits its owning tenant from the authoritative fund_profile_tenancy registry,
        // joined on the normalized fund key, and only fills rows that are still null so a re-run is a no-op.
        sql.Should().Contain("from __SCHEMA__.fund_profile_tenancy t");
        sql.Should().Contain("t.fund_profile_id = lower(trim(b.fund_profile_id))");
        sql.Should().Contain("and b.tenant_id is null");

        // accounting_periods inherits the tenant of its ledger book; rows without a book stay null (unbound).
        sql.Should().Contain("update __SCHEMA__.accounting_periods p");
        sql.Should().Contain("b.ledger_book_id = p.ledger_book_id");
        sql.Should().Contain("and p.ledger_book_id is not null");
        sql.Should().Contain("and p.tenant_id is null");
    }

    [Fact]
    public void Migration_CreatesTenantIndexes_Idempotently()
    {
        var sql = ReadMigration();

        sql.Should().Contain("create index if not exists ix_ledger_books_tenant");
        sql.Should().Contain("create index if not exists ix_accounting_periods_tenant");
        // Both indexes must be partial (over populated rows only) — assert the exact count so an index that
        // accidentally drops the partial filter is caught.
        CountOf(sql, "where tenant_id is not null").Should().Be(
            2, "both tenant indexes must be partial (where tenant_id is not null)");
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
