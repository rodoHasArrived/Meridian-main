using FluentAssertions;
using Meridian.Contracts.Workstation;
using Meridian.FSharp.Ledger;
using Meridian.Strategies.Services;
using Xunit;

namespace Meridian.Tests.Strategies;

/// <summary>
/// Guards the compile-time link between the C# reconciliation enums and the F# workflow's string
/// token vocabulary. If an enum member is renamed, <see cref="ReconciliationCaseWorkflowVocabulary"/>
/// stops compiling; if a token drifts on either side, these assertions fail — so a transition can no
/// longer be silently misrouted at runtime.
/// </summary>
public sealed class ReconciliationCaseWorkflowVocabularyTests
{
    [Fact]
    public void ActionTokens_Match_Enum_Exactly()
    {
        var enumTokens = Enum.GetValues<ReconciliationCaseTransitionAction>()
            .Select(action => ReconciliationCaseWorkflowVocabulary.ToToken(action))
            .ToArray();

        // The C# token for every action equals its member name (nameof mapping stays in sync)...
        foreach (var action in Enum.GetValues<ReconciliationCaseTransitionAction>())
        {
            ReconciliationCaseWorkflowVocabulary.ToToken(action).Should().Be(action.ToString());
        }

        // ...and the workflow recognizes exactly that set of action tokens.
        ReconciliationCaseWorkflowInterop.RecognizedActionTokens
            .Should().BeEquivalentTo(enumTokens);
    }

    [Fact]
    public void LifecycleTokens_RoundTrip_And_Workflow_Tokens_Are_Valid()
    {
        foreach (var state in Enum.GetValues<ReconciliationCaseLifecycleState>())
        {
            var token = ReconciliationCaseWorkflowVocabulary.ToToken(state);
            token.Should().Be(state.ToString());
            ReconciliationCaseWorkflowVocabulary.ParseLifecycleState(token).Should().Be(state);
        }

        // Every token the F# workflow reads/produces must correspond to a real enum member.
        foreach (var token in ReconciliationCaseWorkflowInterop.RecognizedLifecycleStateTokens)
        {
            var parsed = ReconciliationCaseWorkflowVocabulary.ParseLifecycleState(token);
            ReconciliationCaseWorkflowVocabulary.ToToken(parsed).Should().Be(token);
        }
    }

    [Fact]
    public void QueueStatusTokens_RoundTrip_And_Workflow_Tokens_Are_Valid()
    {
        foreach (var status in Enum.GetValues<ReconciliationBreakQueueStatus>())
        {
            var token = ReconciliationCaseWorkflowVocabulary.ToToken(status);
            token.Should().Be(status.ToString());
            ReconciliationCaseWorkflowVocabulary.ParseQueueStatus(token).Should().Be(status);
        }

        foreach (var token in ReconciliationCaseWorkflowInterop.RecognizedQueueStatusTokens)
        {
            var parsed = ReconciliationCaseWorkflowVocabulary.ParseQueueStatus(token);
            ReconciliationCaseWorkflowVocabulary.ToToken(parsed).Should().Be(token);
        }
    }

    [Fact]
    public void ErrorCodeTokens_From_Workflow_Are_All_Parseable()
    {
        foreach (var token in ReconciliationCaseWorkflowInterop.RecognizedErrorCodeTokens)
        {
            var parsed = ReconciliationCaseWorkflowVocabulary.ParseErrorCode(token);
            parsed.ToString().Should().Be(token);
        }
    }
}
