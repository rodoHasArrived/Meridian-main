using FluentAssertions;
using Meridian.Backtesting.Engine;
using Meridian.Contracts.Domain.Models;
using Meridian.Contracts.Operations;
using Meridian.Domain.Events;
using Meridian.Platform.Scheduling;
using Meridian.Storage.Backfill;
using Xunit;

namespace Meridian.Backtesting.Tests;

public sealed class BacktestInputQualityEvidenceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "meridian-input-quality-" + Guid.NewGuid().ToString("N"));
    private static readonly DateOnly Date = new(2024, 7, 3);

    [Fact]
    public async Task VerifiedRepairAndAssessmentTravelWithTheActualPriceInput()
    {
        var item = Bar("SPY", Date, 100m);
        var batch = Batch(item).WithInputEvidence(new(Repaired: true, GapFilled: true, RepairReceiptId: "repair:synthetic",
            QualityScore: .6, QualityAssessedAt: item.Timestamp.AddDays(1), AssessmentFrom: Date, AssessmentTo: Date, QualityThreshold: .8));
        await new BackfillStatusStore(_root).Batches.PublishAsync(batch);
        var evidence = NewEvidence();
        var replayed = await Read(evidence, [item with { BackfillRevisionId = batch.Receipt.RevisionId }]);
        evidence.Observe(replayed.Single());
        var source = evidence.PriceInputEvidence("SPY", item.Timestamp).Should().ContainSingle().Which;
        source.ProviderId.Should().Be("fixture");
        source.BackfillRevisionId.Should().Be(batch.Receipt.RevisionId);
        source.ContentHash.Should().Be(batch.Receipt.ContentHash);
        source.Repaired.Should().BeTrue();
        source.QualityScore.Should().Be(.6);
        evidence.PriceEvidence("SPY", item.Timestamp).Single().ContentHash.Should().Be(batch.Receipt.ContentHash);
    }

    [Fact]
    public async Task DuplicateRowsPinOneRevisionBeforeYieldingRegardlessOfFileOrder()
    {
        var item = Bar("SPY", Date, 100m);
        var batch = Batch(item);
        var first = batch.WithInputEvidence(new(Repaired: true, GapFilled: true));
        var second = batch.WithInputEvidence(new(Repaired: false, GapFilled: false));
        var store = new BackfillStatusStore(_root);
        await store.Batches.PublishAsync(first);
        await store.Batches.PublishAsync(second);
        var rows = new[] { item with { BackfillRevisionId = first.Receipt.RevisionId }, item with { BackfillRevisionId = second.Receipt.RevisionId } };
        var forward = NewEvidence();
        var reverse = NewEvidence();
        var selectedA = (await Read(forward, rows)).Should().ContainSingle().Which;
        var selectedB = (await Read(reverse, rows.Reverse())).Should().ContainSingle().Which;
        selectedA.BackfillRevisionId.Should().Be(selectedB.BackfillRevisionId);
        selectedA.BackfillRevisionId.Should().Be(new[] { first.Receipt.RevisionId, second.Receipt.RevisionId }.Order(StringComparer.Ordinal).First());
        forward.Observe(selectedA);
        reverse.Observe(selectedB);
        forward.PriceInputEvidence("SPY", item.Timestamp).Should().Equal(reverse.PriceInputEvidence("SPY", item.Timestamp));
    }

    [Fact]
    public async Task UnverifiedOrLaterUnstampedPricesCannotInheritEarlierRepairOrQualityClaims()
    {
        var first = Bar("SPY", Date, 100m);
        var batch = Batch(first).WithInputEvidence(new(Repaired: true, GapFilled: true));
        await new BackfillStatusStore(_root).Batches.PublishAsync(batch);
        var evidence = NewEvidence();
        var later = Bar("SPY", Date.AddDays(1), 101m);
        var replay = await Read(evidence, [first with { BackfillRevisionId = batch.Receipt.RevisionId }, later]);
        foreach (var item in replay)
            evidence.Observe(item);
        var source = evidence.PriceInputEvidence("SPY", later.Timestamp).Single();
        source.BackfillRevisionId.Should().BeNull();
        source.Repaired.Should().BeNull();
        source.QualityScore.Should().BeNull();
        evidence.PriceEvidence("SPY", later.Timestamp).Should().BeEmpty();
    }

    [Fact]
    public async Task OutOfRangeAggregateAssessmentRemainsUnknownForTheObservedBar()
    {
        var item = Bar("SPY", Date, 100m);
        var batch = Batch(item).WithInputEvidence(new(QualityScore: .9, QualityAssessedAt: item.Timestamp.AddDays(3),
            AssessmentFrom: Date.AddDays(1), AssessmentTo: Date.AddDays(2)));
        await new BackfillStatusStore(_root).Batches.PublishAsync(batch);
        var evidence = NewEvidence();
        var observed = (await Read(evidence, [item with { BackfillRevisionId = batch.Receipt.RevisionId }])).Single();
        evidence.Observe(observed);
        var source = evidence.PriceInputEvidence("SPY", item.Timestamp).Single();
        source.QualityScore.Should().BeNull();
        source.EvidenceIssue.Should().NotBeNullOrWhiteSpace();
    }

    private BacktestInputEvidence NewEvidence() => new(_root, new HistoricalSessionScheduleResolver(null));
    private static MarketEvent Bar(string symbol, DateOnly date, decimal price)
    {
        var bar = new HistoricalBar(symbol, date, price, price, price, price, 1000, "fixture");
        return MarketEvent.HistoricalBar(bar.ToTimestampUtc(), symbol, bar, "fixture");
    }
    private static BackfillBatchRevision Batch(MarketEvent item) => BackfillBatchRevision.Create("fixture", "fixture",
        "default", item.Symbol, "daily", Date, Date, "us-equity-sessions-v1", [Date], [Date], [BackfillBatchRevision.HashRow(item)]);
    private static async Task<List<MarketEvent>> Read(BacktestInputEvidence evidence, IEnumerable<MarketEvent> rows)
    {
        var result = new List<MarketEvent>();
        await foreach (var item in evidence.VerifyAsync(Events(rows), CancellationToken.None))
            result.Add(item);
        return result;
    }
    private static async IAsyncEnumerable<MarketEvent> Events(IEnumerable<MarketEvent> rows)
    {
        foreach (var row in rows)
        { yield return row; await Task.Yield(); }
    }
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
