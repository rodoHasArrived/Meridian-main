using Meridian.Contracts.Workstation;

namespace Meridian.Strategies.Services;

/// <summary>
/// Compile-time-checked translation between the C# reconciliation enums and the string token
/// vocabulary of the F# <c>ReconciliationCaseWorkflow</c>. The F# kernel is a leaf assembly and
/// cannot reference these enums, so the previous boundary relied on coincidental name equality
/// (<c>enum.ToString()</c> / <c>Enum.Parse</c>): an enum rename or addition silently misrouted a
/// transition at runtime.
/// <para>
/// Every mapping below references its enum member by name (via <c>nameof</c>), so renaming a member
/// breaks compilation here, and the exhaustive <c>_ =&gt; throw</c> arms surface any newly added
/// member immediately instead of silently falling through the workflow. The
/// <c>ReconciliationCaseWorkflowVocabularyTests</c> guard additionally asserts token parity against
/// the vocabulary the F# workflow actually recognizes.
/// </para>
/// </summary>
public static class ReconciliationCaseWorkflowVocabulary
{
    public static string ToToken(ReconciliationCaseTransitionAction action) => action switch
    {
        ReconciliationCaseTransitionAction.StartReview => nameof(ReconciliationCaseTransitionAction.StartReview),
        ReconciliationCaseTransitionAction.RequestApproval => nameof(ReconciliationCaseTransitionAction.RequestApproval),
        ReconciliationCaseTransitionAction.Approve => nameof(ReconciliationCaseTransitionAction.Approve),
        ReconciliationCaseTransitionAction.Post => nameof(ReconciliationCaseTransitionAction.Post),
        ReconciliationCaseTransitionAction.Reopen => nameof(ReconciliationCaseTransitionAction.Reopen),
        ReconciliationCaseTransitionAction.Supersede => nameof(ReconciliationCaseTransitionAction.Supersede),
        _ => throw UnmappedMember(nameof(ReconciliationCaseTransitionAction), action)
    };

    public static string ToToken(ReconciliationCaseLifecycleState state) => state switch
    {
        ReconciliationCaseLifecycleState.Open => nameof(ReconciliationCaseLifecycleState.Open),
        ReconciliationCaseLifecycleState.InReview => nameof(ReconciliationCaseLifecycleState.InReview),
        ReconciliationCaseLifecycleState.AwaitingApproval => nameof(ReconciliationCaseLifecycleState.AwaitingApproval),
        ReconciliationCaseLifecycleState.Approved => nameof(ReconciliationCaseLifecycleState.Approved),
        ReconciliationCaseLifecycleState.Posted => nameof(ReconciliationCaseLifecycleState.Posted),
        ReconciliationCaseLifecycleState.Reopened => nameof(ReconciliationCaseLifecycleState.Reopened),
        ReconciliationCaseLifecycleState.Superseded => nameof(ReconciliationCaseLifecycleState.Superseded),
        ReconciliationCaseLifecycleState.Investigating => nameof(ReconciliationCaseLifecycleState.Investigating),
        ReconciliationCaseLifecycleState.AwaitingEvidence => nameof(ReconciliationCaseLifecycleState.AwaitingEvidence),
        ReconciliationCaseLifecycleState.Resolved => nameof(ReconciliationCaseLifecycleState.Resolved),
        ReconciliationCaseLifecycleState.SignedOff => nameof(ReconciliationCaseLifecycleState.SignedOff),
        _ => throw UnmappedMember(nameof(ReconciliationCaseLifecycleState), state)
    };

    public static string ToToken(ReconciliationBreakQueueStatus status) => status switch
    {
        ReconciliationBreakQueueStatus.Open => nameof(ReconciliationBreakQueueStatus.Open),
        ReconciliationBreakQueueStatus.InReview => nameof(ReconciliationBreakQueueStatus.InReview),
        ReconciliationBreakQueueStatus.Resolved => nameof(ReconciliationBreakQueueStatus.Resolved),
        ReconciliationBreakQueueStatus.Dismissed => nameof(ReconciliationBreakQueueStatus.Dismissed),
        ReconciliationBreakQueueStatus.SignedOff => nameof(ReconciliationBreakQueueStatus.SignedOff),
        _ => throw UnmappedMember(nameof(ReconciliationBreakQueueStatus), status)
    };

