using FluentAssertions;
using Meridian.Contracts.SecurityMaster;
using Xunit;

namespace Meridian.Tests.SecurityMaster;

/// <summary>
/// Exit-criterion coverage for the corporate-action accounting lane policy: ReadyForApproval,
/// approval, and posting are refused with typed problem codes whenever the exact-version binding,
/// balanced journals, policy coverage, lot resolution, exact scope, or the required maker-checker
/// approval is missing. The application service and the durable store share these predicates.
/// </summary>
public sealed class CorporateActionCaseAccountingPolicyTests
{
    private static readonly Guid CaseId = Guid.Parse("7f1f2ee0-8f5f-4bfb-9a5a-0a53b3f0aa11");
    private static readonly Guid ProjectionId = Guid.Parse("64b3e7e5-9be6-4890-9df1-53a7f6f3fd22");
    private static readonly Guid ApprovalId = Guid.Parse("b1a6ff36-6a25-4b5f-8e51-b41d4d1f4c33");
    private static readonly Guid LedgerBookId = Guid.Parse("0e6f9d5c-58fb-4c76-95a3-3bb3b7a11e44");
    private static readonly Guid PeriodId = Guid.Parse("d1c2ba9f-19a4-4a58-8f1d-5d3a2f9be055");

    [Fact]
    public void ReadyForApproval_RefusedWithoutAnyBinding()
    {
        var act = () => CorporateActionCaseAccountingPolicy.EnsureBindingSupportsReadyForApproval(
            null, Case(CorporateActionCaseStates.AccountingReview, version: 5));

        act.Should().Throw<CorporateActionOperationException>()
            .Which.Code.Should().Be(CorporateActionProblemCodes.ProjectionStale);
    }

    [Fact]
    public void ReadyForApproval_RefusedWhenBindingIsBoundToASupersededCaseVersion()
    {
        var act = () => CorporateActionCaseAccountingPolicy.EnsureBindingSupportsReadyForApproval(
            Projection() with { BoundCaseVersion = 4 },
            Case(CorporateActionCaseStates.AccountingReview, version: 5));

        act.Should().Throw<CorporateActionOperationException>()
            .Which.Code.Should().Be(CorporateActionProblemCodes.ProjectionStale);
    }

    [Fact]
    public void ReadyForApproval_RefusedWhenJournalsAreNotBalanced()
    {
        var act = () => CorporateActionCaseAccountingPolicy.EnsureBindingSupportsReadyForApproval(
            Projection() with { TotalCredits = 99.99m },
            Case(CorporateActionCaseStates.AccountingReview, version: 5));

        act.Should().Throw<CorporateActionOperationException>()
            .Which.Code.Should().Be(CorporateActionProblemCodes.JournalUnbalanced);
    }

    [Fact]
    public void ReadyForApproval_RefusedWithoutApprovedPolicyCoverage()
    {
        var act = () => CorporateActionCaseAccountingPolicy.EnsureBindingSupportsReadyForApproval(
            Projection() with { SelectedRuleId = " " },
            Case(CorporateActionCaseStates.AccountingReview, version: 5));

        act.Should().Throw<CorporateActionOperationException>()
            .Which.Code.Should().Be(CorporateActionProblemCodes.PolicyMissing);
    }

    [Fact]
    public void ReadyForApproval_RefusedWithoutAuthoritativeLotResolution()
    {
        var act = () => CorporateActionCaseAccountingPolicy.EnsureBindingSupportsReadyForApproval(
            Projection() with { HasAuthoritativeLotResolution = false },
            Case(CorporateActionCaseStates.AccountingReview, version: 5));

        act.Should().Throw<CorporateActionOperationException>()
            .Which.Code.Should().Be(CorporateActionProblemCodes.AllocationInvalid);
    }

    [Fact]
    public void ReadyForApproval_RefusedWithoutExactAccountingScopeOnTheCase()
    {
        var scopedCase = Case(CorporateActionCaseStates.AccountingReview, version: 5);
        var act = () => CorporateActionCaseAccountingPolicy.EnsureBindingSupportsReadyForApproval(
            Projection(),
            scopedCase with { Scope = scopedCase.Scope with { PeriodId = null } });

        act.Should().Throw<CorporateActionScopeMismatchException>();
    }

    [Fact]
    public void ReadyForApproval_RefusedWhenBindingNamesADifferentLedgerBookThanTheCase()
    {
        var act = () => CorporateActionCaseAccountingPolicy.EnsureBindingSupportsReadyForApproval(
            Projection() with { LedgerBookId = Guid.NewGuid() },
            Case(CorporateActionCaseStates.AccountingReview, version: 5));

        act.Should().Throw<CorporateActionScopeMismatchException>();
    }

