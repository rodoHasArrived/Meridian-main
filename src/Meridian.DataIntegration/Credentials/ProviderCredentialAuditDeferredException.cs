namespace Meridian.DataIntegration.Credentials;

/// <summary>
/// The vault mutation committed, but its audit record could not be appended to the audit log and
/// remains journaled for replay (PRD-002: credential/audit commit ambiguity). Derives from
/// <see cref="IOException"/> so callers that already treat audit I/O failures as retryable keep
/// working, while <see cref="VaultCommitted"/> lets them distinguish "committed, audit owed" from
/// "not committed". Raised by the OAuth token paths, whose callers reconcile their running cache
/// against the durable vault on failure; provider credential mutations return success and surface
/// the backlog through <c>auditPending</c> status metadata instead.
/// </summary>
public sealed class ProviderCredentialAuditDeferredException : IOException
{
    public ProviderCredentialAuditDeferredException(string providerId, string action, string pendingAuditPath)
        : base($"Credential vault mutation '{action}' for '{providerId}' committed, but its audit record could not be appended and is journaled at '{pendingAuditPath}' for replay.")
    {
        ProviderId = providerId;
        Action = action;
        PendingAuditPath = pendingAuditPath;
    }

    public string ProviderId { get; }

    public string Action { get; }

    /// <summary>The write-ahead journal that holds the owed audit record.</summary>
    public string PendingAuditPath { get; }

    /// <summary>Always <c>true</c>: the encrypted vault already holds the mutation.</summary>
    public bool VaultCommitted => true;
}