    public static ReconciliationCaseLifecycleState ParseLifecycleState(string token) => token switch
    {
        nameof(ReconciliationCaseLifecycleState.Open) => ReconciliationCaseLifecycleState.Open,
        nameof(ReconciliationCaseLifecycleState.InReview) => ReconciliationCaseLifecycleState.InReview,
        nameof(ReconciliationCaseLifecycleState.AwaitingApproval) => ReconciliationCaseLifecycleState.AwaitingApproval,
        nameof(ReconciliationCaseLifecycleState.Approved) => ReconciliationCaseLifecycleState.Approved,
        nameof(ReconciliationCaseLifecycleState.Posted) => ReconciliationCaseLifecycleState.Posted,
        nameof(ReconciliationCaseLifecycleState.Reopened) => ReconciliationCaseLifecycleState.Reopened,
        nameof(ReconciliationCaseLifecycleState.Superseded) => ReconciliationCaseLifecycleState.Superseded,
        nameof(ReconciliationCaseLifecycleState.Investigating) => ReconciliationCaseLifecycleState.Investigating,
        nameof(ReconciliationCaseLifecycleState.AwaitingEvidence) => ReconciliationCaseLifecycleState.AwaitingEvidence,
        nameof(ReconciliationCaseLifecycleState.Resolved) => ReconciliationCaseLifecycleState.Resolved,
        nameof(ReconciliationCaseLifecycleState.SignedOff) => ReconciliationCaseLifecycleState.SignedOff,
        _ => throw UnknownToken(nameof(ReconciliationCaseLifecycleState), token)
    };

    public static ReconciliationBreakQueueStatus ParseQueueStatus(string token) => token switch
    {
        nameof(ReconciliationBreakQueueStatus.Open) => ReconciliationBreakQueueStatus.Open,
        nameof(ReconciliationBreakQueueStatus.InReview) => ReconciliationBreakQueueStatus.InReview,
        nameof(ReconciliationBreakQueueStatus.Resolved) => ReconciliationBreakQueueStatus.Resolved,
        nameof(ReconciliationBreakQueueStatus.Dismissed) => ReconciliationBreakQueueStatus.Dismissed,
        nameof(ReconciliationBreakQueueStatus.SignedOff) => ReconciliationBreakQueueStatus.SignedOff,
        _ => throw UnknownToken(nameof(ReconciliationBreakQueueStatus), token)
    };

    public static ReconciliationBreakQueueTransitionErrorCode ParseErrorCode(string token) => token switch
    {
        nameof(ReconciliationBreakQueueTransitionErrorCode.None) => ReconciliationBreakQueueTransitionErrorCode.None,
        nameof(ReconciliationBreakQueueTransitionErrorCode.MissingActor) => ReconciliationBreakQueueTransitionErrorCode.MissingActor,
        nameof(ReconciliationBreakQueueTransitionErrorCode.MissingReason) => ReconciliationBreakQueueTransitionErrorCode.MissingReason,
        nameof(ReconciliationBreakQueueTransitionErrorCode.MissingEvidence) => ReconciliationBreakQueueTransitionErrorCode.MissingEvidence,
        nameof(ReconciliationBreakQueueTransitionErrorCode.IllegalTransition) => ReconciliationBreakQueueTransitionErrorCode.IllegalTransition,
        nameof(ReconciliationBreakQueueTransitionErrorCode.DualReviewRequired) => ReconciliationBreakQueueTransitionErrorCode.DualReviewRequired,
        nameof(ReconciliationBreakQueueTransitionErrorCode.ReopenNotAllowed) => ReconciliationBreakQueueTransitionErrorCode.ReopenNotAllowed,
        _ => throw UnknownToken(nameof(ReconciliationBreakQueueTransitionErrorCode), token)
    };

    private static InvalidOperationException UnmappedMember<TEnum>(string enumName, TEnum member)
        where TEnum : struct, Enum
        => new($"{enumName}.{member} is not mapped to a reconciliation workflow token; " +
               "add the mapping in ReconciliationCaseWorkflowVocabulary and the matching token in the F# workflow.");

    private static InvalidOperationException UnknownToken(string enumName, string token)
        => new($"The F# reconciliation workflow returned token '{token}', which does not map to any {enumName} member.");
}
