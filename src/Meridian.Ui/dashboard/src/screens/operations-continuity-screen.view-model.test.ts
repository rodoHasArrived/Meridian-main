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
  reconciliationState: "InReview",
  approvalState: "ReviewerAssigned",
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
  approvals: [
    {
      approvalId: "approval-close-2026-05",
      status: "ReviewerAssigned",
      operator: "ops-user",
      reviewer: "fund-controller",
      rationale: "Pending final ledger validation before close sign-off.",
      submittedAtUtc: "2026-05-08T15:05:00Z",
      decidedAtUtc: null,
      evidenceLinks: [
        {
          evidenceId: "approval-evidence-1",
          label: "Approval assignment",
          route: "/workstation/accounting/approvals",
          source: "ops-continuity",
          capturedAtUtc: "2026-05-08T15:05:00Z"
        }
      ]
    }
  ],
  reportPackReadiness: {
    isReady: false,
    reportPackId: null,
    blockingReason: "Close workflow has unresolved ledger blockers.",
    evidenceLinks: [
      {
        evidenceId: "report-pack-blocker-1",
        label: "Report pack blocker",
        route: "/workstation/reporting",
        source: "ops-continuity",
        capturedAtUtc: "2026-05-08T15:10:00Z"
      }
    ]
  },
  closeChecklist: [
    {
      taskId: "close-gate-ledgerposting",
      gate: "LedgerPosting",
      label: "Ledger posting controller check",
      owner: "fund-controller",
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
  evidenceLinks: [
    {
      evidenceId: "close-workflow-1",
      label: "Close workflow snapshot",
      route: "/workstation/accounting/operations-continuity",
      source: "ops-continuity",
      capturedAtUtc: "2026-05-08T15:10:00Z"
    }
  ],
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
    expect(vm.selectedDetail?.metadata).toEqual(expect.arrayContaining([
      { label: "Reconciliation", value: "In Review" },
      { label: "Approval", value: "Reviewer Assigned" },
      {
        label: "Sign-off",
        value: "Reviewer Assigned by fund-controller at May 08, 15:05 UTC: Pending final ledger validation before close sign-off."
      },
      { label: "Report pack", value: "Blocked: Close workflow has unresolved ledger blockers." },
      { label: "Close evidence", value: "3 close evidence links" },
      { label: "Latest audit", value: "Ledger Draft Blocked cdb9449e / devhash-ledg" }
    ]));
    expect(vm.gates.map((gate) => gate.label)).toEqual(["Broker intake", "Ledger posting"]);
    expect(vm.blockers[0]).toMatchObject({
      code: "LEDGER_VALIDATION_REQUIRED",
      severityTone: "blocked"
    });
    expect(vm.checklist).toHaveLength(1);
    expect(vm.checklist[0]).toMatchObject({
      id: "close-gate-ledgerposting",
      label: "Ledger posting controller check",
      gateLabel: "Ledger Posting",
      ownerLabel: "fund-controller",
      requiredEvidence: "ledger-evidence-1",
      approvalLabel: "2 control approvals required",
      evidenceLabel: "ledger-evidence-1",
      remediationHref: "/accounting/ledger",
      remediationLabel: "Open remediation",
      acknowledgementLabel: "Ledger validation is still required.",
      statusLabel: "Pending",
      statusTone: "review"
    });
    expect(vm.checklistSummary).toMatchObject({
      taskCountLabel: "1 close task",
      readyCountLabel: "0 ready",
      blockedCountLabel: "1 blocked",
      acknowledgementCountLabel: "0 acknowledged",
      approvalCountLabel: "2 control approvals required",
      evidenceCountLabel: "1/1 evidence pointer",
      dueSoonLabel: "Next due May 09, 00:00 UTC: Ledger posting controller check",
      statusTone: "blocked"
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

  it("counts durable break-case evidence and keeps the latest case-decision audit visible", () => {
    const workflowWithBreakCaseEvidence: OperationsContinuityWorkflow = {
      ...detail,
      timeline: [
        ...detail.timeline,
        {
          auditId: "f21e0941-c844-41f5-a5f8-6af06e46d497",
          occurredAtUtc: "2026-05-08T15:35:00Z",
          workflowId,
          fundAccountId,
          periodId: "2026-05",
          eventType: "reconciliation-case-resolved",
          fromState: "ReconciliationActive",
          toState: "ApprovalPending",
          gate: "Reconciliation",
          fromGateStatus: "ReviewRequired",
          toGateStatus: "Passed",
          actor: "fund-controller",
          rationale: "Accepted broker cash variance after custodian close statement matched the ledger adjustment.",
          correlationId: "case-decision-recon-break-42",
          references: [
            {
              evidenceId: "case-decision-audit-1",
              label: "Case decision audit",
              route: "/workstation/accounting/reconciliation/recon-break-42",
              source: "operations-continuity",
              capturedAtUtc: "2026-05-08T15:35:00Z"
            }
          ],
          previousHash: "devhash-ledger",
          currentHash: "casehash-decision-accepted-202605"
        }
      ],
      breakCases: [
        {
          breakId: "recon-break-42",
          checkId: "cash-balance-check",
          category: "CashBalance",
          severity: "Warning",
          status: "Resolved",
          owner: "fund-controller",
          dueDate: "2026-05-09",
          expectedSource: "ledger",
          actualSource: "custodian",
          expectedAmount: 125000.25,
          actualAmount: 124998.25,
          variance: -2,
          securityId: null,
          symbol: null,
          suggestedAction: "Accept custodian statement evidence and close the reconciliation break.",
          evidenceLinks: [
            {
              evidenceId: "recon-break-close-evidence-1",
              label: "Custodian statement case close evidence",
              route: "/workstation/accounting/reconciliation/recon-break-42/evidence",
              source: "operations-continuity",
              capturedAtUtc: "2026-05-08T15:34:00Z"
            }
          ]
        }
      ]
    };

    const vm = buildOperationsContinuityScreenViewModel({
      workflows: [summary],
      selectedWorkflowId: workflowId,
      detail: workflowWithBreakCaseEvidence,
      loading: false,
      detailLoading: false,
      error: null,
      detailError: null,
      refresh: vi.fn(),
      selectWorkflow: vi.fn()
    });

    expect(vm.selectedDetail?.metadata).toEqual(expect.arrayContaining([
      { label: "Break cases", value: "1" },
      { label: "Close evidence", value: "4 close evidence links" },
      { label: "Latest audit", value: "Reconciliation Case Resolved f21e0941 / casehash-dec" }
    ]));
    expect(vm.timeline[0]).toMatchObject({
      id: "f21e0941-c844-41f5-a5f8-6af06e46d497",
      title: "Reconciliation Case Resolved",
      detail: "Accepted broker cash variance after custodian close statement matched the ledger adjustment.",
      actorLabel: "fund-controller",
      stateLabel: "Reconciliation Active to Approval Pending",
      hashLabel: "casehash-dec"
    });
    expect(vm.timeline[0]?.ariaLabel).toContain("Reconciliation Case Resolved by fund-controller");
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
    expect(vm.checklistEmptyText).toBe("Loading selected workflow checklist...");
    expect(vm.timelineEmptyText).toBe("Loading workflow timeline.");
  });

  it("summarizes completed checklist controls from the shared close-checklist contract", () => {
    const completedDetail: OperationsContinuityWorkflow = {
      ...detail,
      closeChecklist: [
        {
          ...detail.closeChecklist[0]!,
          status: "Acknowledged",
          blockingReason: null,
          requiredApprovalCount: 1,
          acknowledgedAtUtc: "2026-05-09T15:30:00Z",
          acknowledgedBy: "fund-controller"
        },
        {
          taskId: "close-gate-reportpack",
          gate: "Approval",
          label: "Report pack sign-off",
          owner: "fund-admin",
          dueDate: null,
          requiredApprovalCount: 1,
          expiresOn: null,
          status: "Complete",
          blockingReason: null,
          evidencePointer: "report-pack-evidence-1",
          remediationRoute: "/workstation/reporting",
          canAcknowledge: false,
          acknowledgedAtUtc: null,
          acknowledgedBy: null
        }
      ]
    };

    const vm = buildOperationsContinuityScreenViewModel({
      workflows: [summary],
      selectedWorkflowId: workflowId,
      detail: completedDetail,
      loading: false,
      detailLoading: false,
      error: null,
      detailError: null,
      refresh: vi.fn(),
      selectWorkflow: vi.fn()
    });

    expect(vm.checklistSummary).toMatchObject({
      taskCountLabel: "2 close tasks",
      readyCountLabel: "2 ready",
      blockedCountLabel: "0 blocked",
      acknowledgementCountLabel: "1 acknowledged",
      approvalCountLabel: "2 control approvals required",
      evidenceCountLabel: "2/2 evidence pointers",
      dueSoonLabel: "No open due dates",
      statusTone: "ready"
    });
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
