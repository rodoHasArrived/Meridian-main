using FluentAssertions;
using Meridian.Contracts.FundStructure;
using Meridian.Ledger;
using Meridian.Storage.Ledger;
using Npgsql;

namespace Meridian.Tests.Storage;

/// <summary>
/// Journal legs persisted before the append path carried currency detail through to the store read
/// back with no currency at all, which is indistinguishable from a leg that was never meant to have
/// any. These cover what the backfill will complete from retained evidence, what it refuses to
/// guess, and that repairing a leg never disturbs the functional amounts the books balance on.
/// </summary>
public sealed class LedgerCurrencyBackfillTests
{
    private static readonly DateTimeOffset PeriodStart = DateTimeOffset.Parse("2026-05-01T00:00:00Z");
    private static readonly DateTimeOffset OccurredAt = DateTimeOffset.Parse("2026-05-15T18:00:00Z");

    [LedgerDatabaseFact]
    public async Task RepairEvidencedLegs_BookCorroboratedBySingleCurrencyLegs_StampsIdentityTranslation()
    {
        await using var database = await LedgerPostgresTestDatabase.CreateAsync();
        var backfill = new PostgresLedgerCurrencyBackfill(database.Options);
        var (bookId, periodId) = await CreateBookAsync(database, "USD");
        // The corroborating evidence: a leg the book already records as USD against USD at rate 1.
        await AppendAsync(database, bookId, periodId, OccurredAt, Identity("USD", 100m));
        var blind = await AppendAsync(database, bookId, periodId, OccurredAt.AddMinutes(1));

        var before = await backfill.SurveyAsync();
        before.CurrencyBlindLegs.Should().Be(2, "only the legs of the second journal are blind");
        before.RepairableLegs.Should().Be(2);
        before.Scopes.Should().ContainSingle()
            .Which.Disposition.Should().Be(LedgerCurrencyBackfillDisposition.Repairable);

        var repaired = await backfill.RepairEvidencedLegsAsync();

        repaired.Should().Be(2);
        var lines = await ReadLinesAsync(database, periodId, blind.Entry.JournalEntryId);
        foreach (var line in lines)
        {
            line.Currency.Should().NotBeNull();
            line.Currency!.TransactionCurrency.Should().Be("USD");
            line.Currency.FunctionalCurrency.Should().Be("USD");
            line.Currency.FxRateToFunctional.Should().Be(1m, "an identity translation invents no rate");
            line.Currency.TransactionDebit.Should().Be(line.Debit);
            line.Currency.TransactionCredit.Should().Be(line.Credit);
        }

        // The functional amounts are what the books balance on, and the repair only reads them.
        lines.Sum(line => line.Debit).Should().Be(100m);
        lines.Sum(line => line.Credit).Should().Be(100m);

        (await backfill.SurveyAsync()).IsComplete.Should().BeTrue();
        var second = await backfill.RepairEvidencedLegsAsync();
        second.Should().Be(0, "the null guard makes a re-run a no-op");
    }

    [LedgerDatabaseFact]
    public async Task RepairEvidencedLegs_BookThatTransactsInForeignCurrency_LeavesBlindLegsAlone()
    {
        await using var database = await LedgerPostgresTestDatabase.CreateAsync();
        var backfill = new PostgresLedgerCurrencyBackfill(database.Options);
        var (bookId, periodId) = await CreateBookAsync(database, "USD");
        // EUR 50 at 2.0 makes the functional USD 100 the leg already carries.
        await AppendAsync(
            database,
            bookId,
            periodId,
            OccurredAt,
            new LedgerEntryCurrency("EUR", "USD", 50m, 0m, 2m),
            new LedgerEntryCurrency("EUR", "USD", 0m, 50m, 2m));
        var blind = await AppendAsync(database, bookId, periodId, OccurredAt.AddMinutes(1));

        var survey = await backfill.SurveyAsync();

        survey.Scopes.Should().ContainSingle()
            .Which.Disposition.Should().Be(LedgerCurrencyBackfillDisposition.ForeignCurrencyEvidence);
        survey.RepairableLegs.Should().Be(0);
        survey.BlockedLegs.Should().Be(2);

        var repaired = await backfill.RepairEvidencedLegsAsync();

        repaired.Should().Be(0, "a blind leg here may be a foreign leg whose rate is unrecoverable");
        var lines = await ReadLinesAsync(database, periodId, blind.Entry.JournalEntryId);
        lines.Should().OnlyContain(line => line.Currency == null);
    }

    [LedgerDatabaseFact]
    public async Task Survey_LegsInPeriodWithNoLedgerBook_ReportsUnresolvedLedgerBook()
    {
        await using var database = await LedgerPostgresTestDatabase.CreateAsync();
        var backfill = new PostgresLedgerCurrencyBackfill(database.Options);
        var period = await database.SavePeriodAsync(Guid.NewGuid(), "Open");
        await AppendAsync(database, Guid.NewGuid(), period.PeriodId, OccurredAt, bookScoped: false);

        var survey = await backfill.SurveyAsync();

        var scope = survey.Scopes.Should().ContainSingle().Subject;
        scope.Disposition.Should().Be(LedgerCurrencyBackfillDisposition.UnresolvedLedgerBook);
        scope.LedgerBookId.Should().BeNull();
        scope.BaseCurrency.Should().BeNull("there is no book to resolve a functional currency from");
        (await backfill.RepairEvidencedLegsAsync()).Should().Be(0);
    }

