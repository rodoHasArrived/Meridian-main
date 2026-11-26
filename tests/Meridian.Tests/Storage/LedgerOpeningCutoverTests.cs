using FluentAssertions;
using Meridian.Contracts.FundStructure;
using Meridian.Ledger;
using Meridian.Storage.Ledger;
using Npgsql;

namespace Meridian.Tests.Storage;

public sealed class LedgerOpeningCutoverTests
{
    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public async Task OpeningRefusesActivityAcceptedAfterPreparationBeforePosting()
    {
        await using var database = await LedgerPostgresTestDatabase.CreateAsync();
        var (book, period) = await CreateBookAsync(database);
        var account = Guid.NewGuid();
        var opening = Write(book, period, account, day: 15, opening: true);
        await database.JournalStore.AppendAsync(Write(book, period, account, day: 14));

        var post = () => database.JournalStore.AppendAsync(opening);

        await post.Should().ThrowAsync<LedgerValidationException>().WithMessage("*overlap retained account activity*");
        (await database.JournalStore.GetByPeriodAsync(period)).Should().ContainSingle();
    }

    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public async Task PostedOpeningAllowsLaterActivityButRefusesBackdatedOriginatingActivity()
    {
        await using var database = await LedgerPostgresTestDatabase.CreateAsync();
        var (book, period) = await CreateBookAsync(database);
        var account = Guid.NewGuid();
        await database.JournalStore.AppendAsync(Write(book, period, account, day: 15, opening: true));
        await database.JournalStore.AppendAsync(Write(book, period, account, day: 16));
        // Another explicitly scoped account is independent even within the same book.
        await database.JournalStore.AppendAsync(Write(book, period, Guid.NewGuid(), day: 14));

        var post = () => database.JournalStore.AppendAsync(Write(book, period, account, day: 15));

        await post.Should().ThrowAsync<LedgerValidationException>().WithMessage("*retained opening cutoff*");
        (await database.JournalStore.GetByPeriodAsync(period)).Should().HaveCount(3);
    }

    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public async Task RacingOpeningAndEarlierActivityCannotBothCommit()
    {
        await using var database = await LedgerPostgresTestDatabase.CreateAsync();
        var (book, period) = await CreateBookAsync(database);
        var account = Guid.NewGuid();
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<bool> TryPostAsync(LedgerJournalEntryWrite write)
        {
            await start.Task;
            try
            { await database.JournalStore.AppendAsync(write); return true; }
            catch (LedgerValidationException) { return false; }
            catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.SerializationFailure)
            { return false; }
        }
        var opening = TryPostAsync(Write(book, period, account, day: 15, opening: true));
        var activity = TryPostAsync(Write(book, period, account, day: 14));
        start.SetResult();

        var outcomes = await Task.WhenAll(opening, activity);

        outcomes.Count(success => success).Should().Be(1);
        (await database.JournalStore.GetByPeriodAsync(period)).Should().ContainSingle();
    }

    private static async Task<(Guid Book, Guid Period)> CreateBookAsync(LedgerPostgresTestDatabase database)
    {
        var book = Guid.NewGuid();
        var period = Guid.NewGuid();
        var timestamp = new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero);
        await database.JournalStore.SaveLedgerBookAsync(new LedgerBookRecord(book, "fund-cutover", Guid.NewGuid(),
            FundStructureNodeKindDto.Account, "Cutover book", "USD", timestamp, timestamp));
        await database.JournalStore.SavePeriodAsync(new LedgerAccountingPeriod(period, book, 2026, 5,
            "2026-05", new(2026, 5, 1), new(2026, 5, 31), "Open", timestamp, null, 0), 0);
        return (book, period);
    }

    private static LedgerJournalEntryWrite Write(Guid book, Guid period, Guid account, int day, bool opening = false)
    {
        var id = Guid.NewGuid();
        var timestamp = new DateTimeOffset(2026, 5, day, 0, 0, 0, TimeSpan.Zero);
        var accountId = account.ToString("D");
        var description = opening ? "Opening source" : "Account activity";
        var journal = new JournalEntry(id, timestamp, description,
        [
            new LedgerEntry(Guid.NewGuid(), id, timestamp, LedgerAccounts.CashFor(accountId), 100m, 0m, description),
            new LedgerEntry(Guid.NewGuid(), id, timestamp, LedgerAccounts.CapitalAccountFor(accountId), 0m, 100m, description)
        ], new JournalEntryMetadata(FinancialAccountId: accountId, EffectiveDate: new(2026, 5, day),
            IdempotencyKey: opening ? $"portfolio-opening:{book:D}:{account:D}" : null));
        return new LedgerJournalEntryWrite(journal, book, period, LedgerBookId: book);
    }
}
