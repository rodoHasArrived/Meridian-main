import { apiGetJson, apiPostJson, type ApiRequestOptions } from "@/lib/api";
import {
  PREPARER_ATTESTATION_DISCLOSURE,
  type AttestationRequest, type AttestationSubject, type AttestationWorkspace,
  type PreparerReceipt, type RevocationRequest, type AttestationRevocation
} from "@/lib/single-principal-attestation-api";

export const PREPARER_DRAFT_CHECK = "ATTEST-DRAFT-CHECK";
export const PREPARER_DRAFT_POLICY = "single-principal-evidence-v2";
const endpoint = "/api/workstation/evidence/attestations";
const guid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
const digest = /^[0-9a-f]{64}$/;

export interface DraftAttestationWorkspace extends AttestationWorkspace { canAttestDrafts?: boolean; canRevokeDrafts?: boolean }
export interface PreparerDraftSnapshot extends AttestationSubject { preparedBy: string; confirmation: string }

export function preparerDraftSubjectId(securityId: string, runId: string) {
  if (![securityId, runId].every(value => guid.test(value) && value !== "00000000-0000-0000-0000-000000000000"))
    throw new Error("Choose an exact retained Security calculation run before checking it.");
  return `Security/${securityId.toLowerCase()}/${runId.toLowerCase()}`;
}

export async function getDraftAttestationWorkspace(options: ApiRequestOptions = {}) {
  const workspace = await apiGetJson<DraftAttestationWorkspace>(endpoint, { ...options, allowDevelopmentFallback: false });
  const state = workspace?.state;
  if (typeof workspace?.actor !== "string" || !workspace.actor.trim()
      || typeof state?.scope?.tenantId !== "string" || !state.scope.tenantId.trim()
      || typeof state.scope.companyId !== "string" || !state.scope.companyId.trim()
      || typeof state.policyEnabled !== "boolean" || !state.policyVersion?.trim() || !digest.test(state.policyHash)
      || !Array.isArray(state.allowedActions) || !Array.isArray(state.receipts) || !Array.isArray(state.revocations))
    throw new Error("The server did not return a complete scoped draft-check workspace.");
  return workspace;
}

export async function getPreparerDraftSnapshot(securityId: string, runId: string, options: ApiRequestOptions = {}) {
  const expected = preparerDraftSubjectId(securityId, runId);
  const subject = await apiGetJson<PreparerDraftSnapshot>(`${endpoint}/drafts/${encodeURIComponent(securityId)}/${encodeURIComponent(runId)}`,
    { ...options, allowDevelopmentFallback: false });
  if (!subject || subject.subjectKind !== "contractual-cashflow-draft" || subject.subjectId !== expected
      || !digest.test(subject.revision) || !digest.test(subject.hash) || !digest.test(subject.sourceHash)
      || typeof subject.preparedBy !== "string" || !subject.preparedBy.trim()
      || typeof subject.confirmation !== "string" || !subject.confirmation.trim() || typeof subject.eligible !== "boolean"
      || !Array.isArray(subject.checks) || subject.checks.length === 0
      || subject.checks.some(check => typeof check !== "string" || !check.trim()))
    throw new Error("The draft-check snapshot does not match this retained run. Refresh before continuing.");
  return subject;
}

export function isSupportedDraftReceipt(receipt: PreparerReceipt) {
  return Boolean(receipt) && receipt.schemaVersion === 1 && receipt.decisionKind === "PreparerAttestation"
    && receipt.actionId === PREPARER_DRAFT_CHECK && receipt.policyVersion === PREPARER_DRAFT_POLICY
    && receipt.disclosure === PREPARER_ATTESTATION_DISCLOSURE;
}

export async function recordPreparerDraftCheck(request: AttestationRequest, options: ApiRequestOptions = {}) {
  if (request.actionId !== PREPARER_DRAFT_CHECK) throw new Error("Only the named non-posting draft-check action is supported here.");
  const receipt = await apiPostJson<PreparerReceipt>(`${endpoint}/receipts`, request, { ...options, allowDevelopmentFallback: false });
  if (!isSupportedDraftReceipt(receipt) || receipt.commandId !== request.commandId
      || receipt.subjectKind !== request.subjectKind || receipt.subjectId !== request.subjectId
      || receipt.subjectRevision !== request.subjectRevision || receipt.subjectHash !== request.subjectHash)
    throw new Error("The draft-check response does not match the command. Its outcome is unconfirmed; retry the same command.");
  return receipt;
}

export async function revokePreparerDraftCheck(request: RevocationRequest, options: ApiRequestOptions = {}) {
  const result = await apiPostJson<AttestationRevocation>(`${endpoint}/revocations`, request, { ...options, allowDevelopmentFallback: false });
  if (!result || result.commandId !== request.commandId || result.receiptId !== request.receiptId || result.revision !== 2)
    throw new Error("The revocation response does not match the command. Its outcome is unconfirmed; retry the same command.");
  return result;
}
