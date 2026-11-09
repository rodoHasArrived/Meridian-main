namespace Meridian.Contracts.SecurityMaster;

/// <summary>
/// The system-ingest write path for amending security master terms: provider backfill (enriched
/// trading parameters), EDGAR reference-data ingest and ticker symbology. Infrastructure services use
/// it without taking a dependency on the application-layer service.
/// </summary>
/// <remarks>
/// The governed-amendment setting (<c>SecurityMasterWorkbench:RequireGovernedTermAmendments</c>) does
/// not block this path, so the Security Master implementation refuses requests naming an operator
/// source system; operator corrections use <c>ISecurityMasterService</c> or the governed workbench.
/// </remarks>
public interface ISecurityMasterAmender
{
    Task<SecurityDetailDto> AmendTermsAsync(AmendSecurityTermsRequest request, CancellationToken ct = default);
}
