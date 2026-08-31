import { getJson, type ApiRequestOptions } from "@/lib/api";
import {
  securityMasterCorporateActionCaseConflictEndpoint,
  securityMasterCorporateActionCaseConflictsEndpoint,
} from "@/lib/workstation-endpoints";
import type { CorporateActionConflict } from "@/types";

export function listCorporateActionCaseConflicts(
  caseId: string,
  query: { state?: string | null; take?: number | null } = {},
  options: ApiRequestOptions = {}
) {
  return getJson<CorporateActionConflict[]>(
    securityMasterCorporateActionCaseConflictsEndpoint(caseId, query),
    options
  );
}

export function getCorporateActionCaseConflict(
  caseId: string,
  conflictId: string,
  options: ApiRequestOptions = {}
) {
  return getJson<CorporateActionConflict>(
    securityMasterCorporateActionCaseConflictEndpoint(caseId, conflictId),
    options
  );
}
