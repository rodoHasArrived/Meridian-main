using FluentAssertions;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.Ledger;
using Meridian.Storage.Ledger;

namespace Meridian.Tests.Storage;

/// <summary>
/// Guards the operator restart scenario where the in-memory ledger projection is rebuilt from
/// the durable journal spine before as-of close or reporting reads.
/// </summary>
public sealed class LedgerJournalStoreHydrationTests
{
    [Fact]
    public async Task HydrateLedgerAsOfAsync_RebuildsBalancedProjectionFromDurableStoreQuery()
    {
        var ledgerBookId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var periodId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var t1 = DateTimeOffset.Parse("2026-06-30T20:00:00Z");
        var t2 = t1.AddMinutes(5);
        var cash = new LedgerAccount("Cash", LedgerAccountType.Asset);
        var revenue = new LedgerAccount("Management fees", LedgerAccountType.Revenue);
        var store = new QueryableLedgerJournalStore(
            ledgerBookId,
            BuildRecord(periodId, t2, "after as-of", cash, revenue, 30m, globalSequence: 2),
            BuildRecord(periodId, t1, "at as-of", cash, revenue, 100m, globalSequence: 1));

        var hydrated = await store.HydrateLedgerAsOfAsync(ledgerBookId, t1);

        store.CapturedQuery.Should().NotBeNull();
        store.CapturedQuery!.LedgerBookId.Should().Be(ledgerBookId);
        store.CapturedQuery.OccurredTo.Should().Be(t1);
        hydrated.Journal.Should().ContainSingle()
            .Which.Description.Should().Be("at as-of");
        hydrated.TrialBalance()[cash].Should().Be(100m);
        hydrated.TrialBalance()[revenue].Should().Be(100m);
    }

    [Fact]
    public async Task HydrateLedgerAsync_PostsRecordsInDurableSequenceOrder()
    {
        var ledgerBookId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var periodId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var timestamp = DateTimeOffset.Parse("2026-06-30T20:00:00Z");
        var cash = new LedgerAccount("Cash", LedgerAccountType.Asset);
        var revenue = new LedgerAccount("Management fees", LedgerAccountType.Revenue);
        var store = new QueryableLedgerJournalStore(
            ledgerBookId,
            BuildRecord(periodId, timestamp, "second durable entry", cash, revenue, 20m, globalSequence: 2),
            BuildRecord(periodId, timestamp, "first durable entry", cash, revenue, 10m, globalSequence: 1));

        var hydrated = await store.HydrateLedgerAsync(new LedgerJournalEntryQuery(LedgerBookId: ledgerBookId));

        hydrated.Journal.Select(static entry => entry.Description)
            .Should()
            .Equal("first durable entry", "second durable entry");
    }

    [Fact]
    public async Task HydrateLedgerAsOfAsync_RejectsUnscopedLedgerBook()
    {
        var store = new QueryableLedgerJournalStore(Guid.NewGuid());

        var act = () => store.HydrateLedgerAsOfAsync(Guid.Empty, DateTimeOffset.UtcNow);

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*Ledger book id*");
    }

    private static LedgerJournalEntryRecord BuildRecord(
        Guid periodId,
        DateTimeOffset timestamp,
        string description,
        LedgerAccount debitAccount,
        LedgerAccount creditAccount,
        decimal amount,
        long globalSequence)
    {
        var journalEntryId = Guid.NewGuid();
        var entry = new JournalEntry(
            journalEntryId,
            timestamp,
            description,
            [
                new LedgerEntry(Guid.NewGuid(), journalEntryId, timestamp, debitAccount, amount, 0m, description),
                new LedgerEntry(Guid.NewGuid(), journalEntryId, timestamp, creditAccount, 0m, amount, description)
            ]);

        return new LedgerJournalEntryRecord(
            entry,
            AggregateId: Guid.Parse("aaaaaaaa-1111-1111-1111-aaaaaaaaaaaa"),
            PeriodId: periodId,
            CommandId: null,
            CorrelationId: null,
            GlobalSequence: globalSequence,
            CreatedAt: timestamp);
    }

    private sealed class QueryableLedgerJournalStore(Guid ledgerBookId, params LedgerJournalEntryRecord[] records) : ILedgerJournalStore
    {
        public LedgerJournalEntryQuery? CapturedQuery { get; private set; }

        public Task AppendAsync(LedgerJournalEntryWrite entry, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<LedgerJournalEntryRecord>> QueryAsync(
            LedgerJournalEntryQuery query,
            CancellationToken ct = default)
        {
            CapturedQuery = query;
            IEnumerable<LedgerJournalEntryRecord> filtered = records;

            if (query.LedgerBookId.HasValue && query.LedgerBookId != ledgerBookId)
            {
                filtered = [];
            }

            if (query.OccurredTo.HasValue)
            {
                filtered = filtered.Where(record => record.Entry.Timestamp <= query.OccurredTo.Value);
            }

            return Task.FromResult<IReadOnlyList<LedgerJournalEntryRecord>>(filtered.ToList());
        }

        public Task<IReadOnlyList<LedgerJournalEntryRecord>> GetByPeriodAsync(Guid periodId, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<LedgerJournalEntryRecord>> GetByAggregateAsync(Guid aggregateId, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<LedgerAccountingPeriod?> GetPeriodAsync(Guid periodId, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<LedgerAccountingPeriod>> ListPeriodsAsync(
            Guid? ledgerBookId = null,
            string? status = null,
            string? fundProfileId = null,
            Guid? fundStructureNodeId = null,
            CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<LedgerAccountingPeriod> SavePeriodAsync(
            LedgerAccountingPeriod period,
            long expectedVersion,
            PeriodCloseEventRecord? closeEvent = null,
            CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<LedgerBookRecord?> GetLedgerBookAsync(Guid ledgerBookId, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<LedgerBookRecord>> ListLedgerBooksAsync(
            string? fundProfileId = null,
            Guid? fundStructureNodeId = null,
            FundStructureNodeKindDto? fundStructureNodeKind = null,
            CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<LedgerBookRecord> SaveLedgerBookAsync(LedgerBookRecord book, CancellationToken ct = default)
            => throw new NotSupportedException();
    }
}
