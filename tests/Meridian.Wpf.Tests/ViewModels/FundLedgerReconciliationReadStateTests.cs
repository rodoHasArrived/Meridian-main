#if WINDOWS
using System.IO;
using FluentAssertions;
using Meridian.Contracts.Workstation;
using Meridian.Wpf.Models;
using Meridian.Wpf.Tests.Support;
using Meridian.Wpf.ViewModels;
using Meridian.Wpf.Workstation.Models;

namespace Meridian.Wpf.Tests.ViewModels;

public sealed class FundLedgerReconciliationReadStateTests
{
    [Fact]
    public void BuildReconciliationReadPresentation_WhenDetailsUnavailable_DoesNotClaimVerifiedAbsence()
    {
        var snapshot = BuildSnapshot(
            FundReconciliationReadAvailability.Unavailable,
            knownRunCount: 1,
            missingRunCount: 0,
            unavailableRunCount: 1);

        var presentation = FundLedgerViewModel.BuildReconciliationReadPresentation(snapshot);

        presentation.StatusText.Should().Contain("unavailable");
        presentation.StatusText.Should().Contain("cannot be verified");
        presentation.StatusText.Should().NotContain("No reconciliation runs");
        presentation.EmptyStateText.Should().Contain("do not treat the empty list as verified absence");
        presentation.State.Kind.Should().Be(WorkstationStateKind.Error);
        presentation.ShowState.Should().BeTrue();
        presentation.OpenBreaksText.Should().Be("-");
        presentation.SecurityIssuesText.Should().Be("-");
        presentation.ReconciliationRunsText.Should().Be("0/1 loaded");

        var overview = FundLedgerViewModel.BuildReconciliationOverviewClause(snapshot);
        overview.Should().Contain("counts unavailable");
        overview.Should().NotContain("0 reconciliation run(s)");
    }

    [Fact]
    public void BuildReconciliationReadPresentation_WhenRecordMissing_DistinguishesMissingFromOutage()
    {
        var snapshot = BuildSnapshot(
            FundReconciliationReadAvailability.Available,
            knownRunCount: 1,
            missingRunCount: 1,
            unavailableRunCount: 0);

        var presentation = FundLedgerViewModel.BuildReconciliationReadPresentation(snapshot);

        presentation.StatusText.Should().Contain("do not yet have a reconciliation record");
        presentation.StatusText.Should().NotContain("unavailable");
        presentation.State.Kind.Should().Be(WorkstationStateKind.Empty);
        presentation.ShowState.Should().BeTrue();
    }

    [Fact]
    public void BuildReconciliationReadPresentation_WhenSomeReadsFail_ReportsDegradedState()
    {
        var snapshot = BuildSnapshot(
            FundReconciliationReadAvailability.Degraded,
            knownRunCount: 2,
            missingRunCount: 0,
            unavailableRunCount: 1,
            loadedRunCount: 1);

        var presentation = FundLedgerViewModel.BuildReconciliationReadPresentation(snapshot);

        presentation.StatusText.Should().Contain("degraded");
        presentation.StatusText.Should().Contain("1 detail read(s) failed");
        presentation.State.Kind.Should().Be(WorkstationStateKind.Stale);
        presentation.ShowState.Should().BeTrue();
        presentation.OpenBreaksText.Should().Be("0+");
        presentation.SecurityIssuesText.Should().Be("0+");
    }

    [Fact]
    public void BuildReconciliationReadPresentation_WhenNoKnownRuns_UsesVerifiedEmptyState()
    {
        var snapshot = BuildSnapshot(
            FundReconciliationReadAvailability.Available,
            knownRunCount: 0,
            missingRunCount: 0,
            unavailableRunCount: 0);

        var presentation = FundLedgerViewModel.BuildReconciliationReadPresentation(snapshot);

        presentation.StatusText.Should().Be("No strategy runs are recorded for this fund yet.");
        presentation.ShowState.Should().BeFalse();
    }

    [Fact]
    public void BuildReconciliationReadPresentation_WhenSupportingReadsFail_DoesNotShowVerifiedQueueOrCalibration()
    {
        var snapshot = BuildSnapshot(
            FundReconciliationReadAvailability.Available,
            knownRunCount: 1,
            missingRunCount: 0,
            unavailableRunCount: 0,
            loadedRunCount: 1,
            breakQueueReadAvailable: false,
            calibrationReadAvailable: false);

        var presentation = FundLedgerViewModel.BuildReconciliationReadPresentation(snapshot);

        presentation.StatusText.Should().Contain("degraded");
        presentation.StatusText.Should().Contain("break queue read failed");
        presentation.StatusText.Should().Contain("calibration read failed");
        presentation.InReviewBreaksText.Should().Be("-");
        presentation.BreakQueueEmptyStateText.Should().Contain("unavailable");
        presentation.BreakQueueEmptyStateText.Should().Contain("do not treat this empty list as a verified zero-break queue");
        FundLedgerViewModel.BuildReconciliationOverviewClause(snapshot)
            .Should().Contain("break queue unavailable")
            .And.Contain("calibration posture unavailable");
    }

    [Fact]
    public void FundLedgerPage_BindsReconciliationAvailabilityState()
    {
        var xaml = File.ReadAllText(RunMatUiAutomationFacade.GetRepoFilePath(
            @"src\Meridian.Wpf\Views\FundLedgerPage.xaml"));

        xaml.Should().Contain("State=\"{Binding ReconciliationSection.ReadAvailabilityState}\"");
        xaml.Should().Contain("Visibility=\"{Binding ReconciliationSection.HasReadAvailabilityNotice");
        xaml.Should().Contain("AutomationProperties.AutomationId=\"FundReconciliationAvailabilityState\"");
    }

    private static FundReconciliationWorkbenchSnapshot BuildSnapshot(
        FundReconciliationReadAvailability availability,
        int knownRunCount,
        int missingRunCount,
        int unavailableRunCount,
        int loadedRunCount = 0,
        bool breakQueueReadAvailable = true,
        bool calibrationReadAvailable = true)
        => new(
            Summary: new ReconciliationSummary(
                RunCount: loadedRunCount,
                OpenBreakCount: 0,
                BreakAmountTotal: 0m,
                RecentRuns: []),
            CalibrationSummary: null,
            CalibrationProfiles: [],
            BreakQueueItems: [],
            RunRows: [],
            RefreshedAt: new DateTimeOffset(2026, 8, 25, 0, 0, 0, TimeSpan.Zero),
            InReviewBreakCount: 0,
            ReadAvailability: availability,
            KnownRunCount: knownRunCount,
            MissingRunCount: missingRunCount,
            UnavailableRunCount: unavailableRunCount,
            BreakQueueReadAvailable: breakQueueReadAvailable,
            CalibrationReadAvailable: calibrationReadAvailable);
}
#endif
