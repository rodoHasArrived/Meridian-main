using FluentAssertions;

namespace Meridian.Tests.Storage;

/// <summary>
/// W9-GOV-008 criterion 3, database posture. <c>V_ledger_032</c> hash-chains the accounting-action
/// audit family. These are non-DB structural assertions on the migration SQL — the behavioural
/// Postgres coverage lives in the integration suite, which CI skips without a database — and they
/// exist mainly to pin the two properties that are easy to lose in a later edit: the declared
/// pre-chain boundary, and the seed being non-destructive under a replaying migration runner.
/// </summary>
public sealed class AccountingAuditChainMigrationTests
{
    private const string MigrationFile = "V_ledger_032__accounting_audit_chain.sql";

    [Fact]
    public void Migration_AddsNullableChainColumns_Idempotently()
    {
        var sql = ReadMigration();

        sql.Should().Contain("alter table __SCHEMA__.accounting_action_audit_events");
        sql.Should().Contain("add column if not exists chain_sequence bigint null");
        sql.Should().Contain("add column if not exists payload_hash text null");
        sql.Should().Contain("add column if not exists previous_hash text null");
        sql.Should().Contain("add column if not exists entry_hash text null");
    }

    [Fact]
    public void Migration_LeavesPreChainRowsUnchained_RatherThanBackfillingInventedHashes()
    {
        var sql = ReadMigration();

        // The columns are nullable and there is no update statement over the retained history. Rows
        // that predate the chain keep null chain columns — that is what "outside the chain" looks
        // like on disk. A backfill here could only invent hashes, which would present pre-upgrade
        // events as tamper-evident when nothing ever protected them.
        sql.Should().NotContain("update __SCHEMA__.accounting_action_audit_events");
    }

    [Fact]
    public void Migration_DeclaresWhereTheChainStartsAndHowMuchHistoryPrecedesIt()
    {
        var sql = ReadMigration();

        sql.Should().Contain("genesis_sequence bigint not null");
        sql.Should().Contain("pre_chain_event_count bigint not null");

        // The count is captured from the retained history at seed time, so a later reader cannot
        // mistake "these rows were never chained" for "the chain is broken".
        sql.Should().Contain("(select count(*) from __SCHEMA__.accounting_action_audit_events)");
    }

    [Fact]
    public void Migration_SeedsTheHeadWithoutResettingAnAdvancedChain()
    {
        var sql = ReadMigration();

        sql.Should().Contain("insert into __SCHEMA__.accounting_action_audit_chain_head");

        // The runner replays every script. An upsert here would reset next_sequence and last_hash on
        // every startup, discarding the head — after which a truncated history would verify happily.
        sql.Should().Contain("on conflict (chain_id) do nothing");
        sql.Should().NotContain("on conflict (chain_id) do update");
    }

    [Fact]
    public void Migration_ConstrainsTheHeadToASingleRowWithAdvancingSequences()
    {
        var sql = ReadMigration();

        sql.Should().Contain("ck_accounting_audit_chain_singleton");
        sql.Should().Contain("check (chain_id = 1)");
        sql.Should().Contain("next_sequence >= genesis_sequence");
    }

    [Fact]
    public void Migration_RefusesTwoEventsAtTheSameChainPosition()
    {
        var sql = ReadMigration();

        // Two rows at one sequence is a forked chain, and under concurrency the database is the only
        // place that can refuse it — a check in the appender loses the race it exists to prevent.
        sql.Should().Contain("create unique index if not exists ux_accounting_audit_chain_sequence");
        sql.Should().Contain("where chain_sequence is not null");
    }

    [Fact]
    public void Migration_ConstrainsDigestShapeAtTheColumn()
    {
        var sql = ReadMigration();

        // A malformed hash written and then read back reports as a mismatch, which looks like
        // tampering rather than the write bug it is.
        sql.Should().Contain("ck_accounting_audit_entry_hash_digest");
        sql.Should().Contain("ck_accounting_audit_previous_hash_digest");
        sql.Should().Contain("ck_accounting_audit_payload_hash_digest");
        sql.Should().Contain("'^[0-9a-f]{64}$'");
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

        throw new InvalidOperationException("Could not locate the repository root from the test output directory.");
    }
}
