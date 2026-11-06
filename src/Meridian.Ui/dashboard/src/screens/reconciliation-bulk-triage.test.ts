import { describe, expect, it } from "vitest";
import type { ReconciliationBulkCaseworkResult } from "@/types";
import {
  BULK_TRIAGE_MAX_BREAKS,
  bulkTriageDraftProblem,
  buildBulkCaseworkRequest,
  emptyBulkTriageDraft,
  summarizeBulkDryRun,
  summarizeBulkExecute
} from "./reconciliation-bulk-triage";

const options = { operationId: "bulk-1", operator: null };

function result(entries: Array<{ breakId: string; succeeded: boolean; wouldSucceed: boolean; error?: string | null }>, dryRun: boolean): ReconciliationBulkCaseworkResult {
  return {
    bulkActionId: "bulk-1",
    idempotencyKey: "bulk-1",
    dryRun,
    requestedCount: entries.length,
    succeededCount: 0,
    failedCount: 0,
    results: entries.map((entry) => ({ ...entry, item: entry.succeeded ? { breakId: entry.breakId } as never : null })),
    inputHashSha256: "hash",
    outcome: {} as ReconciliationBulkCaseworkResult["outcome"]
  };
}

describe("bulk break triage requests (C2)", () => {
  it("asks the server to name the operator for assign to me, and keeps dry-run and execute keys apart", () => {
    const dryRun = buildBulkCaseworkRequest(emptyBulkTriageDraft("assign"), ["a", "b"], { ...options, dryRun: true });
    const execute = buildBulkCaseworkRequest(emptyBulkTriageDraft("assign"), ["a"], { ...options, dryRun: false });
    expect(dryRun).toMatchObject({ action: "Assign", breakIds: ["a", "b"], assignToCurrentOperator: true, dryRun: true, allowPartialSuccess: true, maxCaseCount: BULK_TRIAGE_MAX_BREAKS });
    expect(dryRun.assignee).toBeUndefined();
    expect(execute).toMatchObject({ dryRun: false, allowPartialSuccess: false, correlationId: "bulk-1" });
    expect(dryRun.idempotencyKey).toBe("bulk-1:dry-run");
    expect(execute.idempotencyKey).toBe("bulk-1:execute");
    expect(execute.commandId).toBe(execute.idempotencyKey);
  });

  it("routes to the named owner and resolves with the reason, codes and evidence", () => {
    const route = buildBulkCaseworkRequest({ ...emptyBulkTriageDraft("route"), assignee: " custody-desk ", reason: "Lag" }, ["a"], { ...options, dryRun: true });
    expect(route).toMatchObject({ action: "Assign", assignee: "custody-desk", note: "Lag" });
    expect(route.assignToCurrentOperator).toBeUndefined();

    const resolve = buildBulkCaseworkRequest({
      ...emptyBulkTriageDraft("resolve"),
      reason: " Journal posted. ",
      rootCauseCode: "LedgerClassification",
      resolutionCode: "LedgerAdjusted",
      evidenceLink: "journal:je-1"
    }, ["a"], { ...options, dryRun: false });
    expect(resolve).toMatchObject({
      action: "Resolve",
      reason: "Journal posted.",
      note: "Journal posted.",
      rootCauseCode: "LedgerClassification",
      resolutionCode: "LedgerAdjusted",
      evidenceLinks: ["journal:je-1"]
    });
  });

  it("explains what is missing before a check can run", () => {
    expect(bulkTriageDraftProblem(emptyBulkTriageDraft("assign"), 0)).toBe("Select at least one break.");
    expect(bulkTriageDraftProblem(emptyBulkTriageDraft("assign"), BULK_TRIAGE_MAX_BREAKS + 1)).toMatch(/at most 100/);
    expect(bulkTriageDraftProblem(emptyBulkTriageDraft("assign"), 2)).toBeNull();
    expect(bulkTriageDraftProblem(emptyBulkTriageDraft("route"), 2)).toBe("Enter the owner to route these breaks to.");
    expect(bulkTriageDraftProblem({ ...emptyBulkTriageDraft("resolve"), reason: "Fixed" }, 2)).toBe("Choose a root cause.");
    expect(bulkTriageDraftProblem({ ...emptyBulkTriageDraft("resolve"), reason: "Fixed", rootCauseCode: "AccrualTiming" }, 2)).toBe("Choose a resolution.");
  });

  it("summarizes the dry run and the execute", () => {
    const preview = summarizeBulkDryRun("resolve", result([
      { breakId: "a", succeeded: false, wouldSucceed: true },
      { breakId: "b", succeeded: false, wouldSucceed: false, error: "Cannot resolve case from Open." }
    ], true));
    expect(preview).toEqual({
      passedIds: ["a"],
      blocked: [{ breakId: "b", error: "Cannot resolve case from Open." }],
      summary: "1 of 2 breaks can be resolved. The others stay as they are."
    });
    expect(summarizeBulkDryRun("assign", result([{ breakId: "a", succeeded: false, wouldSucceed: true }], true)).summary).toBe("The break can be assigned.");

    const outcome = summarizeBulkExecute("route", result([
      { breakId: "a", succeeded: true, wouldSucceed: true },
      { breakId: "b", succeeded: false, wouldSucceed: true, error: null }
    ], false));
    expect(outcome.updatedItems.map((item) => item.breakId)).toEqual(["a"]);
    expect(outcome.failed).toEqual([{ breakId: "b", error: "The case was not changed." }]);
    expect(outcome.summary).toBe("1 break routed. Each break keeps its own audit entry.");
  });
});
