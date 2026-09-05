import { act, renderHook, waitFor } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import type { FinancialOperationsCommandCenter } from "@/types";
import { useAccountingCloseSources, type CloseWorkflowQuery } from "./accounting-screen.close-sources";

const scope: CloseWorkflowQuery = {
  fundProfileId: "fund-alpha", fundAccountId: "account-alpha", ledgerBookId: "book-alpha",
  entityId: "entity-alpha", periodId: "2026-08",
};

function commandCenter(entityId: string, ready: boolean): FinancialOperationsCommandCenter {
  return {
    generatedAtUtc: "2026-09-04T12:00:00Z", fundProfileId: "fund-alpha", fundAccountId: "account-alpha",
    ledgerBookId: "book-alpha", periodId: "2026-08", status: ready ? "Ready" : "Blocked",
    isReadyToComplete: ready, summary: entityId, activeItemCount: 0, blockedItemCount: ready ? 0 : 1,
    reviewItemCount: 0, metrics: [], queueRows: [], activeWorkflow: null,
    closeReadiness: {
      scope: { fundProfileId: "fund-alpha", fundAccountId: "account-alpha", ledgerBookId: "book-alpha", entityId, periodId: "2026-08" },
      evaluatedAtUtc: "2026-09-04T12:00:00Z", status: ready ? "Ready" : "Blocked",
      isComplete: ready, isReadyToClose: ready, contributors: [], blockers: [],
    },
  };
}

function deferred<T>() {
  let resolve!: (value: T) => void;
  let reject!: (error: Error) => void;
  const promise = new Promise<T>((done, fail) => { resolve = done; reject = fail; });
  return { promise, resolve, reject };
}

function servicesFor(getFinancialOperationsCommandCenter: NonNullable<Parameters<typeof useAccountingCloseSources>[2]>["getFinancialOperationsCommandCenter"]) {
  return {
    getFinancialOperationsCommandCenter,
    getPrivateCapitalCloseCockpit: vi.fn(async () => { throw new Error("No private-capital projection."); }),
    getOperationsContinuityWorkflows: vi.fn(async () => []),
    getOperationsContinuityWorkflow: vi.fn(async () => { throw new Error("No workflow selected."); }),
    listDailyValuationSchedules: vi.fn(async () => []),
  };
}

describe("Accounting close source scope isolation", () => {
  it.each(["resolve", "reject"] as const)("ignores a previous entity's late %s after a route change and recovers with current evidence", async completion => {
    const previous = deferred<FinancialOperationsCommandCenter>();
    const current = deferred<FinancialOperationsCommandCenter>();
    const getDecision = vi.fn().mockReturnValueOnce(previous.promise).mockReturnValueOnce(current.promise)
      .mockResolvedValue(commandCenter("entity-beta", true));
    const services = servicesFor(getDecision);
    const { result, rerender } = renderHook(({ query }) => useAccountingCloseSources(query, true, services), {
      initialProps: { query: scope },
    });
    await waitFor(() => expect(getDecision).toHaveBeenCalledTimes(1));
    const previousSignal = getDecision.mock.calls[0][1].signal as AbortSignal;
    rerender({ query: { ...scope, entityId: "entity-beta" } });
    expect(result.current.financialOperationsCommandCenter).toBeNull();
    expect(previousSignal.aborted).toBe(true);
    await act(async () => { current.resolve(commandCenter("entity-beta", false)); });
    expect(result.current.financialOperationsCommandCenter?.closeReadiness?.isReadyToClose).toBe(false);
    const currentError = result.current.financialOperationsCommandCenterError;
    await act(async () => {
      if (completion === "resolve") previous.resolve(commandCenter("entity-alpha", true));
      else previous.reject(new Error("Old scope failed late."));
    });
    expect(result.current.financialOperationsCommandCenter?.closeReadiness?.scope.entityId).toBe("entity-beta");
    expect(result.current.financialOperationsCommandCenter?.closeReadiness?.isReadyToClose).toBe(false);
    expect(result.current.financialOperationsCommandCenterError).toBe(currentError);
    await act(async () => { await result.current.refreshCloseWorkflow(); });
    expect(result.current.financialOperationsCommandCenter?.closeReadiness?.isReadyToClose).toBe(true);
    expect(getDecision).toHaveBeenLastCalledWith(expect.objectContaining({ entityId: "entity-beta" }),
      expect.objectContaining({ allowDevelopmentFallback: false }));
  });

  it("removes a loaded ready decision immediately on scope changes and when leaving close", async () => {
    const next = deferred<FinancialOperationsCommandCenter>();
    const services = servicesFor(vi.fn().mockResolvedValueOnce(commandCenter("entity-alpha", true)).mockReturnValue(next.promise));
    const { result, rerender } = renderHook(({ query, enabled }) => useAccountingCloseSources(query, enabled, services), {
      initialProps: { query: scope, enabled: true },
    });
    await waitFor(() => expect(result.current.financialOperationsCommandCenter?.closeReadiness?.isReadyToClose).toBe(true));
    const previousRefresh = result.current.refreshCloseWorkflow;
    rerender({ query: { ...scope, entityId: "entity-beta" }, enabled: true });
    await act(async () => { await previousRefresh(); });
    expect(services.getFinancialOperationsCommandCenter).toHaveBeenCalledTimes(2);
    expect(result.current.financialOperationsCommandCenter).toBeNull();
    expect(result.current.closeWorkflowLoading).toBe(true);
    rerender({ query: scope, enabled: false });
    await act(async () => { next.resolve(commandCenter("entity-beta", true)); });
    expect(result.current.financialOperationsCommandCenter).toBeNull();
    expect(result.current.closeWorkflowLoading).toBe(false);
  });
});
