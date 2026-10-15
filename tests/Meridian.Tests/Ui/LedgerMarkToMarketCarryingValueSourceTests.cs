using System.Text.Json;
using FluentAssertions;
using Meridian.Application.Accounting;
using Meridian.Ledger;
using Meridian.Storage.Ledger;
using Meridian.Ui.Shared.Services;
using NSubstitute;

namespace Meridian.Tests.Ui;

public sealed class LedgerMarkToMarketCarryingValueSourceTests
{
    private static readonly Guid BookId = Guid.NewGuid();
    private static readonly Guid SecurityId = Guid.NewGuid();
    private static readonly DateTimeOffset AsOf = DateTimeOffset.Parse("2026-07-15T22:00:00Z");

    [Theory]
    [InlineData("entity-a", 100, 10)]
    [InlineData("entity-b", 200, -90)]
    public async Task PrepareAsync_SharedBookAccount_UsesOnlyRequestedEntityAndRetainsItsScope(
        string entityId, decimal expectedCarrying, decimal expectedAdjustment)
    {
        var store = Substitute.For<ILedgerJournalStore>();
        store.QueryAsync(Arg.Any<LedgerJournalEntryQuery>(), Arg.Any<CancellationToken>())
            .Returns(new[] { Record("entity-a", 100m), Record("entity-b", 200m), Record(null, 300m) });
        var quotes = Substitute.For<IMarkPriceSource>();
        quotes.GetMarkPriceAsync("AAPL", DateOnly.FromDateTime(AsOf.UtcDateTime), Arg.Any<CancellationToken>())
            .Returns(new MarkPriceQuote(110m, "official-close", "price://aapl/2026-07-15",
                PriceAsOf: DateOnly.FromDateTime(AsOf.UtcDateTime)));
        var service = new DailyMarkToMarketService(quotes, new LedgerMarkToMarketCarryingValueSource(store));
        var policy = new DailyPortfolioPricingPolicy("fund-a", "policy-a", "Daily close", "market-close",
            "controller", AsOf.AddDays(-1));

        var result = await service.PrepareAsync(new DailyMarkToMarketRequest(policy, "2026-07", AsOf, "USD",
            [new MarkToMarketPosition("AAPL", 1m, 100m, "broker-a", SecurityId: SecurityId)],
            "preparer", "Daily close", LedgerBookId: BookId, EntityId: entityId));

        var line = result.Projection!.Lines.Should().ContainSingle().Subject;
        line.PriorCarryingValue.Should().Be(expectedCarrying);
        line.MarkAdjustment.Should().Be(expectedAdjustment);
        line.CarryingValueEvidenceReference.Should().Contain($"entityId={entityId}");
        var draft = result.Approvals.Should().ContainSingle().Subject.Draft;
        draft.Lines.Should().OnlyContain(item => item.dimensions!.EntityId == entityId);
        draft.Metadata.Tags!["valuation.entityId"].Should().Be(entityId);
        var evidence = JsonSerializer.Deserialize<ValuationMarkEvidence[]>(draft.Metadata.Tags[ValuationMarkEvidenceGuard.EvidenceTag]);
        evidence.Should().ContainSingle().Which.EntityId.Should().Be(entityId);
        ValuationMarkEvidenceGuard.EnsureValid(draft);
        ValuationMarkEvidenceGuard.Validate(draft.Metadata.Tags[ValuationMarkEvidenceGuard.EvidenceTag], "fund-a",
            DateOnly.FromDateTime(AsOf.UtcDateTime), SecurityId, "broker-a", "AAPL",
            draft.Metadata.Tags[ValuationMarkEvidenceGuard.DigestTag], "another-entity").Should().NotBeNull();
        await store.Received(1).QueryAsync(Arg.Is<LedgerJournalEntryQuery>(query => query.LedgerBookId == BookId &&
            query.OccurredTo == AsOf && query.LineDimensions!.EntityId == entityId), Arg.Any<CancellationToken>());
    }

    private static LedgerJournalEntryRecord Record(string? entityId, decimal amount)
    {
        var id = Guid.NewGuid();
        var occurredAt = AsOf.AddDays(-1);
        var dimensions = new LedgerLineDimensionSet(FundId: "fund-a", EntityId: entityId);
        var entry = new JournalEntry(id, occurredAt, "Retained acquisition",
        [
            new LedgerEntry(Guid.NewGuid(), id, occurredAt, LedgerAccounts.Securities("AAPL", "broker-a"),
                amount, 0m, "Retained acquisition", dimensions),
            new LedgerEntry(Guid.NewGuid(), id, occurredAt, LedgerAccounts.Cash, 0m, amount,
                "Retained acquisition", dimensions)
        ]);
        return new LedgerJournalEntryRecord(entry, BookId, Guid.NewGuid(), null, null, 1, occurredAt);
    }
}
