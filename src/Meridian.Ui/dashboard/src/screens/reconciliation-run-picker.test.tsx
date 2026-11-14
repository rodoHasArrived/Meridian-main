import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router-dom";
import { describe, expect, it, vi } from "vitest";
import { ReconciliationStatementRuns } from "./accounting-screen.reconciliation-workbench";
import { buildReconciliationStatementRunsViewState } from "./accounting-screen.reconciliation.view-model";
import type { AccountingWorkspaceResponse } from "@/types";

vi.mock("./accounting-screen.statement-run-detail", () => ({ StatementRunDetailTabs: () => null }));

const runs: AccountingWorkspaceResponse["reconciliationQueue"] = [
  { runId: "run-cash", strategyName: "Cash review", mode: "paper", status: "Running", lastUpdated: "2026-11-01T00:00:00Z", breakCount: 2, openBreakCount: 2, reconciliationStatus: "BreaksOpen" },
  { runId: "run-equity", strategyName: "Equity reconciliation", mode: "paper", status: "Running", lastUpdated: "2026-11-02T00:00:00Z", breakCount: 0, openBreakCount: 0, reconciliationStatus: "Resolved" }
];

function renderPicker(queue = runs) {
  const selectRun = vi.fn();
  const statementRunsView = buildReconciliationStatementRunsViewState({ statementRuns: [], fallbackQueue: queue, selectedRunId: null, loading: false, error: null });
  render(<MemoryRouter><ReconciliationStatementRuns reconciliation={{
    statementRunsView,
    selectedRunId: null,
    reconciliationQueue: queue,
    selectRun,
    refreshStatementRuns: vi.fn()
  }} /></MemoryRouter>);
  return selectRun;
}

describe("Reconciliation run picker", () => {
  it("searches visible run names before selecting their exact IDs with the keyboard", async () => {
    const user = userEvent.setup();
    const selectRun = renderPicker();
    const picker = screen.getByRole("combobox", { name: "Reconciliation run" });
    const details = screen.getByText("Statement runs and import evidence");
    expect(picker.compareDocumentPosition(details) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    expect(picker).toBeRequired();
    await user.type(picker, "Equity");
    expect(screen.queryByRole("option", { name: "Cash review · 2 open" })).not.toBeInTheDocument();
    expect(selectRun).not.toHaveBeenCalled();
    await user.keyboard("{Enter}");
    expect(selectRun).toHaveBeenCalledExactlyOnceWith("run-equity");
  });

  it("keeps a recovery path when no runs match and allows clearing scope", async () => {
    const user = userEvent.setup();
    const selectRun = renderPicker();
    const picker = screen.getByRole("combobox", { name: "Reconciliation run" });
    await user.type(picker, "unmatched");
    expect(screen.getByText("No matching reconciliation runs")).toBeInTheDocument();
    expect(selectRun).not.toHaveBeenCalled();
    await user.keyboard("{Escape}");
    await user.keyboard("{ArrowDown}");
    await user.click(screen.getByRole("option", { name: "Choose a reconciliation run" }));
    expect(selectRun).toHaveBeenCalledExactlyOnceWith(null);
  });
});
