import { act, renderHook, waitFor } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { buildSuccessfulVerifiedOperationOutcome } from "@/test/verified-operation-outcome";
import { useAccountingReconciliationViewModel } from "./accounting-screen.view-model";
import type { AccountingReconciliationServices } from "./reconciliation-casework-outcome";
import type { AccountingWorkspaceResponse, ReconciliationBreakQueueItem, ReconciliationCaseworkOperationResult } from "@/types";
import type { ReconciliationSelectionState } from "./reconciliation-view-state";

const item = {
  breakId: "break-a", runId: "run-a", strategyName: "Strategy A", category: "AmountMismatch", status: "Open", variance: 500,
  reason: "Cash variance", assignedTo: null, detectedAt: "2026-10-28T00:00:00Z", lastUpdatedAt: "2026-10-28T00:00:00Z",
  reviewedBy: null, reviewedAt: null, resolvedBy: null, resolvedAt: null, resolutionNote: null,
  routingTarget: "FundTrialBalance", routingDetail: "Review ledger", recommendedAction: "Inspect source"
} as ReconciliationBreakQueueItem;
const data = { metrics: [], reconciliationQueue: [], breakQueue: [item], cashFlow: null, reporting: null } as unknown as AccountingWorkspaceResponse;
function successfulCaseworkOperation(updatedItem: ReconciliationBreakQueueItem): ReconciliationCaseworkOperationResult {
  return {
    transitionStatus: "Succeeded",
    item: updatedItem,
    outcome: buildSuccessfulVerifiedOperationOutcome({
      operationId: `reconciliation-casework:${updatedItem.breakId}`,
      operationKind: "reconciliation.casework.assign",
      correlationId: `assign:${updatedItem.breakId}`
    }),
    error: null,
    errorCode: null,
    validationIssues: []
  };
}

function selection(scopeKey: string): ReconciliationSelectionState {
  return { scopeKey, selectedRunId: "run-a", selectedBreakId: "break-a", selectRun: vi.fn(), selectBreak: vi.fn() };
}
function services(): AccountingReconciliationServices {
  return {
    getBreakQueue: vi.fn().mockResolvedValue([item]), getStatementRuns: vi.fn().mockResolvedValue([]),
    getCalibrationSummary: vi.fn().mockResolvedValue(null), getStatementRun: vi.fn(), getTrialBalance: vi.fn().mockResolvedValue([]),
    previewTransactionLab: vi.fn(), reviewBreak: vi.fn(), resolveBreak: vi.fn()
  };
}

