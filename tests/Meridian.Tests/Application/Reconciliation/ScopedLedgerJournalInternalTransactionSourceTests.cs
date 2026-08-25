using FluentAssertions;
using Meridian.Application.Reconciliation;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.Ledger;
using Meridian.PortfolioRecords.Accounts;
using Meridian.Storage.Ledger;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Meridian.Tests.Application.Reconciliation;

public sealed class ScopedLedgerJournalInternalTransactionSourceTests
{
    private const string AccountId = "account-a";
    private const string ExternalAccountLabel = "DE89-3704-0044-0532-0130-00";
    private static readonly DateOnly PeriodStart = new(2026, 5, 1);
    private static readonly DateOnly PeriodEnd = new(2026, 5, 31);

    [Fact]
    public async Task GetTransactionsAsync_WithExactAccountingScope_QueriesBookAndPeriodInsteadOfPostingWindow()
    {
        var ledgerBookId = Guid.NewGuid();
        var accountingPeriodId = Guid.NewGuid();
        var latePosted = Journal(
            new DateTimeOffset(2026, 6, 3, 12, 0, 0, TimeSpan.Zero),
            new JournalEntryMetadata(
                FinancialAccountId: AccountId,
                EffectiveDate: PeriodEnd),
            (LedgerAccounts.CashAccount(AccountId), 125m, 0m),
            (LedgerAccounts.CapitalAccountFor(AccountId), 0m, 125m));
        LedgerJournalEntryQuery? capturedQuery = null;
        var store = Store(
            [Record(latePosted, accountingPeriodId)],
            query => capturedQuery = query);

        var transactions = await new LedgerJournalInternalTransactionSource(store)
            .GetTransactionsAsync(Query(ledgerBookId, accountingPeriodId));

        transactions.Should().ContainSingle()
            .Which.TradeDate.Should().Be(
                PeriodEnd,
                "effective-date authority must retain a journal posted after the statement window");
        capturedQuery.Should().NotBeNull();
        capturedQuery!.LedgerBookId.Should().Be(ledgerBookId);
        capturedQuery.PeriodId.Should().Be(accountingPeriodId);
        capturedQuery.OccurredFrom.Should().BeNull();
        capturedQuery.OccurredTo.Should().BeNull();
    }

