using FluentAssertions;
using Meridian.Contracts.Ledger;
using Meridian.FinancialOperations.PrivateCapital;

namespace Meridian.Tests.FinancialOperations.PrivateCapital;

/// <summary>
/// Direct branch coverage for the pure readiness decision cascade. Previously this builder was only
/// exercised transitively through the private-capital command-center/close services, so the precise
/// gate ordering (block → evidence → approval → posting → report → published → ready) and route
/// fallbacks were untested.
/// </summary>
public sealed class PrivateCapitalFundEventLedgerReadinessBuilderTests
{
    private const string ActivityRoute = "/activity";
    private const string EvidenceRoute = "/evidence";
    private const string ApprovalRoute = "/approval";
    private const string ReportRoute = "/report";

    // Baseline inputs satisfy every readiness gate, so each test flips exactly one input and asserts
    // the single branch it isolates.
    private static PrivateCapitalFundEventLedgerReadinessProjection Build(
        ManualJournalEntryStatusDto approvalState = ManualJournalEntryStatusDto.Approved,
        int evidenceLinkCount = 2,
        bool isPostingReady = true,
        bool isReportReady = true,
        bool isPublished = false,
        int reportOutputCount = 1,
        bool hasCriticalValidationIssues = false,
        string? approvalRoute = ApprovalRoute,
        string? primaryReportRoute = ReportRoute)
        => PrivateCapitalFundEventLedgerReadinessBuilder.Build(
            approvalState,
            evidenceLinkCount,
            isPostingReady,
            isReportReady,
            isPublished,
            reportOutputCount,
            hasCriticalValidationIssues,
            ActivityRoute,
            EvidenceRoute,
            approvalRoute,
            primaryReportRoute);

    [Fact]
    public void Build_WhenAllGatesSatisfied_IsReady()
    {
        var projection = Build();

        projection.Readiness.Should().Be(PrivateCapitalFundEventLedgerReadinessDto.Ready);
        projection.Label.Should().Be("Ready");
        projection.NextActionRoute.Should().Be(ReportRoute);
    }

    [Fact]
    public void Build_WhenPublished_ReportsPublishedAndPrefersReportRoute()
    {
        var projection = Build(isPublished: true);

        projection.Readiness.Should().Be(PrivateCapitalFundEventLedgerReadinessDto.Published);
        projection.Label.Should().Be("Published");
        projection.NextActionRoute.Should().Be(ReportRoute);
    }

    [Fact]
    public void Build_WhenPublishedWithoutReportRoute_FallsBackToEvidenceRoute()
    {
        var projection = Build(isPublished: true, primaryReportRoute: null);

        projection.Readiness.Should().Be(PrivateCapitalFundEventLedgerReadinessDto.Published);
        projection.NextActionRoute.Should().Be(EvidenceRoute);
    }

    [Fact]
    public void Build_WhenReportNotReadyWithOutput_IsReportReview()
    {
        var projection = Build(isReportReady: false, reportOutputCount: 1);

        projection.Readiness.Should().Be(PrivateCapitalFundEventLedgerReadinessDto.ReportReview);
        projection.Label.Should().Be("Report review");
    }

    [Fact]
    public void Build_WhenReportNotReadyWithoutOutput_LabelsReportOutputMissing()
    {
        var projection = Build(isReportReady: false, reportOutputCount: 0);

        projection.Readiness.Should().Be(PrivateCapitalFundEventLedgerReadinessDto.ReportReview);
        projection.Label.Should().Be("Report output missing");
    }

    [Fact]
    public void Build_WhenPostingNotReady_IsPostingReviewBeforeReportReview()
    {
        // Posting readiness is evaluated before report readiness, so an unready GL impact wins even
        // when the report is also not ready.
        var projection = Build(isPostingReady: false, isReportReady: false, reportOutputCount: 0);

        projection.Readiness.Should().Be(PrivateCapitalFundEventLedgerReadinessDto.PostingReview);
        projection.Label.Should().Be("Posting review");
        projection.NextActionRoute.Should().Be(ActivityRoute);
    }

    [Fact]
    public void Build_WhenApprovalIsDraft_IsApprovalPending()
    {
        var projection = Build(approvalState: ManualJournalEntryStatusDto.Draft);

        projection.Readiness.Should().Be(PrivateCapitalFundEventLedgerReadinessDto.ApprovalPending);
        projection.Label.Should().Be("Approval pending");
        projection.NextActionRoute.Should().Be(ActivityRoute);
    }

    [Fact]
    public void Build_WhenEvidenceMissing_IsEvidenceMissing()
    {
        var projection = Build(evidenceLinkCount: 0);

        projection.Readiness.Should().Be(PrivateCapitalFundEventLedgerReadinessDto.EvidenceMissing);
        projection.Label.Should().Be("Evidence missing");
        projection.NextActionRoute.Should().Be(EvidenceRoute);
    }

    [Fact]
    public void Build_WhenApprovalRejected_IsBlockedWithRejectedLabel()
    {
        var projection = Build(approvalState: ManualJournalEntryStatusDto.Rejected);

        projection.Readiness.Should().Be(PrivateCapitalFundEventLedgerReadinessDto.Blocked);
        projection.Label.Should().Be("Approval rejected");
        projection.NextActionRoute.Should().Be(ApprovalRoute);
    }

    [Fact]
    public void Build_WhenApprovalNeedsFix_IsBlocked()
    {
        var projection = Build(approvalState: ManualJournalEntryStatusDto.NeedsFix);

        projection.Readiness.Should().Be(PrivateCapitalFundEventLedgerReadinessDto.Blocked);
        projection.Label.Should().Be("Blocked");
    }

    [Fact]
    public void Build_WhenCriticalValidationIssues_IsBlockedEvenWhenApproved()
    {
        var projection = Build(hasCriticalValidationIssues: true);

        projection.Readiness.Should().Be(PrivateCapitalFundEventLedgerReadinessDto.Blocked);
        projection.Label.Should().Be("Blocked");
    }

    [Fact]
    public void Build_WhenBlockedWithoutApprovalRoute_FallsBackToActivityRoute()
    {
        var projection = Build(approvalState: ManualJournalEntryStatusDto.Rejected, approvalRoute: null);

        projection.Readiness.Should().Be(PrivateCapitalFundEventLedgerReadinessDto.Blocked);
        projection.NextActionRoute.Should().Be(ActivityRoute);
    }

    [Fact]
    public void Build_BlockedTakesPrecedenceOverMissingEvidence()
    {
        // A rejected approval must surface as Blocked even when evidence is also missing.
        var projection = Build(approvalState: ManualJournalEntryStatusDto.Rejected, evidenceLinkCount: 0);

        projection.Readiness.Should().Be(PrivateCapitalFundEventLedgerReadinessDto.Blocked);
    }
}
