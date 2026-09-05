using FluentAssertions;
using Meridian.Contracts.Ledger;
using Meridian.FinancialOperations.PrivateCapital;

namespace Meridian.Tests.FinancialOperations.PrivateCapital;

public sealed partial class PrivateCapitalCloseCockpitServiceTests
{
    [Theory]
    [InlineData("period")]
    [InlineData("entity")]
    public async Task GetCockpitAsync_CumulativeForeignSupportCannotReplaceSelectedScopeEvidence_RepairRestoresReadiness(string foreignScope)
    {
        var source = new StubManualJournalEntryWorkbenchService(BuildMixedScopeActivity(foreignScope, repair: false));
        var service = new PrivateCapitalCloseCockpitService(source,
            new StubOperationsContinuityWorkflowService([BuildClosedWorkflow()]));

        // Use the real cumulative subledger builder: both scopes share one account,
        // investor and currency, so the foreign support is present in the selected aggregate.
        var cumulative = source.Activity.CapitalAccountSubledgers.Should().ContainSingle().Subject;
        cumulative.FundEventCount.Should().Be(4);
        cumulative.EndingNetActivity.Should().Be(470m);
        cumulative.NetCapitalActivity.Should().Be(470m);
        cumulative.ReportOutputs.Should().Contain(output =>
            output.ReportOutputType == "CapitalAccountStatement" && output.FundEventId.EndsWith(":foreign"));
        cumulative.EvidenceLinks.Should().Contain("/evidence/foreign/allocation-policy");

        var blocked = await service.GetCockpitAsync(FundProfileId, source.Activity.LedgerBookId,
            FundAccountId, PeriodId, EntityId);

        blocked.FundEventCount.Should().Be(2);
        blocked.IsReadyToClose.Should().BeFalse();
        blocked.Lanes.Should().ContainSingle(lane => lane.LaneId == "partner-capital-tie-outs" && !lane.IsReady);
        blocked.Lanes.Should().ContainSingle(lane => lane.LaneId == "expense-fee-allocation" && !lane.IsReady);
        blocked.Lanes.Single(lane => lane.LaneId == "partner-capital-tie-outs").EvidenceLinks.Should().Contain(link =>
            link.Route == "/evidence/foreign/allocation-policy" && link.Label == "Cumulative capital-account diagnostics");

        // Retain the foreign history and repair only the selected events' support.
        source.Activity = BuildMixedScopeActivity(foreignScope, repair: true);
        var repaired = await service.GetCockpitAsync(FundProfileId, source.Activity.LedgerBookId,
            FundAccountId, PeriodId, EntityId);

        repaired.IsReadyToClose.Should().BeTrue();
        repaired.Lanes.Should().ContainSingle(lane => lane.LaneId == "partner-capital-tie-outs" && lane.IsReady);
        repaired.Lanes.Should().ContainSingle(lane => lane.LaneId == "expense-fee-allocation" && lane.IsReady);
        source.Activity.CapitalAccountSubledgers.Single().EndingNetActivity.Should().Be(cumulative.EndingNetActivity);
        source.Activity.CapitalAccountSubledgers.Single().FundEventCount.Should().Be(cumulative.FundEventCount);
    }

    [Fact]
    public async Task GetCockpitAsync_StatementWithSelectedEventIdButForeignPeriodBlocks_RepairRestoresReadiness()
    {
        var ready = BuildActivity();
        var wrongPeriod = ready with
        {
            ReportOutputs = ready.ReportOutputs.Select(output => output.ReportOutputType == "CapitalAccountStatement"
                ? output with { EffectiveDate = new DateOnly(2026, 5, 31) }
                : output).ToArray()
        };
        var source = new StubManualJournalEntryWorkbenchService(wrongPeriod);
        var service = new PrivateCapitalCloseCockpitService(source,
            new StubOperationsContinuityWorkflowService([BuildClosedWorkflow()]));

        var blocked = await service.GetCockpitAsync(FundProfileId, ready.LedgerBookId, FundAccountId, PeriodId, EntityId);
        blocked.IsReadyToClose.Should().BeFalse();
        blocked.Lanes.Should().ContainSingle(lane => lane.LaneId == "partner-capital-tie-outs" && !lane.IsReady);

        source.Activity = ready;
        var repaired = await service.GetCockpitAsync(FundProfileId, ready.LedgerBookId, FundAccountId, PeriodId, EntityId);
        repaired.IsReadyToClose.Should().BeTrue();
    }

