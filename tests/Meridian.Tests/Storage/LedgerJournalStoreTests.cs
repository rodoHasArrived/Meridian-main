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
    public void PostingGuard_OpenPeriod_AllowsOriginatingAndAdjustmentEntries()
    {
        var period = BuildAccountingPeriod("Open");
        var originating = BuildBalancedJournalWrite(period.PeriodId);
        var adjustment = originating with { PostingKind = LedgerPostingKindDto.Adjustment };

        var originatingAct = () => LedgerPeriodPostingGuard.Validate(originating, period);
        var adjustmentAct = () => LedgerPeriodPostingGuard.Validate(adjustment, period);

        originatingAct.Should().NotThrow();
        adjustmentAct.Should().NotThrow();
    }

    [Fact]
    public void PostingGuard_SoftClosedPeriod_RejectsOriginatingEntry()
    {
        var period = BuildAccountingPeriod("SoftClosed");
        var write = BuildBalancedJournalWrite(period.PeriodId);

        var act = () => LedgerPeriodPostingGuard.Validate(write, period);

        act.Should().Throw<LedgerValidationException>()
            .WithMessage("*soft-closed*Adjustment*");
    }

    [Fact]
    public void PostingGuard_SoftClosedPeriod_AllowsAdjustmentEntry()
    {
        var period = BuildAccountingPeriod("SoftClosed");
        var write = BuildBalancedJournalWrite(period.PeriodId) with
        {
            PostingKind = LedgerPostingKindDto.Adjustment
        };

        var act = () => LedgerPeriodPostingGuard.Validate(write, period);

        act.Should().NotThrow();
    }

    [Fact]
    public void PostingGuard_HardClosedPeriod_RejectsAdjustmentEntry()
    {
        var period = BuildAccountingPeriod("HardClosed");
        var write = BuildBalancedJournalWrite(period.PeriodId) with
        {
            PostingKind = LedgerPostingKindDto.Adjustment
        };

        var act = () => LedgerPeriodPostingGuard.Validate(write, period);

        act.Should().Throw<LedgerValidationException>()
            .WithMessage("*hard-closed*no postings*");
    }

    [Fact]
    public void PostingGuard_UnknownStatus_RejectsEntry()
    {
        var period = BuildAccountingPeriod("Archived");
        var write = BuildBalancedJournalWrite(period.PeriodId);

        var act = () => LedgerPeriodPostingGuard.Validate(write, period);

        act.Should().Throw<LedgerValidationException>()
            .WithMessage("*unsupported status 'Archived'*");
    }

    [Fact]
    public void PostingGuard_TimestampOutsidePeriod_RejectsEntry()
    {
        var period = BuildAccountingPeriod("Open");
        var write = BuildBalancedJournalWrite(
            period.PeriodId,
            DateTimeOffset.Parse("2026-02-01T00:00:00Z"));

        var act = () => LedgerPeriodPostingGuard.Validate(write, period);

        act.Should().Throw<LedgerValidationException>()
            .WithMessage("*outside accounting period*2026-P01*");
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

    [Fact]
    public void LedgerPostingKindMigration_DefinesJournalPostingKindColumnsAndIndexes()
    {
        var sql = ReadMigration("V_ledger_006__journal_posting_kind.sql");

        sql.Should().Contain("add column if not exists posting_kind text not null default 'Originating'");
        sql.Should().Contain("ck_journal_entries_posting_kind");
        sql.Should().Contain("ck_journal_legs_posting_kind");
        sql.Should().Contain("ix_journal_entries_period_posting_kind");
        sql.Should().Contain("ix_journal_legs_period_posting_kind");
    }

    private static LedgerJournalEntryWrite BuildBalancedJournalWrite(
        Guid periodId,
        DateTimeOffset? timestamp = null)
    {
        var journalEntryId = Guid.NewGuid();
        var occurredAt = timestamp ?? DateTimeOffset.Parse("2026-01-31T21:00:00Z");
        const string description = "Balanced month-end test posting";
        return new LedgerJournalEntryWrite(
            new JournalEntry(
                journalEntryId,
                occurredAt,
                description,
                [
                    new LedgerEntry(
                        Guid.NewGuid(),
                        journalEntryId,
                        occurredAt,
                        new LedgerAccount("Cash", LedgerAccountType.Asset),
                        debit: 100m,
                        credit: 0m,
                        description),
                    new LedgerEntry(
                        Guid.NewGuid(),
                        journalEntryId,
                        occurredAt,
                        new LedgerAccount("Management fees", LedgerAccountType.Revenue),
                        debit: 0m,
                        credit: 100m,
                        description)
                ]),
            AggregateId: Guid.NewGuid(),
            PeriodId: periodId);
    }

    private static LedgerAccountingPeriod BuildAccountingPeriod(string status) =>
        new(
            PeriodId: Guid.NewGuid(),
            LedgerBookId: Guid.NewGuid(),
            FiscalYear: 2026,
            PeriodNo: 1,
            Label: "2026-P01",
            StartDate: new DateOnly(2026, 1, 1),
            EndDate: new DateOnly(2026, 1, 31),
            Status: status,
            OpenedAt: DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
            ClosedAt: null,
            Version: 1);

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
