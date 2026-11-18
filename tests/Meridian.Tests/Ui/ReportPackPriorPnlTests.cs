using FluentAssertions;
using Meridian.Contracts.Workstation;
using Meridian.Reporting;
using Meridian.Ui.Shared.Services;
using Xunit;

namespace Meridian.Tests.Ui;

/// <summary>
/// Reported portfolio figures come only from the latest published or restated report version of
/// each period, and the P&amp;L slices compare with the previous published period.
/// </summary>
public sealed class ReportPackPriorPnlTests
{
    private static readonly DateTimeOffset Stamp = new(2026, 6, 3, 12, 0, 0, TimeSpan.Zero);

    // The same complete provenance chain the publication gate requires in ReportPackWorkflowServiceTests.
    private static ReportPackLineProvenanceDto Line(string lineKey, string value) =>
        new(lineKey, "portfolio", "run-1", $"evidence-{lineKey}-{value}",
            RunId: "run-1",
            LedgerEntryId: "ledger-entry-1",
            ReconciliationCaseId: "case-1",
            ReportValue: value,
            SourceSessionId: "provider-session-1",
            ReconciliationRunId: "recon-run-1",
            ProviderEventId: "provider-event-1",
            SecurityMasterId: "security-1",
            SecurityDefinitionId: "definition-1",
            ReconciliationOutcome: "matched",
            ApprovalId: "approval-1");

    private static IReadOnlyList<ReportPackEvidenceLinkDto> EvidenceLinks(IEnumerable<string> evidenceIds) =>
        evidenceIds.Select(static id => new ReportPackEvidenceLinkDto(id, "Line evidence", $"/evidence/{id}", "reporting"))
            .Concat(
            [
                new ReportPackEvidenceLinkDto("ledger-entry-1", "Ledger entry", "/evidence/ledger-entry-1", "ledger"),
                new ReportPackEvidenceLinkDto("provider-event-1", "Provider event", "/evidence/provider-event-1", "provider"),
                new ReportPackEvidenceLinkDto("security-1", "Security Master identity", "/evidence/security-1", "security-master"),
                new ReportPackEvidenceLinkDto("definition-1", "Security definition", "/evidence/definition-1", "security-master"),
                new ReportPackEvidenceLinkDto("case-1", "Reconciliation case", "/evidence/case-1", "reconciliation"),
                new ReportPackEvidenceLinkDto("recon-run-1", "Reconciliation run", "/evidence/recon-run-1", "reconciliation"),
                new ReportPackEvidenceLinkDto("approval-1", "Approval", "/evidence/approval-1", "approval"),
                new ReportPackEvidenceLinkDto("run-1", "Strategy run", "/evidence/run-1", "strategy"),
                new ReportPackEvidenceLinkDto("provider-session-1", "Provider source session", "/evidence/provider-session-1", "provider")
            ])
            .ToArray();

