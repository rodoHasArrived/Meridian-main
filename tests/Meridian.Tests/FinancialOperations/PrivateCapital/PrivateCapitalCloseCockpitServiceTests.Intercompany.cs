using Meridian.FinancialOperations.PrivateCapital;

namespace Meridian.Tests.FinancialOperations.PrivateCapital;

public sealed partial class PrivateCapitalCloseCockpitServiceTests
{
    [Fact]
    public async Task IntercompanyKeywords_DoNotConferCloseReadinessWithoutStructuredPostedSchedule()
    {
        var activity = BuildActivity();
        var service = new PrivateCapitalCloseCockpitService(new StubManualJournalEntryWorkbenchService(activity),
            new StubOperationsContinuityWorkflowService([BuildClosedWorkflow()]));

        var cockpit = await service.GetCockpitAsync(FundProfileId, activity.LedgerBookId, FundAccountId, PeriodId, EntityId);

        var lane = Assert.Single(cockpit.Lanes.Where(item => item.LaneId == "management-company-operations"));
        Assert.False(lane.IsReady);
        Assert.Contains("structured intercompany balance schedule", lane.Summary, StringComparison.Ordinal);
        Assert.False(cockpit.IsReadyToClose);
    }
}
