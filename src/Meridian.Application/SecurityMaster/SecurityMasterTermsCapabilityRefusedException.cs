using Meridian.Application.Composition;

namespace Meridian.Application.SecurityMaster;

/// <summary>
/// The Security Master terms capability floor refused this node: the floor could not be read, is
/// missing while this build writes declared terms, or names a capability this build does not hold
/// (<see cref="SecurityMasterTermsCapabilityVerifier"/>). At host start it refuses startup like any
/// <see cref="StartupRefusedException"/>; on a request it is a temporary refusal of this node, which the
/// API answers with 503 and <see cref="RefusalMessage"/> rather than a 500.
/// </summary>
public sealed class SecurityMasterTermsCapabilityRefusedException : StartupRefusedException
{
    /// <summary>
    /// The operator-facing reason a refused request is answered with. It names no connection,
    /// credential or floor token; the specific cause is in this node's log and the exception message.
    /// </summary>
    public const string RefusalMessage =
        "Security Master terms writes are unavailable on this node: its minimum terms capability floor could not be verified " +
        "or does not match this build. An operator must verify the floor (migration 057 and the activated floor row) before retrying.";

    public SecurityMasterTermsCapabilityRefusedException(string message)
        : base(message)
    {
    }
}
