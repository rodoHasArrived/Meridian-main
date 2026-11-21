using Meridian.Ledger;
using Meridian.Ui.Shared.Endpoints;

namespace Meridian.Tests.Ui;

public sealed class LedgerJournalExecutionLineageTests
{
    [Fact]
    public void UsesTheExecutionJournalsOwnOrderIdentifierBeforeTheGuidForm()
    {
        var fillId = Guid.NewGuid();
        var metadata = new JournalEntryMetadata(ActivityType: "trade-fill", OrderId: null, FillId: fillId,
            Tags: new Dictionary<string, string> { ["source.orderId"] = "PO-1", [LedgerQuery.StrategyRunIdTag] = "run-7" });

        var lineage = LedgerEndpoints.BuildExecutionLineage(metadata);

        Assert.NotNull(lineage);
        Assert.Equal("PO-1", lineage!.OrderId);
        Assert.Equal(fillId, lineage.FillId);
        Assert.Equal("run-7", lineage.StrategyRunId);
    }

    [Fact]
    public void FallsBackToTheRecordedGuidOrderIdWhenNoSourceTagWasRetained()
    {
        var orderId = Guid.NewGuid();
        var lineage = LedgerEndpoints.BuildExecutionLineage(new JournalEntryMetadata(OrderId: orderId, FillId: Guid.NewGuid()));
        Assert.Equal(orderId.ToString("D"), lineage!.OrderId);
        Assert.Null(lineage.StrategyRunId);
    }

    [Fact]
    public void AJournalNotPostedFromAnExecutionCarriesNoLineage()
    {
        Assert.Null(LedgerEndpoints.BuildExecutionLineage(null));
        Assert.Null(LedgerEndpoints.BuildExecutionLineage(new JournalEntryMetadata(ActivityType: "margin-interest",
            Tags: new Dictionary<string, string> { [LedgerQuery.StrategyRunIdTag] = "run-7" })));
    }
}
