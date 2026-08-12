using Meridian.Contracts.Workstation;

namespace Meridian.Contracts.Operations;

/// <summary>
/// Raised when an operation that requires a human operator is attempted by reviewed automation.
/// </summary>
/// <remarks>
/// Derives from <see cref="InvalidOperationException"/> so that existing callers which catch
/// <see cref="InvalidOperationException"/> keep working, while callers that want to render
/// "this action needs a human approver" as a distinct, actionable state can catch this type
/// instead of pattern-matching on message text.
/// </remarks>
public sealed class HumanOperatorRequiredException : InvalidOperationException
{
    public HumanOperatorRequiredException(string action)
        : base(OperationsOriginGuard.RefusalMessage(action))
        => Action = action;

    public HumanOperatorRequiredException(string action, string message)
        : base(message)
        => Action = action;

    /// <summary>The action that was refused, in the phrasing used by the refusal message.</summary>
    public string Action { get; }
}

/// <summary>
/// The single owner of the "reviewed automation may not perform this action; a human operator is
/// required" governance control.
/// </summary>
/// <remarks>
/// <para>
/// The predicate and the refusal message live here so the rule can evolve in one place. A realistic
/// future change — admitting a second approved origin, recording an audit entry when automation is
/// refused, or distinguishing "needs a human" from "needs a <i>second</i> human" — becomes one edit
/// rather than a coordinated sweep where a missed module leaves a silent governance hole.
/// </para>
/// <para>
/// Only the throwing gates carry a <see cref="HumanOperatorRequiredException"/>, directly or as an
/// inner exception. Gates that return the refusal as data surface <see cref="BlockerMessage"/> in
/// an ordinary DTO and throw nothing; absence of the exception does not mean the action passed.
/// </para>
/// </remarks>
public static class OperationsOriginGuard
{
    /// <summary>Returns <see langword="true"/> when the action originates from a human operator.</summary>
    public static bool IsHumanOperator(OperationsActionOriginDto actionOrigin)
        => actionOrigin == OperationsActionOriginDto.HumanOperator;

    /// <summary>The canonical refusal message for <paramref name="action"/>.</summary>
    public static string RefusalMessage(string action)
        => $"Reviewed automation cannot {action}; a human operator approval is required.";

    /// <summary>
    /// The canonical refusal text for a structured result, where the refusal is returned as data
    /// rather than thrown.
    /// </summary>
    /// <remarks>
    /// Deliberately worded differently from <see cref="RefusalMessage"/>: this is read by an
    /// operator looking at a blocked workflow, so it names what automation *may* still do, whereas
    /// the exception message is read where an action already failed. Two audiences, two phrasings —
    /// but both owned here, so a governance wording change stays one edit.
    /// </remarks>
    public static string BlockerMessage(string actionLabel)
        => $"{actionLabel} requires a human operator origin; reviewed automation may suggest, "
           + "summarize, draft, and flag but cannot mutate the operating record.";

    /// <summary>
    /// Builds the uniform refusal signal without throwing it, for modules that raise their own
    /// exception type and carry this as the inner exception.
    /// </summary>
    public static HumanOperatorRequiredException Refusal(string action) => new(action);

    /// <summary>
    /// Throws <see cref="HumanOperatorRequiredException"/> unless <paramref name="actionOrigin"/> is
    /// a human operator.
    /// </summary>
    public static void RequireHumanOperator(OperationsActionOriginDto actionOrigin, string action)
    {
        if (!IsHumanOperator(actionOrigin))
        {
            throw new HumanOperatorRequiredException(action);
        }
    }
}
