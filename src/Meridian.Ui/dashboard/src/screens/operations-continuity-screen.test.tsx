import { cleanup, render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import {
  getOperationsContinuityWorkflow,
  getOperationsContinuityWorkflows
} from "@/lib/api";
import { OperationsContinuityScreen } from "@/screens/operations-continuity-screen";
import { OPERATIONS_CONTINUITY_WORKFLOW_DETAIL_PANEL_ID } from "@/screens/operations-continuity-screen.view-model";
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
  closeChecklist: [
    {
      taskId: "close-gate-ledgerposting",
      gate: "LedgerPosting",
      label: "Ledger posting controller check",
      owner: "fund-controller",
      requiredEvidence: "Validated journal draft, retained ledger hash, and controller approval evidence.",
      dueDate: "2026-05-09",
      requiredApprovalCount: 2,
      expiresOn: "2026-05-12",
      status: "Pending",
      blockingReason: "Ledger validation is still required.",
      evidencePointer: "ledger-evidence-1",
      remediationRoute: "/workstation/accounting/ledger",
      canAcknowledge: false,
      acknowledgedAtUtc: null,
      acknowledgedBy: null
    }
  ],
  closeReadiness: null,
  closePackage: {
    closePackageId: "close-package-2026-05",
    reportPackId: "report-pack-may-2026",
    retainedManifestId: "close-package-2026-05-manifest",
    retainedManifestRoute: "/workstation/accounting/operations-continuity/79f1f386-0bb1-4aef-9a85-fb9d6de8e1f6/close-package/close-package-2026-05-manifest",
    evidenceHash: "b5f6c7d8e9a00112233445566778899aabbccddeeff00112233445566778899",
    publishedAtUtc: "2026-05-10T18:45:00Z",
    publishedBy: "fund-controller",
    signOffRationale: "Controller sign-off after report pack and checklist evidence were retained.",
    evidenceLinks: [],
    checklistControlApprovals: [
      {
        taskId: "close-gate-ledgerposting",
        approvedBy: "fund-controller",
        approvedAtUtc: "2026-05-10T18:40:00Z"
      }
    ]
  },
  accountingRecordSummary: {
    recordId: "accounting-record-2026-05",
    isAuditReady: false,
    completeCategoryCount: 4,
    requiredCategoryCount: 6,
    summary: "Accounting record has 4 of 6 required evidence categories complete.",
    evidenceCategories: [
      {
        key: "source-records",
        label: "Retained source data",
        isComplete: true,
        status: "Broker statements and provider files are retained.",
        routeHint: "/workstation/data/providers",
        requiredEvidence: ["provider statement", "custodian activity file", "bank or account source record"],
        evidenceLinks: [
          {
            evidenceId: "ev-source-1",
            label: "Retained broker source packet",
            route: "/evidence/source-1",
            source: "operations-continuity",
            capturedAtUtc: "2026-05-08T14:20:00Z"
          }
        ]
      },
      {
        key: "normalized-activity",
        label: "Normalized transactions and positions",
        isComplete: true,
        status: "Normalized transactions and positions are available.",
        routeHint: "/workstation/accounting",
        requiredEvidence: ["normalized transactions", "normalized positions", "balance or cash activity projection"],
        evidenceLinks: []
      },
      {
        key: "reconciliation-case-history",
        label: "Reconciliation case history",
        isComplete: false,
        status: "Ledger posting requires a balanced and validated journal draft.",
        routeHint: "/workstation/accounting",
        requiredEvidence: ["reconciliation run", "break-case decision history", "resolved exception evidence"],
        evidenceLinks: []
      },
      {
        key: "ledger-evidence",
        label: "Journal and ledger evidence",
        isComplete: false,
        status: "Ledger validation is still required.",
        routeHint: "/workstation/accounting/ledger",
        requiredEvidence: ["journal preview", "posted ledger batch", "trial-balance support"],
        evidenceLinks: []
      },
      {
        key: "approvals",
        label: "Approval history",
        isComplete: false,
        status: "Approval is pending.",
        routeHint: "/workstation/accounting/approvals",
        requiredEvidence: ["approval submission", "reviewer decision", "checklist control approvals"],
        evidenceLinks: []
      },
      {
        key: "report-pack",
        label: "Report pack",
        isComplete: false,
        status: "Report-pack readiness evidence has not been linked.",
        routeHint: "/workstation/reporting/report-packs",
        requiredEvidence: ["report-pack manifest", "report-pack provenance", "report-pack validation"],
        evidenceLinks: []
      },
      {
        key: "exports",
        label: "Exports and retained evidence",
        isComplete: false,
        status: "Export manifest and retained evidence hash still need close-package publication.",
        routeHint: "/workstation/reporting/report-packs",
        requiredEvidence: ["export manifest", "retained evidence hash", "close-package publication"],
        evidenceLinks: []
      },
      {
        key: "restatement-lineage",
        label: "Restatement lineage",
        isComplete: false,
        status: "Restatement baseline is pending until the close package is published.",
        routeHint: "/workstation/reporting/report-packs",
        requiredEvidence: ["published baseline", "prior-version pointer when restated", "changed-line evidence"],
        evidenceLinks: []
      }
    ],
    evidenceLinks: [],
    auditPackReadiness: {
      isComplete: false,
      generatedInSeconds: 0,
      slaTargetSeconds: 60,
      slaMet: true,
      missingEvidenceCategories: ["ReportPack", "Exports", "RestatementLineage"],
      warnings: ["Audit pack still needs reporting evidence."],
      evidenceCategorySummaries: []
    }
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
    const workflowRow = within(workflows).getByRole("row", { name: /open 2026-05 operations continuity workflow/i });
    expect(workflowRow).toHaveAttribute("aria-controls", OPERATIONS_CONTINUITY_WORKFLOW_DETAIL_PANEL_ID);
    expect(workflowRow).toHaveAttribute("aria-expanded", "true");
    expect(workflowRow).toHaveClass("bg-warning/5");
    expect(screen.getByRole("region", { name: "Operations continuity detail for 2026-05 close workflow" }))
      .toHaveAttribute("id", OPERATIONS_CONTINUITY_WORKFLOW_DETAIL_PANEL_ID);

    expect(await screen.findByRole("heading", { name: "Gates" })).toBeInTheDocument();
    expect(screen.getAllByText("Ledger posting requires a balanced and validated journal draft.")).toHaveLength(2);
    const checklist = screen.getByRole("table", { name: "Operations continuity close checklist" });
    const checklistSummary = screen.getByRole("list", { name: "Close checklist control summary" });
    expect(await screen.findByText("1 close task")).toBeInTheDocument();
    expect(within(checklistSummary).getByText("0 ready")).toBeInTheDocument();
    expect(within(checklistSummary).getByText("1 blocked")).toBeInTheDocument();
    expect(within(checklistSummary).getByText("2 control approvals required")).toBeInTheDocument();
    expect(within(checklistSummary).getByText("1/1 evidence pointer")).toBeInTheDocument();
    expect(within(checklist).getByText("Ledger posting controller check")).toBeInTheDocument();
    expect(within(checklist).getByText("Validated journal draft, retained ledger hash, and controller approval evidence.")).toBeInTheDocument();
    expect(within(checklist).getByText("ledger-evidence-1")).toBeInTheDocument();
    expect(within(checklist).getByText("2 control approvals required")).toBeInTheDocument();
    expect(within(checklist).getByRole("link", { name: "Open remediation for Ledger posting controller check" }))
      .toHaveAttribute("href", "/accounting/ledger");
    expect(await screen.findByText("Ledger Draft Blocked")).toBeInTheDocument();
    const accountingRecordSummary = screen.getByRole("list", { name: "Accounting record evidence summary" });
    expect(within(accountingRecordSummary).getByText("accounting-record-2026-05")).toBeInTheDocument();
    expect(within(accountingRecordSummary).getByText("1 retained evidence link")).toBeInTheDocument();
    const accountingRecordEvidence = screen.getByRole("table", { name: "Operations continuity accounting record evidence" });
    expect(within(accountingRecordEvidence).getByText("Retained source data")).toBeInTheDocument();
    expect(within(accountingRecordEvidence).getByText("Requires provider statement, custodian activity file, bank or account source record")).toBeInTheDocument();
    expect(within(accountingRecordEvidence).getByText("Reconciliation case history")).toBeInTheDocument();
    expect(within(accountingRecordEvidence).getByText("Report pack")).toBeInTheDocument();
    expect(within(accountingRecordEvidence).getByText("Requires report-pack manifest, report-pack provenance, report-pack validation")).toBeInTheDocument();
    expect(within(accountingRecordEvidence).getByText("Exports and retained evidence")).toBeInTheDocument();
    expect(within(accountingRecordEvidence).getByText("Requires export manifest, retained evidence hash, close-package publication")).toBeInTheDocument();
    expect(within(accountingRecordEvidence).getByText("Restatement lineage")).toBeInTheDocument();
    expect(within(accountingRecordEvidence).getAllByText("Review required")).toHaveLength(6);
    expect(within(accountingRecordEvidence).getByRole("link", { name: "Open accounting-record evidence source: Journal and ledger evidence" }))
      .toHaveAttribute("href", "/accounting/ledger");
    const closePackage = screen.getByLabelText("Close package publication summary");
    expect(within(closePackage).getByText("close-package-2026-05")).toBeInTheDocument();
    expect(within(closePackage).getByRole("link", { name: "Open retained close package manifest" }))
      .toHaveAttribute("href", "/accounting/operations-continuity/79f1f386-0bb1-4aef-9a85-fb9d6de8e1f6/close-package/close-package-2026-05-manifest");
    expect(within(closePackage).getByText("Signed by fund-controller")).toBeInTheDocument();
    expect(within(closePackage).getByText("1 checklist control approval")).toBeInTheDocument();

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
    expect(secondRow).toHaveAttribute("aria-controls", OPERATIONS_CONTINUITY_WORKFLOW_DETAIL_PANEL_ID);
    expect(secondRow).toHaveAttribute("aria-expanded", "false");
    await user.click(secondRow);

    await waitFor(() => {
      expect(getOperationsContinuityWorkflow).toHaveBeenLastCalledWith(second.workflowId, expect.objectContaining({ signal: expect.any(AbortSignal) }));
      expect(screen.getByText("2026-04 close workflow")).toBeInTheDocument();
      expect(secondRow).toHaveAttribute("aria-expanded", "true");
    });
  });

  it("renders loading copy instead of an empty-workflows message during initial load", () => {
    vi.mocked(getOperationsContinuityWorkflows).mockReturnValue(new Promise<OperationsContinuityWorkflowSummary[]>(() => undefined));
    vi.mocked(getOperationsContinuityWorkflow).mockResolvedValue(detail);

    renderScreen();

    expect(screen.getByText("Loading operations continuity workflows...")).toBeInTheDocument();
    expect(screen.queryByText("No operations continuity workflows are available for this workstation context.")).not.toBeInTheDocument();
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
