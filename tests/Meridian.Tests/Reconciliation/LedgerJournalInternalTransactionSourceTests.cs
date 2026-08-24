using FluentAssertions;
using Meridian.Application.Reconciliation;
using Meridian.Contracts.FundStructure;
using Meridian.FinancialOperations.Reconciliation;
using Meridian.Ledger;
using Meridian.Storage.Ledger;

namespace Meridian.Tests.Reconciliation;

/// <summary>
/// Verifies the period-scoped journal→custodian-transaction projection that feeds the internal
/// ledger-transaction population for statement reconciliation: posted journals that move cash for
/// the statement's account project into matchable internal transactions (trade/fee/dividend/
/// transaction), pure internal postings and reversal pairs are excluded, out-of-window and
/// unattributable entries never project, and every degraded path (no store, no scoped-query
/// support, no window) fails closed to the empty population so the informational
/// internal-transaction-population-unavailable classification keeps operating.
/// </summary>
public sealed class LedgerJournalInternalTransactionSourceTests
{
    private const string AccountKey = "EXT-1";
    private static readonly DateOnly PeriodStart = new(2026, 5, 1);
    private static readonly DateOnly PeriodEnd = new(2026, 5, 31);

    [Fact]
    public async Task GetTransactionsAsync_ProjectsRepresentativeJournalsAndExcludesInternalPostings()
    {
        var trade = Journal(
            Timestamp(2026, 5, 28),
            "Buy 10 MSFT",
            new JournalEntryMetadata(ActivityType: "buy", Symbol: "MSFT", FinancialAccountId: AccountKey),
            (LedgerAccounts.Securities("MSFT", AccountKey), 5000m, 0m),
            (LedgerAccounts.CommissionExpenseFor(AccountKey), 5m, 0m),
            (LedgerAccounts.CashAccount(AccountKey), 0m, 5005m));
        var fee = Journal(
            Timestamp(2026, 5, 29),
            "Monthly custody fee",
            new JournalEntryMetadata(ActivityType: "commission", FinancialAccountId: AccountKey),
            (LedgerAccounts.CommissionExpenseFor(AccountKey), 25m, 0m),
            (LedgerAccounts.CashAccount(AccountKey), 0m, 25m));
        var dividend = Journal(
            Timestamp(2026, 5, 30),
            "MSFT dividend received",
            new JournalEntryMetadata(Symbol: "MSFT", FinancialAccountId: AccountKey),
            (LedgerAccounts.CashAccount(AccountKey), 40m, 0m),
            (LedgerAccounts.DividendIncomeFor(AccountKey), 0m, 40m));
        // Pure internal postings: a valuation mark moves no cash, and a cash reclass nets to zero
        // in its currency. Neither has a custodian-visible counterpart, so neither may project.
        var valuationMark = Journal(
            Timestamp(2026, 5, 30),
            "Fair value mark MSFT",
            new JournalEntryMetadata(ActivityType: "fair-value-mark", Symbol: "MSFT", FinancialAccountId: AccountKey),
            (LedgerAccounts.Securities("MSFT", AccountKey), 100m, 0m),
            (LedgerAccounts.UnrealizedGainFor(AccountKey), 0m, 100m));
        var internalReclass = Journal(
            Timestamp(2026, 5, 30),
            "Internal cash reclass",
            new JournalEntryMetadata(FinancialAccountId: AccountKey),
            (LedgerAccounts.CashAccount(AccountKey), 500m, 0m),
            (LedgerAccounts.CashAccount(AccountKey), 0m, 500m));

        var source = Source(trade, fee, dividend, valuationMark, internalReclass);

        var transactions = await source.GetTransactionsAsync(Query());

        transactions.Should().HaveCount(3, "only custodian-visible cash movements project");
        transactions.Should().OnlyContain(transaction => transaction.Account == AccountKey);

        var projectedTrade = transactions.Single(transaction => transaction.TransactionType == "trade");
        projectedTrade.SecurityId.Should().Be("MSFT");
        projectedTrade.NetAmount.Should().Be(-5005m, "the trade's net cash out includes the embedded commission");
        projectedTrade.Currency.Should().Be("USD");
        projectedTrade.TradeDate.Should().Be(new DateOnly(2026, 5, 28));
        projectedTrade.TransactionId.Should().Be($"internal-txn:{AccountKey}:{trade.JournalEntryId:D}");
        projectedTrade.EvidenceReference.Should().Be($"internal:journal:{trade.JournalEntryId:D}");

        var projectedFee = transactions.Single(transaction => transaction.TransactionType == "fee");
        projectedFee.NetAmount.Should().Be(-25m);
        projectedFee.SecurityId.Should().BeNull();

        var projectedDividend = transactions.Single(transaction => transaction.TransactionType == "dividend");
        projectedDividend.NetAmount.Should().Be(40m);
        projectedDividend.SecurityId.Should().Be("MSFT");
    }

