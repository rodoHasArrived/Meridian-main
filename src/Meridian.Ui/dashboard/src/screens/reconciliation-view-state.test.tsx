import { act, renderHook, waitFor } from "@testing-library/react";
import { MemoryRouter, useLocation, useNavigate } from "react-router-dom";
import { describe, expect, it } from "vitest";
import { readReconciliationViewState, useReconciliationViewState } from "./reconciliation-view-state";

function wrapper({ children }: { children: React.ReactNode }) {
  return <MemoryRouter initialEntries={["/accounting/reconciliation?fundAccountId=fund-a&reconciliationRun=run-a&reconciliationBreak=break-a&reconciliationSearch=cash&reconciliationStatus=Open&reconciliationSort=oldest#queue"]}>{children}</MemoryRouter>;
}

function useHarness() {
  return { view: useReconciliationViewState(), location: useLocation(), navigate: useNavigate() };
}

describe("reconciliation URL view state", () => {
  it("does not mutate URLs when disabled for another Accounting workstream", async () => {
    const { result } = renderHook(() => ({ view: useReconciliationViewState(false), location: useLocation(), navigate: useNavigate() }), { wrapper });
    act(() => result.current.view.selectRun("run-b"));
    expect(result.current.location.search).toContain("reconciliationRun=run-a");
    act(() => result.current.navigate("/accounting/ledger?fundAccountId=fund-b&reconciliationRun=run-a&reconciliationBreak=break-a"));
    await waitFor(() => expect(result.current.location.pathname).toBe("/accounting/ledger"));
    expect(result.current.location.search).toContain("reconciliationBreak=break-a");
  });

  it("retains queue scroll recorded on the current browser entry during filter navigation", async () => {
    const original = window.history.state;
    try {
      const { result } = renderHook(useHarness, { wrapper });
      window.history.replaceState({ key: result.current.location.key, usr: { reconciliationQueueScroll: 240 } }, "");
      act(() => result.current.view.setQueueStatus("Resolved"));
      await waitFor(() => expect(result.current.location.state).toMatchObject({ reconciliationQueueScroll: 240 }));
    } finally {
      window.history.replaceState(original, "");
    }
  });

  it("restores only view choices and normalizes unsupported filters", () => {
    expect(readReconciliationViewState("?reconciliationRun=run-a&reconciliationBreak=break-a&reconciliationSearch=cash&reconciliationStatus=approved&reconciliationSort=unknown")).toMatchObject({
      selectedRunId: "run-a", selectedBreakId: "break-a", queueSearch: "cash", queueStatus: "all", queueSort: "default"
    });
  });

  it("preserves filters, scope and hash while clearing the incompatible break on run selection", async () => {
    const { result } = renderHook(useHarness, { wrapper });
    act(() => result.current.view.selectRun("run-b"));
    await waitFor(() => expect(result.current.view.selectedRunId).toBe("run-b"));
    expect(result.current.view.selectedBreakId).toBeNull();
    expect(result.current.view.queueSearch).toBe("cash");
    expect(result.current.location.search).toContain("fundAccountId=fund-a");
    expect(result.current.location.hash).toBe("#queue");
  });

  it("returns from evidence and browser Back with the same run, break and filters", async () => {
    const { result } = renderHook(useHarness, { wrapper });
    act(() => result.current.navigate("/reporting/evidence?subject=run-a"));
    act(() => result.current.navigate(-1));
    await waitFor(() => expect(result.current.location.pathname).toBe("/accounting/reconciliation"));
    expect(result.current.view).toMatchObject({ selectedRunId: "run-a", selectedBreakId: "break-a", queueSearch: "cash", queueStatus: "Open", queueSort: "oldest" });
  });

  it("clears retained selection when operating scope changes", async () => {
    const { result } = renderHook(useHarness, { wrapper });
    act(() => result.current.navigate(result.current.location.pathname + result.current.location.search.replace("fund-a", "fund-b")));
    await waitFor(() => expect(result.current.view.selectedRunId).toBeNull());
    expect(result.current.view.selectedBreakId).toBeNull();
    expect(result.current.location.search).not.toContain("reconciliationBreak");
    expect(result.current.view.queueStatus).toBe("Open");
  });

  it("allows Back to restore a different scope and its matching explicit selection", async () => {
    const { result } = renderHook(useHarness, { wrapper });
    act(() => result.current.navigate("/accounting/reconciliation?fundAccountId=fund-b&reconciliationRun=run-b&reconciliationBreak=break-b"));
    await waitFor(() => expect(result.current.view.selectedBreakId).toBe("break-b"));
    act(() => result.current.navigate(-1));
    await waitFor(() => expect(result.current.view.selectedBreakId).toBe("break-a"));
    expect(result.current.view.selectedRunId).toBe("run-a");
  });

  it("clears search and status atomically while preserving sort and selection", async () => {
    const { result } = renderHook(useHarness, { wrapper });
    act(() => result.current.view.clearQueueFilters());
    await waitFor(() => expect(result.current.view.queueSearch).toBe(""));
    expect(result.current.view.queueStatus).toBe("all");
    expect(result.current.view.queueSort).toBe("oldest");
    expect(result.current.view.selectedBreakId).toBe("break-a");
  });

  it("updates filters without discarding selection or unrelated query parameters", async () => {
    const { result } = renderHook(useHarness, { wrapper });
    act(() => result.current.view.setQueueSearch("settlement"));
    await waitFor(() => expect(result.current.view.queueSearch).toBe("settlement"));
    act(() => result.current.view.setQueueStatus("all"));
    act(() => result.current.view.setQueueSort("severity"));
    await waitFor(() => expect(result.current.view.queueSort).toBe("severity"));
    expect(result.current.view.selectedBreakId).toBe("break-a");
    expect(result.current.location.search).toContain("fundAccountId=fund-a");
    expect(result.current.location.search).not.toContain("reconciliationStatus");
  });
});