    private static PrivateCapitalActivityProjectionDto BuildMixedScopeActivity(string foreignScope, bool repair)
    {
        var baseline = BuildActivity();
        var foreignRecords = baseline.FundEventRecords.Select(record => ForeignScopeRecord(record, foreignScope)).ToArray();
        var selectedRecords = baseline.FundEventRecords.Select(record => repair ? record : RemoveSelectedSupport(record)).ToArray();
        var records = foreignRecords.Concat(selectedRecords).ToArray();
        decimal running = 0m;
        var entries = records.SelectMany(static record => record.CapitalAccountSubledgerEntries)
            .OrderBy(static entry => entry.EffectiveDate)
            .ThenBy(static entry => entry.SubledgerEntryId, StringComparer.OrdinalIgnoreCase)
            .Select(entry => entry with { RunningNetActivity = running += entry.NetCapitalActivity }).ToArray();
        var impacts = records.SelectMany(static record => record.LedgerImpacts).ToArray();
        var outputs = records.SelectMany(static record => record.ReportOutputs).ToArray();
        var account = baseline.CapitalAccounts.Single() with
        {
            Contributions = 500m,
            ManagementFees = 30m,
            NetActivity = 470m,
            FundEventCount = 4,
            FundEventIds = records.Select(static record => record.FundEventId).ToArray()
        };
        var subledgers = PrivateCapitalCapitalAccountSubledgerBuilder.Build(FundProfileId, baseline.LedgerBookId,
            baseline.ProjectedAtUtc, [account], records, entries, impacts, outputs, []);
        return baseline with
        {
            FundEventCount = records.Length,
            PostedFundEventCount = records.Length,
            NetCapitalActivity = 470m,
            FundEvents = records.Select(static record => record.FundEvent).ToArray(),
            CapitalAccounts = [account],
            CapitalAccountSubledgerEntries = entries,
            LedgerImpacts = impacts,
            ReportOutputs = outputs,
            FundEventRecords = records,
            CapitalAccountSubledgers = subledgers
        };
    }

    private static PrivateCapitalFundEventLedgerRecordDto ForeignScopeRecord(
        PrivateCapitalFundEventLedgerRecordDto record, string foreignScope)
    {
        var eventId = record.FundEventId + ":foreign";
        var date = foreignScope == "period" ? new DateOnly(2026, 5, 31) : record.EffectiveDate;
        var journalId = Guid.NewGuid();
        var links = record.EvidenceLinks.Select(ForeignEvidenceLink).ToArray();
        return record with
        {
            FundEventRecordId = record.FundEventRecordId + ":foreign",
            FundEventId = eventId,
            JournalEntryId = journalId,
            EffectiveDate = date,
            EvidenceLinks = links,
            FundEvent = record.FundEvent with
            {
                FundEventId = eventId,
                JournalEntryId = journalId,
                EffectiveDate = date,
                EvidenceLinks = record.FundEvent.EvidenceLinks.Select(ForeignEvidenceLink).ToArray()
            },
            CapitalAccountSubledgerEntries = record.CapitalAccountSubledgerEntries.Select(entry => entry with
            {
                SubledgerEntryId = entry.SubledgerEntryId + ":foreign",
                FundEventId = eventId,
                JournalEntryId = journalId,
                EffectiveDate = date,
                EvidenceLinks = entry.EvidenceLinks.Select(ForeignEvidenceLink).ToArray()
            }).ToArray(),
            LedgerImpacts = record.LedgerImpacts.Select(impact => impact with
            {
                LedgerImpactId = impact.LedgerImpactId + ":foreign",
                FundEventId = eventId,
                JournalEntryId = journalId,
                EffectiveDate = date,
                EvidenceLinks = impact.EvidenceLinks.Select(ForeignEvidenceLink).ToArray(),
                Lines = impact.Lines.Select(line => line with
                {
                    EntityId = foreignScope == "entity" ? "entity-other" : line.EntityId,
                    EvidenceLink = line.EvidenceLink is null ? null : ForeignEvidenceLink(line.EvidenceLink)
                }).ToArray()
            }).ToArray(),
            ReportOutputs = record.ReportOutputs.Select(output => output with
            {
                ReportOutputId = output.ReportOutputId + ":foreign",
                FundEventId = eventId,
                EffectiveDate = date,
                EvidenceLinks = output.EvidenceLinks.Select(ForeignEvidenceLink).ToArray()
            }).ToArray(),
            EvidenceCategories = record.EvidenceCategories.Select(category => category with
            {
                EvidenceLinks = category.EvidenceLinks.Select(ForeignEvidenceLink).ToArray()
            }).ToArray()
        };
    }

    private static PrivateCapitalFundEventLedgerRecordDto RemoveSelectedSupport(PrivateCapitalFundEventLedgerRecordDto record)
    {
        var links = WithoutAllocation(record.EvidenceLinks);
        return record with
        {
            EvidenceLinks = links,
            EvidenceLinkCount = links.Count,
            FundEvent = record.FundEvent with { EvidenceLinks = WithoutAllocation(record.FundEvent.EvidenceLinks) },
            CapitalAccountSubledgerEntries = record.CapitalAccountSubledgerEntries.Select(entry => entry with
            {
                EvidenceLinks = WithoutAllocation(entry.EvidenceLinks)
            }).ToArray(),
            LedgerImpacts = record.LedgerImpacts.Select(impact => impact with
            {
                EvidenceLinks = WithoutAllocation(impact.EvidenceLinks),
                Lines = impact.Lines.Select(line => line with
                {
                    EvidenceLink = line.EvidenceLink?.Contains("allocation", StringComparison.OrdinalIgnoreCase) == true
                        ? null : line.EvidenceLink
                }).ToArray()
            }).ToArray(),
            EvidenceCategories = record.EvidenceCategories.Where(category =>
                !category.CategoryId.Contains("allocation", StringComparison.OrdinalIgnoreCase)).ToArray(),
            ReportOutputs = record.ReportOutputs.Where(static output => output.ReportOutputType != "CapitalAccountStatement").ToArray()
        };
    }

    private static IReadOnlyList<string> WithoutAllocation(IEnumerable<string> links)
        => links.Where(static link => !link.Contains("allocation", StringComparison.OrdinalIgnoreCase)).ToArray();

    private static string ForeignEvidenceLink(string link)
        => link.Replace("/evidence/", "/evidence/foreign/", StringComparison.Ordinal);
}