describe("reconciliation selection continuity", () => {
  it("clears pending action state on run changes and ignores a late error after returning to the run", async () => {
    const api = services();
    let rejectAction!: (error: Error) => void;
    api.reviewBreak = vi.fn().mockReturnValue(new Promise<ReconciliationCaseworkOperationResult>((_resolve, reject) => { rejectAction = reject; }));
    const { result, rerender } = renderHook(({ runId }) => useAccountingReconciliationViewModel(data, "reconciliation", api, null, null,
      { ...selection("scope-a"), selectedRunId: runId }), { initialProps: { runId: "run-a" } });
    await waitFor(() => expect(result.current.rows).toHaveLength(1));
    let pending!: Promise<void>;
    act(() => { pending = result.current.assignBreak("break-a"); });
    expect(result.current.breakAction?.breakId).toBe("break-a");
    rerender({ runId: "run-b" });
    expect(result.current.breakAction).toBeNull();
    expect(result.current.actionErrorText).toBeNull();
    rerender({ runId: "run-a" });
    await act(async () => { rejectAction(new Error("old-run-error")); await pending; });
    expect(result.current.actionErrorText).toBeNull();
    expect(result.current.breakAction).toBeNull();
    expect(api.getBreakQueue).toHaveBeenCalledTimes(1);
  });

  it("defaults to a known break run when statement and strategy run identities differ", async () => {
    const api = services();
    api.getStatementRuns = vi.fn().mockResolvedValue([{ runId: "stmt-run-a", importId: "import-a", positionMatches: 0, cashMatches: 0, transactionMatches: 0, openExceptionCount: 1, startedAtUtc: "2026-10-28T00:00:00Z" }]);
    const bootstrap = { ...data, reconciliationQueue: [{ runId: "run-a", strategyName: "Strategy A", mode: "paper", status: "Running", lastUpdated: "2026-10-28T00:00:00Z", breakCount: 1, openBreakCount: 1, reconciliationStatus: "BreaksOpen" }] } as AccountingWorkspaceResponse;
    const replaceSelection = vi.fn();
    const state = { ...selection("scope-a"), selectedRunId: null, selectedBreakId: null, replaceSelection };
    renderHook(() => useAccountingReconciliationViewModel(bootstrap, "reconciliation", api, null, null, state));
    await waitFor(() => expect(replaceSelection).toHaveBeenCalledWith("run-a", "break-a"));
    expect(replaceSelection).not.toHaveBeenCalledWith("stmt-run-a", null);
  });

  it("rejects an old action after switching away from and back to the same scope", async () => {
    const api = services();
    let completeAction!: (value: ReconciliationCaseworkOperationResult) => void;
    let completeQueue!: (value: ReconciliationBreakQueueItem[]) => void;
    api.reviewBreak = vi.fn().mockReturnValue(new Promise<ReconciliationCaseworkOperationResult>((resolve) => { completeAction = resolve; }));
    api.getBreakQueue = vi.fn().mockResolvedValueOnce([item]).mockResolvedValueOnce([item])
      .mockReturnValueOnce(new Promise<ReconciliationBreakQueueItem[]>((resolve) => { completeQueue = resolve; }));
    const { result, rerender } = renderHook(({ scope }) => useAccountingReconciliationViewModel(data, "reconciliation", api, null, null, selection(scope)), { initialProps: { scope: "scope-a" } });
    await waitFor(() => expect(result.current.rows).toHaveLength(1));
    let pending!: Promise<void>;
    act(() => { pending = result.current.assignBreak("break-a"); });
    rerender({ scope: "scope-b" });
    await waitFor(() => expect(api.getBreakQueue).toHaveBeenCalledTimes(2));
    rerender({ scope: "scope-a" });
    expect(result.current.rows).toEqual([]);
    await act(async () => { completeQueue([item]); });
    await waitFor(() => expect(result.current.rows).toHaveLength(1));
    await act(async () => {
      completeAction(successfulCaseworkOperation({ ...item, assignedTo: "old-owner" }));
      await pending;
    });
    expect(result.current.rows[0].ownerLabel).not.toBe("old-owner");
  });

  it("disables actions on retained rows after a same-scope refresh fails", async () => {
    const api = services();
    api.getBreakQueue = vi.fn().mockResolvedValueOnce([item]).mockRejectedValueOnce(new Error("unavailable"));
    const state = selection("scope-a");
    const { result, rerender } = renderHook(({ bootstrap }) => useAccountingReconciliationViewModel(bootstrap, "reconciliation", api, null, null, state), { initialProps: { bootstrap: data } });
    await waitFor(() => expect(result.current.rows[0]?.canAssign).toBe(true));
    rerender({ bootstrap: { ...data, breakQueue: [item] } });
    await waitFor(() => expect(result.current.errorText).toContain("unavailable"));
    expect(result.current.rows[0]).toMatchObject({ canAssign: false, canResolve: false, canDismiss: false });
    expect(result.current.rows[0].assignDisabledReason).toContain("Refresh the current reconciliation queue");
  });

  it("hides prior-scope rows and refuses actions until the new scope queue loads", async () => {
    const api = services();
    api.getBreakQueue = vi.fn().mockResolvedValueOnce([item]).mockReturnValueOnce(new Promise(() => {}));
    const { result, rerender } = renderHook(({ scope }) => useAccountingReconciliationViewModel(data, "reconciliation", api, null, null, selection(scope)), { initialProps: { scope: "scope-a" } });
    await waitFor(() => expect(result.current.rows).toHaveLength(1));
    rerender({ scope: "scope-b" });
    expect(result.current.rows).toEqual([]);
    expect(result.current.selectedDetail).toBeNull();
    await act(async () => { await result.current.assignBreak("break-a"); });
    expect(api.reviewBreak).not.toHaveBeenCalled();
    expect(result.current.actionErrorText).toContain("Refresh the current reconciliation queue");
  });

  it("materializes matching defaults only after successful authoritative reads", async () => {
    const api = services();
    api.getStatementRuns = vi.fn().mockResolvedValue([{ runId: "run-a", importId: "import-a", positionMatches: 0, cashMatches: 0, transactionMatches: 0, openExceptionCount: 1, startedAtUtc: "2026-10-28T00:00:00Z" }]);
    const replaceSelection = vi.fn();
    const state = { ...selection("scope-a"), selectedRunId: null, selectedBreakId: null, replaceSelection };
    renderHook(() => useAccountingReconciliationViewModel(data, "reconciliation", api, null, null, state));
    await waitFor(() => expect(replaceSelection).toHaveBeenCalledWith("run-a", "break-a"));
  });

  it("does not select a break from a different explicitly selected run", async () => {
    const api = services();
    const state = { ...selection("scope-a"), selectedRunId: "other-run", selectedBreakId: null };
    const { result } = renderHook(() => useAccountingReconciliationViewModel(data, "reconciliation", api, null, null, state));
    await waitFor(() => expect(api.getBreakQueue).toHaveBeenCalled());
    expect(result.current.rows).toEqual([]);
    expect(result.current.selectedDetail).toBeNull();
  });

  it("leaves a disappeared explicit break unselected and explains its absence", async () => {
    const api = services();
    api.getBreakQueue = vi.fn().mockResolvedValue([]);
    const { result } = renderHook(() => useAccountingReconciliationViewModel(data, "reconciliation", api, null, null, selection("scope-a")));
    await waitFor(() => expect(result.current.selectionNotice).toContain("selected break is no longer available"));
    expect(result.current.selectedDetail).toBeNull();
    expect(result.current.selectedBreakId).toBeNull();
  });

  it("ignores a late action response after operating scope changes", async () => {
    const api = services();
    let complete!: (value: ReconciliationCaseworkOperationResult) => void;
    api.reviewBreak = vi.fn().mockReturnValue(new Promise<ReconciliationCaseworkOperationResult>((resolve) => { complete = resolve; }));
    const { result, rerender } = renderHook(({ scope }) => useAccountingReconciliationViewModel(data, "reconciliation", api, null, null, selection(scope)), { initialProps: { scope: "scope-a" } });
    await waitFor(() => expect(result.current.loadingText).toBeNull());
    let pending!: Promise<void>;
    act(() => { pending = result.current.assignBreak("break-a"); });
    expect(result.current.breakAction?.breakId).toBe("break-a");
    rerender({ scope: "scope-b" });
    await act(async () => {
      complete(successfulCaseworkOperation({ ...item, assignedTo: "old-scope-owner" }));
      await pending;
    });
    expect(result.current.rows[0].ownerLabel).not.toBe("old-scope-owner");
    expect(result.current.breakAction).toBeNull();
    expect(result.current.actionErrorText).toBeNull();
  });
});