    private static ReportPackWorkflowRecordDto Record(
        string period,
        ReportPackWorkflowStateDto state,
        int version,
        decimal realized,
        decimal unrealized,
        string fund = "fund-alpha",
        string template = "monthly-pack") =>
        new(
            ReportId: Guid.NewGuid(),
            FundProfileId: fund,
            FundAccountId: "acct-main",
            Period: period,
            TemplateId: new VersionedReportTemplateIdDto(template, 1),
            State: state,
            Version: version,
            CreatedAt: Stamp,
            CreatedBy: "author",
            UpdatedAt: Stamp.AddMinutes(version),
            AuditTrail: [],
            Restatement: null,
            LineProvenance:
            [
                Line("portfolio.realized-pnl", realized.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                Line("portfolio.unrealized-pnl", unrealized.ToString(System.Globalization.CultureInfo.InvariantCulture))
            ]);

    [Fact]
    public void OnlyTheLatestPublishedOrRestatedVersionOfEachPeriodIsReported()
    {
        var draft = Record("2026-05", ReportPackWorkflowStateDto.Draft, 1, 999m, 0m);
        var inReview = Record("2026-05", ReportPackWorkflowStateDto.InReview, 1, 888m, 0m, template: "other-pack");
        var published = Record("2026-05", ReportPackWorkflowStateDto.Published, 1, 100m, 0m);
        var restated = published with { ReportId = Guid.NewGuid(), State = ReportPackWorkflowStateDto.Restated, Version = 2, LineProvenance = [Line("portfolio.realized-pnl", "120")] };

        var reported = ReportPackRunReadService.SelectReportedRecords([draft, inReview, published, restated]);

        reported.Should().ContainSingle().Which.Should().Be(restated);
    }

    [Fact]
    public void CutsUseTheLatestPeriodOnlyAndSlicesCompareWithThePreviousPublishedPeriod()
    {
        IReadOnlyList<ReportPackWorkflowRecordDto> records =
        [
            Record("2026-04", ReportPackWorkflowStateDto.Published, 1, 30m, 10m),
            Record("2026-05", ReportPackWorkflowStateDto.Published, 1, 42m, 18m),
            Record("2026-05", ReportPackWorkflowStateDto.Draft, 2, 5_000m, 0m)
        ];

        var cuts = ReportPackRunReadService.BuildPortfolioReportingCuts(records);
        var cut = cuts.Should().ContainSingle().Which;
        cut.TotalPnl.Should().Be(60m, "only the May published version counts, not April or the draft");

        var prior = ReportPackRunReadService.BuildPriorReportedPnl(records);
        prior.TotalPnl.Should().Be(40m);
        prior.Periods.Should().Equal("2026-04");

        var slices = ReportPackRunReadService.BuildPortfolioReportingPnlSlices(cuts, prior);
        slices.Should().HaveCount(4).And.OnlyContain(slice => slice.PriorTotalPnl == 40m && slice.PnlChange == 20m);
        slices[0].ReadinessSummary.Should().Contain("compared with the previous published period (2026-04");
        slices[0].Tags.Should().NotContain("no-prior");
        slices[0].VersionStamp.Should().EndWith(":prior-2");
    }

    [Fact]
    public void WithoutAPriorPublishedPeriodThePriorIsZeroAndSaysSo()
    {
        IReadOnlyList<ReportPackWorkflowRecordDto> records = [Record("2026-05", ReportPackWorkflowStateDto.Published, 1, 42m, 18m)];

        var prior = ReportPackRunReadService.BuildPriorReportedPnl(records);
        var slices = ReportPackRunReadService.BuildPortfolioReportingPnlSlices(ReportPackRunReadService.BuildPortfolioReportingCuts(records), prior);

        prior.SourceCount.Should().Be(0);
        slices.Should().OnlyContain(slice => slice.PriorTotalPnl == 0m && slice.PnlChange == 60m && slice.Tags.Contains("no-prior"));
        slices[0].ReadinessSummary.Should().Contain("No prior published report version is available for comparison.");
    }

    [Fact]
    public void AFundWithOnlyUnpublishedReportsHasNoCut()
    {
        ReportPackRunReadService.BuildPortfolioReportingCuts([Record("2026-05", ReportPackWorkflowStateDto.Approved, 1, 42m, 18m)])
            .Should().BeEmpty();
    }

    [Fact]
    public void ThePayloadComparesTwoPublishedPeriodsEndToEnd()
    {
        var workflow = new ReportPackWorkflowService();
        Publish(workflow, "2026-04", "30", "10");
        Publish(workflow, "2026-05", "42", "18");
        workflow.Create("fund-alpha", "acct-main", "2026-06", new VersionedReportTemplateIdDto("monthly-pack", 1), "author",
            [Line("portfolio.realized-pnl", "7777")]);

        var payload = new ReportPackRunReadService(new DefaultReportingTemplateCatalog(), workflowService: workflow).BuildPayload();

        payload.PortfolioCuts.Should().ContainSingle().Which.TotalPnl.Should().Be(60m);
        payload.PnlSlices.Should().NotBeNull().And.OnlyContain(slice => slice.PriorTotalPnl == 40m && slice.PnlChange == 20m);
    }

    private static void Publish(ReportPackWorkflowService workflow, string period, string realized, string unrealized)
    {
        var lines = new[] { Line("portfolio.realized-pnl", realized), Line("portfolio.unrealized-pnl", unrealized) };
        var created = workflow.Create("fund-alpha", "acct-main", period, new VersionedReportTemplateIdDto("monthly-pack", 1), "author", lines);
        workflow.Transition(created.ReportId, ReportPackWorkflowStateDto.InReview, "reviewer", "reviewer");
        workflow.Transition(created.ReportId, ReportPackWorkflowStateDto.Approved, "approver", "approver");
        workflow.Publish(created.ReportId, "publisher", "publisher", "controller", $"sha256:{period}", $"manifest-{period}", $"vault/{period}.json",
            EvidenceLinks(lines.Select(static line => line.EvidenceId)));
    }
}
