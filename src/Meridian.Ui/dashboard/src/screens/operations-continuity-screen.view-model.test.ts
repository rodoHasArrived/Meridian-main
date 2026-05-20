import { cleanup, renderHook, waitFor } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import {
  buildOperationsContinuityScreenViewModel,
  OPERATIONS_CONTINUITY_WORKFLOW_DETAIL_PANEL_ID,
  useOperationsContinuityScreenViewModel
} from "@/screens/operations-continuity-screen.view-model";
import type {
  OperationsContinuityWorkflow,
  OperationsContinuityWorkflowSummary,
  OperationsGate
} from "@/types";

afterEach(() => {
  cleanup();
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

describe("Operations Continuity view model", () => {
  it("lists workflows, opens detail, and ranks blocked-gate next action with a local route", () => {
    const vm = buildOperationsContinuityScreenViewModel({
      workflows: [summary],
      selectedWorkflowId: workflowId,
      detail,
      loading: false,
      detailLoading: false,
      error: null,
      detailError: null,
      refresh: vi.fn(),
      selectWorkflow: vi.fn()
    });

    expect(vm.workflows).toHaveLength(1);
    expect(vm.workflows[0]).toMatchObject({
      title: "2026-05 close",
      statusLabel: "Ledger Posting Draft",
      gatesLabel: "1/2 gates passed",
      blockersLabel: "1 blocker",
      detailPanelId: OPERATIONS_CONTINUITY_WORKFLOW_DETAIL_PANEL_ID,
      expanded: true,
      rowClassName: "bg-warning/5"
    });
    expect(vm.selectedDetail).toMatchObject({
      id: OPERATIONS_CONTINUITY_WORKFLOW_DETAIL_PANEL_ID,
      ariaLabel: "Operations continuity detail for 2026-05 close workflow",
      description: "Selected close-lane evidence, gate progress, Security Master snapshot, and blocker count."
    });
    expect(vm.workflowsTableCaption).toContain("Select a row to inspect close-lane gates");
    expect(vm.selectedDetail?.metadata).toContainEqual({ label: "Break cases", value: "0" });
    expect(vm.gates.map((gate) => gate.label)).toEqual(["Broker intake", "Ledger posting"]);
    expect(vm.blockers[0]).toMatchObject({
      code: "LEDGER_VALIDATION_REQUIRED",
      severityTone: "blocked"
    });
    expect(vm.timeline[0]).toMatchObject({
      title: "Ledger Draft Blocked",
      stateLabel: "Ledger Posting Draft to Blocked"
    });
    expect(vm.nextAction).toMatchObject({
      title: "Resolve Ledger Posting blockers",
      href: "/accounting",
      disabled: false,
      disabledReason: null
    });
  });

  it("explains disabled next actions when the server omits a local route", () => {
    const routeLess = {
      ...summary,
      nextActions: [
        {
          code: "APPROVE_EXTERNALLY",
          label: "Approve externally",
          route: null,
          gate: "Approval" as const
        }
      ],
      gates: []
    };

    const vm = buildOperationsContinuityScreenViewModel({
      workflows: [routeLess],
      selectedWorkflowId: workflowId,
      detail: null,
      loading: false,
      detailLoading: false,
      error: null,
      detailError: null,
      refresh: vi.fn(),
      selectWorkflow: vi.fn()
    });

    expect(vm.nextAction.disabled).toBe(true);
    expect(vm.nextAction.disabledReason).toBe("The server did not provide a local workstation route for this action.");
  });

  it("prefers blocked gate actions over earlier lower-priority workflow actions", () => {
    const prioritizedSummary = {
      ...summary,
      nextActions: [
        {
          code: "CONTINUE_BROKERINGEST",
          label: "Continue Broker Ingest",
          route: "/workstation/accounting",
          gate: "BrokerIngest" as const
        },
        {
          code: "RESOLVE_LEDGERPOSTING_BLOCKERS",
          label: "Resolve Ledger Posting blockers",
          route: "/workstation/accounting",
          gate: "LedgerPosting" as const
        }
      ]
    };

    const vm = buildOperationsContinuityScreenViewModel({
      workflows: [prioritizedSummary],
      selectedWorkflowId: workflowId,
      detail: null,
      loading: false,
      detailLoading: false,
      error: null,
      detailError: null,
      refresh: vi.fn(),
      selectWorkflow: vi.fn()
    });

    expect(vm.nextAction).toMatchObject({
      title: "Resolve Ledger Posting blockers",
      href: "/accounting",
      disabled: false
    });
  });

  it("uses loading copy for empty evidence tables while data is still loading", () => {
    const vm = buildOperationsContinuityScreenViewModel({
      workflows: [],
      selectedWorkflowId: null,
      detail: null,
      loading: true,
      detailLoading: true,
      error: null,
      detailError: null,
      refresh: vi.fn(),
      selectWorkflow: vi.fn()
    });

    expect(vm.workflowsEmptyText).toBe("Loading operations continuity workflows...");
    expect(vm.gatesEmptyText).toBe("Loading selected workflow gates...");
    expect(vm.blockersEmptyText).toBe("Loading selected workflow blockers...");
    expect(vm.timelineEmptyText).toBe("Loading workflow timeline.");
  });

  it("aborts in-flight list requests when the hook unmounts", () => {
    let capturedSignal: AbortSignal | undefined;
    const services = {
      listWorkflows: vi.fn((_filters, options) => {
        capturedSignal = options?.signal;
        return new Promise<OperationsContinuityWorkflowSummary[]>(() => undefined);
      }),
      getWorkflow: vi.fn()
    };

    const { unmount } = renderHook(() => useOperationsContinuityScreenViewModel(services));
    unmount();

    expect(capturedSignal?.aborted).toBe(true);
  });

  it("loads detail after selecting the newest workflow", async () => {
    const services = {
      listWorkflows: vi.fn().mockResolvedValue([summary]),
      getWorkflow: vi.fn().mockResolvedValue(detail)
    };

    const { result } = renderHook(() => useOperationsContinuityScreenViewModel(services));

    await waitFor(() => {
      expect(result.current.loading).toBe(false);
      expect(result.current.selectedWorkflowId).toBe(workflowId);
      expect(result.current.timeline).toHaveLength(1);
    });
    expect(services.getWorkflow).toHaveBeenCalledWith(workflowId, expect.objectContaining({ signal: expect.any(AbortSignal) }));
  });
});
