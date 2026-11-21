using FluentAssertions;
using Meridian.Ledger;
using Meridian.Storage.Ledger;

namespace Meridian.Tests.Storage;

public sealed partial class LedgerBalanceCheckpointPostgresTests
{
    [LedgerDatabaseFact]
    public async Task DeletedNativeCheckpoint_WithRetainedAuditedClose_CannotFallBackToJournalReplay()
    {
        await using var database = await LedgerPostgresTestDatabase.CreateAsync();
        var (book, period) = await CreateBookAndPeriod(database);
        await Append(database, book, period, 100m);
        await HardClose(database, period);
        await Execute(database, """
            alter table {schema}.ledger_balance_checkpoint_rows disable trigger user;
            alter table {schema}.ledger_balance_checkpoints disable trigger user;
            delete from {schema}.ledger_balance_checkpoint_rows;
            delete from {schema}.ledger_balance_checkpoints;
            alter table {schema}.ledger_balance_checkpoints enable trigger user;
            alter table {schema}.ledger_balance_checkpoint_rows enable trigger user;
            """);
        (await Scalar(database, "select count(*) from {schema}.period_close_events where balance_checkpoint_id is not null"))
            .Should().Be(1, "the retained close still certifies the now-missing native artifact");

        Func<Task> read = () => database.RestartJournalStore().ReadTrialBalanceAsOfAsync(book.LedgerBookId, MayEnd);

        await read.Should().ThrowAsync<LedgerValidationException>();
    }

    [LedgerDatabaseFact]
    public async Task BalancedTailMutation_AfterCheckpoint_CannotReplaceRetainedAuditFacts()
    {
        await using var database = await LedgerPostgresTestDatabase.CreateAsync();
        var (book, may) = await CreateBookAndPeriod(database);
        await Append(database, book, may, 100m);
        await HardClose(database, may);
        var june = await CreatePeriod(database, book, 6);
        await Append(database, book, june, 25m, occurredAt: DateTimeOffset.Parse("2026-06-15T12:00:00Z"));
        var tail = (await database.JournalStore.GetByPeriodAsync(june.PeriodId)).Should().ContainSingle().Subject;
        await Execute(database, $$"""
            alter table {schema}.journal_legs disable trigger user;
            update {schema}.journal_legs set debit=debit*2, credit=credit*2
            where journal_entry_id='{{tail.Entry.JournalEntryId:D}}';
            alter table {schema}.journal_legs enable trigger user;
            """);

        Func<Task> read = () => database.RestartJournalStore()
            .ReadTrialBalanceAsOfAsync(book.LedgerBookId, MayEnd.AddDays(30));

        await read.Should().ThrowAsync<LedgerValidationException>();
    }

    [LedgerDatabaseFact]
    public async Task DeletedLatestTailJournal_WithShrunkenLiveWatermark_RemainsDetectableFromAudit()
    {
        await using var database = await LedgerPostgresTestDatabase.CreateAsync();
        var (book, may) = await CreateBookAndPeriod(database);
        await Append(database, book, may, 100m);
        await HardClose(database, may);
        var june = await CreatePeriod(database, book, 6);
        await Append(database, book, june, 25m, occurredAt: DateTimeOffset.Parse("2026-06-15T12:00:00Z"));
        var tail = (await database.JournalStore.GetByPeriodAsync(june.PeriodId)).Should().ContainSingle().Subject;
        await DeleteRetainedJournalForTamperTest(database, tail.Entry.JournalEntryId);
        (await Scalar(database, "select max(global_sequence) from {schema}.journal_entries"))
            .Should().Be(await Scalar(database, "select source_sequence from {schema}.ledger_balance_checkpoints"));
        (await Scalar(database, $"select count(*) from {{schema}}.ledger_event_audit_events where subject_id='{tail.Entry.JournalEntryId:D}'"))
            .Should().Be(1, "deleting current rows must not remove the independently retained journal fact");

        Func<Task> read = () => database.RestartJournalStore()
            .ReadTrialBalanceAsOfAsync(book.LedgerBookId, MayEnd.AddDays(30));

        await read.Should().ThrowAsync<LedgerValidationException>();
    }

    [LedgerDatabaseFact]
    public async Task PrepostedFutureJournal_DeletedAndItsAuditDateForgedBeforeClose_CannotEscapeBoundedProof()
    {
        await using var database = await LedgerPostgresTestDatabase.CreateAsync();
        var (book, may) = await CreateBookAndPeriod(database);
        var june = await CreatePeriod(database, book, 6);
        await Append(database, book, may, 100m);
        await Append(database, book, june, 25m, occurredAt: DateTimeOffset.Parse("2026-06-15T12:00:00Z"));
        var future = (await database.JournalStore.GetByPeriodAsync(june.PeriodId)).Should().ContainSingle().Subject;
        await HardClose(database, may);
        // Keep the live watermark above the checkpoint after removing the older future row.
        // A simple shrunken-watermark guard cannot substitute for proving the earlier audit suffix.
        await Append(database, book, june, 1m, occurredAt: DateTimeOffset.Parse("2026-06-20T12:00:00Z"));
        await DeleteRetainedJournalForTamperTest(database, future.Entry.JournalEntryId);
        await Execute(database, $$"""
            alter table {schema}.ledger_event_audit_events disable trigger user;
            update {schema}.ledger_event_audit_events
            set fact_snapshot=jsonb_set(fact_snapshot::jsonb, '{journal,occurred_at}',
                to_jsonb('2026-05-15T12:00:00+00:00'::text), false)::text
            where subject_kind='journal' and subject_id='{{future.Entry.JournalEntryId:D}}';
            alter table {schema}.ledger_event_audit_events enable trigger user;
            """);

        Func<Task> read = () => database.RestartJournalStore()
            .ReadTrialBalanceAsOfAsync(book.LedgerBookId, MayEnd.AddDays(30));

        await read.Should().ThrowAsync<LedgerValidationException>(
            "the checkpoint's authenticated future-audit lower bound must defeat an untrusted forged effective date");
    }

    private static Task DeleteRetainedJournalForTamperTest(LedgerPostgresTestDatabase database, Guid journalId)
        => Execute(database, $$"""
            alter table {schema}.journal_legs disable trigger user;
            alter table {schema}.journal_entries disable trigger user;
            alter table {schema}.journal_entry_integrity_seals disable trigger user;
            delete from {schema}.journal_entry_integrity_seals where journal_entry_id='{{journalId:D}}';
            delete from {schema}.journal_legs where journal_entry_id='{{journalId:D}}';
            delete from {schema}.journal_entries where journal_entry_id='{{journalId:D}}';
            set constraints all immediate;
            alter table {schema}.journal_entry_integrity_seals enable trigger user;
            alter table {schema}.journal_entries enable trigger user;
            alter table {schema}.journal_legs enable trigger user;
            """);
}