    [Fact]
    public async Task GetTransactionsAsync_ProjectedTradeWithFitIdMatchesStatementRowInsteadOfBreaking()
    {
        var trade = Journal(
            Timestamp(2026, 5, 28),
            "Buy 5 MSFT",
            new JournalEntryMetadata(
                ActivityType: "buy",
                Symbol: "MSFT",
                FinancialAccountId: AccountKey,
                EffectiveDate: new DateOnly(2026, 5, 28),
                SettlementReference: "EXT-9",
                Tags: new Dictionary<string, string>
                {
                    ["quantity"] = "5",
                    ["settlementDate"] = "2026-05-30",
                }),
            (LedgerAccounts.Securities("MSFT", AccountKey), 100m, 0m),
            (LedgerAccounts.CashAccount(AccountKey), 0m, 100m));
        var internalTransactions = await Source(trade).GetTransactionsAsync(Query());

        var statementRow = new NormalizedStatementTransaction(
            "import-1:3",
            "EXT-9",
            AccountKey,
            "MSFT",
            "USD",
            new DateOnly(2026, 5, 28),
            new DateOnly(2026, 5, 30),
            "trade",
            5m,
            -100m,
            "import-1:3");

        var result = new StatementMatchingEngine().Run(new StatementMatchingRequest(
            [],
            [],
            [statementRow],
            [],
            [],
            internalTransactions,
            new StatementMatchingToleranceProfile(0m, 0m, 0m, 0m, 0m)));

        var match = result.Results.Should().ContainSingle().Subject;
        match.MatchTier.Should().Be(
            StatementMatchTier.Exact,
            "a projected journal carrying the custodian id, amount, dates, and quantity must match instead of breaking");
        match.RuleIds.Should().Contain("statement-transaction-external-id-v1");
        match.InternalEvidenceReference.Should().Be($"internal:journal:{trade.JournalEntryId:D}");
    }

    [Fact]
    public async Task GetTransactionsAsync_ExcludesEntriesOutsideTheStatementWindow()
    {
        // The fake store returns the out-of-window record regardless of the query filter; the
        // projection must re-check the window itself, mirroring the position-snapshot ceiling check.
        var inWindow = Journal(
            Timestamp(2026, 5, 20),
            "In-window deposit",
            new JournalEntryMetadata(FinancialAccountId: AccountKey),
            (LedgerAccounts.CashAccount(AccountKey), 1000m, 0m),
            (LedgerAccounts.CapitalAccountFor(AccountKey), 0m, 1000m));
        var afterWindow = Journal(
            Timestamp(2026, 6, 15),
            "Post-period withdrawal",
            new JournalEntryMetadata(FinancialAccountId: AccountKey),
            (LedgerAccounts.CapitalAccountFor(AccountKey), 400m, 0m),
            (LedgerAccounts.CashAccount(AccountKey), 0m, 400m));
        var store = new QueryRecordingLedgerJournalStore([Record(inWindow), Record(afterWindow)]);

        var transactions = await new LedgerJournalInternalTransactionSource(store).GetTransactionsAsync(Query());

        var transaction = transactions.Should().ContainSingle().Subject;
        transaction.NetAmount.Should().Be(1000m);
        transaction.TransactionType.Should().Be("transaction");
        store.LastQuery.Should().NotBeNull("the journal read must be bounded to the statement window");
        store.LastQuery!.OccurredFrom.Should().Be(
            new DateTimeOffset(PeriodStart.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero));
        store.LastQuery.OccurredTo.Should().Be(
            new DateTimeOffset(PeriodEnd.ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero));
    }

    [Fact]
    public async Task GetTransactionsAsync_ExcludesJournalsNotAttributableToTheAccount()
    {
        var otherAccount = Journal(
            Timestamp(2026, 5, 20),
            "Other account's fee",
            new JournalEntryMetadata(ActivityType: "fee", FinancialAccountId: "OTHER-ACCOUNT"),
            (LedgerAccounts.CommissionExpenseFor("OTHER-ACCOUNT"), 25m, 0m),
            (LedgerAccounts.CashAccount("OTHER-ACCOUNT"), 0m, 25m));
        // Attribution may also come from the line-level account scope when metadata is unstamped.
        var lineScoped = Journal(
            Timestamp(2026, 5, 21),
            "Line-scoped dividend",
            new JournalEntryMetadata(Symbol: "SPY"),
            (LedgerAccounts.CashAccount(AccountKey), 12m, 0m),
            (LedgerAccounts.DividendIncomeFor(AccountKey), 0m, 12m));

        var transactions = await Source(otherAccount, lineScoped).GetTransactionsAsync(Query());

        var transaction = transactions.Should().ContainSingle(
            "a journal stamped for another financial account must never project into this account's population").Subject;
        transaction.EvidenceReference.Should().Be($"internal:journal:{lineScoped.JournalEntryId:D}");
    }

