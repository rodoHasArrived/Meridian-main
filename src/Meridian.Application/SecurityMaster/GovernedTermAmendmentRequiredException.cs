namespace Meridian.Application.SecurityMaster;

/// <summary>
/// A direct (operator) term amendment refused because
/// <see cref="SecurityMasterWorkbenchOptions.RequireGovernedTermAmendments"/> is on. The edit must be
/// staged through the governed workbench (Draft → Submitted → Approved → Published) instead.
/// Raised by <see cref="SecurityMasterService"/> itself, so every in-process caller of
/// <c>ISecurityMasterService</c> sees the same refusal the HTTP routes and the WPF editor show.
/// The HTTP routes answer it with 403 and the shared <see cref="GovernedTermAmendmentPolicy.RefusalTitle"/>
/// and <see cref="GovernedTermAmendmentPolicy.RefusalDetail"/>.
/// </summary>
public sealed class GovernedTermAmendmentRequiredException : InvalidOperationException
{
    public GovernedTermAmendmentRequiredException()
        : base($"{GovernedTermAmendmentPolicy.RefusalTitle} {GovernedTermAmendmentPolicy.RefusalDetail}")
    {
    }

    /// <summary>The problem title shared with the HTTP 403 response and the WPF editor.</summary>
    public string Title => GovernedTermAmendmentPolicy.RefusalTitle;

    /// <summary>The guidance shared with the HTTP 403 response and the WPF editor.</summary>
    public string Detail => GovernedTermAmendmentPolicy.RefusalDetail;
}
