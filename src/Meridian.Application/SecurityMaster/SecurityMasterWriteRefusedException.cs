namespace Meridian.Application.SecurityMaster;

/// <summary>
/// A Security Master create or term amendment refused for what the request asked: terms this node
/// reads but does not write (ADR-024), a missing or malformed term, a vocabulary value, an F# domain
/// validation error, a profile failing catalog validation, or a round-trip guard whose reason names the
/// repair. The message is written for the caller; the direct create and term-amendment routes return it
/// as the 400 Problem Details detail.
/// </summary>
/// <remarks>
/// Derives from <see cref="InvalidOperationException"/>, which these refusals were raised as, so existing
/// handlers, ingest classification and subclass-tolerant assertions keep working. It is never raised for
/// a stored row this node cannot decode, a missing security, a stale version or a store failure. Do not
/// map it in <c>MeridianApiExceptionHandler</c>: deactivate and workbench publish also raise it and keep
/// their responses.
/// </remarks>
public sealed class SecurityMasterWriteRefusedException : InvalidOperationException
{
    public SecurityMasterWriteRefusedException(string message)
        : base(message)
    {
    }

    public SecurityMasterWriteRefusedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
