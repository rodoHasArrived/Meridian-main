import type { PrivateCapitalScope } from "@/types/private-capital";

const targetFields = ["tenantId", "companyId", "entityId", "fundProfileId", "ledgerBookId", "currency", "callId", "investorId"] as const;
export type NoticeOperationTarget = PrivateCapitalScope & { callId: string; investorId: string };
export interface PendingNoticeOperation { version: 1; target: NoticeOperationTarget; operationId: string }
const uuid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
/** Public identity of the backend's retained run for this operation UUID. */
export const noticeOperationRunId = (operationId: string) => `capital-call-notice-op-${operationId.replaceAll("-", "").toLowerCase()}`;

export function noticeOperationTarget(scope: PrivateCapitalScope, callId: string, investorId: string): NoticeOperationTarget {
  const target: NoticeOperationTarget = { tenantId: scope.tenantId, companyId: scope.companyId, entityId: scope.entityId,
    fundProfileId: scope.fundProfileId, ledgerBookId: scope.ledgerBookId, currency: scope.currency, callId, investorId };
  if (targetFields.some(field => typeof target[field] !== "string" || !target[field].trim())) throw new Error("Notice recovery requires the exact retained scope, call and investor.");
  return Object.freeze(target);
}
export function noticeOperationStorageKey(target: NoticeOperationTarget) {
  return `meridian.private-capital.notice-operation.v1:${JSON.stringify(targetFields.map(field => target[field]))}`;
}
function isPending(value: unknown, target: NoticeOperationTarget): value is PendingNoticeOperation {
  if (!value || typeof value !== "object" || Array.isArray(value)) return false;
  const record = value as Partial<PendingNoticeOperation>;
  return Object.keys(value).length === 3 && record.version === 1 && typeof record.operationId === "string"
    && uuid.test(record.operationId) && !/^0{8}-0{4}-0{4}-0{4}-0{12}$/.test(record.operationId)
    && !!record.target && typeof record.target === "object" && Object.keys(record.target).length === targetFields.length
    && targetFields.every(field => record.target![field] === target[field]);
}
export function readPendingNoticeOperation(target: NoticeOperationTarget): { operation: PendingNoticeOperation | null; clearedMalformed: boolean } {
  const key = noticeOperationStorageKey(target);
  let raw: string | null;
  try { raw = sessionStorage.getItem(key); }
  catch { throw new Error("Tab recovery storage is unavailable. Notice generation has not been sent; restore storage access before continuing."); }
  if (raw === null) return { operation: null, clearedMalformed: false };
  let value: unknown;
  try { value = JSON.parse(raw); } catch { value = null; }
  if (isPending(value, target)) return { operation: value, clearedMalformed: false };
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
