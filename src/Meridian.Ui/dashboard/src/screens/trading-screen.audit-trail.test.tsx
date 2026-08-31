import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, describe, expect, it, vi } from "vitest";
import * as executionAuditApi from "@/lib/api/execution-audit.api";
import { ExecutionAuditTrailPanel } from "@/screens/trading-screen.audit-trail";
import type { AuditTrailExplorerResult } from "@/types/execution-audit.types";

vi.mock("@/lib/api/execution-audit.api", () => ({
  searchExecutionAuditTrail: vi.fn()
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
});
