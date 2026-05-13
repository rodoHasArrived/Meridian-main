using FluentAssertions;
using Meridian.Contracts.Ledger;
using Meridian.Ledger;
using Meridian.Storage.Ledger;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Tests.Storage;

public sealed class LedgerJournalStoreTests
{
    [Fact]
    public void LedgerJournalStoreOptions_DefaultsToLedgerSchemaAndPeriodLocking()
    {
        var options = new LedgerJournalStoreOptions();

        options.SchemaName.Should().Be("ledger");
        options.EnablePeriodLocking.Should().BeTrue();
        options.ConnectionString.Should().BeEmpty();
    }

    [Fact]
    public void AddLedgerJournalStore_RegistersOptionsAndStore()
    {
        const string connectionString = "Host=localhost;Database=meridian_test;Username=meridian;Password=secret";
        var services = new ServiceCollection();

        services.AddLedgerJournalStore(connectionString);

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<LedgerJournalStoreOptions>().ConnectionString.Should().Be(connectionString);
        provider.GetRequiredService<ILedgerJournalStore>().Should().BeOfType<PostgresLedgerJournalStore>();
        provider.GetRequiredService<ILedgerBookService>().Should().BeOfType<PostgresLedgerBookService>();
    }

    [Fact]
    public void AddLedgerJournalStore_RejectsBlankConnectionString()
    {
        var services = new ServiceCollection();

        var act = () => services.AddLedgerJournalStore(" ");

        act.Should().Throw<ArgumentException>()
            .WithMessage("*connection string*");
    }

    [Fact]
    public async Task AppendAsync_UnbalancedJournal_RejectsBeforeOpeningConnection()
    {
        var store = new PostgresLedgerJournalStore(new LedgerJournalStoreOptions());
        var write = new LedgerJournalEntryWrite(
            BuildUnbalancedJournalEntry(),
            AggregateId: Guid.NewGuid(),
            PeriodId: Guid.NewGuid());

        var act = () => store.AppendAsync(write);

        await act.Should().ThrowAsync<LedgerValidationException>()
            .WithMessage("*not balanced*");
    }

    [Fact]
    public void LedgerJournalMigration_DefinesJournalTablesAndLineageColumns()
    {
        var sql = ReadMigration("V_ledger_001__journal_entries.sql");

        sql.Should().Contain("create table if not exists __SCHEMA__.journal_entries");
        sql.Should().Contain("create table if not exists __SCHEMA__.journal_legs");
        sql.Should().Contain("unique (journal_entry_id)");
        sql.Should().Contain("aggregate_id uuid not null");
        sql.Should().Contain("period_id uuid not null");
        sql.Should().Contain("command_id uuid null");
        sql.Should().Contain("correlation_id uuid null");
    }

    [Fact]
    public void LedgerPeriodMigration_DefinesAccountingPeriodsAndCloseAudit()
    {
        var sql = ReadMigration("V_ledger_002__accounting_periods.sql");

        sql.Should().Contain("create table if not exists __SCHEMA__.accounting_periods");
        sql.Should().Contain("optimistic_version bigint not null default 1");
        sql.Should().Contain("create table if not exists __SCHEMA__.period_close_events");
        sql.Should().Contain("period_version bigint not null");
    }

    [Fact]
    public void LedgerBasisLineageMigration_DefinesJournalBasisColumnsAndIndexes()
    {
        var sql = ReadMigration("V_ledger_005__journal_basis_lineage.sql");

        sql.Should().Contain("add column if not exists accounting_basis text not null default 'Primary'");
        sql.Should().Contain("add column if not exists accounting_policy_id text not null default 'legacy-v1'");
        sql.Should().Contain("add column if not exists rule_id text null");
        sql.Should().Contain("add column if not exists source_event_id uuid null");
        sql.Should().Contain("ix_journal_entries_basis_period");
        sql.Should().Contain("ix_journal_entries_source_event");
        sql.Should().Contain("ix_journal_legs_basis_account");
    }

    private static JournalEntry BuildUnbalancedJournalEntry()
    {
        var journalEntryId = Guid.NewGuid();
        var timestamp = DateTimeOffset.Parse("2026-01-31T21:00:00Z");
        const string description = "Unbalanced month-end test posting";
        return new JournalEntry(
            journalEntryId,
            timestamp,
            description,
            [
                new LedgerEntry(
                    Guid.NewGuid(),
                    journalEntryId,
                    timestamp,
                    new LedgerAccount("Cash", LedgerAccountType.Asset),
                    debit: 100m,
                    credit: 0m,
                    description),
            ]);
    }

    private static string ReadMigration(string fileName)
    {
        var root = FindRepoRoot();
        var path = Path.Combine(root, "src", "Meridian.Storage", "Ledger", "Migrations", fileName);
        return File.ReadAllText(path);
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "src", "Meridian.Storage")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Unable to locate Meridian repository root.");
    }
}
