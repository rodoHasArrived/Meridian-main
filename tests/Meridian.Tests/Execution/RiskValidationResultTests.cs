using FluentAssertions;
using Meridian.Execution;
using Meridian.Execution.Sdk;

namespace Meridian.Tests.Execution;

/// <summary>
/// The decision has to follow from the violations. Construction is factory-only precisely so that
/// no caller can assert a decision the findings do not support, so these pin what the factories
/// derive.
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
    public void FromViolations_WithABlockingSeverity_IsNotApproved(RiskRuleSeverity severity)
    {
        var result = RiskValidationResult.FromViolations([Violation(severity)]);

        result.Decision.Should().Be(RiskDecisionKind.Rejected);
        result.IsApproved.Should().BeFalse();
        result.BlockingViolation.Should().NotBeNull();
        result.RejectCode.Should().Be("CODE");
    }

    [Theory]
    [InlineData(RiskRuleSeverity.Info)]
    [InlineData(RiskRuleSeverity.Warning)]
    public void FromViolations_WithOnlyAnnotations_AdmitsWithWarnings(RiskRuleSeverity severity)
    {
        var result = RiskValidationResult.FromViolations([Violation(severity)]);

        result.Decision.Should().Be(RiskDecisionKind.ApprovedWithWarnings);
        result.IsApproved.Should().BeTrue();
        // Nothing blocked, so there is no rejection to attribute.
        result.BlockingViolation.Should().BeNull();
        result.RejectReason.Should().BeNull();
    }

    [Fact]
    public void FromViolations_WithAnAcknowledgementRequest_Escalates()
    {
        var result = RiskValidationResult.FromViolations(
            [Violation(RiskRuleSeverity.Warning, requiresAcknowledgement: true)]);

        result.Decision.Should().Be(RiskDecisionKind.Escalated);
        result.IsApproved.Should().BeTrue("an escalation awaits sign-off rather than blocking outright");
    }

    /// <summary>
    /// A blocking violation outranks an acknowledgement request. Escalating instead would put a
    /// rejected order in front of an operator as though sign-off could release it.
    /// </summary>
    [Fact]
    public void FromViolations_WithBothBlockingAndAcknowledgement_Rejects()
    {
        var result = RiskValidationResult.FromViolations(
        [
            Violation(RiskRuleSeverity.Warning, requiresAcknowledgement: true),
            Violation(RiskRuleSeverity.Error)
        ]);

        result.Decision.Should().Be(RiskDecisionKind.Rejected);
    }

    [Fact]
    public void FromViolations_Empty_IsApproved()
    {
        var result = RiskValidationResult.FromViolations([]);

        result.Decision.Should().Be(RiskDecisionKind.Approved);
        result.Violations.Should().BeEmpty();
    }

    /// <summary>
    /// Sealing construction only holds the invariant if the violations cannot change afterwards.
    /// <see cref="IReadOnlyList{T}"/> is a read-only view, not an immutable collection, so a caller
    /// keeping the underlying list could otherwise turn an approval into one carrying a blocking
    /// finding — and the OMS routes on <c>IsApproved</c>.
    /// </summary>
    [Fact]
    public void FromViolations_DoesNotAliasACallerMutableList()
    {
        var mutable = new List<RiskViolation> { Violation(RiskRuleSeverity.Warning) };

        var result = RiskValidationResult.FromViolations(mutable);
        mutable.Add(Violation(RiskRuleSeverity.Critical));

        result.Decision.Should().Be(RiskDecisionKind.ApprovedWithWarnings);
        result.Violations.Should().HaveCount(1, "the result snapshots what it judged");
        result.BlockingViolation.Should().BeNull();
    }

    [Fact]
    public void FromViolations_Null_Throws()
    {
        var act = () => RiskValidationResult.FromViolations(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    /// <summary>
    /// The bare-reason factory has to produce a result whose blocking violation, reason, and code
    /// are all populated — a rejection that reports none of them tells an operator nothing.
    /// </summary>
    [Fact]
    public void Rejected_SynthesisesAnAttributableBlockingViolation()
    {
        var result = RiskValidationResult.Rejected("no good");

        result.Decision.Should().Be(RiskDecisionKind.Rejected);
        result.IsApproved.Should().BeFalse();
        result.RejectReason.Should().Be("no good");
        result.RejectCode.Should().Be("RISK_REJECTED");
        result.BlockingViolation!.IsBlocking.Should().BeTrue();
    }

    [Fact]
    public void Approved_HasNoFindings()
    {
        var result = RiskValidationResult.Approved();

        result.Decision.Should().Be(RiskDecisionKind.Approved);
        result.IsApproved.Should().BeTrue();
        result.Violations.Should().BeEmpty();
        result.BlockingViolation.Should().BeNull();
    }

    [Fact]
    public void ToSummary_CarriesTheDecisionAndEveryViolation()
    {
        var violations = new[] { Violation(RiskRuleSeverity.Error), Violation(RiskRuleSeverity.Warning) };

        var summary = RiskValidationResult.FromViolations(violations).ToSummary();

        summary.Decision.Should().Be(RiskDecisionKind.Rejected);
        summary.Violations.Should().HaveCount(2, "the submitter sees every finding, not just the blocking one");
    }
}
