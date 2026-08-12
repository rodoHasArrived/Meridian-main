using FluentAssertions;
using Meridian.Contracts.Operations;
using Meridian.Contracts.Workstation;

namespace Meridian.Tests.Contracts.Operations;

public sealed class OperationsOriginGuardTests
{
    [Fact]
    public void IsHumanOperator_IsTrueOnlyForAHumanOperator()
    {
        OperationsOriginGuard.IsHumanOperator(OperationsActionOriginDto.HumanOperator).Should().BeTrue();

        var automationOrigins = Enum.GetValues<OperationsActionOriginDto>()
            .Where(static origin => origin != OperationsActionOriginDto.HumanOperator);

        foreach (var origin in automationOrigins)
        {
            OperationsOriginGuard.IsHumanOperator(origin)
                .Should()
                .BeFalse($"{origin} is automation, not a human operator");
        }
    }

    [Fact]
    public void RequireHumanOperator_DoesNothingForAHumanOperator()
    {
        var act = () => OperationsOriginGuard.RequireHumanOperator(
            OperationsActionOriginDto.HumanOperator,
            "approve reports");

        act.Should().NotThrow();
    }

    [Theory]
    [InlineData(OperationsActionOriginDto.AutomationSuggestion)]
    [InlineData(OperationsActionOriginDto.AssistantDraft)]
    [InlineData(OperationsActionOriginDto.AutomationAssistant)]
    public void RequireHumanOperator_RefusesEveryAutomationOrigin(OperationsActionOriginDto origin)
    {
        var act = () => OperationsOriginGuard.RequireHumanOperator(origin, "approve reports");

        act.Should()
            .Throw<HumanOperatorRequiredException>()
            .WithMessage("Reviewed automation cannot approve reports; a human operator approval is required.")
            .Which.Action.Should().Be("approve reports");
    }

    // The compatibility guarantee that lets the InvalidOperationException call sites adopt the
    // shared guard without breaking any existing caller that catches InvalidOperationException.
    [Fact]
    public void HumanOperatorRequiredException_IsAnInvalidOperationException()
    {
        var exception = new HumanOperatorRequiredException("approve reports");

        exception.Should().BeAssignableTo<InvalidOperationException>();
    }

    // Sites that already had bespoke wording keep it while still emitting the typed refusal, so a
    // caller gets both the module's message and a machine-readable Action.
    [Fact]
    public void HumanOperatorRequiredException_CanKeepASiteSpecificMessage()
    {
        var exception = new HumanOperatorRequiredException(
            "post generated accounting posting candidates",
            "Generated accounting posting candidates require a human-operator action origin before append.");

        exception.Action.Should().Be("post generated accounting posting candidates");
        exception.Message.Should().Be(
            "Generated accounting posting candidates require a human-operator action origin before append.");
        exception.Should().BeAssignableTo<InvalidOperationException>();
    }

    [Fact]
    public void RefusalMessage_IsTheCanonicalWording() =>
        OperationsOriginGuard.RefusalMessage("archive reports")
            .Should()
            .Be("Reviewed automation cannot archive reports; a human operator approval is required.");

    [Fact]
    public void Refusal_BuildsTheSignalWithoutThrowing()
    {
        var refusal = OperationsOriginGuard.Refusal("execute accounting migration runs");

        refusal.Action.Should().Be("execute accounting migration runs");
        refusal.Message.Should().Be(
            "Reviewed automation cannot execute accounting migration runs; a human operator approval is required.");
    }

    // Gates that return a refusal as data rather than throwing use a different wording, aimed at an
    // operator reading a blocked workflow. Pinned verbatim: these call sites previously held the
    // string inline, so this asserts centralising it changed no API-visible text.
    [Fact]
    public void TheBlockerMessageKeepsItsStructuredResultWording()
    {
        OperationsOriginGuard.BlockerMessage("Approving a continuity plan").Should().Be(
            "Approving a continuity plan requires a human operator origin; reviewed automation may "
            + "suggest, summarize, draft, and flag but cannot mutate the operating record.");
    }

    // The two reconciliation gates that report refusals as structured results used to hold this
    // text inline. Centralising them must not have reworded either one.
    [Theory]
    [InlineData("resolve, sign off, or reopen reconciliation cases",
        "Reviewed automation cannot resolve, sign off, or reopen reconciliation cases; a human operator approval is required.")]
    [InlineData("resolve or dismiss reconciliation breaks",
        "Reviewed automation cannot resolve or dismiss reconciliation breaks; a human operator approval is required.")]
    public void TheStructuredReconciliationRefusalsKeepTheirWording(string action, string expected)
    {
        OperationsOriginGuard.RefusalMessage(action).Should().Be(expected);
    }

    // Modules that keep their own exception type carry the refusal as the inner exception, so a
    // caller can identify a governance refusal uniformly regardless of which module raised it.
    [Fact]
    public void AModuleExceptionCanCarryTheRefusalAsItsInnerSignal()
    {
        var action = "settle a bank transfer";
        var wrapped = new InvalidDataException(
            OperationsOriginGuard.RefusalMessage(action),
            OperationsOriginGuard.Refusal(action));

        wrapped.InnerException.Should().BeOfType<HumanOperatorRequiredException>()
            .Which.Action.Should().Be(action);
    }
}
