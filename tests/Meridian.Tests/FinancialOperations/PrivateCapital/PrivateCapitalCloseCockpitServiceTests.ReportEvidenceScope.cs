using FluentAssertions;
using Meridian.FinancialOperations.PrivateCapital;

namespace Meridian.Tests.FinancialOperations.PrivateCapital;

public sealed partial class PrivateCapitalCloseCockpitServiceTests
{
    [Theory]
    [InlineData("AdministratorNav", "account")]
    [InlineData("AdministratorNav", "investor")]
    [InlineData("AdministratorNav", "currency")]
    [InlineData("ShadowNavPack", "account")]
    [InlineData("ShadowNavPack", "investor")]
    [InlineData("ShadowNavPack", "currency")]
    public async Task GetCockpitAsync_ForeignNavSubjectCannotSupportSelectedClose_RepairRestoresReadiness(
        string reportType, string defect)
    {
        var ready = BuildActivity();
        var original = ready.ReportOutputs.Single(output => output.ReportOutputType == reportType);
        var foreign = defect switch
        {
            "account" => original with { CapitalAccountId = "capital-account:foreign" },
            "investor" => original with { InvestorId = "investor:foreign" },
            _ => original with { Currency = "EUR" }
        };
        // Keep the exact event, period, amount, publication and approval evidence. The valid
        // partner statement also remains, so only the foreign NAV subject can block this close.
        var source = new StubManualJournalEntryWorkbenchService(ready with
        {
            ReportOutputs = ready.ReportOutputs.Select(output => output.ReportOutputId == original.ReportOutputId
                ? foreign : output).ToArray()
        });
        var service = new PrivateCapitalCloseCockpitService(source,
            new StubOperationsContinuityWorkflowService([BuildClosedWorkflow()]));

        var blocked = await service.GetCockpitAsync(FundProfileId, ready.LedgerBookId, FundAccountId, PeriodId, EntityId);
        blocked.IsReadyToClose.Should().BeFalse();
        blocked.ReportOutputCount.Should().Be(ready.ReportOutputs.Count - 1);
        blocked.Lanes.Should().ContainSingle(lane => lane.LaneId == "nav-support" && !lane.IsReady);
        blocked.Lanes.Should().ContainSingle(lane => lane.LaneId == "partner-capital-tie-outs" && lane.IsReady);

        source.Activity = ready;
        var repaired = await service.GetCockpitAsync(FundProfileId, ready.LedgerBookId, FundAccountId, PeriodId, EntityId);
        repaired.IsReadyToClose.Should().BeTrue();
        repaired.ReportOutputCount.Should().Be(ready.ReportOutputs.Count);
        repaired.Lanes.Should().ContainSingle(lane => lane.LaneId == "nav-support" && lane.IsReady);
    }

    [Fact]
    public async Task GetCockpitAsync_RetainedAllocationEntryCanProveReportSubjectForUnassignedEvent()
    {
        var ready = BuildActivity();
        // The production posted-event projector uses an unassigned event scope when one event
        // fans out to multiple capital accounts; report provenance can select a retained impact.
        var source = new StubManualJournalEntryWorkbenchService(ready with
        {
            FundEventRecords = ready.FundEventRecords.Select(record => record with
            {
                CapitalAccountId = "capital-account:unassigned",
                InvestorId = null,
                FundEvent = record.FundEvent with { CapitalAccountId = "capital-account:unassigned", InvestorId = null }
            }).ToArray()
        });
        var service = new PrivateCapitalCloseCockpitService(source,
            new StubOperationsContinuityWorkflowService([BuildClosedWorkflow()]));

        var cockpit = await service.GetCockpitAsync(FundProfileId, ready.LedgerBookId, FundAccountId, PeriodId, EntityId);
        cockpit.IsReadyToClose.Should().BeTrue();
        cockpit.ReportOutputCount.Should().Be(ready.ReportOutputs.Count);
        cockpit.Lanes.Should().ContainSingle(lane => lane.LaneId == "nav-support" && lane.IsReady);
    }
}
