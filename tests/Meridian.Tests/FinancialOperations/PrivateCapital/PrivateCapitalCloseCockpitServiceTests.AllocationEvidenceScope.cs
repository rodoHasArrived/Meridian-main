using FluentAssertions;
using Meridian.Contracts.Ledger;
using Meridian.FinancialOperations.PrivateCapital;

namespace Meridian.Tests.FinancialOperations.PrivateCapital;

public sealed partial class PrivateCapitalCloseCockpitServiceTests
{
    [Theory]
    [InlineData("report-output")]
    [InlineData("subledger-entry")]
    [InlineData("ledger-impact")]
    [InlineData("category")]
    public async Task GetCockpitAsync_AllocationRollupCannotLaunderForeignChildEvidence_SourceRepairRestoresReadiness(string child)
    {
        var source = new StubManualJournalEntryWorkbenchService(BuildAllocationRollupActivity(child, repair: false));
        var service = new PrivateCapitalCloseCockpitService(source,
            new StubOperationsContinuityWorkflowService([BuildClosedWorkflow()]));
        var fee = source.Activity.FundEventRecords.Single(static record => record.FundEventType == "ManagementFee");
        // The production record builder merges same-event child links into the union,
        // even when the child's effective date is outside the selected close period.
        fee.EvidenceLinks.Should().Contain("/evidence/foreign/allocation-support");
        fee.FundEvent.EvidenceLinks.Should().NotContain(link => link.Contains("allocation", StringComparison.OrdinalIgnoreCase));

        var blocked = await service.GetCockpitAsync(FundProfileId, source.Activity.LedgerBookId,
            FundAccountId, PeriodId, EntityId);

        blocked.IsReadyToClose.Should().BeFalse();
        blocked.Lanes.Should().ContainSingle(lane => lane.LaneId == "expense-fee-allocation" && !lane.IsReady);

        source.Activity = BuildAllocationRollupActivity(child, repair: true);
        var repaired = await service.GetCockpitAsync(FundProfileId, source.Activity.LedgerBookId,
            FundAccountId, PeriodId, EntityId);

        repaired.IsReadyToClose.Should().BeTrue();
        repaired.Lanes.Should().ContainSingle(lane => lane.LaneId == "expense-fee-allocation" && lane.IsReady);
        source.Activity.FundEventRecords.Single(static record => record.FundEventType == "ManagementFee")
            .EvidenceLinks.Should().Contain("/evidence/foreign/allocation-support");
    }

    private static PrivateCapitalActivityProjectionDto BuildAllocationRollupActivity(string child, bool repair)
    {
        var baseline = BuildActivity();
        var originalFee = baseline.FundEventRecords.Single(static record => record.FundEventType == "ManagementFee");
        var fee = RemoveSelectedSupport(originalFee);
        var events = baseline.FundEvents.Select(fundEvent => fundEvent.FundEventId == fee.FundEventId
            ? repair ? originalFee.FundEvent : fee.FundEvent
            : fundEvent).ToArray();
        var entries = baseline.CapitalAccountSubledgerEntries.Where(entry => entry.FundEventId != fee.FundEventId)
            .Concat(fee.CapitalAccountSubledgerEntries).ToList();
        var impacts = baseline.LedgerImpacts.Where(impact => impact.FundEventId != fee.FundEventId)
            .Concat(fee.LedgerImpacts).ToList();
        var outputs = baseline.ReportOutputs.ToList();
        var foreignDate = new DateOnly(2026, 5, 31);
        const string foreignLink = "/evidence/foreign/allocation-support";
        switch (child)
        {
            case "subledger-entry":
                entries.Add(fee.CapitalAccountSubledgerEntries.Single() with
                {
                    SubledgerEntryId = "foreign-allocation-entry",
                    EffectiveDate = foreignDate,
                    GrossAmount = 0m,
                    NetCapitalActivity = 0m,
                    RunningNetActivity = 0m,
                    EvidenceLinks = [foreignLink]
                });
                break;
            case "ledger-impact":
                impacts.Add(fee.LedgerImpacts.Single() with
                {
                    LedgerImpactId = "foreign-allocation-impact",
                    EffectiveDate = foreignDate,
                    EvidenceLinks = [foreignLink]
                });
                break;
            default:
                outputs.Add(baseline.ReportOutputs.First() with
                {
                    ReportOutputId = "foreign-allocation-report",
                    ReportOutputType = "AllocationSupport",
                    DisplayName = "Allocation support",
                    FundEventId = fee.FundEventId,
                    FundEventType = fee.FundEventType,
                    EffectiveDate = foreignDate,
                    EvidenceLinkCount = 1,
                    EvidenceLinks = [foreignLink]
                });
                break;
        }

        var records = PrivateCapitalFundEventLedgerRecordBuilder.Build(FundProfileId, events, entries, impacts, outputs);
        if (child == "category")
        {
            records = records.Select(record => record.FundEventId != fee.FundEventId ? record : record with
            {
                EvidenceCategories = [.. record.EvidenceCategories,
                    new("allocation-review", "Allocation review", true, "Retained allocation review", 1, [foreignLink])]
            }).ToArray();
        }

        return baseline with
        {
            FundEvents = events,
            FundEventRecords = records,
            CapitalAccountSubledgerEntries = entries,
            LedgerImpacts = impacts,
            ReportOutputs = outputs,
            CapitalAccountSubledgers = PrivateCapitalCapitalAccountSubledgerBuilder.Build(FundProfileId,
                baseline.LedgerBookId, baseline.ProjectedAtUtc, baseline.CapitalAccounts, records, entries, impacts, outputs, [])
        };
    }
}
