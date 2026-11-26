import { apiGetJson, apiPostJson } from "@/lib/api";

export interface SecurityImpactIssue { code: string; reason: string }
export interface SecurityImpactProjection {
  modelVersion: string;
  projection: { schedule: Array<Record<string, unknown>>; blockedReason?: string | null; blocker?: { code: string; message: string } | null };
}
export interface SecurityImpactPreviewReceipt {
  contentSha256: string;
  dependencyFingerprint: string;
  capturedAt: string;
  asOf: string;
  securityId: string;
  revisionId: string;
  canonicalVersion: number;
  fundProfileId?: string | null;
  previousTerms?: { assetSpecificTerms: Record<string, unknown> };
  proposedTerms?: { assetSpecificTerms: Record<string, unknown> };
  books: { completeness: string; bookIds: string[]; basis: string; issues: SecurityImpactIssue[] };
  periods: Array<{ bookId: string; periodId: string; version: number; startDate: string; endDate: string; status: string }>;
  reports: { completeness: string; runIds: string[]; issues: SecurityImpactIssue[] };
  before: SecurityImpactProjection | null;
  after: SecurityImpactProjection | null;
  issues: SecurityImpactIssue[];
}
export function createSecurityImpactPreview(securityId: string, revisionId: string, asOf: string, expectedFundProfileId?: string): Promise<SecurityImpactPreviewReceipt> {
  return apiPostJson<SecurityImpactPreviewReceipt>(`/api/security-master/${encodeURIComponent(securityId)}/revisions/${encodeURIComponent(revisionId)}/impact-preview`, { asOf }, { allowDevelopmentFallback: false }).then(receipt => {
    assertReceipt(receipt);
    if (receipt.securityId.toLowerCase() !== securityId.toLowerCase() || receipt.revisionId.toLowerCase() !== revisionId.toLowerCase()) throw new Error("Impact evidence belongs to another proposal.");
    if (expectedFundProfileId && receipt.fundProfileId !== expectedFundProfileId) throw new Error("Impact evidence does not match the draft's retained fund profile.");
    return receipt;
  });
}
export function getSecurityImpactPreview(receiptId: string, signal?: AbortSignal): Promise<SecurityImpactPreviewReceipt> {
  return apiGetJson<SecurityImpactPreviewReceipt>(`/api/security-master/impact-previews/${encodeURIComponent(receiptId)}`, { signal, allowDevelopmentFallback: false }).then(receipt => {
    assertReceipt(receipt);
    if (receipt.contentSha256 !== receiptId) throw new Error("Impact receipt identity does not match the requested evidence.");
    return receipt;
  });
}

function assertReceipt(receipt: SecurityImpactPreviewReceipt): void {
  if (!receipt || !/^[0-9a-f]{64}$/.test(receipt.contentSha256) || !/^[0-9a-f]{64}$/.test(receipt.dependencyFingerprint)
    || !receipt.securityId || !receipt.revisionId || !Number.isSafeInteger(receipt.canonicalVersion)
    || !receipt.books || !receipt.reports || !Array.isArray(receipt.periods) || !Array.isArray(receipt.issues))
    throw new Error("Impact evidence has missing or inconsistent retained authority.");
}