    [LedgerDatabaseFact]
    public async Task Affirm_BookWithNoCurrencyEvidenceAtAll_CompletesLegsAndRecordsTheAuthority()
    {
        await using var database = await LedgerPostgresTestDatabase.CreateAsync();
        var backfill = new PostgresLedgerCurrencyBackfill(database.Options);
        var (bookId, periodId) = await CreateBookAsync(database, "USD");
        var blind = await AppendAsync(database, bookId, periodId, OccurredAt);
        await SoftClosePeriodAsync(database, bookId, periodId);

        var before = await backfill.SurveyAsync();
        var scope = before.Scopes.Should().ContainSingle().Subject;
        scope.Disposition.Should().Be(LedgerCurrencyBackfillDisposition.UnaffirmedSingleCurrency);
        scope.BaseCurrency.Should().Be("USD");
        scope.ClosedPeriodLegs.Should().Be(2, "an operator affirming this is completing closed history");
        before.AffirmableLegs.Should().Be(2);
        (await backfill.RepairEvidencedLegsAsync())
            .Should().Be(0, "silence is not evidence, so nothing repairs without the affirmation");

        var result = await backfill.AffirmSingleCurrencyBookAsync(
            bookId,
            "usd",
            "fund-controller",
            "Book has transacted only in USD since inception; custodian statements reviewed.");

        result.LegsRepaired.Should().Be(2);
        result.AffirmedCurrency.Should().Be("USD");
        result.Actor.Should().Be("fund-controller");
        var lines = await ReadLinesAsync(database, periodId, blind.Entry.JournalEntryId);
        lines.Should().OnlyContain(line => line.Currency != null && line.Currency.IsFunctionalCurrency);
        (await backfill.SurveyAsync()).IsComplete.Should().BeTrue();

        var affirmation = (await ReadAffirmationsAsync(database, bookId)).Should().ContainSingle().Subject;
        affirmation.Currency.Should().Be("USD");
        affirmation.Actor.Should().Be("fund-controller");
        affirmation.LegsRepaired.Should().Be(2);
    }

    [LedgerDatabaseFact]
    public async Task Affirm_BookThatTransactsInForeignCurrency_IsRefusedRatherThanOverruling()
    {
        await using var database = await LedgerPostgresTestDatabase.CreateAsync();
        var backfill = new PostgresLedgerCurrencyBackfill(database.Options);
        var (bookId, periodId) = await CreateBookAsync(database, "USD");
        await AppendAsync(
            database,
            bookId,
            periodId,
            OccurredAt,
            new LedgerEntryCurrency("EUR", "USD", 50m, 0m, 2m),
            new LedgerEntryCurrency("EUR", "USD", 0m, 50m, 2m));
        var blind = await AppendAsync(database, bookId, periodId, OccurredAt.AddMinutes(1));

        var act = () => backfill.AffirmSingleCurrencyBookAsync(bookId, "USD", "fund-controller", "Assumed single currency.");

        (await act.Should().ThrowAsync<LedgerValidationException>())
            .WithMessage("*ForeignCurrencyEvidence*");
        var lines = await ReadLinesAsync(database, periodId, blind.Entry.JournalEntryId);
        lines.Should().OnlyContain(line => line.Currency == null);
        (await ReadAffirmationsAsync(database, bookId)).Should().BeEmpty();
    }

    [LedgerDatabaseFact]
    public async Task Affirm_CurrencyThatIsNotTheBooksOwnBase_IsRefused()
    {
        await using var database = await LedgerPostgresTestDatabase.CreateAsync();
        var backfill = new PostgresLedgerCurrencyBackfill(database.Options);
        var (bookId, periodId) = await CreateBookAsync(database, "USD");
        var blind = await AppendAsync(database, bookId, periodId, OccurredAt);

        var act = () => backfill.AffirmSingleCurrencyBookAsync(bookId, "EUR", "fund-controller", "Believed to be euro.");

        (await act.Should().ThrowAsync<LedgerValidationException>())
            .WithMessage("*denominated in 'USD'*");
        var lines = await ReadLinesAsync(database, periodId, blind.Entry.JournalEntryId);
        lines.Should().OnlyContain(line => line.Currency == null);
        (await ReadAffirmationsAsync(database, bookId)).Should().BeEmpty();
    }

    [LedgerDatabaseFact]
    public async Task Affirm_BookWhoseLegsAreAlreadyDetermined_IsRefusedAsNeedingNoAssertion()
    {
        await using var database = await LedgerPostgresTestDatabase.CreateAsync();
        var backfill = new PostgresLedgerCurrencyBackfill(database.Options);
        var (bookId, periodId) = await CreateBookAsync(database, "USD");
        await AppendAsync(database, bookId, periodId, OccurredAt, Identity("USD", 100m));
        await AppendAsync(database, bookId, periodId, OccurredAt.AddMinutes(1));

        var act = () => backfill.AffirmSingleCurrencyBookAsync(bookId, "USD", "fund-controller", "Belt and braces.");

        (await act.Should().ThrowAsync<LedgerValidationException>())
            .WithMessage("*Repairable*");
    }

