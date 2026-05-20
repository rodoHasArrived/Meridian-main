import { cleanup, render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import {
  getOperationsContinuityWorkflow,
  getOperationsContinuityWorkflows
} from "@/lib/api";
import { OperationsContinuityScreen } from "@/screens/operations-continuity-screen";
import type {
  OperationsContinuityWorkflow,
  OperationsContinuityWorkflowSummary,
  OperationsGate
} from "@/types";

vi.mock("@/lib/api", () => ({
  getOperationsContinuityWorkflows: vi.fn(),
  getOperationsContinuityWorkflow: vi.fn()
}));

afterEach(() => {
  cleanup();
  vi.clearAllMocks();
});

beforeEach(() => {
  vi.mocked(getOperationsContinuityWorkflows).mockResolvedValue([summary]);
  vi.mocked(getOperationsContinuityWorkflow).mockResolvedValue(detail);
});

const workflowId = "79f1f386-0bb1-4aef-9a85-fb9d6de8e1f6";
const fundAccountId = "53bf0251-17f6-4fb7-8dbe-6fb4966e2749";

const gates: OperationsGate[] = [
  {
    gateKey: "BrokerIngest",
    displayName: "Broker intake",
    status: "Passed",
    isRequired: true,
    description: "Broker data is normalized.",
    blockers: [],
    nextActions: [],
    completedAtUtc: "2026-05-08T14:20:00Z",
    completedBy: "ops-user"
  },
  {
    gateKey: "LedgerPosting",
    displayName: "Ledger posting",
    status: "Blocked",
    isRequired: true,
    description: "Ledger draft must be validated.",
    blockers: [
      {
        code: "LEDGER_VALIDATION_REQUIRED",
        message: "Ledger posting requires a balanced and validated journal draft.",
        gate: "LedgerPosting",
        severity: "Critical",
        evidenceLinks: []
      }
    ],
    nextActions: [
      {
        code: "RESOLVE_LEDGERPOSTING_BLOCKERS",
        label: "Resolve Ledger Posting blockers",
        route: "/workstation/accounting",
        gate: "LedgerPosting"
      }
    ],
    completedAtUtc: null,
    completedBy: null
  }
];

const summary: OperationsContinuityWorkflowSummary = {
  workflowId,
  fundAccountId,
  periodId: "2026-05",
  securityMasterSnapshotId: "9f2f0d07-f8d3-4d6e-a2f1-3116286de3d4",
  brokerSource: "custodian",
  status: "LedgerPostingDraft",
  version: 4,
  createdAtUtc: "2026-05-08T14:00:00Z",
  updatedAtUtc: "2026-05-08T15:10:00Z",
  gates,
  nextActions: []
};

const detail: OperationsContinuityWorkflow = {
  ...summary,
  brokerIntakeState: "Complete",
  securityMasterState: "Complete",
  ledgerPostingState: "Drafted",
  reconciliationState: "Pending",
  approvalState: "Pending",
  timeline: [
    {
      auditId: "cdb9449e-7402-48b7-9acf-8568b7363e16",
      occurredAtUtc: "2026-05-08T15:10:00Z",
      workflowId,
      fundAccountId,
      periodId: "2026-05",
      eventType: "ledger-draft-blocked",
      fromState: "LedgerPostingDraft",
      toState: "Blocked",
      gate: "LedgerPosting",
      fromGateStatus: "InProgress",
      toGateStatus: "Blocked",
      actor: "ops-user",
      rationale: "Journal validation is still required.",
      correlationId: "dev-continuity",
      references: [],
      previousHash: "devhash-started",
      currentHash: "devhash-ledger"
    }
  ],
  breakCases: [],
  ledgerPreview: null,
  approvals: [],
  reportPackReadiness: {
    isReady: false,
    reportPackId: null,
    blockingReason: "Close workflow has unresolved ledger blockers.",
    evidenceLinks: []
  },
  evidenceLinks: [],
  blockers: gates[1]!.blockers
};

describe("OperationsContinuityScreen", () => {
  it("renders workflow list, detail gates, blockers, timeline, and enabled next action", async () => {
    renderScreen();

    expect(await screen.findByRole("heading", { name: "Operations continuity" })).toBeInTheDocument();
    const workflows = await screen.findByRole("table", { name: "Operations continuity workflows" });
    expect(within(workflows).getByText("2026-05 close")).toBeInTheDocument();

    expect(await screen.findByRole("heading", { name: "Gates" })).toBeInTheDocument();
    expect(screen.getByText("Ledger posting requires a balanced and validated journal draft.")).toBeInTheDocument();
    expect(screen.getByText("Ledger Draft Blocked")).toBeInTheDocument();

    const nextAction = screen.getByRole("link", { name: "Open operations continuity next action: Resolve Ledger Posting blockers" });
    expect(nextAction).toHaveAttribute("href", "/accounting");
  });

  it("opens a different workflow detail when the operator selects a row", async () => {
    const second = {
      ...summary,
      workflowId: "8ef225db-4648-479a-9a3a-020cc6b9d53c",
      periodId: "2026-04",
      updatedAtUtc: "2026-05-08T13:00:00Z",
      status: "Closed" as const,
      gates: gates.map((gate) => ({ ...gate, status: "Passed" as const, blockers: [] }))
    };
    vi.mocked(getOperationsContinuityWorkflows).mockResolvedValue([summary, second]);
    vi.mocked(getOperationsContinuityWorkflow).mockImplementation(async (id) => ({
      ...detail,
      workflowId: id,
      periodId: id === second.workflowId ? "2026-04" : "2026-05",
      status: id === second.workflowId ? "Closed" : "LedgerPostingDraft"
    }));

    const user = userEvent.setup();
    renderScreen();

    const secondRow = await screen.findByRole("row", { name: /2026-04 operations continuity workflow/i });
    await user.click(secondRow);

    await waitFor(() => {
      expect(getOperationsContinuityWorkflow).toHaveBeenLastCalledWith(second.workflowId, expect.objectContaining({ signal: expect.any(AbortSignal) }));
      expect(screen.getByText("2026-04 close workflow")).toBeInTheDocument();
    });
  });
});

function renderScreen() {
  return render(
    <MemoryRouter initialEntries={["/accounting/operations-continuity"]}>
      <Routes>
        <Route path="/accounting/operations-continuity" element={<OperationsContinuityScreen />} />
      </Routes>
    </MemoryRouter>
  );
}
