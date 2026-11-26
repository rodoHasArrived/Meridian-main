import { apiGetJson, apiPostJson, type ApiRequestOptions } from "@/lib/api/http-client";

export interface RateRepairScope {
  fundAccountId: string;
  ledgerBookId: string;
  fundProfileId: string;
  periodId: string;
}
export interface RateRepairCohort {
  sourceSystem: string;
  sourceField: string;
  sourceSchemaVersion: string;
  rateRole: string;
}
export interface RateRepairAction { command: string; enabled: boolean; disabledReason: string | null }
export interface RateRepairCandidate {
  securityId: string; displayName: string; assetClass: string; version: number;
  sourceSystem: string; sourceField: string; sourceSchemaVersion: string | null;
  issuer: string | null; rawValue: string | null; currentUnit: string | null; rateRole: string;
  allowedUnits: string[]; eligible: boolean; blockerCode: string | null; blockerReason: string | null; fixPath: string;
}
export interface RateRepairDiscoveryIssue { securityId: string | null; code: string; message: string }
export interface RateRepairDiscovery {
  asOfUtc: string; candidates: RateRepairCandidate[]; actions: RateRepairAction[];
  skip: number; pageSize: number; scannedCount: number; nextSkip: number | null; isComplete: boolean;
  scopeLabel: string; issues: RateRepairDiscoveryIssue[];
}
export interface CreateRateRepairBatch {
  requestId: string; scope: RateRepairScope; cohort: RateRepairCohort;
  items: Array<{ securityId: string; expectedVersion: number }>;
  unit: string; effectiveFrom: string; evidenceId: string; justification: string; evidenceAppliesToAllRecords: boolean;
}
export interface RateRepairItem {
  securityId: string; baseVersion: number; displayName: string; assetClass: string; rawValue: string | null;
  state: string; revisionId: string | null; workflowId: string | null; impactReceiptId: string | null;
  approvalReceiptId: string | null; errorCode: string | null; error: string | null;
}
export interface RateRepairBatch {
  batchId: string; version: number; createdAtUtc: string; createdBy: string;
  scope: RateRepairScope; cohort: RateRepairCohort; unit: string; effectiveFrom: string;
  evidenceId: string; evidenceSha256: string; justification: string; items: RateRepairItem[]; actions: RateRepairAction[];
}
export interface RateRepairCommand { expectedVersion: number; reviewer?: string | null; rationale?: string | null }
export type RateRepairCommandName = "stage" | "submit" | "approve" | "publish";

const base = "/api/security-master/rate-repairs";
const batchPath = (batchId: string) => `${base}/${encodeURIComponent(batchId)}`;
const authoritative = (options: ApiRequestOptions) => ({ ...options, allowDevelopmentFallback: false });
const sameId = (left: string, right: string) => left.toLowerCase() === right.toLowerCase();

