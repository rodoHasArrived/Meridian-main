import { apiGetJson } from "@/lib/api/http-client";

export interface SecurityReportRateValue { field: string; rawValue: string; unit: string | null; normalizedFraction: string | null; reason: string | null }
export interface RetainedSecurityDefinition { securityId: string; displayName: string; assetClass: string; version: number; currency: string; effectiveFrom: string; assetSpecificTerms: unknown }
export interface SecurityReportInput {
  journalEntryId: string; receiptSha256: string; security: RetainedSecurityDefinition; modelVersion: string;
  calculationContext: Record<string, unknown>; source: unknown; rates: SecurityReportRateValue[]; changeProvenance: string;
  attribution?: { securityVersion: number; eventActor: string | null; eventRecordedAt: string | null; revisionId: string | null;
    draftedBy: string | null; submittedBy: string | null; approvedBy: string | null; approvedAt: string | null;
    approvalId: string | null; evidenceRoute: string | null; gaps: string[] } | null;
}
export interface SecurityReportInputRun {
  runId: string; reportAsOf: string; sourceCutoffUtc: string; releasedAtUtc: string; sourceCheckpointHash: string | null;
  inputs: SecurityReportInput[]; gaps: string[];
}
export interface SecurityReportInputCompare {
  securityId: string; currentCanonical: RetainedSecurityDefinition | null; currentRates: SecurityReportRateValue[];
  reports: SecurityReportInputRun[]; gaps: string[];
}
export interface SecurityReportInputLink { securityId: string; journalEntryId: string; exactInputAvailable: boolean }

export function getSecurityReportInputs(securityId: string, runId?: string | null, signal?: AbortSignal): Promise<SecurityReportInputCompare> {
  const query = runId ? `?runId=${encodeURIComponent(runId)}` : "";
  return apiGetJson(`/api/security-master/${encodeURIComponent(securityId)}/report-inputs${query}`, { signal, allowDevelopmentFallback: false });
}
export function getReportSecurityInputLinks(runId: string, signal?: AbortSignal): Promise<SecurityReportInputLink[]> {
  return apiGetJson(`/api/security-master/report-runs/${encodeURIComponent(runId)}/security-inputs`, { signal, allowDevelopmentFallback: false });
}
