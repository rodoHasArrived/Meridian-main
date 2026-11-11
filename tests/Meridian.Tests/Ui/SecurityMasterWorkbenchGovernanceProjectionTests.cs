using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.FixedIncome;
using Meridian.Contracts.SecurityMaster;
using Meridian.Tests.FixedIncome;
using NSubstitute;
using static Meridian.Tests.Ui.SecurityMasterWorkbenchScheduleBookHarness;

namespace Meridian.Tests.Ui;

public sealed class SecurityMasterWorkbenchGovernanceProjectionTests
{
    [Fact]
    public async Task TrustSnapshot_ExternalCashFlowSource_DoesNotRunTheCalculatedTermsReader()
    {
        var security = BondExerciseNoticeWatchTests.Bond();
        var cashFlows = Substitute.For<ISecurityMasterCashFlowService>();
        cashFlows.GetCashFlowSourceAsync(security.SecurityId, Arg.Any<CancellationToken>())
            .Returns(new SecurityCashFlowSourceDto(security.SecurityId, StructuredCashFlowSourceKind.ClientProvided, null, true, "operator", null));
        var snapshot = await CreateService(new StoredRowSecurityMasterQueryService(ProjectionOf(security)), cashFlowService: cashFlows)
            .GetTrustSnapshotAsync(security.SecurityId, null);
        snapshot!.CashFlowBlocker.Should().BeNull();
        await cashFlows.DidNotReceive().GetProjectionBlockerAsync(Arg.Any<SecurityDetailDto>(),
            Arg.Any<BondCalculationContext>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TrustSnapshot_CarriesTypedNegativeInterestBlocker_OnSelectedTermsAndTime_AndRefusesReady()
    {
        var security = BondExerciseNoticeWatchTests.Bond();
        var asOf = new DateTimeOffset(2026, 12, 1, 0, 0, 0, TimeSpan.Zero);
        var cashFlows = Substitute.For<ISecurityMasterCashFlowService>();
        cashFlows.GetCashFlowSourceAsync(security.SecurityId, Arg.Any<CancellationToken>())
            .Returns(new SecurityCashFlowSourceDto(security.SecurityId, StructuredCashFlowSourceKind.CalculatedBullet, asOf, false, null, null));
        cashFlows.GetProjectionBlockerAsync(Arg.Is<SecurityDetailDto>(selected => selected.SecurityId == security.SecurityId && selected.Version == security.Version),
            Arg.Is<BondCalculationContext>(context => context.AsOf == asOf && context.SecurityVersion == security.Version && context.KnownAt == null),
            Arg.Any<CancellationToken>())
            .Returns(SecurityInterestSupport.NegativeInterestBlocker);

        var snapshot = await CreateService(new StoredRowSecurityMasterQueryService(ProjectionOf(security)), now: asOf, cashFlowService: cashFlows)
            .GetTrustSnapshotAsync(security.SecurityId, null);

        snapshot!.CashFlowBlocker!.Code.Should().Be("NegativeInterestUnsupported");
        snapshot.CashFlowBlocker.Reason.Should().Be(SecurityInterestSupport.NegativeInterestReason);
        snapshot.ScheduleSummary!.Summary.Should().Contain("NegativeInterestUnsupported");
        var passport = snapshot.InstrumentPassport!;
        passport.OperationsWorkbench!.Panels.Single(panel => panel.PanelId == "terms").Items
            .Single(item => item.ItemId == "payment-schedule").Status.Should().Be("Review");
        passport.OperationsWorkbench.Readiness.Single(row => row.ReadinessId == "close").IsReady.Should().BeFalse();
        passport.OperationsWorkbench.Readiness.Single(row => row.ReadinessId == "ledger").IsReady.Should().BeFalse();
        await cashFlows.DidNotReceive().GetProjectionAsync(Arg.Any<SecurityDetailDto>(), Arg.Any<StructuredCashFlowScenario>(),
            Arg.Any<BondCalculationContext>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TrustSnapshot_ReportsEffectiveLatchedRequirements_AndIgnoredConfiguration()
    {
        var security = BondExerciseNoticeWatchTests.Bond();
        var requirements = Substitute.For<ISecurityMasterGovernanceRequirements>();
        requirements.Current.Returns(new SecurityMasterGovernanceSnapshot(
            SecurityMasterGovernanceProfile.Institutional,
            SecurityMasterGovernanceProfileSource.Latched,
            SecurityMasterGovernanceRequirementBasis.Profile,
            SecurityMasterGovernanceRequirementBasis.Profile,
            SecurityMasterGovernanceRequirementBasis.Profile,
            [SecurityMasterGovernanceRequirements.GovernanceProfileKey]));

        var snapshot = await CreateService(
            new StoredRowSecurityMasterQueryService(ProjectionOf(security)), governanceRequirements: requirements)
            .GetTrustSnapshotAsync(security.SecurityId, null);

        snapshot!.Governance.Should().NotBeNull();
        snapshot.Governance!.Profile.Should().Be("Institutional");
        snapshot.Governance.ProfileSource.Should().Be("Latched");
        snapshot.Governance.IsProfileLatched.Should().BeTrue();
        snapshot.Governance.RequireGovernedTermAmendments.Should().BeTrue();
        snapshot.Governance.RequireIndependentReviewer.Should().BeTrue();
        snapshot.Governance.RequireIndependentProfileApprover.Should().BeTrue();
        snapshot.Governance.ContradictedKeys.Should().Equal(SecurityMasterGovernanceRequirements.GovernanceProfileKey);
    }

    [Fact]
    public async Task TrustSnapshot_WithoutRequirements_DoesNotInventStandardProfile()
    {
        var security = BondExerciseNoticeWatchTests.Bond();
        var snapshot = await CreateService(new StoredRowSecurityMasterQueryService(ProjectionOf(security)))
            .GetTrustSnapshotAsync(security.SecurityId, null);
        snapshot!.Governance.Should().BeNull();
    }
}
