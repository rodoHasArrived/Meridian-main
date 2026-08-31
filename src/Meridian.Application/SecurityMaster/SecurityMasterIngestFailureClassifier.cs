using Meridian.Storage.SecurityMaster;
using Npgsql;

namespace Meridian.Application.SecurityMaster;

/// <summary>
/// Decides whether a failed Security Master mutation means "this record is already mastered"
/// (a skip) or a genuine failure, for the ingest paths that report imported/skipped/failed counts.
/// </summary>
/// <remarks>
/// <para>
/// Shared so the CSV/JSON import, the EDGAR orchestrator, and the CLI cannot drift into disagreeing
/// about what a duplicate is. Each previously sniffed <c>ex.Message</c> for "already exists" or
/// "duplicate", which was wrong in both directions: the most common duplicate signal — re-ingesting
/// a mastered security, which fails the create at stream version 0 — carries neither phrase and was
/// counted as a hard failure, while any unrelated error whose text happened to contain "duplicate"
/// was silently skipped. Message text is also a server-locale and provider-version detail, not a
/// contract.
/// </para>
/// <para>
/// Classification is by TYPE and by SQLSTATE, both of which are contractual.
/// </para>
/// </remarks>
internal static class SecurityMasterIngestFailureClassifier
{
    /// <summary>
    /// True when <paramref name="exception"/> means the security is already mastered, so the ingest
    /// should count a skip rather than a failure.
    /// </summary>
    /// <remarks>
    /// Never call this for <see cref="OperationCanceledException"/> — cancellation is not an ingest
    /// outcome and must propagate. Callers filter it out before reaching here.
    /// </remarks>
    public static bool IsAlreadyMastered(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        return exception switch
        {
            // The create raced or repeated: the stream already had events at version 0.
            SecurityMasterStreamVersionConflictException conflict => conflict.IsAlreadyCreated,

            // A unique index rejected the row — the primary-identifier index, or the
            // (security_id, stream_version) event key. 23505 is the contract, not the message.
            PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } => true,

            // Providers wrap driver failures; unwrap one level rather than miss the SQLSTATE.
            _ => exception.InnerException is { } inner
                 && inner is not OperationCanceledException
                 && IsAlreadyMastered(inner)
        };
    }
}