    [Fact]
    public async Task GetTransactionsAsync_ExcludesReversalPairs()
    {
        var original = Journal(
            Timestamp(2026, 5, 12),
            "Dividend posted in error",
            new JournalEntryMetadata(ActivityType: "dividend", Symbol: "SPY", FinancialAccountId: AccountKey),
            (LedgerAccounts.CashAccount(AccountKey), 75m, 0m),
            (LedgerAccounts.DividendIncomeFor(AccountKey), 0m, 75m));
        var reversal = LedgerJournalReversal.Reverse(
            original,
            Guid.NewGuid(),
            Timestamp(2026, 5, 13),
            "posted in error");

        var transactions = await Source(original, reversal).GetTransactionsAsync(Query());

        transactions.Should().BeEmpty(
            "a reversal and the entry it reverses net to nothing internally, so neither is a custodian-comparable movement");
    }

    [Fact]
    public async Task GetTransactionsAsync_ProjectsOneRecordPerCurrencyForFxConversions()
    {
        var journalId = Guid.NewGuid();
        var timestamp = Timestamp(2026, 5, 15);
        const string description = "Convert EUR to USD";
        var fxConversion = new JournalEntry(
            journalId,
            timestamp,
            description,
            [
                new LedgerEntry(
                    Guid.NewGuid(),
                    journalId,
                    timestamp,
                    LedgerAccounts.CashInCurrency("EUR", AccountKey),
                    1085m,
                    0m,
                    description,
                    currency: new LedgerEntryCurrency("EUR", "USD", 1000m, 0m, 1.085m)),
                new LedgerEntry(
                    Guid.NewGuid(),
                    journalId,
                    timestamp,
                    LedgerAccounts.CashInCurrency("USD", AccountKey),
                    0m,
                    1085m,
                    description,
                    currency: new LedgerEntryCurrency("USD", "USD", 0m, 1085m, 1m)),
            ],
            new JournalEntryMetadata(FinancialAccountId: AccountKey));

        var transactions = await Source(fxConversion).GetTransactionsAsync(Query());

        transactions.Should().HaveCount(2, "an FX conversion appears on a statement as one movement per currency");
        transactions.Should().ContainSingle(transaction =>
            transaction.Currency == "EUR" && transaction.NetAmount == 1000m);
        transactions.Should().ContainSingle(transaction =>
            transaction.Currency == "USD" && transaction.NetAmount == -1085m);
        transactions.Should().OnlyContain(transaction =>
            transaction.TransactionId.StartsWith($"internal-txn:{AccountKey}:{journalId:D}:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task GetTransactionsAsync_WithoutJournalStore_FailsClosedToEmpty()
    {
        var source = new LedgerJournalInternalTransactionSource();

        var transactions = await source.GetTransactionsAsync(Query());

        // The provider then returns an empty ledger-transaction population, so the statement-run
        // matcher keeps stamping transaction breaks with the informational
        // internal-transaction-population-unavailable classification (covered end to end by
        // StatementRunWorkflowServiceTests).
        transactions.Should().BeEmpty();
    }

    [Fact]
    public async Task GetTransactionsAsync_WhenStoreLacksScopedQuerySupport_FailsClosedToEmpty()
    {
        var source = new LedgerJournalInternalTransactionSource(new ScopedQueryUnsupportedLedgerJournalStore());

        var transactions = await source.GetTransactionsAsync(Query());

        transactions.Should().BeEmpty("a store without scoped journal reads must degrade the transaction lane, not throw");
    }

    [Fact]
    public async Task GetTransactionsAsync_WithoutAPeriodWindow_FailsClosedToEmpty()
    {
        var store = new QueryRecordingLedgerJournalStore([]);
        var source = new LedgerJournalInternalTransactionSource(store);

        var transactions = await source.GetTransactionsAsync(
            new InternalLedgerTransactionQuery(AccountKey, [AccountKey], default, default, "USD"));

        transactions.Should().BeEmpty("a period-scoped projection is meaningless without a statement window");
        store.LastQuery.Should().BeNull("no unbounded journal read may be issued");
    }

    private static LedgerJournalInternalTransactionSource Source(params JournalEntry[] entries) =>
        new(new QueryRecordingLedgerJournalStore(entries.Select(Record).ToArray()));

    private static InternalLedgerTransactionQuery Query() =>
        new(AccountKey, [AccountKey], PeriodStart, PeriodEnd, "USD");

    private static DateTimeOffset Timestamp(int year, int month, int day) =>
        new(year, month, day, 14, 30, 0, TimeSpan.Zero);

    private static JournalEntry Journal(
        DateTimeOffset timestamp,
        string description,
        JournalEntryMetadata metadata,
        params (LedgerAccount Account, decimal Debit, decimal Credit)[] lines)
    {
        var journalId = Guid.NewGuid();
        return new JournalEntry(
            journalId,
            timestamp,
            description,
            lines
                .Select(line => new LedgerEntry(
                    Guid.NewGuid(),
                    journalId,
                    timestamp,
                    line.Account,
                    line.Debit,
                    line.Credit,
                    description))
                .ToArray(),
            metadata);
    }

    private static LedgerJournalEntryRecord Record(JournalEntry entry) => new(
        entry,
        AggregateId: Guid.NewGuid(),
        PeriodId: Guid.NewGuid(),
        CommandId: null,
        CorrelationId: null,
        GlobalSequence: 1,
        CreatedAt: DateTimeOffset.UnixEpoch);

    private sealed class QueryRecordingLedgerJournalStore(IReadOnlyList<LedgerJournalEntryRecord> records)
        : ILedgerJournalStore
    {
        public LedgerJournalEntryQuery? LastQuery { get; private set; }

        public Task<IReadOnlyList<LedgerJournalEntryRecord>> QueryAsync(
            LedgerJournalEntryQuery query,
            CancellationToken ct = default)
        {
            LastQuery = query;
            return Task.FromResult(records);
        }

        public Task AppendAsync(LedgerJournalEntryWrite entry, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<LedgerJournalEntryRecord>> GetByPeriodAsync(Guid periodId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<LedgerJournalEntryRecord>> GetByAggregateAsync(Guid aggregateId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<LedgerAccountingPeriod?> GetPeriodAsync(Guid periodId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<LedgerAccountingPeriod>> ListPeriodsAsync(
            Guid? ledgerBookId = null,
            string? status = null,
            string? fundProfileId = null,
            Guid? fundStructureNodeId = null,
            CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<LedgerAccountingPeriod> SavePeriodAsync(
            LedgerAccountingPeriod period,
            long expectedVersion,
            PeriodCloseEventRecord? closeEvent = null,
            CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<LedgerBookRecord?> GetLedgerBookAsync(Guid ledgerBookId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<LedgerBookRecord>> ListLedgerBooksAsync(
            string? fundProfileId = null,
            Guid? fundStructureNodeId = null,
            FundStructureNodeKindDto? fundStructureNodeKind = null,
            CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<LedgerBookRecord> SaveLedgerBookAsync(LedgerBookRecord book, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }

    /// <summary>
    /// Implements only the interface's required members, so <c>QueryAsync</c> falls through to the
    /// default implementation's <see cref="NotSupportedException"/> — the shape of every journal
    /// store that predates scoped queries.
    /// </summary>
    private sealed class ScopedQueryUnsupportedLedgerJournalStore : ILedgerJournalStore
    {
        public Task AppendAsync(LedgerJournalEntryWrite entry, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<LedgerJournalEntryRecord>> GetByPeriodAsync(Guid periodId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<LedgerJournalEntryRecord>> GetByAggregateAsync(Guid aggregateId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<LedgerAccountingPeriod?> GetPeriodAsync(Guid periodId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<LedgerAccountingPeriod>> ListPeriodsAsync(
            Guid? ledgerBookId = null,
            string? status = null,
            string? fundProfileId = null,
            Guid? fundStructureNodeId = null,
            CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<LedgerAccountingPeriod> SavePeriodAsync(
            LedgerAccountingPeriod period,
            long expectedVersion,
            PeriodCloseEventRecord? closeEvent = null,
            CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<LedgerBookRecord?> GetLedgerBookAsync(Guid ledgerBookId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<LedgerBookRecord>> ListLedgerBooksAsync(
            string? fundProfileId = null,
            Guid? fundStructureNodeId = null,
            FundStructureNodeKindDto? fundStructureNodeKind = null,
            CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<LedgerBookRecord> SaveLedgerBookAsync(LedgerBookRecord book, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }
}
