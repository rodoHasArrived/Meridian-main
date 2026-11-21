using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.Operations;
using Meridian.Execution.Events;
using Meridian.Execution.Sdk;
using Meridian.Execution.Serialization;
using Meridian.Execution.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meridian.Tests.Execution;

public sealed class PaperFillMarketDataEvidenceTests
{
    private static readonly MarketDataInputEvidence Repaired = new(ProviderId: "source-a", EventReference: "entry",
        Repaired: true, GapFilled: true, RepairReceiptId: "repair-1", QualityScore: .5d,
        QualityAssessedAt: new DateTimeOffset(2026, 11, 20, 0, 0, 0, TimeSpan.Zero),
        AssessmentFrom: new DateOnly(2026, 11, 1), AssessmentTo: new DateOnly(2026, 11, 30), QualityThreshold: .8d);
    private static readonly MarketDataInputEvidence Clean = new(ProviderId: "source-b", EventReference: "exit",
        Repaired: false, GapFilled: false, QualityScore: .95d,
        QualityAssessedAt: new DateTimeOffset(2026, 11, 21, 0, 0, 0, TimeSpan.Zero),
        AssessmentFrom: new DateOnly(2026, 11, 1), AssessmentTo: new DateOnly(2026, 11, 30), QualityThreshold: .8d);

    [Fact]
    public void PartialClosing_RetainsBothAcquisitionAndExitSources()
    {
        var portfolio = new PaperTradingPortfolio(10_000m);
        portfolio.ApplyFillWithAccountingEvidence(Fill("open", OrderSide.Buy, 3m, [Repaired]), ownerAccountId: null);
        var first = portfolio.ApplyFillWithAccountingEvidence(Fill("close-1", OrderSide.Sell, 1m, [Clean]), ownerAccountId: null);
        var last = portfolio.ApplyFillWithAccountingEvidence(Fill("close-2", OrderSide.Sell, 2m, [Clean]), ownerAccountId: null);

        first.MarketDataEvidence.Should().Contain(Repaired).And.Contain(Clean);
        last.MarketDataEvidence.Should().Contain(Repaired).And.Contain(Clean);
        MarketDataInputEvidenceTags.BuildWarnings([last.MarketDataEvidence]).Should().Contain(warning =>
            warning.Kind == "Repaired" && warning.AffectedFillCount == 1);
    }

