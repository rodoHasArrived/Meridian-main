import { apiGetJson, apiPostJson, type ApiRequestOptions } from "@/lib/api";
import { WORKSTATION_API_ENDPOINTS } from "@/lib/workstation-endpoints";
import type { LedgerBook } from "@/types";
import type { CapitalCallIssuanceIntakeRunResult } from "@/lib/api/capital-call-issuance.api";
import type {
  CreatePrivateCapitalCommitment, ImportPrivateCapitalBankObservation, PreviewPrivateCapitalCall, PrivateCapitalCall,
  PrivateCapitalBankObservationImport, PrivateCapitalCallPreview, PrivateCapitalCommitment, PrivateCapitalFundingRequest, PrivateCapitalWorkbench
} from "@/types/private-capital";

const base = "/api/ledger/private-capital";
const authoritative = (options: ApiRequestOptions): ApiRequestOptions => ({ ...options, allowDevelopmentFallback: false });
const endpoint = (bookId: string, suffix: string) => `${base}/${suffix}?${new URLSearchParams({ ledgerBookId: bookId })}`;
function verifyBook(bookId: string, result: { scope: { ledgerBookId: string } }) {
  if (!result?.scope?.ledgerBookId || result.scope.ledgerBookId.toLowerCase() !== bookId.toLowerCase()) {
    throw new Error("The returned capital records do not match the selected ledger book. Refresh before continuing.");
  }
}
export function getPrivateCapitalBooks(options: ApiRequestOptions = {}) {
  return apiGetJson<LedgerBook[]>(WORKSTATION_API_ENDPOINTS.ledgerBooks, authoritative(options));
}
export async function getPrivateCapitalWorkbench(bookId: string, options: ApiRequestOptions = {}) {
  const result = await apiGetJson<PrivateCapitalWorkbench>(endpoint(bookId, "workbench"), authoritative(options));
  verifyBook(bookId, result);
  if (!Array.isArray(result.commitments) || !Array.isArray(result.calls) || !Array.isArray(result.receipts)
    || [...result.commitments, ...result.calls, ...result.receipts].some(row =>
      !row.scope || (["tenantId", "companyId", "entityId", "fundProfileId", "ledgerBookId", "currency"] as const).some(key => row.scope[key] !== result.scope[key]))) {
    throw new Error("Capital records contain incomplete or inconsistent scope. Refresh before continuing.");
  }
  return result;
}
export async function createPrivateCapitalCommitment(bookId: string, request: CreatePrivateCapitalCommitment, options: ApiRequestOptions = {}) {
  const result = await apiPostJson<PrivateCapitalCommitment>(endpoint(bookId, "commitments"), request, authoritative(options));
  verifyBook(bookId, result); return result;
}
export async function approvePrivateCapitalCommitment(bookId: string, commitmentId: string, revision: number, options: ApiRequestOptions = {}) {
  const result = await apiPostJson<PrivateCapitalCommitment>(endpoint(bookId, `commitments/${encodeURIComponent(commitmentId)}/approve`), { revision }, authoritative(options));
  verifyBook(bookId, result); return result;
}
export async function previewPrivateCapitalCall(bookId: string, request: PreviewPrivateCapitalCall, options: ApiRequestOptions = {}) {
  const result = await apiPostJson<PrivateCapitalCallPreview>(endpoint(bookId, "calls/preview"), request, authoritative(options));
  if (result.call) verifyBook(bookId, result.call);
  if (!Array.isArray(result.blockers) || (result.isReady && (!result.call?.planHash
    || typeof result.call.callId !== "string" || result.call.callId.trim().toLowerCase() !== request.callId.trim().toLowerCase()))) {
    throw new Error("The server did not return a complete allocation preview.");
  }
  return result;
}
export async function retainPrivateCapitalCall(bookId: string, preview: PreviewPrivateCapitalCall, expectedPlanHash: string, options: ApiRequestOptions = {}) {
  const result = await apiPostJson<PrivateCapitalCall>(endpoint(bookId, "calls"), { preview, expectedPlanHash }, authoritative(options));
  verifyBook(bookId, result); return result;
}
export function intakePrivateCapitalCall(bookId: string, callId: string, options: ApiRequestOptions = {}) {
  return apiPostJson<CapitalCallIssuanceIntakeRunResult>(endpoint(bookId, `calls/${encodeURIComponent(callId)}/intake`), {}, authoritative(options));
}
export function intakePrivateCapitalFunding(request: PrivateCapitalFundingRequest, options: ApiRequestOptions = {}) {
  return apiPostJson<CapitalCallIssuanceIntakeRunResult>(endpoint(request.ledgerBookId, `calls/${encodeURIComponent(request.callId)}/funding-intake`), request, authoritative(options));
}
export function generatePrivateCapitalNotice(bookId: string, callId: string, investorId: string, options: ApiRequestOptions = {}) {
  return apiPostJson<unknown>(endpoint(bookId, `calls/${encodeURIComponent(callId)}/investors/${encodeURIComponent(investorId)}/notice`), {}, authoritative(options));
}
export async function importPrivateCapitalBankObservation(bookId: string, request: ImportPrivateCapitalBankObservation, options: ApiRequestOptions = {}) {
  const result = await apiPostJson<PrivateCapitalBankObservationImport>(endpoint(bookId, "bank-observations"), request, authoritative(options));
  if (!result?.bankTransaction?.bankTransactionId || result.evidenceLink !== request.evidenceLink || result.documentId !== request.documentId
    || !/^[a-f0-9]{64}$/i.test(result.sourceHashSha256)) {
    throw new Error("The retained bank observation does not match the reviewed source identity. Inspect the retained observation before retrying.");
  }
  return result;
}
