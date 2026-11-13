using FluentAssertions;
using Meridian.Ledger;
using Meridian.Storage.Ledger;

namespace Meridian.Tests.Storage;

public sealed partial class LedgerBalanceCheckpointPostgresTests
{
    [LedgerDatabaseFact]
    public async Task PrefixJournalMovedIntoTail_WithMatchingForgedFact_CannotUseUnverifiedAuditPrefix()
    {
        await using var database = await LedgerPostgresTestDatabase.CreateAsync();
        var (book, may) = await CreateBookAndPeriod(database);
        await Append(database, book, may, 100m);
        var original = (await database.JournalStore.GetByPeriodAsync(may.PeriodId))
            .Should().ContainSingle().Subject;
        await HardClose(database, may);
        var juneAsOf = MayEnd.AddDays(30);
        (await database.RestartJournalStore().ReadTrialBalanceAsOfAsync(book.LedgerBookId, juneAsOf))[Cash("broker-a")]
            .Should().Be(100m);

        // The authenticated checkpoint already contains this May journal. Move its current
        // effective date into June and make the old fact body agree, but leave every retained
        // audit hash and the audit head untouched. A reader must not count it again as a tail
        // journal whose audit fact lies before the suffix it has actually verified.
        await Execute(database, $$"""
            set timezone='UTC';
            set datestyle='ISO, YMD';
            alter table {schema}.journal_entries disable trigger user;
            alter table {schema}.ledger_event_audit_events disable trigger user;
            update {schema}.journal_entries set occurred_at='2026-06-15T12:00:00Z'
            where journal_entry_id='{{original.Entry.JournalEntryId:D}}';
            update {schema}.ledger_event_audit_events a
            set fact_snapshot=jsonb_set(a.fact_snapshot::jsonb, '{journal,occurred_at}',
                to_jsonb(j)->'occurred_at', false)::text
            from {schema}.journal_entries j
            where a.subject_kind='journal' and a.subject_id=j.journal_entry_id
                and j.journal_entry_id='{{original.Entry.JournalEntryId:D}}';
            alter table {schema}.ledger_event_audit_events enable trigger user;
            alter table {schema}.journal_entries enable trigger user;
            """);

        Func<Task> read = () => database.RestartJournalStore()
            .ReadTrialBalanceAsOfAsync(book.LedgerBookId, juneAsOf);

        await read.Should().ThrowAsync<LedgerValidationException>(
            "a matching forged fact outside the verified suffix cannot authorize adding an already-checkpointed journal again");
    }
}
