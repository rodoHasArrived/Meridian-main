// Security Master passport-workbench and review-queue route helpers, split out of
// workstation-endpoints.ts to keep it under the 2,000-line ratchet. Re-exported from there.
import { UI_API_ROUTES } from "./ui-api-routes.generated";

/**
 * Passport Workbench governed-write routes. Each carries a `{securityId:guid}` path segment that is
 * authoritative server-side, so the helpers substitute it explicitly rather than relying on the body.
 */
function securityMasterWorkbenchPath(route: string, securityId: string): string {
  return route.replace("{securityId:guid}", encodeURIComponent(securityId));
}

export function securityMasterAssetClassDefinitionEndpoint(assetClass: string): string {
  return UI_API_ROUTES.SecurityMasterAssetClassDefinition.replace("{assetClass}", encodeURIComponent(assetClass));
}

export function securityMasterWorkbenchFieldEndpoint(securityId: string): string {
  return securityMasterWorkbenchPath(UI_API_ROUTES.SecurityMasterWorkbenchField, securityId);
}

export function securityMasterWorkbenchResolveConflictEndpoint(securityId: string): string {
  return securityMasterWorkbenchPath(UI_API_ROUTES.SecurityMasterWorkbenchResolveConflict, securityId);
}

export function securityMasterWorkbenchSubmitEndpoint(securityId: string): string {
  return securityMasterWorkbenchPath(UI_API_ROUTES.SecurityMasterWorkbenchSubmit, securityId);
}

export function securityMasterWorkbenchApproveEndpoint(securityId: string): string {
  return securityMasterWorkbenchPath(UI_API_ROUTES.SecurityMasterWorkbenchApprove, securityId);
}

export function securityMasterWorkbenchPublishEndpoint(securityId: string): string {
  return securityMasterWorkbenchPath(UI_API_ROUTES.SecurityMasterWorkbenchPublish, securityId);
}

export function securityMasterWorkbenchDiscardEndpoint(securityId: string): string {
  return securityMasterWorkbenchPath(UI_API_ROUTES.SecurityMasterWorkbenchDiscard, securityId);
}

/** Review inbox: revisions awaiting action across every security (review P5/D1), or one security's. */
export function securityMasterWorkbenchPendingRevisionsEndpoint(states?: readonly string[], securityId?: string | null): string {
  const route = UI_API_ROUTES.SecurityMasterWorkbenchPendingRevisions;
  const query: string[] = [];
  if (states && states.length > 0) query.push(`state=${encodeURIComponent(states.join(","))}`);
  if (securityId) query.push(`securityId=${encodeURIComponent(securityId)}`);
  return query.length > 0 ? `${route}?${query.join("&")}` : route;
}

/** Held amendments (review A2), optionally narrowed to one security and one status (default Held). */
export function securityMasterHeldAmendmentsEndpoint(filter: { securityId?: string | null; status?: string | null } = {}): string {
  const query = new URLSearchParams();
  if (filter.securityId) query.set("securityId", filter.securityId);
  if (filter.status) query.set("status", filter.status);
  const suffix = query.toString();
  return suffix ? `${UI_API_ROUTES.SecurityMasterHeldAmendments}?${suffix}` : UI_API_ROUTES.SecurityMasterHeldAmendments;
}

export function securityMasterHeldAmendmentDiscardEndpoint(heldAmendmentId: string): string {
  return UI_API_ROUTES.SecurityMasterHeldAmendmentDiscard.replace("{heldAmendmentId:guid}", encodeURIComponent(heldAmendmentId));
}

/** Global exception queue (review D2); the server defaults the status to Open. */
export function securityMasterExceptionCasesEndpoint(status?: string | null): string {
  return status
    ? `${UI_API_ROUTES.SecurityMasterExceptionCases}?status=${encodeURIComponent(status)}`
    : UI_API_ROUTES.SecurityMasterExceptionCases;
}

export function securityMasterExceptionCasesSyncEndpoint(): string {
  return UI_API_ROUTES.SecurityMasterExceptionCasesSync;
}

export function securityMasterExceptionCaseAssignEndpoint(caseId: string): string {
  return UI_API_ROUTES.SecurityMasterExceptionCaseAssign.replace("{caseId:guid}", encodeURIComponent(caseId));
}
