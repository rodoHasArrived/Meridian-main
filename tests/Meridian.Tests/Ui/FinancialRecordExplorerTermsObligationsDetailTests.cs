using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.Calendars;
using Meridian.Contracts.FixedIncome;
using Meridian.Contracts.SecurityMaster;
using Meridian.Instruments.AssetOperations;
using Meridian.Tests.FixedIncome;
using Meridian.Ui.Shared.Services;

namespace Meridian.Tests.Ui;

/// <summary>
/// The explorer's terms cell names the "next timeline event": the first timeline event outside the
/// Lifecycle lane. These tests pin which event that is for bonds, with and without a declared call
/// schedule, when a notice deadline or a scalar call date has already passed.
/// </summary>
[Trait("Category", "Unit")]
public sealed class FinancialRecordExplorerTermsObligationsDetailTests
{
    private static readonly IBusinessDayCalendarSource Calendars =
        new StaticBusinessDayCalendarSource([BusinessDayCalendars.WeekendsOnly]);

    [Fact]
    public async Task DeclaringBond_WithAnElapsedNoticeDeadline_DoesNotNameThePastDeadlineAsNext()
    {
        // As of 2026-12-20 the 2026-12-16 deadline has elapsed (MissingEvidence) and sorts first.
        var detail = await ProjectAsync(DeclaringBond(), new DateTimeOffset(2026, 12, 20, 0, 0, 0, TimeSpan.Zero));
        detail.TermsObligationsTimeline!.Events.First(static row => row.EventLane != "Lifecycle")
            .Should().Match<AssetTermsObligationTimelineEventDto>(row =>
                row.EventKind == "IssuerCallNoticeDeadline" && row.Status == "MissingEvidence",
                "the elapsed deadline is the first non-lifecycle row of the timeline");

        var text = FinancialRecordExplorerReadService.BuildTermsObligationsDetail(detail);

        text.Should().Contain("Next timeline event is CorporateActionCheckpoint due 2027-01-01; status Projected.");
        text.Should().NotContain("IssuerCallNoticeDeadline");
    }

    [Fact]
    public async Task DeclaringBond_WithAnElapsedPutNoticeDeadline_DoesNotNameItAsNextEither()
    {
        var security = BondExerciseNoticeWatchTests.Bond(puts: [new Dictionary<string, object?>
        {
            ["putDate"] = "2027-01-15",
            ["putPrice"] = 100m,
            ["exerciseStyle"] = "European",
            ["priceBasis"] = "PercentOfPar",
            ["minimumNoticeDays"] = 30,
            ["noticeDayType"] = "Calendar"
        }]);

        var detail = await ProjectAsync(security, new DateTimeOffset(2026, 12, 20, 0, 0, 0, TimeSpan.Zero));

        detail.TermsObligationsTimeline!.Events.Should().Contain(row =>
            row.EventKind == "HolderPutNoticeDeadline" && row.Status == "MissingEvidence", "the elapsed deadline stays on the timeline");
        FinancialRecordExplorerReadService.BuildTermsObligationsDetail(detail)
            .Should().Contain("Next timeline event is CorporateActionCheckpoint due 2027-01-01; status Projected.");
    }

    [Fact]
    public async Task DeclaringBond_WithAnOpenNoticeDeadline_NamesItAsNext()
    {
        var detail = await ProjectAsync(DeclaringBond(), new DateTimeOffset(2026, 12, 1, 0, 0, 0, TimeSpan.Zero));

        FinancialRecordExplorerReadService.BuildTermsObligationsDetail(detail)
            .Should().Contain("Next timeline event is IssuerCallNoticeDeadline due 2026-12-16; status Projected.");
    }

    [Fact]
    public async Task UndeclaredBond_WithAPastScalarCallDate_NamesTheNextCheckpoint_AsBefore()
    {
        // The scalar review is not raised for a call date already past, so nothing past-dated leads.
        var security = BondExerciseNoticeWatchTests.Bond(extra: new() { ["callDate"] = "2026-12-16", ["isCallable"] = true });

        var detail = await ProjectAsync(security, new DateTimeOffset(2026, 12, 20, 0, 0, 0, TimeSpan.Zero));

        FinancialRecordExplorerReadService.BuildTermsObligationsDetail(detail)
            .Should().Contain("Next timeline event is CorporateActionCheckpoint due 2027-01-01; status Projected.");
    }

    [Fact]
    public void PastDueEventOfAnotherKind_StillLeads_AsBefore()
    {
        // Only elapsed declared notice deadlines are passed over. Any other past-due row keeps leading.
        var id = Guid.Parse("8a0b1c2d-0000-4000-8000-0000000000f5");
        AssetTermsObligationTimelineEventDto Event(string kind, DateOnly date, string status)
            => new(Guid.NewGuid(), id, kind, "Obligations", date, null, null, null, null, null, null, "USD", status,
                "SecurityMaster", id.ToString("D"), null, null, kind);
        var timeline = new AssetTermsObligationsTimelineDto(id, new DateOnly(2026, 12, 20), "test", "ReviewRequired",
            [Event("BorrowerNotice", new DateOnly(2026, 12, 10), "MissingEvidence"), Event("CovenantTest", new DateOnly(2027, 1, 1), "Projected")],
            [], DateTimeOffset.UnixEpoch, "SecurityMaster", id.ToString("D"));
        var subject = new AssetOperationSubjectDto(id, "DirectLoan", "Loan", null, []);
        var readiness = new AssetOperationsReadinessDto(id, "ReviewRequired", [], [], [], [], DateTimeOffset.UnixEpoch, "SecurityMaster", null);
        var detail = new AssetOperationsDetailDto(subject, [], [], [], [], [], [], [], [], readiness, [])
        {
            TermsObligationsTimeline = timeline
        };

        FinancialRecordExplorerReadService.BuildTermsObligationsDetail(detail)
            .Should().Be("Next timeline event is BorrowerNotice due 2026-12-10; status MissingEvidence.");
    }

    private static SecurityDetailDto DeclaringBond() => BondExerciseNoticeWatchTests.Bond([new Dictionary<string, object?>
    {
        ["callDate"] = "2027-01-15",
        ["callPrice"] = 101m,
        ["exerciseStyle"] = "European",
        ["priceBasis"] = "PercentOfPar",
        ["minimumNoticeDays"] = 30,
        ["maximumNoticeDays"] = 60,
        ["noticeDayType"] = "Calendar"
    }]);

    private static Task<AssetOperationsDetailDto> ProjectAsync(SecurityDetailDto security, DateTimeOffset asOf)
        => new AssetObligationProjectionService(calendars: Calendars).ProjectFromSecurityMasterAsync(
            security, new BondCalculationContext(asOf),
            new StructuredCashFlowProjectionDto(security.SecurityId, StructuredCashFlowSourceKind.CalculatedBullet,
                StructuredCashFlowScenario.Base, asOf, []));
}
