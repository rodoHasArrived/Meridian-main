using Meridian.FSharp.SecurityMasterInterop;

namespace Meridian.Application.SecurityMaster;

// Which failures of a create or term amendment are refusals the caller can act on, raised as
// SecurityMasterWriteRefusedException so the direct write routes can answer 400 with the reason.
public sealed partial class SecurityMasterService
{
    // F# command errors that describe the stream, not the submitted terms. A stale amend must not be
    // reported as a refused one, so these win even when the validator also reported request errors.
    private static readonly HashSet<string> CommandErrorsThatAreNotRefusals =
        new(StringComparer.Ordinal) { "version_conflict", "missing_state", "security_id_mismatch" };

    /// <summary>
    /// Maps the REQUEST into its command or kind; a plain <see cref="InvalidOperationException"/> from it
    /// is about the submitted document. Never wrap a mapping of STORED data (the rebuild, or
    /// <c>ToRecord</c> of the current projection): a row this node cannot decode is a server fault.
    /// Subclasses pass through unchanged.
    /// </summary>
    private static T MapRequestOrRefuse<T>(Func<T> mapRequest)
    {
        try
        {
            return mapRequest();
        }
        catch (InvalidOperationException ex) when (ex.GetType() == typeof(InvalidOperationException))
        {
            throw new SecurityMasterWriteRefusedException(ex.Message, ex);
        }
    }

    private static InvalidOperationException CommandRejection(SecurityMasterCommandResultWrapper result)
    {
        var errors = result.ErrorDetails;
        var errorText = string.Join("; ", errors.Select(static e => $"[{e.Code}] {e.Message}"));
        return errors.Length == 0 || errors.Any(static e => CommandErrorsThatAreNotRefusals.Contains(e.Code))
            ? new InvalidOperationException(errorText)
            : new SecurityMasterWriteRefusedException(errorText);
    }
}
