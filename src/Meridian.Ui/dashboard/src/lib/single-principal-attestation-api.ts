import { apiGetJson, apiPostJson, type ApiRequestOptions } from "@/lib/api";

export const EVIDENCE_ACKNOWLEDGMENT = "ATTEST-EVIDENCE-ACK";
export const PREPARER_ATTESTATION_DISCLOSURE = "Attested by preparer — not independently reviewed";
const endpoint = "/api/workstation/evidence/attestations";
export interface AttestationScope { tenantId: string; companyId: string; ledgerBookId: string | null }
export interface PrincipalDeclaration {
  declarationId: string; revision: number; scope: AttestationScope; enabled: boolean;
  principalId: string; policyVersion: string; policyHash: string; changedBy: string; recordedAt: string; reason: string;
}
export interface PreparerReceipt {
  receiptId: string; commandId: string; assertionId: string; schemaVersion: number;
  decisionKind: string; disclosure: string; scope: AttestationScope; actionId: string;
  subjectKind: string; subjectId: string; subjectRevision: string; subjectHash: string;
  sourceArtifactId: string; sourceHash: string; sourceActor: string | null; sourceProvenance: string;
  preparedBy: string; attestedBy: string; origin: string; declarationId: string;
  declarationRevision: number; policyVersion: string; policyHash: string; recordedAt: string;
  reason: string; confirmation: string; checks: string[];
  sourceSubjectKind?: string | null; sourceSubjectId?: string | null;
}
export interface AttestationRevocation {
  commandId: string; receiptId: string; revision: number; actor: string; recordedAt: string; reason: string;
}
export interface AttestationWorkspace {
  actor: string; canAdministerDeclaration: boolean; canAttest: boolean; canRevoke: boolean;
  state: {
    scope: AttestationScope; policyEnabled: boolean; policyVersion: string; policyHash: string;
    disclosure: string; confirmation: string; allowedActions: string[];
    declaration: PrincipalDeclaration | null; declarationHistory: PrincipalDeclaration[];
    receipts: PreparerReceipt[]; revocations: AttestationRevocation[];
    auditTrail: { commandId: string; sequence: number; action: string; actor: string; recordedAt: string; reason: string; receiptId: string | null }[];
  };
}
export interface AttestationSubject {
  subjectKind: string; subjectId: string; revision: string; hash: string;
  sourceArtifactId: string; sourceHash: string; sourceActor: string | null;
  sourceProvenance: string; eligible: boolean; checks: string[];
  sourceSubjectKind?: string | null; sourceSubjectId?: string | null;
}

export function isSupportedPreparerReceipt(receipt: PreparerReceipt) {
  return receipt.schemaVersion === 1 && receipt.decisionKind === "PreparerAttestation"
    && receipt.actionId === EVIDENCE_ACKNOWLEDGMENT;
}
export interface DeclarationRequest {
  commandId: string; expectedRevision: number; enabled: boolean;
  principalId: string; policyVersion: string; reason: string;
}
export interface AttestationRequest {
  commandId: string; expectedDeclarationRevision: number; actionId: string;
  subjectKind: string; subjectId: string; subjectRevision: string; subjectHash: string;
  reason: string; confirmation: string;
}
export interface RevocationRequest {
  commandId: string; receiptId: string; expectedReceiptRevision: number; reason: string;
}

export function getAttestationWorkspace(options: ApiRequestOptions = {}) {
  return apiGetJson<AttestationWorkspace>(endpoint, { ...options, allowDevelopmentFallback: false });
}
export async function getAttestationSubject(vaultId: string, documentId: string, options: ApiRequestOptions = {}) {
  const query = new URLSearchParams({ vaultId, documentId });
  const subject = await apiGetJson<AttestationSubject>(`${endpoint}/subjects?${query}`, { ...options, allowDevelopmentFallback: false });
  if (subject.subjectKind !== "evidence-document" || subject.subjectId !== `${vaultId}/${documentId}`
      || !subject.revision || !subject.hash || !subject.sourceHash || !Array.isArray(subject.checks)) {
    throw new Error("The attestation snapshot does not match this retained document. Refresh before continuing.");
  }
  return subject;
}
export function changePrincipalDeclaration(request: DeclarationRequest, options: ApiRequestOptions = {}) {
  return apiPostJson<unknown>(`${endpoint}/declaration`, request, options);
}
export function recordPreparerAttestation(request: AttestationRequest, options: ApiRequestOptions = {}) {
  return apiPostJson<unknown>(`${endpoint}/receipts`, request, options);
}
export function revokePreparerAttestation(request: RevocationRequest, options: ApiRequestOptions = {}) {
  return apiPostJson<unknown>(`${endpoint}/revocations`, request, options);
}
