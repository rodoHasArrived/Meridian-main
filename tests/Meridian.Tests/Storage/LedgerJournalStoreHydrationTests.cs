using FluentAssertions;
using Meridian.Contracts.FundStructure;
using Meridian.Ledger;
using Meridian.Storage.Ledger;
using Xunit;

namespace Meridian.Tests.Storage;

/// <summary>
/// Guards the operator restart and close-reporting scenario where an in-memory
/// ledger projection is rebuilt from the durable journal spine before as-of reads.
/// </summary>
public sealed class LedgerJournalStoreHydrationTests
{
    [Fact]
    public async Task HydrateLedgerAsOfAsync_RebuildsBalancedProjectionFromDurableStoreQuery()
    {
        var ledgerBookId = Guid.Parse("929e382b-62be-4333-b038-fcb19a7aca91");
        var periodId = Guid.Parse("cb8322d4-19f4-46ed-9665-bbbba2db2f5b");
        var t0 = DateTimeOffset.Parse("2026-02-28T16:00:00Z");
        var t1 = t0.AddHours(1);
        var asOf = t0.AddMinutes(30);
        var cash = new LedgerAccount("Assets:Cash", LedgerAccountType.Asset);
        var revenue = new LedgerAccount("Revenue:Fees", LedgerAccountType.Revenue);
        var records = new[]
        {
            BuildRecord(
                "after as-of",
                t1,
                periodId,
                globalSequence: 2,
                (cash, 25m, 0m),
                (revenue, 0m, 25m)),
            BuildRecord(
                "before as-of",
                t0,
                periodId,
                globalSequence: 1,
                (cash, 100m, 0m),
                (revenue, 0m, 100m)),
        };
        var store = new QueryableLedgerJournalStore(records);
        var dimensions = new LedgerLineDimensionSet(FundId: "fund-alpha");

        var ledger = await store.HydrateLedgerAsOfAsync(ledgerBookId, asOf, dimensions);

        store.LastQuery.Should().NotBeNull();
        store.LastQuery!.LedgerBookId.Should().Be(ledgerBookId);
        store.LastQuery.OccurredTo.Should().Be(asOf);
        store.LastQuery.LineDimensions.Should().BeSameAs(dimensions);
        ledger.Journal.Should().ContainSingle()
            .Which.Description.Should().Be("before as-of");
        ledger.TrialBalance()[cash].Should().Be(100m);
        ledger.TrialBalance()[revenue].Should().Be(100m);
    }

    [Fact]
    public async Task HydrateLedgerAsync_PostsRecordsInDurableSequenceOrder()
    {
        var ledgerBookId = Guid.Parse("b61f9504-f442-409b-91da-fba7d68fa1f7");
        var periodId = Guid.Parse("c97605f9-7ede-4b2a-bbdb-b366b6ef5d14");
        var timestamp = DateTimeOffset.Parse("2026-03-01T12:00:00Z");
        var cash = new LedgerAccount("Assets:Cash", LedgerAccountType.Asset);
        var revenue = new LedgerAccount("Revenue:Fees", LedgerAccountType.Revenue);
        var records = new[]
        {
            BuildRecord(
                "second",
                timestamp,
                periodId,
                globalSequence: 2,
                (cash, 20m, 0m),
                (revenue, 0m, 20m)),
            BuildRecord(
                "first",
                timestamp,
                periodId,
                globalSequence: 1,
                (cash, 10m, 0m),
                (revenue, 0m, 10m)),
        };
        var store = new QueryableLedgerJournalStore(records);

        var ledger = await store.HydrateLedgerAsync(new LedgerJournalEntryQuery(LedgerBookId: ledgerBookId));

        ledger.Journal.Select(static entry => entry.Description)
            .Should()
            .Equal("first", "second");
        ledger.TrialBalance()[cash].Should().Be(30m);
        ledger.TrialBalance()[revenue].Should().Be(30m);
    }

    [Fact]
    public async Task HydrateLedgerPeriodAsync_ScopesStoreQueryToBookAndPeriod()
    {
        var ledgerBookId = Guid.Parse("f79cbb92-3751-491a-80e4-816a0c37fb4a");
        var targetPeriodId = Guid.Parse("75c2bc6f-5c04-4d24-8b71-96fdd5023e77");
        var otherPeriodId = Guid.Parse("c6c821eb-bf62-45fc-93ea-744d029471ed");
        var timestamp = DateTimeOffset.Parse("2026-03-31T12:00:00Z");
        var cash = new LedgerAccount("Assets:Cash", LedgerAccountType.Asset);
        var revenue = new LedgerAccount("Revenue:Fees", LedgerAccountType.Revenue);
        var records = new[]
        {
            BuildRecord(
                "target period",
                timestamp,
                targetPeriodId,
                globalSequence: 1,
                (cash, 75m, 0m),
                (revenue, 0m, 75m)),
            BuildRecord(
                "other period",
                timestamp,
                otherPeriodId,
                globalSequence: 2,
                (cash, 125m, 0m),
                (revenue, 0m, 125m)),
        };
        var store = new QueryableLedgerJournalStore(records);
        var dimensions = new LedgerLineDimensionSet(FundId: "fund-alpha");

        var ledger = await store.HydrateLedgerPeriodAsync(ledgerBookId, targetPeriodId, dimensions);

        store.LastQuery.Should().NotBeNull();
        store.LastQuery!.LedgerBookId.Should().Be(ledgerBookId);
        store.LastQuery.PeriodId.Should().Be(targetPeriodId);
        store.LastQuery.LineDimensions.Should().BeSameAs(dimensions);
        ledger.Journal.Should().ContainSingle()
            .Which.Description.Should().Be("target period");
    }

