import type { PrivateCapitalScope } from "@/types/private-capital";

const legacyTargetFields = ["tenantId", "companyId", "entityId", "fundProfileId", "ledgerBookId", "currency", "callId", "investorId"] as const;
const targetFields = [...legacyTargetFields, "commitmentId"] as const;
export type NoticeOperationTarget = PrivateCapitalScope & { callId: string; investorId: string; commitmentId: string };
export interface PendingNoticeOperation { version: 2; target: NoticeOperationTarget; operationId: string; legacyOmittedSelector?: true }
interface ReadOptions { investorInstallmentCount?: number }
const uuid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
/** Public identity of the backend's retained run for this operation UUID. */
export const noticeOperationRunId = (operationId: string) => `capital-call-notice-op-${operationId.replaceAll("-", "").toLowerCase()}`;

export function noticeOperationTarget(scope: PrivateCapitalScope, callId: string, investorId: string, commitmentId: string): NoticeOperationTarget {
  const target: NoticeOperationTarget = { tenantId: scope.tenantId, companyId: scope.companyId, entityId: scope.entityId,
    fundProfileId: scope.fundProfileId, ledgerBookId: scope.ledgerBookId, currency: scope.currency, callId, investorId, commitmentId };
  if (targetFields.some(field => typeof target[field] !== "string" || !target[field].trim())) throw new Error("Notice recovery requires the exact retained scope, call, investor and commitment.");
  return Object.freeze(target);
}
export function noticeOperationStorageKey(target: NoticeOperationTarget) {
  return `meridian.private-capital.notice-operation.v2:${JSON.stringify(targetFields.map(field => target[field]))}`;
}
const legacyKey = (target: NoticeOperationTarget) => `meridian.private-capital.notice-operation.v1:${JSON.stringify(legacyTargetFields.map(field => target[field]))}`;
function validOperationId(value: unknown): value is string { return typeof value === "string" && uuid.test(value) && !/^0{8}-0{4}-0{4}-0{4}-0{12}$/.test(value); }
function isPending(value: unknown, target: NoticeOperationTarget): value is PendingNoticeOperation {
  if (!value || typeof value !== "object" || Array.isArray(value)) return false;
  const record = value as Partial<PendingNoticeOperation>;
  return Object.keys(value).length === (record.legacyOmittedSelector === true ? 4 : 3) && record.version === 2
    && (record.legacyOmittedSelector === undefined || record.legacyOmittedSelector === true) && validOperationId(record.operationId)
    && !!record.target && typeof record.target === "object" && Object.keys(record.target).length === targetFields.length
    && targetFields.every(field => record.target![field] === target[field]);
}
function isLegacy(value: unknown, target: NoticeOperationTarget): value is { version: 1; operationId: string; target: Omit<NoticeOperationTarget, "commitmentId"> } {
  if (!value || typeof value !== "object" || Array.isArray(value)) return false;
  const record = value as Record<string, unknown>;
  return Object.keys(record).length === 3 && record.version === 1 && validOperationId(record.operationId)
    && !!record.target && typeof record.target === "object" && Object.keys(record.target).length === legacyTargetFields.length
    && legacyTargetFields.every(field => (record.target as Record<string, unknown>)[field] === target[field]);
}
function parse(raw: string): unknown { try { return JSON.parse(raw); } catch { return null; } }
function removeLegacy(target: NoticeOperationTarget, operationId: string) {
  const raw = sessionStorage.getItem(legacyKey(target));
  if (raw === null) return;
  const value = parse(raw);
  if (!isLegacy(value, target) || value.operationId !== operationId) throw new Error("The prior investor-level notice intent changed. Inspect retained runs before continuing.");
  sessionStorage.removeItem(legacyKey(target));
}
export function readPendingNoticeOperation(target: NoticeOperationTarget, options: ReadOptions = {}): { operation: PendingNoticeOperation | null; clearedMalformed: boolean } {
  const key = noticeOperationStorageKey(target);
  let raw: string | null;
  try { raw = sessionStorage.getItem(key); }
  catch { throw new Error("Tab recovery storage is unavailable. Notice generation has not been sent; restore storage access before continuing."); }
  if (raw === null) {
    let previous: string | null;
    try { previous = sessionStorage.getItem(legacyKey(target)); }
    catch { throw new Error("Tab recovery storage is unavailable. No notice request was sent."); }
    if (previous === null) return { operation: null, clearedMalformed: false };
    const legacy = parse(previous);
    if (isLegacy(legacy, target)) {
      if (options.investorInstallmentCount !== 1) throw new Error("A prior investor-level notice intent cannot be assigned to multiple installments. Inspect retained reporting history; automatic recovery is unavailable for this legacy request.");
      const migrated: PendingNoticeOperation = { version: 2, target, operationId: legacy.operationId, legacyOmittedSelector: true };
      writePendingNoticeOperation(migrated);
      removeLegacy(target, migrated.operationId);
      return { operation: migrated, clearedMalformed: false };
    }
    try { sessionStorage.removeItem(legacyKey(target)); } catch { throw new Error("An invalid notice recovery record could not be removed. No notice request was sent."); }
    return { operation: null, clearedMalformed: true };
  }
  const value = parse(raw);
  if (isPending(value, target)) {
    if (value.legacyOmittedSelector) {
      if (options.investorInstallmentCount !== undefined && options.investorInstallmentCount !== 1) throw new Error("The prior investor-level notice intent has no unambiguous installment. Automatic recovery is unavailable.");
      removeLegacy(target, value.operationId);
    }
    return { operation: value, clearedMalformed: false };
  }
  try { sessionStorage.removeItem(key); }
  catch { throw new Error("An invalid notice recovery record could not be removed. No notice request was sent."); }
  return { operation: null, clearedMalformed: true };
}
export function retainPendingNoticeOperation(operation: PendingNoticeOperation) {
  if (!isPending(operation, operation.target)) throw new Error("The pending notice operation is invalid. No notice request was sent.");
  const prior = readPendingNoticeOperation(operation.target).operation;
  if (prior && prior.operationId !== operation.operationId) throw new Error("Another notice operation is pending for this investor. Recover its original operation before starting another run.");
  writePendingNoticeOperation(operation);
}
/** Called only by an explicit action after an exact, authorized terminal-failure response. */
export function replaceFailedNoticeOperation(original: PendingNoticeOperation, next: PendingNoticeOperation) {
  if (!isPending(original, original.target) || !isPending(next, original.target) || original.operationId === next.operationId) throw new Error("The replacement notice must have a new identity and the same exact target. No request was sent.");
  const retained = readPendingNoticeOperation(original.target).operation;
  if (!retained || ![original.operationId, next.operationId].includes(retained.operationId)) throw new Error("The confirmed failed notice intent changed. Reload and recover the retained operation before starting another run.");
  writePendingNoticeOperation(next);
}
function writePendingNoticeOperation(operation: PendingNoticeOperation) {
  const key = noticeOperationStorageKey(operation.target), serialized = JSON.stringify(operation);
  try {
    sessionStorage.setItem(key, serialized);
    if (sessionStorage.getItem(key) !== serialized) throw new Error("Recovery record was not retained.");
  } catch { throw new Error("Notice generation has not been sent because its original operation could not be retained in this tab. Restore storage access and retry the same request."); }
}
export function completePendingNoticeOperation(operation: PendingNoticeOperation) {
  const retained = readPendingNoticeOperation(operation.target).operation;
  if (retained && retained.operationId !== operation.operationId) throw new Error("Notice recovery changed while the request was in flight. Inspect retained runs before continuing.");
  try { sessionStorage.removeItem(noticeOperationStorageKey(operation.target)); }
  catch { throw new Error("The notice result was confirmed, but its tab recovery record could not be cleared. Recover the original operation before starting another run."); }
}
