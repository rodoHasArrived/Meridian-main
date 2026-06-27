namespace Meridian.Application.SecurityMaster;

/// <summary>
/// Raised when a governed Security Master write is attempted with a stale expected version — the
/// passport advanced since the caller loaded it. Endpoints map this to HTTP 409 with the current
/// version so the client can refetch without losing data.
/// </summary>
public sealed class SecurityMasterConcurrencyException : Exception
{
    public SecurityMasterConcurrencyException(Guid securityId, long expectedVersion, long currentVersion)
        : base($"Security '{securityId:D}' was modified concurrently. Expected version {expectedVersion} but the current version is {currentVersion}.")
    {
        SecurityId = securityId;
        ExpectedVersion = expectedVersion;
        CurrentVersion = currentVersion;
    }

    public Guid SecurityId { get; }

    public long ExpectedVersion { get; }

    public long CurrentVersion { get; }
}