    [Fact]
    public async Task QueryAsync_SourceEventId_ReturnsOnlyTheMatchingEconomicEvent()
    {
        var targetSourceEventId = Guid.NewGuid();
        var otherSourceEventId = Guid.NewGuid();
        var periodId = Guid.NewGuid();
        var timestamp = DateTimeOffset.Parse("2026-03-31T12:00:00Z");
        var cash = new LedgerAccount("Assets:Cash", LedgerAccountType.Asset);
        var revenue = new LedgerAccount("Revenue:Fees", LedgerAccountType.Revenue);
        var records = new[]
        {
            BuildRecord(
                "target event",
                timestamp,
                periodId,
                globalSequence: 1,
                (cash, 75m, 0m),
                (revenue, 0m, 75m)) with { SourceEventId = targetSourceEventId },
            BuildRecord(
                "other event",
                timestamp.AddMinutes(1),
                periodId,
                globalSequence: 2,
                (cash, 125m, 0m),
                (revenue, 0m, 125m)) with { SourceEventId = otherSourceEventId }
        };
        var store = new QueryableLedgerJournalStore(records);

        var matches = await store.QueryAsync(new LedgerJournalEntryQuery(SourceEventId: targetSourceEventId));

        matches.Should().ContainSingle()
            .Which.Entry.Description.Should().Be("target event");
    }

    [Fact]
    public async Task HydrateLedgerAsOfAsync_RejectsUnscopedLedgerBook()
    {
        var store = new QueryableLedgerJournalStore([]);

        var act = () => store.HydrateLedgerAsOfAsync(Guid.Empty, DateTimeOffset.UtcNow);

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*Ledger book id is required*");
    }

    [Fact]
    public async Task HydrateLedgerPeriodAsync_RejectsUnscopedPeriod()
    {
        var store = new QueryableLedgerJournalStore([]);

        var act = () => store.HydrateLedgerPeriodAsync(Guid.NewGuid(), Guid.Empty);

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*Period id is required*");
    }

    private static LedgerJournalEntryRecord BuildRecord(
        string description,
        DateTimeOffset timestamp,
        Guid periodId,
        long globalSequence,
        params (LedgerAccount Account, decimal Debit, decimal Credit)[] lines)
    {
        var journalEntryId = Guid.NewGuid();
        var entry = new JournalEntry(
            journalEntryId,
            timestamp,
            description,
            lines.Select(line => new LedgerEntry(
                Guid.NewGuid(),
                journalEntryId,
                timestamp,
                line.Account,
                line.Debit,
                line.Credit,
                description)).ToArray());

        return new LedgerJournalEntryRecord(
            entry,
            AggregateId: Guid.NewGuid(),
            PeriodId: periodId,
            CommandId: null,
            CorrelationId: null,
            GlobalSequence: globalSequence,
            CreatedAt: timestamp);
    }

    private sealed class QueryableLedgerJournalStore(IReadOnlyList<LedgerJournalEntryRecord> records) : ILedgerJournalStore
    {
        public LedgerJournalEntryQuery? LastQuery { get; private set; }

        public Task AppendAsync(LedgerJournalEntryWrite entry, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<LedgerJournalEntryRecord>> QueryAsync(
            LedgerJournalEntryQuery query,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            LastQuery = query;
            IEnumerable<LedgerJournalEntryRecord> filtered = records;

            if (query.OccurredFrom.HasValue)
            {
                filtered = filtered.Where(record => record.Entry.Timestamp >= query.OccurredFrom.Value);
            }

            if (query.OccurredTo.HasValue)
            {
                filtered = filtered.Where(record => record.Entry.Timestamp <= query.OccurredTo.Value);
            }

            if (query.PeriodId.HasValue)
            {
                filtered = filtered.Where(record => record.PeriodId == query.PeriodId.Value);
            }

            if (query.SourceEventId.HasValue)
            {
                filtered = filtered.Where(record => record.SourceEventId == query.SourceEventId.Value);
            }

            return Task.FromResult<IReadOnlyList<LedgerJournalEntryRecord>>(filtered.ToList());
        }

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
