using FluentAssertions;

namespace Meridian.Tests.SecurityMaster;

/// <summary>Migration 050 creates the four contract-model tables additively, with the invariants the store relies on.</summary>
[Trait("Category", "Unit")]
public sealed class OtcContractsMigrationTests
{
    private const string MigrationFile = "050_otc_contracts.sql";

    [Fact]
    public void Migration_CreatesTheContractModel_WithItsInvariants()
    {
        var sql = ReadMigration();

        sql.Should().Contain("create table if not exists __SCHEMA__.otc_master_agreements");
        sql.Should().Contain("create table if not exists __SCHEMA__.otc_collateral_agreements");
        sql.Should().Contain("create table if not exists __SCHEMA__.otc_netting_sets");
        sql.Should().Contain("create table if not exists __SCHEMA__.otc_contracts");
        sql.Should().Contain("references __SCHEMA__.securities (security_id)", "a contract names its instrument");
        sql.Should().NotContain("references __SCHEMA__.securities (security_id) on delete cascade", "an instrument cannot be deleted from under its contracts");
        sql.Should().Contain("references __SCHEMA__.otc_netting_sets (netting_set_id)");
        sql.Should().Contain("check (direction in ('Buy', 'Sell'))");
        sql.Should().Contain("check (status in ('Pending', 'Confirmed', 'Terminated', 'Novated', 'Matured'))");
        sql.Should().Contain("check (notional > 0)");
        sql.Should().Contain("check (threshold_ours >= 0 and threshold_theirs >= 0 and minimum_transfer_amount >= 0)");
        sql.Should().Contain("create unique index if not exists otc_contracts_external_trade_idx", "an external trade id maps to one contract");
        sql.Should().NotContain("drop ");
    }

    private static string ReadMigration()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Meridian.sln")))
        {
            directory = directory.Parent;
        }

        directory.Should().NotBeNull();
        return File.ReadAllText(Path.Combine(directory!.FullName, "src", "Meridian.Storage", "SecurityMaster", "Migrations", MigrationFile));
    }
}
