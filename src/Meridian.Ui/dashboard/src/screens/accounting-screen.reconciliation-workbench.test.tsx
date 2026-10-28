import { useState, type ComponentProps } from "react";
import { MemoryRouter } from "react-router-dom";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import { ReconciliationWorkbench } from "./accounting-screen.reconciliation-workbench";
import { buildReconciliationBreakQueueState, useReconciliationResolveDialogViewModel } from "./accounting-screen.view-model";
import type { ReconciliationBreakQueueItem } from "@/types";

type WorkbenchProps = ComponentProps<typeof ReconciliationWorkbench>;
const breaks: ReconciliationBreakQueueItem[] = [
  { breakId: "cash-1", runId: "run-1", strategyName: "Cash review", category: "AmountMismatch", status: "Open", variance: 500, reason: "Cash variance over tolerance.", assignedTo: null, detectedAt: "2026-10-01T00:00:00Z", lastUpdatedAt: "2026-10-01T00:00:00Z", reviewedBy: null, reviewedAt: null, resolvedBy: null, resolvedAt: null, resolutionNote: null },
  { breakId: "fees-2", runId: "run-1", strategyName: "Fee review", category: "FeeMismatch", status: "Open", variance: 20, reason: "Review fee difference.", assignedTo: "ops", detectedAt: "2026-10-02T00:00:00Z", lastUpdatedAt: "2026-10-02T00:00:00Z", reviewedBy: null, reviewedAt: null, resolvedBy: null, resolvedAt: null, resolutionNote: null }
];

function renderWorkbench(runSelector?: WorkbenchProps["runSelector"]) {
  const assignBreak = vi.fn().mockResolvedValue(undefined);
  const resolveBreak = vi.fn().mockResolvedValue(undefined);
  function Harness() {
    const [selectedBreakId, selectBreak] = useState("cash-1");
    const [queueSearch, setQueueSearch] = useState("");
    const [queueStatus, setQueueStatus] = useState("all");
    const [queueSort, setQueueSort] = useState("default");
    const state = buildReconciliationBreakQueueState({ breakQueue: breaks, selectedBreakId, loading: false, loadError: null, action: null, actionError: null });
    const reconciliation = { ...state, selectBreak, assignBreak, detailActions: null } as unknown as WorkbenchProps["reconciliation"];
    const dialog = useReconciliationResolveDialogViewModel(resolveBreak);
    return <ReconciliationWorkbench reconciliation={reconciliation} resolveDialog={dialog} runSelector={runSelector} controls={{ queueSearch, queueStatus, queueSort, setQueueSearch, setQueueStatus, setQueueSort, clearQueueFilters: () => { setQueueSearch(""); setQueueStatus("all"); } }} />;
  }
  render(<MemoryRouter><Harness /></MemoryRouter>);
  return { assignBreak, resolveBreak };
}

function breakTable() { return screen.getByRole("treegrid", { name: "Reconciliation break queue" }); }
function inspector() { return screen.getByRole("region", { name: "Selected reconciliation break" }); }

describe("Reconciliation workbench", () => {
  it("keeps selected evidence visible when filtering the selected break out of the queue", async () => {
    const user = userEvent.setup();
    renderWorkbench();
    await user.type(screen.getByRole("textbox", { name: "Search breaks" }), "Fee review");
    expect(within(breakTable()).queryByText("Cash review")).not.toBeInTheDocument();
    expect(within(breakTable()).getByText("Fee review")).toBeInTheDocument();
    expect(within(inspector()).getByRole("heading", { name: /Cash review/ })).toBeInTheDocument();
    expect(screen.getByText("The selected break is outside these filters. Its evidence remains in the inspector.")).toBeInTheDocument();
    expect(screen.getByText("1 of 2 loaded breaks")).toBeInTheDocument();
  });

  it("keeps controls available for no matches and clears filters without losing the sort", async () => {
    const user = userEvent.setup();
    renderWorkbench();
    await user.selectOptions(screen.getByRole("combobox", { name: "Sort breaks" }), "newest");
    await user.selectOptions(screen.getByRole("combobox", { name: "Case status" }), "Resolved");
    await user.type(screen.getByRole("textbox", { name: "Search breaks" }), "unmatched");
    expect(screen.getByText("No breaks match these filters. Clear filters to review all loaded breaks.")).toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: "Clear filters" }));
    expect(screen.getByRole("textbox", { name: "Search breaks" })).toHaveValue("");
    expect(screen.getByRole("combobox", { name: "Case status" })).toHaveValue("all");
    expect(screen.getByRole("combobox", { name: "Sort breaks" })).toHaveValue("newest");
    const rows = within(breakTable()).getAllByRole("row");
    expect(rows[1]).toHaveTextContent("Fee review");
    expect(rows[2]).toHaveTextContent("Cash review");
  });

  it("places commands in the inspector and invokes them only for the selected break", async () => {
    const user = userEvent.setup();
    const { assignBreak } = renderWorkbench();
    expect(within(breakTable()).queryByRole("button", { name: /assign/i })).not.toBeInTheDocument();
    await user.click(within(inspector()).getByRole("button", { name: /assign/i }));
    expect(assignBreak).toHaveBeenCalledExactlyOnceWith("cash-1");
  });

  it("hands keyboard focus into the inspector and restores it to the selected queue row", async () => {
    const user = userEvent.setup();
    renderWorkbench();
    const row = within(breakTable()).getAllByRole("row")[1];
    row.focus();
    await user.keyboard("{Enter}");
    await waitFor(() => expect(inspector()).toHaveFocus());
    await user.keyboard("{Escape}");
    await waitFor(() => expect(row).toHaveFocus());
  });

  it("keeps the break queue visible when activating a statement-run selector row", async () => {
    const user = userEvent.setup();
    renderWorkbench(<table><tbody><tr tabIndex={0} data-dense-row-id="statement-run" aria-label="Select statement run"><td>Statement run</td></tr></tbody></table>);
    screen.getByRole("row", { name: "Select statement run" }).focus();
    await user.keyboard("{Enter}");
    expect(screen.getByRole("region", { name: "Reconciliation workbench" })).not.toHaveClass("reconciliation-workbench--detail-open");
  });

  it("discards an open rationale when selection moves to another break", async () => {
    const user = userEvent.setup();
    renderWorkbench();
    await user.click(within(inspector()).getByRole("button", { name: /resolve/i }));
    await user.type(screen.getByRole("textbox", { name: "Resolve rationale" }), "Review in progress");
    await user.click(within(breakTable()).getAllByRole("row")[2]);
    await waitFor(() => expect(screen.queryByRole("textbox", { name: "Resolve rationale" })).not.toBeInTheDocument());
    await user.click(within(inspector()).getByRole("button", { name: /resolve/i }));
    expect(screen.getByRole("textbox", { name: "Resolve rationale" })).toHaveValue("");
  });
});
