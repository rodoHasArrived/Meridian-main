// BLUEPRINT: tests for Phase 2 conflict resolution of the Security Master Passport Workbench.
// See docs/plans/security-master-passport-workbench.md (test plan: "Unit — ResolveSourceConflict").
//
// Intended RED state — blueprint types not implemented yet:
//   - ISecurityMasterWorkbenchCommandService.ResolveSourceConflictAsync
//   - ResolveSourceConflictRequest, SecurityMasterConflictResolutionDto
//   - ISecurityMasterConflictAuthorityPolicy / SecurityMasterConflictAuthorityDecision
//
// Core rule (blueprint D1 / Data Flow C): an operator may override the policy winner, but only by
// acknowledging the deviation and supplying a reason — the deviation IS the audited artifact, never
// a silent value swap.

using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.Workstation;
using Meridian.FinancialOperations.OperationsContinuity;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Meridian.Tests.SecurityMaster.Workbench;

public sealed class SecurityMasterWorkbenchConflictResolutionTests
{
    private static readonly Guid SecurityId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid ConflictId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    [Fact]
    public async Task Resolve_ChosenEqualsPolicy_NoDeviation()
    {
        // Accepting the policy winner records a clean resolution with no deviation.
        var harness = new Harness(policyWinner: "GoldenCopy");
        harness.EventStore.SeedVersion(SecurityId, version: 2);

        var request = new ResolveSourceConflictRequest(
            SecurityId: SecurityId,
            ConflictId: ConflictId,
            ExpectedVersion: 2,
            ChosenWinnerSource: "GoldenCopy", // == policy winner
            Actor: "ops.analyst",
            Reason: "Accept golden-copy.",
            AcknowledgePolicyDeviation: false);

        var result = await harness.Service.ResolveSourceConflictAsync(request);

        result.IsPolicyDeviation.Should().BeFalse();
        result.PolicyWinnerSource.Should().Be("GoldenCopy");
        result.ChosenWinnerSource.Should().Be("GoldenCopy");
        harness.EventStore.Appends.Should().ContainSingle("a resolution records a SourceConflictResolved event");
    }

    [Fact]
    public async Task Resolve_OverrideWithoutAck_Throws422()
    {
        // INVARIANT: choosing a winner that diverges from the policy without acknowledging the
        // deviation must be rejected (maps to HTTP 422 policy-deviation-unacknowledged).
        var harness = new Harness(policyWinner: "GoldenCopy");
        harness.EventStore.SeedVersion(SecurityId, version: 2);

        var request = new ResolveSourceConflictRequest(
            SecurityId: SecurityId,
            ConflictId: ConflictId,
            ExpectedVersion: 2,
            ChosenWinnerSource: "Polygon", // diverges from policy winner
            Actor: "ops.analyst",
            Reason: "Prefer Polygon.",
            AcknowledgePolicyDeviation: false); // not acknowledged

        var act = async () => await harness.Service.ResolveSourceConflictAsync(request);

        await act.Should().ThrowAsync<ArgumentException>();
        harness.EventStore.Appends.Should().BeEmpty("an unacknowledged deviation must not append any event");
    }

    [Fact]
    public async Task Resolve_OverrideWithAck_RecordsDeviationReason()
    {
        // An acknowledged override is allowed and records BOTH the policy winner and the operator's
        // chosen winner with the reason — the deviation is retained as the audited artifact.
        var harness = new Harness(policyWinner: "GoldenCopy");
        harness.EventStore.SeedVersion(SecurityId, version: 2);

        var request = new ResolveSourceConflictRequest(
            SecurityId: SecurityId,
            ConflictId: ConflictId,
            ExpectedVersion: 2,
            ChosenWinnerSource: "Polygon",
            Actor: "ops.analyst",
            Reason: "Polygon confirmed by issuer notice 2026-03-15.",
            AcknowledgePolicyDeviation: true);

        var result = await harness.Service.ResolveSourceConflictAsync(request);

        result.IsPolicyDeviation.Should().BeTrue();
        result.PolicyWinnerSource.Should().Be("GoldenCopy");
        result.ChosenWinnerSource.Should().Be("Polygon");
        result.Reason.Should().Contain("issuer notice");
    }

    // ---- Harness --------------------------------------------------------------------------------

    private sealed class Harness
    {
        public FakeSecurityMasterEventStore EventStore { get; } = new();
        public Mock<IOperationsContinuityWorkflowService> ApprovalWorkflow { get; } = new(MockBehavior.Loose);
        public Mock<ISecurityMasterWorkbenchQueryService> QueryService { get; } = new(MockBehavior.Loose);
        public Mock<ISecurityMasterConflictAuthorityPolicy> ConflictPolicy { get; } = new(MockBehavior.Loose);

        public ISecurityMasterWorkbenchCommandService Service { get; }

        public Harness(string policyWinner)
        {
            ConflictPolicy
                .Setup(p => p.Evaluate(
                    It.IsAny<SecurityMasterConflictAssessmentDto>(),
                    It.IsAny<IReadOnlyList<InstrumentPassportProviderConfidenceDto>>()))
                .Returns(new SecurityMasterConflictAuthorityDecision(
                    PolicyWinnerSource: policyWinner,
                    Rule: "golden-copy",
                    Rationale: "golden-copy rule designates winner",
                    IsBulkEligible: true));

            Service = new SecurityMasterWorkbenchCommandService(
                EventStore,
                ConflictPolicy.Object,
                QueryService.Object,
                ApprovalWorkflow.Object,
                Array.Empty<ISecurityMasterRevisionPublishedHandler>(),
                NullLogger<SecurityMasterWorkbenchCommandService>.Instance);
        }
    }
}