    [Fact]
    public void CrossingZero_EndsTheOldAcquisitionHistoryForTheResidualPosition()
    {
        var portfolio = new PaperTradingPortfolio(10_000m);
        portfolio.ApplyFillWithAccountingEvidence(Fill("open", OrderSide.Buy, 3m, [Repaired]), ownerAccountId: null);
        var crossing = portfolio.ApplyFillWithAccountingEvidence(Fill("cross", OrderSide.Sell, 5m, [Clean]), ownerAccountId: null);
        var cover = portfolio.ApplyFillWithAccountingEvidence(Fill("cover", OrderSide.Buy, 2m, [Clean]), ownerAccountId: null);

        crossing.MarketDataEvidence.Should().Contain(Repaired).And.Contain(Clean);
        cover.MarketDataEvidence.Should().NotContain(Repaired).And.Contain(Clean);
        MarketDataInputEvidenceTags.BuildWarnings([cover.MarketDataEvidence]).Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnassessedAcquisition_RemainsUnknownWhenTheExitHasEvidence(bool explicitEmpty)
    {
        var portfolio = new PaperTradingPortfolio(10_000m);
        portfolio.ApplyFillWithAccountingEvidence(Fill("open", OrderSide.Buy, 3m, explicitEmpty ? [] : null), ownerAccountId: null);
        var closing = portfolio.ApplyFillWithAccountingEvidence(Fill("close", OrderSide.Sell, 3m, [Clean]), ownerAccountId: null);

        closing.MarketDataEvidence.Should().Contain(source => source.ProviderId == null);
        MarketDataInputEvidenceTags.BuildWarnings([closing.MarketDataEvidence]).Should().Contain(warning =>
            warning.Kind == "Unknown" && warning.AffectedFillCount == 1);
    }

    [Fact]
    public void EventSourceSnapshot_IsImmutableAndComparedByValueAfterSerialization()
    {
        var inputs = new List<MarketDataInputEvidence> { Repaired };
        var evt = new TradeExecutedEvent(Guid.NewGuid(), "order", "SYN", OrderSide.Buy, 1m, 10m,
            0m, 0m, 0m, DateTimeOffset.UtcNow)
        { MarketDataEvidence = inputs }.FreezeFees();
        inputs.Add(Clean);
        evt.MarketDataEvidence.Should().ContainSingle();
        var rehydrated = JsonSerializer.Deserialize(JsonSerializer.Serialize(evt,
            ExecutionJsonContext.Default.TradeExecutedEvent), ExecutionJsonContext.Default.TradeExecutedEvent)!;

        evt.HasSameEconomics(rehydrated).Should().BeTrue();
        evt.HasSameEconomics(rehydrated with { MarketDataEvidence = [Clean] }).Should().BeFalse();
    }

    [Fact]
    public void AddingKnownInputs_DoesNotUpgradeUnassessedAcquisitionHistory()
    {
        var portfolio = new PaperTradingPortfolio(10_000m);
        portfolio.ApplyFillWithAccountingEvidence(Fill("unknown-open", OrderSide.Buy, 3m, null), ownerAccountId: null);
        portfolio.ApplyFillWithAccountingEvidence(Fill("known-add", OrderSide.Buy, 1m, [Clean]), ownerAccountId: null);
        var closing = portfolio.ApplyFillWithAccountingEvidence(Fill("close", OrderSide.Sell, 4m, [Clean]), ownerAccountId: null);

        closing.MarketDataEvidence.Should().Contain(Clean).And.Contain(source => source.ProviderId == null);
        MarketDataInputEvidenceTags.BuildWarnings([closing.MarketDataEvidence]).Should().Contain(warning => warning.Kind == "Unknown");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NativeSessionJournal_RetainsInputTagsAndExactReceiptAcrossRestart(bool observedInputs)
    {
        var path = Path.Combine(Path.GetTempPath(), "paper-input-evidence", Guid.NewGuid().ToString("N"));
        try
        {
            var store = new JsonlFilePaperSessionStore(path, NullLogger<JsonlFilePaperSessionStore>.Instance);
            string sessionId;
            string originalJournal;
            await using (var service = new PaperSessionPersistenceService(NullLogger<PaperSessionPersistenceService>.Instance, store))
            {
                var summary = await service.CreateSessionAsync(new("source-test", null, 10_000m));
                sessionId = summary.SessionId;
                var report = Fill("source-fill", OrderSide.Buy, 3m, observedInputs ? [Repaired] : null)
                    with
                { Timestamp = summary.CreatedAt.AddMinutes(1) };
                await service.RecordFillAsync(sessionId, report);
                var entries = service.GetLedger(sessionId)!.Journal.Where(entry => entry.Metadata.FillId.HasValue).ToArray();
                entries.Should().NotBeEmpty();
                foreach (var entry in entries)
                {
                    if (observedInputs)
                        MarketDataInputEvidenceTags.Read(entry.Metadata.Tags).Should().Contain(Repaired);
                    else
                        (entry.Metadata.Tags?.ContainsKey(MarketDataInputEvidenceTags.VersionTag) ?? false).Should().BeFalse();
                }
                originalJournal = JsonSerializer.Serialize(service.GetLedger(sessionId)!.Journal);
            }
            await using var restarted = new PaperSessionPersistenceService(NullLogger<PaperSessionPersistenceService>.Instance, store);
            await restarted.InitialiseAsync();
            JsonSerializer.Serialize(restarted.GetLedger(sessionId)!.Journal).Should().Be(originalJournal);
        }
        finally { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); }
    }

    private static ExecutionReport Fill(string id, OrderSide side, decimal quantity,
        IReadOnlyList<MarketDataInputEvidence>? sources) => new()
        {
            OrderId = id,
            Symbol = "SYN",
            Side = side,
            ReportType = ExecutionReportType.Fill,
            OrderStatus = OrderStatus.Filled,
            OrderQuantity = quantity,
            FilledQuantity = quantity,
            FillPrice = 10m,
            Timestamp = new DateTimeOffset(2026, 11, 21, 12, 0, 0, TimeSpan.Zero),
            MarketDataEvidence = sources
        };
}