    [Fact]
    public void ReadyForApproval_AdmittedOnACurrentBalancedPolicyCoveredBinding()
    {
        var act = () => CorporateActionCaseAccountingPolicy.EnsureBindingSupportsReadyForApproval(
            Projection(),
            Case(CorporateActionCaseStates.AccountingReview, version: 5));

        act.Should().NotThrow();
    }

    [Fact]
    public void MakerChecker_RefusesThePreparerAsApprover_CaseInsensitively()
    {
        var act = () => CorporateActionCaseAccountingPolicy.EnsureIndependentOfPreparer(
            Projection(), "Fund-Accountant");

        act.Should().Throw<CorporateActionOperationException>()
            .Which.Code.Should().Be(CorporateActionProblemCodes.MakerCheckerRequired);
    }

    [Fact]
    public void Posting_RefusedWithoutAnyApproval()
    {
        var act = () => CorporateActionCaseAccountingPolicy.EnsureApprovalAuthorizesPosting(
            null, Projection(), "controller");

        act.Should().Throw<CorporateActionOperationException>()
            .Which.Code.Should().Be(CorporateActionProblemCodes.MakerCheckerRequired);
    }

    [Fact]
    public void Posting_RefusedWhenTheApprovalWasVoidedByAGovernedReturn()
    {
        var act = () => CorporateActionCaseAccountingPolicy.EnsureApprovalAuthorizesPosting(
            Approval() with { VoidedAtUtc = DateTimeOffset.UtcNow, VoidedBy = "fund-accountant" },
            Projection(),
            "controller");

        act.Should().Throw<CorporateActionOperationException>()
            .Which.Code.Should().Be(CorporateActionProblemCodes.MakerCheckerRequired);
    }

    [Fact]
    public void Posting_RefusedWhenTheApprovalBindsASupersededProjection()
    {
        var act = () => CorporateActionCaseAccountingPolicy.EnsureApprovalAuthorizesPosting(
            Approval() with { ProjectionId = Guid.NewGuid() },
            Projection(),
            "controller");

        act.Should().Throw<CorporateActionOperationException>()
            .Which.Code.Should().Be(CorporateActionProblemCodes.MakerCheckerRequired);
    }

    [Fact]
    public void Posting_RefusedWhenThePostingOperatorIsNotTheApprover()
    {
        var act = () => CorporateActionCaseAccountingPolicy.EnsureApprovalAuthorizesPosting(
            Approval(), Projection(), "someone-else");

        act.Should().Throw<CorporateActionOperationException>()
            .Which.Code.Should().Be(CorporateActionProblemCodes.MakerCheckerRequired);
    }

    [Fact]
    public void Posting_RefusedWhenTheApprovingPosterIsAlsoThePreparer()
    {
        var act = () => CorporateActionCaseAccountingPolicy.EnsureApprovalAuthorizesPosting(
            Approval() with { ApprovedBy = "fund-accountant" },
            Projection(),
            "fund-accountant");

        act.Should().Throw<CorporateActionOperationException>()
            .Which.Code.Should().Be(CorporateActionProblemCodes.MakerCheckerRequired);
    }

    [Fact]
    public void Posting_AdmittedForTheIndependentApprovingOperator()
    {
        var act = () => CorporateActionCaseAccountingPolicy.EnsureApprovalAuthorizesPosting(
            Approval(), Projection(), "controller");

        act.Should().NotThrow();
    }

    [Theory]
    [InlineData(CorporateActionCaseStates.Detected)]
    [InlineData(CorporateActionCaseStates.TermsConfirmed)]
    [InlineData(CorporateActionCaseStates.ReadyForApproval)]
    [InlineData(CorporateActionCaseStates.Approved)]
    [InlineData(CorporateActionCaseStates.Posted)]
    public void AttachIsAllowedOnlyInAccountingReview(string state)
    {
        var act = () => CorporateActionCaseAccountingPolicy.EnsureProjectionAttachable(
            Case(state, version: 5));

        act.Should().Throw<CorporateActionStateConflictException>();
        var admit = () => CorporateActionCaseAccountingPolicy.EnsureProjectionAttachable(
            Case(CorporateActionCaseStates.AccountingReview, version: 5));
        admit.Should().NotThrow();
    }

    [Theory]
    [InlineData(CorporateActionCaseStates.AccountingReview)]
    [InlineData(CorporateActionCaseStates.Approved)]
    [InlineData(CorporateActionCaseStates.Posted)]
    public void ApprovalIsAllowedOnlyInReadyForApproval(string state)
    {
        var act = () => CorporateActionCaseAccountingPolicy.EnsureApprovable(Case(state, version: 5));

        act.Should().Throw<CorporateActionStateConflictException>();
        var admit = () => CorporateActionCaseAccountingPolicy.EnsureApprovable(
            Case(CorporateActionCaseStates.ReadyForApproval, version: 5));
        admit.Should().NotThrow();
    }

