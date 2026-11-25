import { apiGetJson, apiPostJson, type ApiRequestOptions } from "@/lib/api";
import type { CorporateActionCaseScope } from "@/types/workstation-7";
import type {
  AttestCashDividendRequest, CashDividendAccountRoleBinding, CashDividendAttestationResult, CashDividendAttestationWorkspace,
  CashDividendDeclarationResult, CashDividendPostingResult, ChangeCashDividendBookDeclarationRequest,
  PostAttestedCashDividendRequest
} from "@/types/cash-dividend-attestation";

export const CASH_DIVIDEND_ATTESTATION_POLICY = "single-principal-cash-dividend-v1";
export const CASH_DIVIDEND_ATTESTATION_CONFIRMATION = "I inspected this exact regular mandatory cash-dividend projection and the complete related-event entitlement. I attest as its preparer, not as an independent reviewer. Existing posting controls remain required.";

function endpoint(caseId: string) {
  return `/api/security-master/corporate-actions/cases/${encodeURIComponent(caseId)}/cash-dividend-attestation`;
}

export async function getCashDividendAttestationWorkspace(caseId: string, expectedScope: CorporateActionCaseScope,
  options: ApiRequestOptions = {}) {
  const value = await apiGetJson<CashDividendAttestationWorkspace>(endpoint(caseId),
    { ...options, allowDevelopmentFallback: false });
  if (!value?.case || value.case.caseId !== caseId || !sameCashDividendScope(value.scope, expectedScope)
      || !sameCashDividendScope(value.case.scope, expectedScope))
    throw new Error("The cash-dividend workspace does not match the selected case and book. Refresh before proceeding.");
  if (value.preview && (value.preview.case.caseId !== caseId
      || !sameCashDividendScope(value.preview.case.scope, expectedScope)
      || value.preview.projection.caseId !== caseId
      || value.preview.projection.ledgerBookId !== expectedScope.ledgerBookId))
    throw new Error("The retained cash-dividend preview does not match this case and book.");
  if (value.attestation && value.attestation.caseId !== caseId || value.posting &&
      (value.posting.caseId !== caseId || value.posting.ledgerBookId !== expectedScope.ledgerBookId))
    throw new Error("The retained cash-dividend decision or posting belongs to a different case or book.");
  return value;
}

export async function changeCashDividendBookDeclaration(caseId: string,
  request: ChangeCashDividendBookDeclarationRequest, options: ApiRequestOptions = {}) {
  const result = await apiPostJson<CashDividendDeclarationResult>(`${endpoint(caseId)}/declaration`, request, options);
  if (result.declaration.ledgerBookId !== request.ledgerBookId || result.declaration.enabled !== request.enabled
      || result.declaration.policyVersion !== request.policyVersion || result.declaration.policyHash !== request.policyHash
      || request.accountRoleBinding && !sameAccountingBinding(result.declaration.accountRoleBinding, request.accountRoleBinding))
    throw new Error("The declaration response does not match this exact command. Inspect the retained result before retrying.");
  return result;
}

export async function attestCashDividend(caseId: string, request: AttestCashDividendRequest, options: ApiRequestOptions = {}) {
  const result = await apiPostJson<CashDividendAttestationResult>(`${endpoint(caseId)}/attest`, request, options);
  if (result.case.caseId !== caseId || result.attestation.caseId !== caseId
      || result.attestation.projectionId !== request.projectionId
      || result.attestation.entitlementSnapshotHash !== request.expectedEligibilityHash)
    throw new Error("The attestation response does not match this exact command. Inspect the retained result before retrying.");
  return result;
}

export async function postAttestedCashDividend(caseId: string, request: PostAttestedCashDividendRequest,
  options: ApiRequestOptions = {}) {
  const result = await apiPostJson<CashDividendPostingResult>(`${endpoint(caseId)}/post`, request, options);
  if (result.case.caseId !== caseId || result.posting.caseId !== caseId
      || result.posting.projectionId !== request.projectionId || result.posting.attestationId !== request.attestationId)
    throw new Error("The posting response does not match this exact command. Inspect the retained journal before retrying.");
  return result;
}

export function sameCashDividendScope(actual: CorporateActionCaseScope | undefined, expected: CorporateActionCaseScope) {
  return sameText(actual?.tenantId, expected.tenantId) && sameText(actual?.companyId, expected.companyId)
    && sameText(actual?.ledgerBookId, expected.ledgerBookId);
}
export function sameCashDividendActor(left?: string | null, right?: string | null) {
  return sameText(left, right);
}
function sameText(left?: string | null, right?: string | null) {
  return Boolean(left?.trim() && right?.trim()) && left!.trim().toLowerCase() === right!.trim().toLowerCase();
}

function sameAccountingBinding(actual: CashDividendAccountRoleBinding | null | undefined, expected: CashDividendAccountRoleBinding) {
  return actual != null && (["schemaVersion", "ledgerBookId", "dividendReceivableAccount", "dividendIncomeAccount",
    "dividendReceivableAccountName", "dividendIncomeAccountName", "rulePackId", "rulePackVersion", "selectedRuleId", "selectedRuleVersion"] as const)
    .every(field => actual[field] === expected[field]);
}
