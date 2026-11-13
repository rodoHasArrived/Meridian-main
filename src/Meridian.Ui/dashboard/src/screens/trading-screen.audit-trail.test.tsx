import { screen, waitFor, within } from "@testing-library/react";
import { renderWithRouter as render } from "@/test/render";
import userEvent from "@testing-library/user-event";
import { afterEach, describe, expect, it, vi } from "vitest";
import * as executionAuditApi from "@/lib/api/execution-audit.api";
import { exportTableQuery } from "@/lib/table-workbench";
import { ExecutionAuditTrailPanel } from "@/screens/trading-screen.audit-trail";
import type { AuditTrailExplorerResult } from "@/types/execution-audit.types";

vi.mock("@/lib/api/execution-audit.api", () => ({
  searchExecutionAuditTrail: vi.fn()
}));
vi.mock("@/lib/table-workbench", async () => ({
  ...await vi.importActual<typeof import("@/lib/table-workbench")>("@/lib/table-workbench"),
  exportTableQuery: vi.fn().mockResolvedValue(undefined)
}));

const searchExecutionAuditTrail = vi.mocked(executionAuditApi.searchExecutionAuditTrail);

const result: AuditTrailExplorerResult = {
  asOf: "2026-05-29T14:05:00Z",
  totalMatched: 2,
  returned: 2,
  entries: [
    {
      auditId: "audit-1",
      occurredAt: "2026-05-29T14:03:11Z",
      objectKind: "Order",
      objectId: "ord-991",
      category: "Execution",
      action: "Submit",
      outcome: "Accepted",
      actor: "trader@example.com",
      runId: "run-7",
      symbol: "SPY",
      correlationId: "corr-42",
      evidenceRoute: null,
      actionLedgerSource: "execution",
      actionLedgerSequence: 118,
      previousActionHash: "aaa",
      currentActionHash: "bbb",
      actionLedgerStatus: "Verified"
    },
    {
      auditId: "audit-2",
      occurredAt: "2026-05-29T14:04:02Z",
      objectKind: "ExecutionControl",
      objectId: "circuit-breaker",
      category: "Control",
      action: "Trip",
      outcome: "Rejected",
      actor: null,
      message: "Circuit breaker tripped",
      evidenceRoute: null,
      currentActionHash: null,
      actionLedgerStatus: null,
      actionLedgerSequence: null
    }
  ]
};

afterEach(() => {
  vi.resetAllMocks();
});

describe("ExecutionAuditTrailPanel", () => {
  it("loads the audit trail and renders actor, outcome, and ledger position", async () => {
    searchExecutionAuditTrail.mockResolvedValue(result);
    render(<ExecutionAuditTrailPanel />);

    const table = await screen.findByLabelText("Execution audit trail entries");
    await waitFor(() => expect(within(table).getByText("Order ord-991")).toBeInTheDocument());
    expect(within(table).getByText("Verified #118")).toBeInTheDocument();
    // An entry with no hash is reported as unchained, and an unattributed action as System.
    expect(within(table).getByText("Unchained")).toBeInTheDocument();
    expect(within(table).getByText("System")).toBeInTheDocument();
    expect(searchExecutionAuditTrail).toHaveBeenCalledWith({ searchText: undefined, limit: 50 });
  });

  it("re-queries the server with the submitted filter", async () => {
    searchExecutionAuditTrail.mockResolvedValue(result);
    const user = userEvent.setup();
    render(<ExecutionAuditTrailPanel />);

    await screen.findByLabelText("Execution audit trail entries");
    await user.type(screen.getByLabelText("Audit trail search text"), "SPY");
    await user.click(screen.getByRole("button", { name: "Search" }));

    await waitFor(() => expect(searchExecutionAuditTrail).toHaveBeenLastCalledWith({ searchText: "SPY", limit: 50 }));
  });

  it("warns that results are truncated instead of presenting a partial trail as complete", async () => {
    searchExecutionAuditTrail.mockResolvedValue({ ...result, totalMatched: 412, returned: 2 });
    render(<ExecutionAuditTrailPanel />);

    expect(await screen.findByText(/2 most recent of 412 matches/)).toBeInTheDocument();
  });

  it("surfaces a failed read rather than rendering an empty trail", async () => {
    searchExecutionAuditTrail.mockRejectedValue(new Error("audit service unavailable"));
    render(<ExecutionAuditTrailPanel />);

    expect(await screen.findByText("audit service unavailable")).toBeInTheDocument();
    expect(screen.getByText("Audit trail has not loaded.")).toBeInTheDocument();
  });

  it("exports all authorized matches using the submitted query, selected columns and lifetime signal", async () => {
    searchExecutionAuditTrail.mockResolvedValue({ ...result, totalMatched: 412, returned: 2 });
    vi.mocked(exportTableQuery).mockResolvedValue(undefined);
    const user = userEvent.setup();
    const view = render(<ExecutionAuditTrailPanel />);
    await screen.findByText(/2 most recent of 412 matches/);
    const exportButton = screen.getByRole("button", { name: "Export CSV" });
    expect(exportButton).toBeEnabled();
    await user.type(screen.getByLabelText("Audit trail search text"), "SPY");
    await user.click(exportButton);
    expect(exportTableQuery).toHaveBeenLastCalledWith("trading-execution-audit", ["column-1", "column-2", "column-3", "column-4", "column-5", "column-6", "column-7"], "", undefined, undefined, expect.any(AbortSignal));
    await user.click(screen.getByRole("button", { name: "Search" }));
    await waitFor(() => expect(searchExecutionAuditTrail).toHaveBeenLastCalledWith({ searchText: "SPY", limit: 50 }));
    await waitFor(() => expect(exportButton).toBeEnabled());
    await user.click(screen.getByText("Columns", { selector: "summary" }));
    await user.click(screen.getByRole("checkbox", { name: "Outcome" }));
    await user.click(exportButton);
    expect(exportTableQuery).toHaveBeenLastCalledWith("trading-execution-audit", ["column-1", "column-2", "column-3", "column-5", "column-6", "column-7"], "SPY", undefined, undefined, expect.any(AbortSignal));
    const signal = vi.mocked(exportTableQuery).mock.calls.at(-1)![5];
    expect(signal?.aborted).toBe(false);
    view.unmount();
    expect(signal?.aborted).toBe(true);
  });
});