    [Theory]
    [InlineData(CorporateActionCaseStates.AccountingReview)]
    [InlineData(CorporateActionCaseStates.ReadyForApproval)]
    [InlineData(CorporateActionCaseStates.Posted)]
    public void PostingIsAllowedOnlyInApproved(string state)
    {
        var act = () => CorporateActionCaseAccountingPolicy.EnsurePostable(Case(state, version: 5));

        act.Should().Throw<CorporateActionStateConflictException>();
        var admit = () => CorporateActionCaseAccountingPolicy.EnsurePostable(
            Case(CorporateActionCaseStates.Approved, version: 5));
        admit.Should().NotThrow();
    }

    [Fact]
    public void ScopeAssertion_MustMatchTheStoredScopeExactly()
    {
        var stored = Scope();

        var mismatch = () => CorporateActionCaseAccountingPolicy.EnsureScopeAssertionMatches(
            stored with { LedgerBookId = Guid.NewGuid().ToString("D") }, stored);
        mismatch.Should().Throw<CorporateActionScopeMismatchException>();

        var missing = () => CorporateActionCaseAccountingPolicy.EnsureScopeAssertionMatches(null, stored);
        missing.Should().Throw<CorporateActionScopeMismatchException>();

        var match = () => CorporateActionCaseAccountingPolicy.EnsureScopeAssertionMatches(stored, stored);
        match.Should().NotThrow();
    }

    [Fact]
    public void LifecyclePolicy_GovernedReturnFromApprovedAndRestatementFromPostedExist()
    {
        CorporateActionCaseTransitionPolicy.CanTransition(
                CorporateActionCaseStates.Approved,
                CorporateActionCaseStates.AccountingReview)
            .Should().BeTrue("an approved-but-unposted case may be withdrawn for rework, voiding its approval");
        CorporateActionCaseTransitionPolicy.CanTransition(
                CorporateActionCaseStates.Posted,
                CorporateActionCaseStates.RestatementRequired)
            .Should().BeTrue("posted journals stay immutable; corrections open the governed restatement lane");
    }

    private static CorporateActionCaseScopeDto Scope() =>
        new(
            "tenant-a",
            "company-a",
            FundProfileId: "fund-alpha",
            LedgerBookId: LedgerBookId.ToString("D"),
            PeriodId: PeriodId.ToString("D"),
            AccountingBasis: "GAAP",
            FunctionalCurrency: "USD");

    private static CorporateActionProcessingCaseDto Case(string state, long version) =>
        new(
            CaseId,
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Scope(),
            state,
            version,
            MethodologyProfileId: null,
            AssignedTo: null,
            BlockedReason: null,
            "acceptor",
            DateTimeOffset.UtcNow.AddDays(-1),
            "acceptor",
            DateTimeOffset.UtcNow);

    private static CorporateActionCaseAccountingProjectionDto Projection() =>
        new(
            ProjectionId,
            CaseId,
            BoundCaseVersion: 5,
            AccountingEventId: Guid.NewGuid(),
            AccountingEventVersion: 1,
            SpineVersion: 3,
            ProjectionInputHash: new string('a', 64),
            PostingIntentHash: new string('b', 64),
            PostingIdempotencyKey: $"corporate-action-posting/v1:{new string('c', 64)}",
            DraftedCandidateFingerprint: new string('d', 64),
            PolicyDecisionId: Guid.NewGuid(),
            PolicyDecisionVersion: 2,
            RulePackId: "corp-act-pack",
            RulePackVersion: "7",
            SelectedRuleId: "cash-dividend",
            SelectedRuleVersion: "3",
            LedgerBookId: LedgerBookId,
            PeriodId: PeriodId,
            ExpectedPeriodVersion: 4,
            AccountingBasis: "Gaap",
            FundProfileId: "fund-alpha",
            Currency: "USD",
            EffectiveDate: new DateOnly(2026, 8, 14),
            TotalDebits: 120.50m,
            TotalCredits: 120.50m,
            LotSnapshotId: Guid.NewGuid(),
            LotSnapshotVersion: 9,
            HasAuthoritativeLotResolution: true,
            PreparedBy: "fund-accountant",
            PreparedAtUtc: DateTimeOffset.UtcNow.AddMinutes(-30),
            IsCurrent: true);

    private static CorporateActionCaseAccountingApprovalDto Approval() =>
        new(
            ApprovalId,
            CaseId,
            ProjectionId,
            BoundCaseVersion: 6,
            ApprovedBy: "controller",
            ApprovedAtUtc: DateTimeOffset.UtcNow.AddMinutes(-5),
            Reason: "Reviewed the exact-version projection and evidence.",
            EvidenceReference: "document://approvals/corp-act-1",
            EvidenceHash: new string('e', 64));
}
