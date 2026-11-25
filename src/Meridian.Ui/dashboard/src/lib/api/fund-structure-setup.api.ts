// Fund Structure setup draft validation and creation endpoints. Re-exported through @/lib/api.
// Request and result contracts live in fund-structure-setup.contracts.ts.
import type { FundStructureSetupDraft, FundStructureSetupPreview, FundStructureSetupResult } from "./fund-structure-setup.contracts";
import { FUND_STRUCTURE_API_ENDPOINTS } from "@/lib/workstation-endpoints";
import { postJson, type ApiRequestOptions } from "@/lib/api/http-client";

export async function validateFundStructureSetupDraft(draft: FundStructureSetupDraft, options: ApiRequestOptions = {}): Promise<FundStructureSetupPreview> {
  return postJson<FundStructureSetupPreview>(FUND_STRUCTURE_API_ENDPOINTS.setupDraftValidate, draft, options);
}

export async function createFundStructureSetupDraft(draft: FundStructureSetupDraft, options: ApiRequestOptions = {}): Promise<FundStructureSetupResult> {
  return postJson<FundStructureSetupResult>(FUND_STRUCTURE_API_ENDPOINTS.setupDraftCreate, draft, options);
}