    private static LedgerEntryCurrency Identity(string currency, decimal amount)
        => new(currency, currency, amount, 0m, 1m);

    private static async Task<(Guid BookId, Guid PeriodId)> CreateBookAsync(
        LedgerPostgresTestDatabase database,
        string baseCurrency)
    {
        var bookId = Guid.NewGuid();
        var periodId = Guid.NewGuid();
        await database.JournalStore.SaveLedgerBookAsync(new LedgerBookRecord(
            bookId,
            $"fund-{bookId:N}",
            Guid.NewGuid(),
            FundStructureNodeKindDto.Fund,
            "Currency Backfill Book",
            baseCurrency,
            PeriodStart,
            PeriodStart));
        await database.JournalStore.SavePeriodAsync(BuildPeriod(bookId, periodId, "Open"), expectedVersion: 0);
        return (bookId, periodId);
    }

    private static Task SoftClosePeriodAsync(LedgerPostgresTestDatabase database, Guid bookId, Guid periodId)
        => database.JournalStore.SavePeriodAsync(BuildPeriod(bookId, periodId, "SoftClosed"), expectedVersion: 1);

    private static LedgerAccountingPeriod BuildPeriod(Guid bookId, Guid periodId, string status)
        => new(
            periodId,
            bookId,
            2026,
            5,
            "2026-05",
            new DateOnly(2026, 5, 1),
            new DateOnly(2026, 5, 31),
            status,
            PeriodStart,
            ClosedAt: null,
            Version: 0);

    /// <summary>
    /// Appends a balanced two-line journal. Passing no currency reproduces exactly what the store
    /// retained before the fix: a leg whose functional amounts are intact and whose currency
    /// columns are null.
    /// </summary>
    private static async Task<LedgerJournalEntryWrite> AppendAsync(
        LedgerPostgresTestDatabase database,
        Guid aggregateId,
        Guid periodId,
        DateTimeOffset occurredAt,
        LedgerEntryCurrency? debitCurrency = null,
        LedgerEntryCurrency? creditCurrency = null,
        bool bookScoped = true)
    {
        var journalId = Guid.NewGuid();
        const string description = "Currency backfill fixture";
        var entry = new JournalEntry(
            journalId,
            occurredAt,
            description,
            [
                new LedgerEntry(
                    Guid.NewGuid(),
                    journalId,
                    occurredAt,
                    new LedgerAccount("Investments", LedgerAccountType.Asset),
                    100m,
                    0m,
                    description,
                    dimensions: null,
                    debitCurrency),
                new LedgerEntry(
                    Guid.NewGuid(),
                    journalId,
                    occurredAt,
                    new LedgerAccount("Cash", LedgerAccountType.Asset),
                    0m,
                    100m,
                    description,
                    dimensions: null,
                    creditCurrency ?? Mirror(debitCurrency))
            ]);

        var write = new LedgerJournalEntryWrite(
            entry,
            aggregateId,
            periodId,
            LedgerBookId: bookScoped ? aggregateId : null);
        await database.JournalStore.AppendAsync(write);
        return write;
    }

    /// <summary>Mirrors a debit-side currency onto the balancing credit leg.</summary>
    private static LedgerEntryCurrency? Mirror(LedgerEntryCurrency? debitCurrency)
        => debitCurrency is null
            ? null
            : new LedgerEntryCurrency(
                debitCurrency.TransactionCurrency,
                debitCurrency.FunctionalCurrency,
                0m,
                debitCurrency.TransactionDebit,
                debitCurrency.FxRateToFunctional);

    private static async Task<IReadOnlyList<LedgerEntry>> ReadLinesAsync(
        LedgerPostgresTestDatabase database,
        Guid periodId,
        Guid journalEntryId)
    {
        var retained = await database.JournalStore.GetByPeriodAsync(periodId);
        return retained
            .Single(record => record.Entry.JournalEntryId == journalEntryId)
            .Entry.Lines;
    }

    private static async Task<IReadOnlyList<(string Currency, string Actor, int LegsRepaired)>> ReadAffirmationsAsync(
        LedgerPostgresTestDatabase database,
        Guid ledgerBookId)
    {
        await using var connection = new NpgsqlConnection(database.Options.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            select affirmed_currency, actor, legs_repaired
            from {database.Options.SchemaName}.journal_leg_currency_affirmations
            where ledger_book_id = @ledger_book_id
            order by affirmed_at;
            """;
        command.Parameters.AddWithValue("ledger_book_id", ledgerBookId);

        var rows = new List<(string Currency, string Actor, int LegsRepaired)>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rows.Add((reader.GetString(0), reader.GetString(1), reader.GetInt32(2)));
        }

        return rows;
    }
}
