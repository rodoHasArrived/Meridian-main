using FluentAssertions;

namespace Meridian.Tests.SecurityMaster;

/// <summary>
/// Migration 054 creates the exposure snapshot, collateral balance and margin call tables
/// additively, with the invariants the store relies on; migration 055 admits the collateral
/// governance stream without narrowing any existing one.
/// </summary>
[Trait("Category", "Unit")]
public sealed class OtcCollateralMigrationTests
{
    private const string MigrationFile = "054_otc_exposure_and_collateral.sql";
    private const string GovernanceStreamMigrationFile = "055_security_governance_events_otc_collateral_stream.sql";

    [Fact]
    public void Migration_CreatesTheCollateralModel_WithItsInvariants()
    {
        var sql = ReadMigration(MigrationFile);

        sql.Should().Contain("create table if not exists __SCHEMA__.otc_exposure_snapshots");
        sql.Should().Contain("create table if not exists __SCHEMA__.otc_collateral_balances");
        sql.Should().Contain("create table if not exists __SCHEMA__.otc_margin_calls");
        sql.Should().Contain("references __SCHEMA__.otc_netting_sets (netting_set_id)");
        sql.Should().Contain("references __SCHEMA__.otc_collateral_agreements (collateral_agreement_id)");
        sql.Should().Contain("references __SCHEMA__.otc_exposure_snapshots (snapshot_id)", "a call cites the snapshot it was issued from");
        sql.Should().Contain("references __SCHEMA__.securities (security_id)", "security collateral may name its instrument");
        sql.Should().NotContain("on delete cascade");

        sql.Should().Contain("check (version >= 1)");
        sql.Should().Contain("check (netting_basis in ('CloseOutNet', 'Gross'))");
        sql.Should().Contain("check (status in ('Complete', 'Incomplete'))");
        sql.Should().Contain("unique (netting_set_id, as_of_date, version)", "recomputing a date appends the next version");
        sql.Should().Contain("check (direction in ('Posted', 'Received'))");
        sql.Should().Contain("check (kind in ('Cash', 'Security'))");
        sql.Should().Contain("check (quantity >= 0 and unit_value > 0 and valued_amount >= 0)");
        sql.Should().Contain("check (haircut_percent >= 0 and haircut_percent <= 100)");
        sql.Should().Contain("check (movement_kind in ('Delivery', 'Return'))");
        sql.Should().Contain("check (mover in ('Fund', 'Counterparty'))");
        sql.Should().Contain("check (status in ('Issued', 'Agreed', 'Disputed', 'Settled', 'Cancelled'))");
        sql.Should().Contain("check (amount_called > 0 and (amount_agreed is null or amount_agreed > 0) and (settled_amount is null or settled_amount > 0))");
        sql.Should().Contain("check (due_on is null or due_on >= issued_on)");
        sql.Should().Contain("check (status <> 'Settled' or (settled_on is not null and settled_amount is not null))");
        sql.Should().Contain("check (status <> 'Disputed' or dispute_reason is not null)");

        sql.Should().Contain("create unique index if not exists otc_margin_calls_one_open_per_side_idx");
        sql.Should().Contain("on __SCHEMA__.otc_margin_calls (netting_set_id, mover, movement_kind)", "one open call per netting set, mover and movement kind: a delivery and a return by the same party are distinct obligations");
        sql.Should().Contain("where status in ('Issued', 'Agreed', 'Disputed')", "only open calls are unique");
        sql.Should().Contain("before update or delete on __SCHEMA__.otc_exposure_snapshots", "snapshots are append-only");
        sql.Should().Contain("errcode = '23514'");
        sql.Should().NotContain("drop ");
        sql.Should().NotContain("truncate");
    }

    [Fact]
    public void GovernanceStreamMigration_ReplacesOnlyTheStreamCheck_AndKeepsEveryExistingStream()
    {
        var sql = ReadMigration(GovernanceStreamMigrationFile);

        sql.Should().Contain("drop constraint security_governance_events_stream_check;");
        sql.Should().Contain("check (stream in ('OperatorOverrides', 'Aliases', 'ProjectionRebuild', 'UnderlyingGraph', 'Curves', 'OtcCollateral'))");
        System.Text.RegularExpressions.Regex.Matches(sql, @"\bdrop\b").Should().ContainSingle("only the stream check is replaced");
        sql.Should().NotContain("delete ").And.NotContain("update ").And.NotContain("truncate");
        sql.Should().Contain("comment on column __SCHEMA__.security_governance_events.security_id", "collateral events carry no security id");
    }

    private static string ReadMigration(string migrationFile)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Meridian.sln")))
        {
            directory = directory.Parent;
        }

        directory.Should().NotBeNull();
        return File.ReadAllText(Path.Combine(directory!.FullName, "src", "Meridian.Storage", "SecurityMaster", "Migrations", migrationFile));
    }
}
