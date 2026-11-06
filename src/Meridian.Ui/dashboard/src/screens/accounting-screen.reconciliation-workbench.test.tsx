import { useState, type ComponentProps } from "react";
import { MemoryRouter } from "react-router-dom";
import { act, render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { resetSinceLastVisitCache, type RecordSurfaceVisit, type SurfaceVisitServices } from "@/lib/since-last-visit";
import { ReconciliationWorkbench } from "./accounting-screen.reconciliation-workbench";
import { buildReconciliationBreakQueueState, useReconciliationResolveDialogViewModel } from "./accounting-screen.view-model";
import type { BulkTriageServices } from "./reconciliation-bulk-triage-bar";
import type { ReconciliationBreakQueueItem, ReconciliationBulkCaseworkRequest, ReconciliationBulkCaseworkResult } from "@/types";

type WorkbenchProps = ComponentProps<typeof ReconciliationWorkbench>;
const breaks: ReconciliationBreakQueueItem[] = [
  { breakId: "cash-1", runId: "run-1", strategyName: "Cash review", category: "AmountMismatch", status: "Open", variance: 500, reason: "Cash variance over tolerance.", assignedTo: null, detectedAt: "2026-10-01T00:00:00Z", lastUpdatedAt: "2026-10-01T00:00:00Z", reviewedBy: null, reviewedAt: null, resolvedBy: null, resolvedAt: null, resolutionNote: null },
  { breakId: "fees-2", runId: "run-1", strategyName: "Fee review", category: "FeeMismatch", status: "Open", variance: 20, reason: "Review fee difference.", assignedTo: "ops", detectedAt: "2026-10-02T00:00:00Z", lastUpdatedAt: "2026-10-02T00:00:00Z", reviewedBy: null, reviewedAt: null, resolvedBy: null, resolvedAt: null, resolutionNote: null }
];

function bulkResult(request: ReconciliationBulkCaseworkRequest, wouldSucceed: (breakId: string) => boolean): ReconciliationBulkCaseworkResult {
  const results = request.breakIds.map((breakId) => {
    const passes = wouldSucceed(breakId);
    const item = breaks.find((candidate) => candidate.breakId === breakId)!;
    return {
      breakId,
      succeeded: !request.dryRun && passes,
      wouldSucceed: passes,
      error: passes ? null : "Cannot resolve case from Open.",
      item: !request.dryRun && passes ? { ...item, assignedTo: "ops-user" } : item
    };
  });
  return {
    bulkActionId: request.commandId,
    idempotencyKey: request.idempotencyKey,
    dryRun: request.dryRun,
    requestedCount: results.length,
    succeededCount: results.filter((entry) => request.dryRun ? entry.wouldSucceed : entry.succeeded).length,
    failedCount: results.filter((entry) => !entry.wouldSucceed).length,
    results,
    inputHashSha256: "hash",
    outcome: {} as ReconciliationBulkCaseworkResult["outcome"]
  };
}

function fakeBulkServices(wouldSucceed: (breakId: string) => boolean = () => true) {
  const dryRun = vi.fn((request: ReconciliationBulkCaseworkRequest) => Promise.resolve(bulkResult(request, wouldSucceed)));
  const execute = vi.fn((request: ReconciliationBulkCaseworkRequest) => Promise.resolve(bulkResult(request, wouldSucceed)));
  const getTaxonomy = vi.fn().mockResolvedValue({
    version: 1,
    rootCauses: [{ code: "LedgerClassification", displayName: "Ledger classification", version: 1, isActive: true }],
    resolutionCodes: [{ code: "LedgerAdjusted", displayName: "Ledger adjusted", version: 1, isActive: true }]
  });
  return { dryRun, execute, getTaxonomy } satisfies BulkTriageServices;
}

const offline: RecordSurfaceVisit = () => Promise.reject(new Error("offline"));
const offlineVisits: Partial<SurfaceVisitServices> = { record: offline, read: offline };

function renderWorkbench(
  runSelector?: WorkbenchProps["runSelector"],
  bulkServices: BulkTriageServices = fakeBulkServices(),
  visitServices: Partial<SurfaceVisitServices> = offlineVisits
) {
  const assignBreak = vi.fn().mockResolvedValue(undefined);
  const resolveBreak = vi.fn().mockResolvedValue(undefined);
  const applyBreakQueueItems = vi.fn();
  function Harness() {
    const [selectedBreakId, selectBreak] = useState("cash-1");
    const [queueSearch, setQueueSearch] = useState("");
    const [queueStatus, setQueueStatus] = useState("all");
    const [queueSort, setQueueSort] = useState("default");
    const state = buildReconciliationBreakQueueState({ breakQueue: breaks, selectedBreakId, loading: false, loadError: null, action: null, actionError: null });
    const reconciliation = { ...state, selectBreak, assignBreak, beginBreakQueueUpdate: () => applyBreakQueueItems, detailActions: null } as unknown as WorkbenchProps["reconciliation"];
    const dialog = useReconciliationResolveDialogViewModel(resolveBreak);
    return <ReconciliationWorkbench reconciliation={reconciliation} resolveDialog={dialog} runSelector={runSelector} bulkServices={bulkServices} visitServices={visitServices} controls={{ queueSearch, queueStatus, queueSort, setQueueSearch, setQueueStatus, setQueueSort, clearQueueFilters: () => { setQueueSearch(""); setQueueStatus("all"); } }} />;
  }
  render(<MemoryRouter><Harness /></MemoryRouter>);
  return { assignBreak, resolveBreak, applyBreakQueueItems };
}

function breakTable() { return screen.getByRole("treegrid", { name: "Reconciliation break queue" }); }
function inspector() { return screen.getByRole("region", { name: "Selected reconciliation break" }); }
function bulkBar() { return screen.getByRole("region", { name: "Bulk break actions" }); }
function queueRows() { return within(breakTable()).getAllByRole("row").slice(1); }

beforeEach(() => {
  resetSinceLastVisitCache();
  window.sessionStorage.clear();
});

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

  it("checks a bulk assign with a dry run, then assigns only the passed breaks with one confirm (C2)", async () => {
    const user = userEvent.setup();
    const services = fakeBulkServices();
    const { applyBreakQueueItems } = renderWorkbench(undefined, services);
    await user.click(screen.getByRole("checkbox", { name: /Add Cash review .* to the bulk selection/ }));
    await user.click(screen.getByRole("checkbox", { name: /Add Fee review .* to the bulk selection/ }));
    expect(within(bulkBar()).getByText("2 breaks selected")).toBeInTheDocument();

    await user.click(within(bulkBar()).getByRole("button", { name: "Assign to me" }));
    await waitFor(() => expect(within(bulkBar()).getByText("All 2 breaks can be assigned.")).toBeInTheDocument());
    const dryRun = services.dryRun.mock.calls[0]![0];
    expect(dryRun).toMatchObject({ breakIds: ["cash-1", "fees-2"], action: "Assign", dryRun: true, allowPartialSuccess: true, assignToCurrentOperator: true });
    expect(services.execute).not.toHaveBeenCalled();

    await user.click(within(bulkBar()).getByRole("button", { name: "Assign 2 breaks" }));
    await waitFor(() => expect(applyBreakQueueItems).toHaveBeenCalledOnce());
    const executed = services.execute.mock.calls[0]![0];
    expect(executed).toMatchObject({ breakIds: ["cash-1", "fees-2"], dryRun: false, allowPartialSuccess: false, correlationId: dryRun.correlationId });
    expect(executed.idempotencyKey).not.toBe(dryRun.idempotencyKey);
    expect(within(bulkBar()).getByText("2 breaks assigned. Each break keeps its own audit entry.")).toBeInTheDocument();
    expect(within(bulkBar()).getByText("No breaks selected")).toBeInTheDocument();
  });

  it("resolves with a reason only the breaks the dry run passed and lists the rest (C2)", async () => {
    const user = userEvent.setup();
    const services = fakeBulkServices((breakId) => breakId === "fees-2");
    renderWorkbench(undefined, services);
    await user.click(within(bulkBar()).getByRole("button", { name: "Select all 2" }));
    await user.click(within(bulkBar()).getByRole("button", { name: "Resolve with reason" }));
    expect(within(bulkBar()).getByRole("button", { name: "Check 2 breaks" })).toBeDisabled();

    await user.type(within(bulkBar()).getByRole("textbox", { name: "Reason" }), "Journal posted for the fee accrual.");
    await waitFor(() => expect(within(bulkBar()).getByRole("option", { name: "Ledger classification" })).toBeInTheDocument());
    await user.selectOptions(within(bulkBar()).getByRole("combobox", { name: "Root cause" }), "LedgerClassification");
    await user.selectOptions(within(bulkBar()).getByRole("combobox", { name: "Resolution" }), "LedgerAdjusted");
    await user.type(within(bulkBar()).getByRole("textbox", { name: "Evidence reference (optional)" }), "journal:je-7");
    await user.click(within(bulkBar()).getByRole("button", { name: "Check 2 breaks" }));

    await waitFor(() => expect(within(bulkBar()).getByText("1 of 2 breaks can be resolved. The others stay as they are.")).toBeInTheDocument());
    expect(within(bulkBar()).getByRole("list", { name: "Breaks the check blocked" })).toHaveTextContent("cash-1: Cannot resolve case from Open.");
    await user.click(within(bulkBar()).getByRole("button", { name: "Resolve 1 break" }));
    await waitFor(() => expect(services.execute).toHaveBeenCalledOnce());
    expect(services.execute.mock.calls[0]![0]).toMatchObject({
      breakIds: ["fees-2"],
      action: "Resolve",
      reason: "Journal posted for the fee accrual.",
      rootCauseCode: "LedgerClassification",
      resolutionCode: "LedgerAdjusted",
      evidenceLinks: ["journal:je-7"]
    });
  });

  it("freezes the draft during a check and drops a dry run whose draft was abandoned (C2)", async () => {
    const user = userEvent.setup();
    const services = fakeBulkServices();
    let release!: () => void;
    services.dryRun.mockImplementationOnce((request) => new Promise((resolve) => {
      release = () => resolve(bulkResult(request, () => true));
    }));
    renderWorkbench(undefined, services);
    await user.click(screen.getByRole("checkbox", { name: /Add Cash review/ }));
    await user.click(within(bulkBar()).getByRole("button", { name: "Route to owner" }));
    await user.type(within(bulkBar()).getByRole("textbox", { name: "Owner" }), "desk-a");
    await user.click(within(bulkBar()).getByRole("button", { name: "Check 1 break" }));
    expect(within(bulkBar()).getByRole("textbox", { name: "Owner" })).toBeDisabled();

    // The operator abandons the check and starts again for another owner before it returns.
    await user.click(within(bulkBar()).getByRole("button", { name: "Cancel" }));
    await user.click(within(bulkBar()).getByRole("button", { name: "Route to owner" }));
    await user.type(within(bulkBar()).getByRole("textbox", { name: "Owner" }), "desk-b");
    await act(async () => release());

    expect(within(bulkBar()).queryByText(/can be routed/)).not.toBeInTheDocument();
    expect(within(bulkBar()).queryByRole("button", { name: "Route 1 break" })).not.toBeInTheDocument();
    expect(within(bulkBar()).getByRole("button", { name: "Check 1 break" })).toBeEnabled();
    expect(services.execute).not.toHaveBeenCalled();
  });

  it("routes the selection to a named owner (C2)", async () => {
    const user = userEvent.setup();
    const services = fakeBulkServices();
    renderWorkbench(undefined, services);
    await user.click(screen.getByRole("checkbox", { name: /Add Cash review/ }));
    await user.click(within(bulkBar()).getByRole("button", { name: "Route to owner" }));
    await user.type(within(bulkBar()).getByRole("textbox", { name: "Owner" }), "custody-desk");
    await user.click(within(bulkBar()).getByRole("button", { name: "Check 1 break" }));
    await waitFor(() => expect(services.dryRun).toHaveBeenCalledOnce());
    expect(services.dryRun.mock.calls[0]![0]).toMatchObject({ action: "Assign", assignee: "custody-desk" });
    expect(services.dryRun.mock.calls[0]![0].assignToCurrentOperator).toBeUndefined();
  });

  it("drives the queue from the keyboard map without the row type-ahead taking the keys (C2)", async () => {
    const user = userEvent.setup();
    const services = fakeBulkServices();
    const { assignBreak } = renderWorkbench(undefined, services);
    queueRows()[0]!.focus();

    await user.keyboard("j");
    await waitFor(() => expect(queueRows()[1]).toHaveAttribute("aria-selected", "true"));
    await waitFor(() => expect(queueRows()[1]).toHaveFocus());
    await user.keyboard("k");
    await waitFor(() => expect(queueRows()[0]).toHaveAttribute("aria-selected", "true"));

    await user.keyboard("a");
    expect(assignBreak).toHaveBeenCalledExactlyOnceWith("cash-1");

    await user.keyboard("x");
    expect(within(bulkBar()).getByText("1 break selected")).toBeInTheDocument();
    await user.keyboard("a");
    await waitFor(() => expect(services.dryRun).toHaveBeenCalledOnce());
    expect(assignBreak).toHaveBeenCalledOnce();

    await user.keyboard("e");
    expect(screen.getByRole("region", { name: "Reconciliation workbench" })).toHaveClass("reconciliation-workbench--detail-open");
  });

  it("opens the shortcut sheet with ? and lists every key in the map (C2)", async () => {
    const user = userEvent.setup();
    renderWorkbench();
    queueRows()[0]!.focus();
    await user.keyboard("?");
    const sheet = screen.getByRole("dialog", { name: "Break queue shortcuts" });
    for (const description of ["Move to the next break", "Move to the previous break", "Open the evidence drawer for the current break", "Show these shortcuts"]) {
      expect(within(sheet).getByText(description)).toBeInTheDocument();
    }
    await user.click(within(sheet).getByRole("button", { name: "Close keyboard shortcuts" }));
    expect(screen.queryByRole("dialog", { name: "Break queue shortcuts" })).not.toBeInTheDocument();
  });

  it("leaves queue keys alone while typing in a field", async () => {
    const user = userEvent.setup();
    renderWorkbench();
    await user.type(screen.getByRole("textbox", { name: "Search breaks" }), "jak");
    expect(screen.getByRole("textbox", { name: "Search breaks" })).toHaveValue("jak");
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
  });

  it("marks breaks that are new since the operator's last visit (C3)", async () => {
    const visitRecorder = vi.fn<RecordSurfaceVisit>().mockResolvedValue({
      surface: "reconciliation-breaks",
      previousVisitAt: "2026-10-01T12:00:00Z",
      visitedAt: "2026-10-03T09:00:00Z"
    });
    renderWorkbench(undefined, fakeBulkServices(), { record: visitRecorder, read: offline });
    expect(await screen.findByText("1 new since your last visit (Oct 1, 2026, 12:00 UTC).")).toBeInTheDocument();
    expect(visitRecorder).toHaveBeenCalledExactlyOnceWith("reconciliation-breaks");
    const [cashRow, feeRow] = queueRows();
    expect(within(feeRow!).getByText("New")).toBeInTheDocument();
    expect(feeRow).toHaveAccessibleName(/, new since your last visit$/);
    expect(within(cashRow!).queryByText("New")).not.toBeInTheDocument();
  });

  it("shows no markers when the visit cannot be recorded (C3)", async () => {
    renderWorkbench();
    await waitFor(() => expect(within(breakTable()).getAllByRole("row")).toHaveLength(3));
    expect(screen.queryByText(/since your last visit/)).not.toBeInTheDocument();
  });
});
