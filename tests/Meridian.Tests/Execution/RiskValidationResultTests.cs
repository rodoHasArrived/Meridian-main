using FluentAssertions;
using Meridian.Execution;
using Meridian.Execution.Sdk;

namespace Meridian.Tests.Execution;

/// <summary>
/// The decision has to follow from the fields the OMS actually routes on. <c>Decision</c> is
/// derived, never stored, so these pin that derivation and the snapshotting that stops a caller
/// changing a result after it was decided.
/// </summary>
public sealed class RiskValidationResultTests
{
    private static RiskViolation Violation(
        RiskRuleSeverity severity,
        bool requiresAcknowledgement = false) =>
        new(
            RuleName: "Rule",
            Severity: severity,
            Code: "CODE",
            Message: "message",
            RequiresAcknowledgement: requiresAcknowledgement);

    [Theory]
    [InlineData(RiskRuleSeverity.Error)]
    [InlineData(RiskRuleSeverity.Critical)]
    public void Decision_WithABlockingViolation_IsRejected(RiskRuleSeverity severity)
    {
        var result = RiskValidationResult.Rejected("message") with
        {
            Violations = [Violation(severity)],
        };

        result.Decision.Should().Be(RiskDecisionKind.Rejected);
        result.IsApproved.Should().BeFalse();
        result.BlockingViolation.Should().NotBeNull();
        result.BlockingViolation!.Code.Should().Be("CODE");
    }

    [Theory]
    [InlineData(RiskRuleSeverity.Info)]
    [InlineData(RiskRuleSeverity.Warning)]
    public void Decision_WithOnlyAnnotations_AdmitsWithWarnings(RiskRuleSeverity severity)
    {
        var result = RiskValidationResult.Approved() with
        {
            Violations = [Violation(severity)],
        };

        result.Decision.Should().Be(RiskDecisionKind.ApprovedWithWarnings);
        result.IsApproved.Should().BeTrue();
        // Nothing blocked, so there is no rejection to attribute.
        result.BlockingViolation.Should().BeNull();
        result.RejectReason.Should().BeNull();
    }

    /// <summary>
    /// An escalated order is parked, not admitted. Reporting it as approved would route the one
    /// order the escalation exists to hold.
    /// </summary>
    [Fact]
    public void Decision_WhenParkedForApproval_IsEscalatedAndNotApproved()
    {
        var result = RiskValidationResult.Escalated("needs sign-off", escalationId: "ESC-1");

        result.Decision.Should().Be(RiskDecisionKind.Escalated);
        result.IsApproved.Should().BeFalse("a parked order has not passed the gate");
        result.EscalationId.Should().Be("ESC-1");
    }

    /// <summary>
    /// A blocking violation outranks an acknowledgement request. Escalating instead would put a
    /// rejected order in front of an operator as though sign-off could release it.
    /// </summary>
    [Fact]
    public void BlockingViolation_IsSelectedBySeverityNotByPosition()
    {
        var result = RiskValidationResult.Rejected("blocked") with
        {
            Violations =
            [
                Violation(RiskRuleSeverity.Warning, requiresAcknowledgement: true),
                Violation(RiskRuleSeverity.Error),
            ],
        };

        result.Decision.Should().Be(RiskDecisionKind.Rejected);
        result.BlockingViolation!.Severity.Should().Be(RiskRuleSeverity.Error);
    }

    [Fact]
    public void Decision_WithNoFindings_IsApproved()
    {
        var result = RiskValidationResult.Approved();

        result.Decision.Should().Be(RiskDecisionKind.Approved);
        result.Violations.Should().BeEmpty();
    }

    /// <summary>
    /// <see cref="IReadOnlyList{T}"/> is a read-only view, not an immutable collection, so a caller
    /// keeping the underlying list could otherwise turn an approval into one carrying a blocking
    /// finding after the decision was derived — and the OMS routes on <c>IsApproved</c>.
    /// </summary>
    [Fact]
    public void Violations_DoNotAliasACallerMutableList()
    {
        var mutable = new List<RiskViolation> { Violation(RiskRuleSeverity.Warning) };

        var result = RiskValidationResult.Approved() with { Violations = mutable };
        mutable.Add(Violation(RiskRuleSeverity.Critical));

        result.Decision.Should().Be(RiskDecisionKind.ApprovedWithWarnings);
        result.Violations.Should().ContainSingle();
        result.BlockingViolation.Should().BeNull();
    }

    /// <summary>
    /// Stopping at the first failing callback would strand every later reservation as pending —
    /// capacity consumed by an order that already reached its terminal state.
    /// </summary>
    [Fact]
    public void CommitReservations_WhenOneCallbackThrows_StillSettlesTheRest()
    {
        var first = new ThrowingReservation();
        var second = new RecordingReservation();
        var outcome = RiskValidationResult.Approved() with { Reservations = [first, second] };

        var act = outcome.CommitReservations;

        act.Should().Throw<AggregateException>();
        second.Committed.Should().BeTrue("a failing callback must not strand the reservations after it");
    }

    [Fact]
    public void RollbackReservations_WhenOneCallbackThrows_StillSettlesTheRest()
    {
        var second = new RecordingReservation();
        var outcome = RiskValidationResult.Approved() with
        {
            Reservations = [new ThrowingReservation(), second],
        };

        var act = outcome.RollbackReservations;

        act.Should().Throw<AggregateException>();
        second.RolledBack.Should().BeTrue();
    }

    /// <summary>
    /// Ownership transfers to the caller, so the outcome cannot keep looking at a list the
    /// validator may reuse for its next evaluation.
    /// </summary>
    [Fact]
    public void Reservations_AreSnapshotAtConstruction()
    {
        var working = new List<IRiskReservation> { new RecordingReservation() };
        var outcome = RiskValidationResult.Approved() with { Reservations = working };

        working.Clear();
        working.Add(new ThrowingReservation());

        outcome.Reservations.Should().HaveCount(1);
        outcome.CommitReservations();
    }

    [Fact]
    public void ToSummary_CarriesTheDecisionAndEveryViolation()
    {
        var violations = new[] { Violation(RiskRuleSeverity.Error), Violation(RiskRuleSeverity.Warning) };

        var summary = (RiskValidationResult.Rejected("blocked") with { Violations = violations }).ToSummary();

        summary.Decision.Should().Be(RiskDecisionKind.Rejected);
        summary.Violations.Should().HaveCount(2, "the submitter sees every finding, not just the blocking one");
    }

    private sealed class RecordingReservation : IRiskReservation
    {
        public bool Committed { get; private set; }

        public bool RolledBack { get; private set; }

        public void Commit() => Committed = true;

        public void Rollback() => RolledBack = true;
    }

    private sealed class ThrowingReservation : IRiskReservation
    {
        public void Commit() => throw new InvalidOperationException("commit failed");

        public void Rollback() => throw new InvalidOperationException("rollback failed");
    }
}
