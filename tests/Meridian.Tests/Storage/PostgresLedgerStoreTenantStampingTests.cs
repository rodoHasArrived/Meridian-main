using FluentAssertions;

namespace Meridian.Tests.Storage;

/// <summary>
/// SEC-005 slice 4c-ii: the Postgres ledger store stamps the partition <c>tenant_id</c> on fund-scoped
/// writes — ledger books from the authoritative fund_profile_tenancy registry, accounting periods from
/// their ledger book — so rows created after the 4c-i backfill carry their owning tenant. These are non-DB
/// structural assertions on the store's embedded SQL (the behavioral Postgres coverage is the integration
/// suite, skipped without a database); they guard the stamping from being silently dropped.
/// </summary>
public sealed class PostgresLedgerStoreTenantStampingTests
{
    [Fact]
    public void LedgerBookInsert_StampsTenantFromRegistry()
    {
        var source = ReadStoreSource();

        // The ledger_books insert resolves the owning tenant from the fund_profile_tenancy registry on the
        // application's normalized fund key, so a new book carries its partition tenant.
        source.Should().Contain("(select t.tenant_id");
        source.Should().Contain("fund_profile_tenancy");
        source.Should().Contain("where t.fund_profile_id = lower(trim(@fund_profile_id))");
    }

    [Fact]
    public void AccountingPeriodInsert_InheritsTenantFromLedgerBook()
    {
        var source = ReadStoreSource();

        // A period inherits its ledger book's tenant (matching the 4c-i backfill), or null when it has no
        // book / the book is not yet stamped (fail-open).
        source.Should().Contain("(select b.tenant_id");
        source.Should().Contain("where b.ledger_book_id = @ledger_book_id");
    }

    private static string ReadStoreSource()
    {
        var root = FindRepoRoot();
        var path = Path.Combine(root, "src", "Meridian.Storage", "Ledger", "PostgresLedgerJournalStore.cs");
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
