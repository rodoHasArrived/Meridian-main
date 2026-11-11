import { beforeEach, describe, expect, it } from "vitest";
import { completePendingNoticeOperation, noticeOperationStorageKey, noticeOperationTarget, readPendingNoticeOperation, retainPendingNoticeOperation } from "@/lib/private-capital-notice-intent";
import { capitalCall } from "@/screens/private-capital.test-fixtures";

const target = noticeOperationTarget(capitalCall.scope, capitalCall.callId, "investor-a");
const operation = { version: 1 as const, target, operationId: "8f7c8a52-1a44-4f4e-9a2b-6f0d5a7c9e66" };
describe("Tab-scoped notice operation intent", () => {
  beforeEach(() => sessionStorage.clear());
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
    expect(Object.keys(JSON.parse(sessionStorage.getItem(noticeOperationStorageKey(target))!)).sort()).toEqual(["operationId", "target", "version"]);
  });
  it("refuses to replace or clear an unresolved operation with another identity", () => {
    retainPendingNoticeOperation(operation);
    const other = { ...operation, operationId: "9f7c8a52-1a44-4f4e-9a2b-6f0d5a7c9e77" };
    expect(() => retainPendingNoticeOperation(other)).toThrow("Another notice operation is pending");
    expect(() => completePendingNoticeOperation(other)).toThrow("Notice recovery changed");
    expect(readPendingNoticeOperation(target).operation).toEqual(operation);
  });
});
