using Meridian.Contracts.SecurityMaster;

namespace Meridian.Application.SecurityMaster;

/// <summary>
/// The publication write path of the governed workbench lifecycle. Only the published-revision merge
/// (<see cref="ApprovedFieldEditCanonicalMergeHandler"/>) uses it.
/// </summary>
/// <remarks>
/// <para>Security Master term amendments have three typed write paths, so a caller cannot opt out of
/// governance with a flag on the request:</para>
/// <list type="bullet">
/// <item><description><c>ISecurityMasterService.AmendTermsAsync</c> and its preferred, convertible and
/// term-block siblings are the operator-direct path. They are refused with
/// <see cref="GovernedTermAmendmentRequiredException"/> while
/// <see cref="SecurityMasterWorkbenchOptions.RequireGovernedTermAmendments"/> is on.</description></item>
/// <item><description><see cref="ISecurityMasterAmender"/> is the system-ingest path (provider backfill,
/// EDGAR ingest, ticker symbology). It is not governed by that setting, and it refuses requests that name
/// an operator source system, so an operator edit cannot be routed through it.</description></item>
/// <item><description>This interface is the publication path. It accepts an amendment only when
/// <c>SourceRecordId</c> names a revision the durable revision store holds as Approved (or already
/// Published, for a retried publish) for the same security.</description></item>
/// </list>
/// </remarks>
public interface IGovernedPublicationAmender
{
    /// <summary>Applies the merged amendment of an approved revision named by <c>request.SourceRecordId</c>.</summary>
    Task<SecurityDetailDto> AmendApprovedRevisionAsync(AmendSecurityTermsRequest request, CancellationToken ct = default);
}