    [Fact]
    public async Task GetTransactionsAsync_WithPartialAccountingScope_FailsClosedWithoutQuerying()
    {
        var store = Substitute.For<ILedgerJournalStore>();
        var source = new LedgerJournalInternalTransactionSource(store);

        var transactions = await source.GetTransactionsAsync(Query(
            ledgerBookId: Guid.NewGuid(),
            accountingPeriodId: null));

        transactions.Should().BeEmpty();
        await store.DidNotReceive()
            .QueryAsync(Arg.Any<LedgerJournalEntryQuery>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetTransactionsAsync_MixedAccountJournal_NetsOnlyRequestedAccountCashLines()
    {
        var accountingPeriodId = Guid.NewGuid();
        var mixedAccountJournal = Journal(
            new DateTimeOffset(2026, 5, 20, 12, 0, 0, TimeSpan.Zero),
            new JournalEntryMetadata(FinancialAccountId: AccountId),
            (LedgerAccounts.CashAccount(AccountId), 100m, 0m),
            (LedgerAccounts.CashAccount("account-b"), 0m, 40m),
            (LedgerAccounts.CapitalAccountFor(AccountId), 0m, 60m));
        var source = new LedgerJournalInternalTransactionSource(
            Store([Record(mixedAccountJournal, accountingPeriodId)]));

        var transaction = (await source.GetTransactionsAsync(Query()))
            .Should().ContainSingle().Subject;

        transaction.NetAmount.Should().Be(
            100m,
            "another financial account's cash leg must never alter this account's population");
    }

    [Fact]
    public async Task GetTransactionsAsync_UnscopedCashLine_RequiresMatchingEntryMetadata()
    {
        var accountingPeriodId = Guid.NewGuid();
        var metadataScoped = Journal(
            new DateTimeOffset(2026, 5, 20, 12, 0, 0, TimeSpan.Zero),
            new JournalEntryMetadata(FinancialAccountId: AccountId),
            (LedgerAccounts.Cash, 25m, 0m),
            (LedgerAccounts.CapitalAccount, 0m, 25m));
        var lineScopedOnly = Journal(
            new DateTimeOffset(2026, 5, 21, 12, 0, 0, TimeSpan.Zero),
            new JournalEntryMetadata(FinancialAccountId: "account-b"),
            (LedgerAccounts.Cash, 30m, 0m),
            (LedgerAccounts.CapitalAccountFor(AccountId), 0m, 30m));
        var source = new LedgerJournalInternalTransactionSource(
            Store(
            [
                Record(metadataScoped, accountingPeriodId),
                Record(lineScopedOnly, accountingPeriodId),
            ]));

        var transaction = (await source.GetTransactionsAsync(Query()))
            .Should().ContainSingle(
                "an unscoped cash line is attributable only through matching entry metadata")
            .Subject;

        transaction.NetAmount.Should().Be(25m);
        transaction.EvidenceReference.Should().Be($"internal:journal:{metadataScoped.JournalEntryId:D}");
    }

    [Fact]
    public async Task GetTransactionsAsync_QueryFailure_DoesNotLogExternalAccountLabel()
    {
        var store = Substitute.For<ILedgerJournalStore>();
        store.QueryAsync(Arg.Any<LedgerJournalEntryQuery>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromException<IReadOnlyList<LedgerJournalEntryRecord>>(
                new InvalidOperationException("journal store unavailable")));
        var logger = new CapturingLogger<LedgerJournalInternalTransactionSource>();
        var source = new LedgerJournalInternalTransactionSource(store, logger);

        var transactions = await source.GetTransactionsAsync(Query(Guid.NewGuid(), Guid.NewGuid()));

        transactions.Should().BeEmpty();
        logger.Messages.Should().ContainSingle();
        logger.Messages[0].Should().NotContain(
            ExternalAccountLabel,
            "custodian account numbers and IBANs are reconciliation labels, not safe telemetry identifiers");
        logger.Messages[0].Should().Contain("retained ledger book");
    }

    [Fact]
    public async Task RetainedPopulationProvider_PropagatesExactAccountingScopeToLedgerSource()
    {
        var fundAccountId = Guid.NewGuid();
        var ledgerBookId = Guid.NewGuid();
        var accountingPeriodId = Guid.NewGuid();
        var accounts = Substitute.For<IAccountQueryService>();
        accounts.GetAccountAsync(fundAccountId, Arg.Any<CancellationToken>())
            .Returns(new AccountSummaryDto(
                fundAccountId,
                AccountTypeDto.Brokerage,
                EntityId: null,
                FundId: null,
                SleeveId: null,
                VehicleId: null,
                AccountCode: "FUND-ACCOUNT",
                DisplayName: "Fund account",
                BaseCurrency: "USD",
                Institution: null,
                IsActive: true,
                EffectiveFrom: DateTimeOffset.UnixEpoch,
                EffectiveTo: null,
                PortfolioId: null,
                LedgerReference: null,
                StrategyId: null,
                RunId: null));
        accounts.GetBalanceTimelineAsync(
                fundAccountId,
                Arg.Any<DateOnly?>(),
                Arg.Any<DateOnly?>(),
                Arg.Any<CancellationToken>())
            .Returns([]);
        InternalLedgerTransactionQuery? capturedQuery = null;
        var ledgerSource = Substitute.For<IInternalLedgerTransactionSource>();
        ledgerSource.GetTransactionsAsync(
                Arg.Do<InternalLedgerTransactionQuery>(query => capturedQuery = query),
                Arg.Any<CancellationToken>())
            .Returns([]);
        var provider = new RetainedInternalReconciliationPopulationProvider(
            accounts,
            ledgerTransactionSource: ledgerSource);

        await provider.GetPopulationsAsync(new InternalReconciliationPopulationContext(
            fundAccountId.ToString("D"),
            ExternalAccountLabel,
            PeriodStart,
            PeriodEnd,
            "USD",
            ledgerBookId,
            accountingPeriodId));

        capturedQuery.Should().NotBeNull();
        capturedQuery!.LedgerBookId.Should().Be(ledgerBookId);
        capturedQuery.AccountingPeriodId.Should().Be(accountingPeriodId);
    }

    private static ILedgerJournalStore Store(
        IReadOnlyList<LedgerJournalEntryRecord> records,
        Action<LedgerJournalEntryQuery>? capture = null)
    {
        var store = Substitute.For<ILedgerJournalStore>();
        store.QueryAsync(Arg.Any<LedgerJournalEntryQuery>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                capture?.Invoke(call.Arg<LedgerJournalEntryQuery>());
                return Task.FromResult(records);
            });
        return store;
    }

    private static InternalLedgerTransactionQuery Query(
        Guid? ledgerBookId = null,
        Guid? accountingPeriodId = null) =>
        new(
            ExternalAccountLabel,
            [AccountId],
            PeriodStart,
            PeriodEnd,
            "USD",
            ledgerBookId,
            accountingPeriodId);

    private static JournalEntry Journal(
        DateTimeOffset timestamp,
        JournalEntryMetadata metadata,
        params (LedgerAccount Account, decimal Debit, decimal Credit)[] lines)
    {
        var journalEntryId = Guid.NewGuid();
        return new JournalEntry(
            journalEntryId,
            timestamp,
            "statement reconciliation journal",
            lines.Select(line => new LedgerEntry(
                    Guid.NewGuid(),
                    journalEntryId,
                    timestamp,
                    line.Account,
                    line.Debit,
                    line.Credit,
                    "statement reconciliation journal"))
                .ToArray(),
            metadata);
    }

    private static LedgerJournalEntryRecord Record(JournalEntry entry, Guid accountingPeriodId) =>
        new(
            entry,
            AggregateId: Guid.NewGuid(),
            PeriodId: accountingPeriodId,
            CommandId: null,
            CorrelationId: null,
            GlobalSequence: 1,
            CreatedAt: entry.Timestamp);

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];

        public IDisposable BeginScope<TState>(TState state)
            where TState : notnull => NoopScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Messages.Add(formatter(state, exception));

        private sealed class NoopScope : IDisposable
        {
            public static NoopScope Instance { get; } = new();

            public void Dispose()
            {
            }
        }
    }
}
