namespace Meridian.Application.SecurityMaster;

/// <summary>
/// Raised when one or more revision-published handlers fail during
/// <c>PublishRevisionAsync</c>. The revision is left in its pre-publish (Approved) state so the
/// caller can retry; handlers are idempotent, so a retry re-runs the full ordered fan-out. This
/// gives callers a failure signal instead of a publish that silently dropped a required side effect.
/// </summary>
public sealed class SecurityMasterPublishFailedException : Exception
{
    public SecurityMasterPublishFailedException(Guid securityId, Guid revisionId, IReadOnlyList<string> failedHandlers)
        : base($"Publishing Security Master revision '{revisionId:D}' for security '{securityId:D}' failed in {failedHandlers.Count} handler(s): {string.Join(", ", failedHandlers)}.")
    {
        SecurityId = securityId;
        RevisionId = revisionId;
        FailedHandlers = failedHandlers;
    }

    public Guid SecurityId { get; }

    public Guid RevisionId { get; }

    public IReadOnlyList<string> FailedHandlers { get; }
}