/** All source scalars stay text; only an explicit declaration is submitted. */
export async function discoverRateRepairs(options: ApiRequestOptions = {}, page = { skip: 0, pageSize: 200 }): Promise<RateRepairDiscovery> {
  const result = await apiGetJson<RateRepairDiscovery>(`${base}/candidates?${new URLSearchParams({ skip: String(page.skip), pageSize: String(page.pageSize) })}`, authoritative(options));
  if (!result || result.skip !== page.skip || result.pageSize !== page.pageSize || !Number.isSafeInteger(result.scannedCount)
    || result.scannedCount < 0 || result.scannedCount > page.pageSize || typeof result.isComplete !== "boolean"
    || !result.scopeLabel?.trim() || !Array.isArray(result.issues) || !Array.isArray(result.actions) || !Array.isArray(result.candidates)
    || result.candidates.length > result.scannedCount || (result.nextSkip !== null && (!Number.isSafeInteger(result.nextSkip) || result.nextSkip <= result.skip))
    || (result.isComplete && result.nextSkip !== null)
    || result.candidates.some(row => !Number.isSafeInteger(row.version) || !Array.isArray(row.allowedUnits)
      || (row.rawValue !== null && typeof row.rawValue !== "string")))
    throw new Error("Source scan completeness or exact record authority is unavailable. Refresh the queue after the source response is repaired.");
  return result;
}
export function listRateRepairBatches(options: ApiRequestOptions = {}): Promise<RateRepairBatch[]> {
  return apiGetJson(base, authoritative(options));
}
export async function getRateRepairBatch(batchId: string, options: ApiRequestOptions = {}): Promise<RateRepairBatch> {
  return assertBatch(await apiGetJson<RateRepairBatch>(batchPath(batchId), authoritative(options)), batchId);
}
export async function createRateRepairBatch(input: CreateRateRepairBatch, options: ApiRequestOptions = {}): Promise<RateRepairBatch> {
  const result = assertBatch(await apiPostJson<RateRepairBatch>(base, input, authoritative(options)));
  return assertRateRepairProposal(result, input);
}
export function assertRateRepairProposal(result: RateRepairBatch, input: CreateRateRepairBatch): RateRepairBatch {
  if (!sameId(result.batchId, input.requestId) || result.unit !== input.unit || !sameId(result.scope.fundAccountId, input.scope.fundAccountId)
    || !sameId(result.scope.ledgerBookId, input.scope.ledgerBookId) || result.scope.fundProfileId !== input.scope.fundProfileId
    || result.scope.periodId !== input.scope.periodId || result.evidenceId !== input.evidenceId || result.justification !== input.justification
    || Date.parse(result.effectiveFrom) !== Date.parse(input.effectiveFrom)
    || Object.keys(input.cohort).some(key => result.cohort[key as keyof RateRepairCohort] !== input.cohort[key as keyof RateRepairCohort])
    || result.items.length !== input.items.length || input.items.some(item => !result.items.some(row =>
      sameId(row.securityId, item.securityId) && row.baseVersion === item.expectedVersion))) {
    throw new Error("The retained repair does not match the reviewed scope, evidence, unit and record versions. Reload retained batches before continuing.");
  }
  return result;
}
export function assertFrozenRateRepairUnchanged(previous: RateRepairBatch, result: RateRepairBatch): RateRepairBatch {
  assertRateRepairProposal(result, { requestId: previous.batchId, scope: previous.scope, cohort: previous.cohort,
    items: previous.items.map(item => ({ securityId: item.securityId, expectedVersion: item.baseVersion })), unit: previous.unit,
    effectiveFrom: previous.effectiveFrom, evidenceId: previous.evidenceId, justification: previous.justification, evidenceAppliesToAllRecords: true });
  if (result.evidenceSha256 !== previous.evidenceSha256 || result.version < previous.version
    || previous.items.some(item => result.items.find(row => sameId(row.securityId, item.securityId))?.rawValue !== item.rawValue))
    throw new Error("The command response changed the frozen source scalar or evidence. Refresh retained authority before continuing.");
  return result;
}
export async function commandRateRepairBatch(batchId: string, command: RateRepairCommandName, input: RateRepairCommand,
  options: ApiRequestOptions = {}): Promise<RateRepairBatch> {
  return assertBatch(await apiPostJson<RateRepairBatch>(`${batchPath(batchId)}/${command}`, input, authoritative(options)), batchId);
}
function assertBatch(batch: RateRepairBatch, expectedId?: string): RateRepairBatch {
  if (!batch || !batch.batchId || (expectedId && !sameId(batch.batchId, expectedId)) || !Number.isSafeInteger(batch.version)
    || !Array.isArray(batch.items) || !Array.isArray(batch.actions)
    || new Set(batch.items.map(item => item.securityId.toLowerCase())).size !== batch.items.length
    || batch.items.some(item => !Number.isSafeInteger(item.baseVersion) || (item.rawValue !== null && typeof item.rawValue !== "string"))) {
    throw new Error("The repair response has missing or inconsistent retained authority. Refresh the batch before taking another action.");
  }
  return batch;
}
