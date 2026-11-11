import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { completePendingNoticeOperation, noticeOperationStorageKey, noticeOperationTarget, readPendingNoticeOperation, retainPendingNoticeOperation } from "@/lib/private-capital-notice-intent";
import { capitalCall } from "@/screens/private-capital.test-fixtures";

const target = noticeOperationTarget(capitalCall.scope, capitalCall.callId, "investor-a", "commitment-a");
const operation = { version: 2 as const, target, operationId: "8f7c8a52-1a44-4f4e-9a2b-6f0d5a7c9e66" };
describe("Tab-scoped notice operation intent", () => {
  beforeEach(() => sessionStorage.clear());
  afterEach(() => vi.restoreAllMocks());
  it("cleans malformed or foreign-scope records instead of replaying them", () => {
    const key = noticeOperationStorageKey(target);
    for (const content of ["not JSON", JSON.stringify({ ...operation, target: { ...target, companyId: "foreign" } }), JSON.stringify({ ...operation, amount: "100" })]) {
      sessionStorage.setItem(key, content);
      expect(readPendingNoticeOperation(target)).toEqual({ operation: null, clearedMalformed: true });
      expect(sessionStorage.getItem(key)).toBeNull();
    }
  });
  it("retains only operation and exact target identifiers and isolates another investor", () => {
    retainPendingNoticeOperation(operation);
    expect(readPendingNoticeOperation(target).operation).toEqual(operation);
    expect(readPendingNoticeOperation({ ...target, investorId: "investor-b" }).operation).toBeNull();
    expect(readPendingNoticeOperation({ ...target, commitmentId: "commitment-b" }).operation).toBeNull();
    expect(Object.keys(JSON.parse(sessionStorage.getItem(noticeOperationStorageKey(target))!)).sort()).toEqual(["operationId", "target", "version"]);
  });
  it("refuses to replace or clear an unresolved operation with another identity", () => {
    retainPendingNoticeOperation(operation);
    const other = { ...operation, operationId: "9f7c8a52-1a44-4f4e-9a2b-6f0d5a7c9e77" };
    expect(() => retainPendingNoticeOperation(other)).toThrow("Another notice operation is pending");
    expect(() => completePendingNoticeOperation(other)).toThrow("Notice recovery changed");
    expect(readPendingNoticeOperation(target).operation).toEqual(operation);
  });
  it("leaves legacy recovery intact when migration cannot be durably retained", () => {
    const legacyTarget = { tenantId: target.tenantId, companyId: target.companyId, entityId: target.entityId, fundProfileId: target.fundProfileId,
      ledgerBookId: target.ledgerBookId, currency: target.currency, callId: target.callId, investorId: target.investorId };
    const oldKey = `meridian.private-capital.notice-operation.v1:${JSON.stringify(Object.values(legacyTarget))}`;
    const old = JSON.stringify({ version: 1, target: legacyTarget, operationId: operation.operationId });
    sessionStorage.setItem(oldKey, old);
    vi.spyOn(Storage.prototype, "setItem").mockImplementation(() => { throw new Error("Storage unavailable"); });
    expect(() => readPendingNoticeOperation(target, { investorInstallmentCount: 1 })).toThrow("original operation could not be retained");
    expect(sessionStorage.getItem(oldKey)).toBe(old);
    expect(sessionStorage.getItem(noticeOperationStorageKey(target))).toBeNull();
  });
});
