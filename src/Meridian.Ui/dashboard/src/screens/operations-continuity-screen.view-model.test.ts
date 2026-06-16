import { cleanup, renderHook, waitFor } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import {
  buildOperationsContinuityScreenViewModel,
  OPERATIONS_CONTINUITY_WORKFLOW_DETAIL_PANEL_ID,
  useOperationsContinuityScreenViewModel
} from "@/screens/operations-continuity-screen.view-model";
import type {
  OperationsCloseChecklistTask,
  OperationsCloseCalendar,
  OperationsContinuityWorkflow,
  OperationsContinuityWorkflowSummary,
  OperationsEvidenceLink,
  OperationsGate,
  OperationsReconciliationLaneSummary,
  PrivateCapitalCloseCockpit
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

const reconciliationLanes: OperationsReconciliationLaneSummary[] = [
  {
    laneId: "cash-reconciliation",
    label: "Cash reconciliation",
    status: "Ready",
    isReady: true,
    breakCount: 0,
    summary: "Cash reconciliation is covered by retained bank and custodian cash evidence.",
    routeHint: "/workstation/accounting/reconciliation",
    evidenceLinks: [
      {
        evidenceId: "cash-lane-evidence-1",
        label: "Cash reconciliation evidence",
        route: "/workstation/accounting/reconciliation/cash",
        source: "operations-continuity",
        capturedAtUtc: "2026-05-08T15:35:00Z"
      }
    ],
    requiredActions: []
  },
  {
    laneId: "position-reconciliation",
    label: "Position reconciliation",
    status: "Ready",
    isReady: true,
    breakCount: 0,
    summary: "Position reconciliation has matched portfolio and custodian positions.",
    routeHint: "/workstation/accounting/reconciliation",
    evidenceLinks: [],
    requiredActions: []
  },
  {
    laneId: "trade-reconciliation",
    label: "Trade reconciliation",
    status: "Ready",
    isReady: true,
    breakCount: 0,
    summary: "Trade reconciliation matched fills, orders, and execution activity.",
    routeHint: "/workstation/accounting/reconciliation",
    evidenceLinks: [],
    requiredActions: []
  },
  {
    laneId: "income-reconciliation",
    label: "Income reconciliation",
    status: "Ready",
    isReady: true,
    breakCount: 0,
    summary: "Income reconciliation retained expected dividend, interest, and accrual evidence.",
    routeHint: "/workstation/accounting/reconciliation",
    evidenceLinks: [],
    requiredActions: []
  },
  {
    laneId: "mbs-factor-reconciliation",
    label: "MBS factor reconciliation",
    status: "ReviewRequired",
    isReady: false,
    breakCount: 1,
    summary: "MBS factor reconciliation has 1 open break requiring controller review.",
    routeHint: "/workstation/accounting/reconciliation",
    evidenceLinks: [
      {
        evidenceId: "factor-break-evidence-1",
        label: "Factor variance evidence",
        route: "/workstation/accounting/reconciliation/recon-break-factor-1",
        source: "operations-continuity",
        capturedAtUtc: "2026-05-08T15:36:00Z"
      }
    ],
    requiredActions: ["Resolve or assign MBS factor reconciliation breaks and retain evidence."]
  },
  {
    laneId: "bank-reconciliation",
    label: "Bank reconciliation",
    status: "Ready",
    isReady: true,
    breakCount: 0,
    summary: "Bank reconciliation retained normalized bank transaction evidence.",
    routeHint: "/workstation/accounting/reconciliation",
    evidenceLinks: [],
    requiredActions: []
  },
  {
    laneId: "gl-reconciliation",
    label: "GL reconciliation support",
    status: "Ready",
    isReady: true,
    breakCount: 0,
    summary: "GL reconciliation support has expected journal preview evidence.",
    routeHint: "/workstation/accounting/ledger",
    evidenceLinks: [],
    requiredActions: []
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
  reconciliationLanes,
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
  closePackage: null,
  dashboardSummary: {
    dashboardId: "operations-dashboard:fund-alpha:2026-05",
    stage: "Resolve Exceptions",
    status: "Missing",
    isReady: false,
    readyMetricCount: 2,
    totalMetricCount: 6,
    summary: "Financial Operations dashboard is in Resolve Exceptions with 4 metrics requiring review.",
    metrics: [
      {
        metricId: "receive-activity",
        label: "Receive Activity",
        value: "Complete",
        status: "Ready",
        detail: "Broker activity has been received and normalized for this account-period workflow.",
        routeHint: "/workstation/accounting",
        evidenceLinks: [],
        requiredActions: []
      },
      {
        metricId: "match-records",
        label: "Match Records",
        value: "6/7 lanes ready",
        status: "ReviewRequired",
        detail: "Cash, position, trade, income, MBS factor, bank, and GL reconciliation lanes are tracked from the shared workflow detail.",
        routeHint: "/workstation/accounting/reconciliation",
        evidenceLinks: [
          {
            evidenceId: "factor-break-evidence-1",
            label: "Factor variance evidence",
            route: "/workstation/accounting/reconciliation/recon-break-factor-1",
            source: "operations-continuity",
            capturedAtUtc: "2026-05-08T15:36:00Z"
          }
        ],
        requiredActions: ["Complete source-backed reconciliation lanes before approval."]
      },
      {
        metricId: "resolve-exceptions",
        label: "Resolve Exceptions",
        value: "1 open",
        status: "ReviewRequired",
        detail: "1 reconciliation break requires assignment, escalation, or resolution evidence.",
        routeHint: "/workstation/accounting/reconciliation",
        evidenceLinks: [],
        requiredActions: ["Assign, escalate, or resolve open exceptions and retain resolution evidence."]
      },
      {
        metricId: "approve-results",
        label: "Approve Results",
        value: "ReviewerAssigned",
        status: "ReviewRequired",
        detail: "Approval history is not complete for this workflow.",
        routeHint: "/workstation/accounting/approvals",
        evidenceLinks: [
          {
            evidenceId: "approval-evidence-1",
            label: "Approval assignment",
            route: "/workstation/accounting/approvals",
            source: "ops-continuity",
            capturedAtUtc: "2026-05-08T15:05:00Z"
          }
        ],
        requiredActions: ["Complete workflow approval and checklist-control approvals."]
      },
      {
        metricId: "produce-evidence",
        label: "Produce Evidence",
        value: "Evidence package pending",
        status: "Missing",
        detail: "Close workflow has unresolved ledger blockers.",
        routeHint: "/workstation/reporting/report-packs",
        evidenceLinks: [],
        requiredActions: ["Publish and retain the evidence package before period close."]
      },
      {
        metricId: "close-support",
        label: "Close Support",
        value: "Close readiness pending",
        status: "Missing",
        detail: "Close checklist, period lock, and reopen evidence are governed by the shared workflow.",
        routeHint: "/workstation/accounting/operations-continuity",
        evidenceLinks: [],
        requiredActions: ["Clear close readiness blockers and retain period-lock or reopen evidence."]
      }
    ],
    evidenceLinks: [
      {
        evidenceId: "close-workflow-1",
        label: "Close workflow snapshot",
        route: "/workstation/accounting/operations-continuity",
        source: "ops-continuity",
        capturedAtUtc: "2026-05-08T15:10:00Z"
      }
    ],
    requiredActions: [
      "Complete source-backed reconciliation lanes before approval.",
      "Publish and retain the evidence package before period close."
    ]
  },
  reviewedAutomation: {
    summaryId: "reviewed-automation:fund-alpha:2026-05",
    stage: "Report commentary and audit request list draft review",
    status: "ReviewRequired",
    requiresHumanReview: true,
    summary: "Automation may draft reporting commentary and audit request lists, but publication remains behind human approval.",
    allowedUseCases: ["Draft report commentary", "Draft audit request lists"],
    prohibitedActions: ["Approve workflow", "Publish close package"],
    evidenceLinks: [
      {
        evidenceId: "automation-review-evidence-1",
        label: "Reviewed automation draft packet",
        route: "/workstation/reporting/report-packs/automation-review",
        source: "operations-continuity",
        capturedAtUtc: "2026-05-08T15:40:00Z"
      }
    ],
    requiredActions: ["Review drafted report commentary and audit request lists against retained evidence before submission."],
    artifacts: [
      {
        artifactId: "reviewed-automation:report-commentary-draft",
        artifactKind: "Report commentary",
        title: "Report commentary draft",
        status: "ReviewRequired",
        requiresHumanReview: true,
        confidencePercent: 84,
        sourceSummary: "Draft commentary is generated from retained close, ledger, reconciliation, and report-pack evidence.",
        suggestedOperatorAction: "Review commentary against retained evidence before report approval or publication.",
        blockedMaterialAction: "Cannot publish reports or release support packages.",
        evidenceLinks: [
          {
            evidenceId: "automation-review-evidence-1",
            label: "Reviewed automation draft packet",
            route: "/workstation/reporting/report-packs/automation-review",
            source: "operations-continuity",
            capturedAtUtc: "2026-05-08T15:40:00Z"
          }
        ],
        reviewChecklist: ["Review drafted report commentary and audit request lists against retained evidence before submission."]
      },
      {
        artifactId: "reviewed-automation:audit-request-list-draft",
        artifactKind: "Audit request list",
        title: "Audit request list draft",
        status: "ReviewRequired",
        requiresHumanReview: true,
        confidencePercent: 79,
        sourceSummary: "Draft audit request lists summarize missing support and unresolved evidence gaps.",
        suggestedOperatorAction: "Review each requested support item and assign an owner before audit release.",
        blockedMaterialAction: "Cannot erase evidence or satisfy audit requests without retained support.",
        evidenceLinks: [],
        reviewChecklist: ["Review drafted report commentary and audit request lists against retained evidence before submission."]
      }
    ]
  },
  evidencePackages: [
    {
      packageId: "accounting-record-2026-05",
      label: "Accounting record evidence",
      status: "ReviewRequired",
      isReady: false,
      summary: "Accounting record has 3 of 8 required evidence categories complete.",
      routeHint: "/workstation/accounting/operations-continuity",
      completeCategoryCount: 3,
      requiredCategoryCount: 8,
      evidenceLinkCount: 1,
      evidenceLinks: [
        {
          evidenceId: "close-workflow-1",
          label: "Close workflow snapshot",
          route: "/workstation/accounting/operations-continuity",
          source: "ops-continuity",
          capturedAtUtc: "2026-05-08T15:10:00Z"
        }
      ],
      requiredActions: ["Complete all accounting-record evidence categories before publishing the evidence package."]
    },
    {
      packageId: "report-pack:fund-alpha:2026-05",
      label: "Report pack evidence",
      status: "Missing",
      isReady: false,
      summary: "Close workflow has unresolved ledger blockers.",
      routeHint: "/workstation/reporting/report-packs",
      completeCategoryCount: 0,
      requiredCategoryCount: 1,
      evidenceLinkCount: 0,
      evidenceLinks: [],
      requiredActions: ["Link ready report-pack evidence before close publication."]
    },
    {
      packageId: "close-package:fund-alpha:2026-05",
      label: "Close package manifest",
      status: "Missing",
      isReady: false,
      summary: "Close package manifest and retained evidence hash have not been published.",
      routeHint: "/workstation/accounting/operations-continuity",
      completeCategoryCount: 0,
      requiredCategoryCount: 1,
      evidenceLinkCount: 0,
      evidenceLinks: [],
      requiredActions: ["Publish the close package manifest and retain the evidence hash."]
    },
    {
      packageId: "audit-support:fund-alpha:2026-05",
      label: "Audit support package",
      status: "ReviewRequired",
      isReady: false,
      summary: "5 audit evidence categories are missing.",
      routeHint: "/workstation/reporting/evidence",
      completeCategoryCount: 3,
      requiredCategoryCount: 8,
      evidenceLinkCount: 1,
      evidenceLinks: [
        {
          evidenceId: "close-workflow-1",
          label: "Close workflow snapshot",
          route: "/workstation/accounting/operations-continuity",
          source: "ops-continuity",
          capturedAtUtc: "2026-05-08T15:10:00Z"
        }
      ],
      requiredActions: ["Complete missing audit evidence categories before releasing the package."]
    },
    {
      packageId: "period-lock-reopen:fund-alpha:2026-05",
      label: "Period lock and reopen evidence",
      status: "Missing",
      isReady: false,
      summary: "Period 2026-05 has not been locked by a close package; governed reopen evidence will be required if a closed workflow is reopened.",
      routeHint: "/workstation/accounting/operations-continuity",
      completeCategoryCount: 1,
      requiredCategoryCount: 2,
      evidenceLinkCount: 0,
      evidenceLinks: [],
      requiredActions: ["Close the workflow and retain the period-lock package before evidence release."]
    }
  ],
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

const closeCockpit: PrivateCapitalCloseCockpit = {
  fundProfileId: "fund-alpha",
  ledgerBookId: "ledger-main",
  fundAccountId,
  periodId: "2026-05",
  entityId: "entity-master",
  projectedAtUtc: "2026-05-10T18:30:00Z",
  cockpitRoute: "/workstation/accounting/operations-continuity",
  overallStatus: "ReviewRequired",
  isReadyToClose: false,
  readinessScore: 72,
  workflowCount: 1,
  fundEventCount: 3,
  capitalAccountCount: 4,
  reportOutputCount: 2,
  deliveredReportOutputCount: 1,
  readyLaneCount: 2,
  blockedLaneCount: 1,
  lanes: [
    {
      laneId: "partner-capital-tie-outs",
      label: "Partner capital account tie-outs",
      status: "Ready",
      isReady: true,
      summary: "Partner capital subledger, ledger, and investor statement evidence tie out.",
      route: "/workstation/accounting/private-capital/capital-account-subledger",
      evidenceLinkCount: 2,
      evidenceLinks: [
        {
          evidenceId: "partner-tie-out-evidence-1",
          label: "Partner capital tie-out packet",
          route: "/workstation/accounting/private-capital/capital-account-subledger/tie-outs",
          source: "operations-continuity",
          capturedAtUtc: "2026-05-10T18:00:00Z"
        }
      ],
      requiredActions: []
    },
    {
      laneId: "expense-fee-allocation",
      label: "Expense, fee, and allocation review",
      status: "Ready",
      isReady: true,
      summary: "Management fee, expense, and allocation review evidence is retained.",
      route: "/workstation/accounting/private-capital/fund-events/management-fee",
      evidenceLinkCount: 2,
      evidenceLinks: [],
      requiredActions: []
    },
    {
      laneId: "nav-support",
      label: "NAV support",
      status: "ReviewRequired",
      isReady: false,
      summary: "Shadow NAV support package still needs retained positions, cash, and pricing evidence.",
      route: "/workstation/portfolio/nav",
      evidenceLinkCount: 1,
      evidenceLinks: [],
      requiredActions: ["Retain NAV support for positions, cash, and pricing"]
    },
    {
      laneId: "close-package",
      label: "Evidence package",
      status: "Blocked",
      isReady: false,
      summary: "Close evidence package publication is blocked until the manifest is retained.",
      route: "/workstation/accounting/operations-continuity",
      evidenceLinkCount: 0,
      evidenceLinks: [],
      requiredActions: ["Publish the close package manifest"]
    },
    {
      laneId: "period-lock",
      label: "Period lock evidence",
      status: "ReviewRequired",
      isReady: false,
      summary: "Period lock evidence remains pending until close approval completes.",
      route: "/workstation/accounting/operations-continuity",
      evidenceLinkCount: 0,
      evidenceLinks: [],
      requiredActions: ["Close the workflow and retain period-lock evidence"]
    }
  ],
  workflows: [
    {
      workflowId,
      fundAccountId,
      periodId: "2026-05",
      status: "LedgerPostingDraft",
      closeReadinessScore: 72,
      isReadyToClose: false,
      workflowRoute: "/workstation/accounting/operations-continuity",
      closePackageId: null,
      closePackageRoute: null,
      blockerCount: 1,
      openChecklistCount: 2,
      updatedAtUtc: "2026-05-08T15:10:00Z"
    }
  ],
  approvalHistory: [
    {
      approvalId: "approval-close-2026-05",
      workflowId,
      fundAccountId,
      periodId: "2026-05",
      status: "ReviewerAssigned",
      operator: "ops-user",
      reviewer: "fund-controller",
      rationale: "Pending final ledger validation before close sign-off.",
      submittedAtUtc: "2026-05-08T15:05:00Z",
      decidedAtUtc: null,
      workflowRoute: "/workstation/accounting/operations-continuity",
      evidenceLinkCount: 1,
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
  navSupportPackages: [
    {
      packageId: "nav-support:fund-alpha:2026-05",
      label: "NAV support package",
      status: "ReviewRequired",
      isReady: false,
      summary: "NAV support package has retained cash and position evidence but still needs pricing and shadow NAV evidence.",
      route: "/workstation/portfolio/nav",
      shadowNav: 1250000,
      currency: "USD",
      evidenceLinkCount: 1,
      evidenceLinks: [
        {
          evidenceId: "nav-support-evidence-1",
          label: "NAV support evidence",
          route: "/workstation/portfolio/nav/support-package",
          source: "operations-continuity",
          capturedAtUtc: "2026-05-10T18:10:00Z"
        }
      ],
      components: [
        {
          componentId: "positions",
          label: "Positions",
          status: "Ready",
          isReady: true,
          summary: "Position support retained.",
          route: "/workstation/portfolio",
          score: 100
        },
        {
          componentId: "cash",
          label: "Cash",
          status: "Ready",
          isReady: true,
          summary: "Cash support retained.",
          route: "/workstation/accounting/cash",
          score: 100
        },
        {
          componentId: "pricing",
          label: "Pricing",
          status: "ReviewRequired",
          isReady: false,
          summary: "Pricing support still needs retained evidence.",
          route: "/workstation/data/pricing",
          score: 60
        },
        {
          componentId: "shadow-nav",
          label: "Shadow NAV",
          status: "ReviewRequired",
          isReady: false,
          summary: "Shadow NAV report output evidence is pending.",
          route: "/workstation/reporting/shadow-nav-pack",
          score: 50
        }
      ],
      requiredActions: ["Retain NAV support package for positions, cash, pricing, and shadow NAV evidence."]
    }
  ],
  evidencePackages: [
    {
      packageId: "private-capital:fund-event-accounting",
      label: "Fund-event accounting evidence",
      status: "Ready",
      isReady: true,
      summary: "Fund-event accounting package retains source activity, journals, capital accounts, and allocation review.",
      routeHint: "/workstation/accounting/private-capital/fund-events",
      completeCategoryCount: 4,
      requiredCategoryCount: 4,
      evidenceLinkCount: 3,
      evidenceLinks: [
        {
          evidenceId: "fund-event-accounting-evidence",
          label: "Fund-event accounting evidence",
          route: "/workstation/accounting/private-capital/fund-events",
          source: "private-capital",
          capturedAtUtc: "2026-05-10T18:05:00Z"
        }
      ],
      requiredActions: []
    },
    {
      packageId: "private-capital:nav-support",
      label: "NAV support evidence package",
      status: "ReviewRequired",
      isReady: false,
      summary: "NAV support evidence package still needs pricing and shadow NAV support.",
      routeHint: "/workstation/portfolio/nav",
      completeCategoryCount: 1,
      requiredCategoryCount: 3,
      evidenceLinkCount: 1,
      evidenceLinks: [
        {
          evidenceId: "nav-package-evidence",
          label: "NAV package evidence",
          route: "/workstation/portfolio/nav/support-package",
          source: "operations-continuity",
          capturedAtUtc: "2026-05-10T18:10:00Z"
        }
      ],
      requiredActions: ["Retain complete NAV support package evidence before close sign-off."]
    }
  ],
  blockers: [
    {
      code: "LEDGER_VALIDATION_REQUIRED",
      category: "Ledger",
      severity: "Critical",
      message: "Ledger posting requires a balanced and validated journal draft.",
      gate: "LedgerPosting",
      routeHint: "/workstation/accounting/ledger"
    }
  ],
  nextActions: [
    {
      code: "RESOLVE_LEDGERPOSTING_BLOCKERS",
      label: "Resolve Ledger Posting blockers",
      route: "/workstation/accounting/ledger",
      gate: "LedgerPosting"
    }
  ],
  liveCapabilities: ["workflow-readiness", "capital-account-evidence"],
  plannedCapabilities: ["tax-support-drilldown"]
};

const closeCalendar: OperationsCloseCalendar = {
  generatedAtUtc: "2026-05-10T18:45:00Z",
  items: [
    {
      workflowId,
      fundAccountId,
      periodId: "2026-05",
      status: "LedgerPostingDraft",
      version: 4,
      nextDueDate: "2026-05-12",
      nextDueTaskId: "ledger-controller-check",
      nextDueLabel: "Ledger posting controller check",
      nextDueOwner: "fund-controller",
      readinessSeverity: "Critical",
      readinessScore: 72,
      isReadyToClose: false,
      blockerCount: 1,
      openChecklistCount: 2,
      requiredApprovalCount: 2,
      completedApprovalCount: 1,
      route: "/workstation/accounting/operations-continuity",
      readinessComponents: [],
      readinessBlockers: [],
      readinessNextActions: []
    }
  ]
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
      { label: "Period lock", value: "Open until close publication" },
      { label: "Close evidence", value: "3 close evidence links" },
      { label: "Latest audit", value: "Ledger Draft Blocked cdb9449e / devhash-ledg" }
    ]));
    expect(vm.gates.map((gate) => gate.label)).toEqual(["Broker intake", "Ledger posting"]);
    expect(vm.blockers[0]).toMatchObject({
      code: "LEDGER_VALIDATION_REQUIRED",
      severityTone: "blocked"
    });
    expect(vm.dashboard).toMatchObject({
      statusLabel: "Missing",
      statusTone: "blocked",
      stageLabel: "Core flow: Resolve Exceptions",
      metricCountLabel: "2/6 metrics ready",
      evidenceLabel: "1 retained evidence link",
      requiredActionsLabel: "Complete source-backed reconciliation lanes before approval.; Publish and retain the evidence package before period close."
    });
    expect(vm.dashboard.metrics).toHaveLength(6);
    expect(vm.dashboard.metrics[1]).toMatchObject({
      id: "match-records",
      label: "Match Records",
      valueLabel: "6/7 lanes ready",
      statusLabel: "Review Required",
      evidenceLabel: "1 evidence link",
      requiredActionsLabel: "Complete source-backed reconciliation lanes before approval.",
      routeHref: "/accounting/reconciliation"
    });
    expect(vm.commandSpine).toMatchObject({
      statusLabel: "Missing",
      statusTone: "blocked",
      summaryLabel: "Core flow: Resolve Exceptions; 2/6 metrics ready; Complete source-backed reconciliation lanes before approval.; Publish and retain the evidence package before period close."
    });
    expect(vm.commandSpine.rows.map((row) => row.id)).toEqual([
      "receive-activity",
      "match-records",
      "resolve-exceptions",
      "approve-results",
      "produce-evidence",
      "close-support"
    ]);
    expect(vm.commandSpine.rows[0]).toMatchObject({
      stageLabel: "Receive Activity",
      commandLabel: "Start/import/normalize activity",
      postureLabel: "Complete",
      statusLabel: "Ready",
      guardLabel: "Shared command guard clear",
      routeHref: "/accounting"
    });
    expect(vm.commandSpine.rows[2]).toMatchObject({
      stageLabel: "Resolve Exceptions",
      commandLabel: "Assign, escalate, or resolve breaks",
      statusLabel: "Review Required",
      guardLabel: "Assign, escalate, or resolve open exceptions and retain resolution evidence.",
      routeHref: "/accounting/reconciliation"
    });
    expect(vm.commandSpine.rows[3]).toMatchObject({
      stageLabel: "Approve Results",
      commandLabel: "Submit or decide approval",
      canSubmitApproval: false,
      submitApprovalLabel: "Submit approval",
      submitApprovalDisabledReason: "Approval state is already Reviewer Assigned.",
      submitApprovalReportPackId: null,
      submitApprovalReviewer: "fund-controller",
      submitApprovalAriaLabel: "Submit workflow approval for 2026-05"
    });
    expect(vm.reviewedAutomation).toMatchObject({
      statusLabel: "Review Required",
      statusTone: "review",
      stageLabel: "Stage: Report commentary and audit request list draft review",
      reviewLabel: "Human review required",
      summaryLabel: "Automation may draft reporting commentary and audit request lists, but publication remains behind human approval.",
      allowedUseCasesLabel: "Draft report commentary, Draft audit request lists",
      prohibitedActionsLabel: "Approve workflow, Publish close package",
      evidenceLabel: "1 retained review evidence link",
      requiredActionsLabel: "Review drafted report commentary and audit request lists against retained evidence before submission."
    });
    expect(vm.reviewedAutomation.artifacts).toHaveLength(2);
    expect(vm.reviewedAutomation.artifacts[0]).toMatchObject({
      id: "reviewed-automation:report-commentary-draft",
      kindLabel: "Report commentary",
      title: "Report commentary draft",
      statusLabel: "Review Required",
      statusTone: "review",
      reviewLabel: "Human review required",
      confidenceLabel: "84% confidence",
      evidenceLabel: "1 evidence link",
      blockedActionLabel: "Cannot publish reports or release support packages."
    });
    expect(vm.reviewedAutomation.artifacts[1]).toMatchObject({
      id: "reviewed-automation:audit-request-list-draft",
      kindLabel: "Audit request list",
      confidenceLabel: "79% confidence",
      evidenceLabel: "No retained evidence",
      suggestedActionLabel: "Review each requested support item and assign an owner before audit release."
    });
    expect(vm.financialOperationsQueue).toMatchObject({
      statusLabel: "Blocked",
      statusTone: "blocked",
      summaryLabel: "9 open work items: 4 blocked, 5 review."
    });
    expect(vm.financialOperationsQueue.items.map((item) => item.id)).toEqual([
      "reconciliation-lane:mbs-factor-reconciliation",
      "workflow-blocker:LEDGER_VALIDATION_REQUIRED:0",
      "checklist:close-gate-ledgerposting",
      "approval:approval-close-2026-05",
      "evidence-package:accounting-record-2026-05",
      "evidence-package:report-pack:fund-alpha:2026-05",
      "evidence-package:close-package:fund-alpha:2026-05",
      "evidence-package:audit-support:fund-alpha:2026-05",
      "evidence-package:period-lock-reopen:fund-alpha:2026-05"
    ]);
    expect(vm.financialOperationsQueue.items[0]).toMatchObject({
      kindLabel: "Reconciliation lane",
      title: "MBS factor reconciliation",
      statusLabel: "Review Required",
      statusTone: "review",
      ownerLabel: "Reconciliation operations",
      dueLabel: "1 open break",
      evidenceLabel: "1 evidence link",
      actionLabel: "Resolve or assign MBS factor reconciliation breaks and retain evidence.",
      routeHref: "/accounting/reconciliation"
    });
    expect(vm.financialOperationsQueue.items[1]).toMatchObject({
      kindLabel: "Workflow blocker",
      title: "LEDGER_VALIDATION_REQUIRED",
      detail: "Ledger Posting: Ledger posting requires a balanced and validated journal draft.",
      statusLabel: "Critical",
      statusTone: "blocked",
      ownerLabel: "Operations control",
      dueLabel: "Ledger Posting gate",
      evidenceLabel: "No linked evidence",
      actionLabel: "Resolve Ledger Posting blocker and retain evidence.",
      routeHref: "/accounting/operations-continuity"
    });
    expect(vm.financialOperationsQueue.items[2]).toMatchObject({
      kindLabel: "Checklist",
      title: "Ledger posting controller check",
      statusLabel: "Pending",
      ownerLabel: "fund-controller",
      dueLabel: "May 09, 00:00 UTC",
      evidenceLabel: "ledger-evidence-1",
      routeHref: "/accounting/ledger"
    });
    expect(vm.financialOperationsQueue.items[3]).toMatchObject({
      kindLabel: "Approval",
      title: "approval-close-2026-05",
      statusLabel: "Reviewer Assigned",
      statusTone: "review",
      ownerLabel: "Reviewer fund-controller",
      dueLabel: "Submitted May 08, 15:05 UTC",
      evidenceLabel: "1 evidence link",
      routeHref: "/accounting/approvals"
    });
    expect(vm.workflowApprovalHistory).toHaveLength(1);
    expect(vm.workflowApprovalHistory[0]).toMatchObject({
      id: "approval-close-2026-05",
      workflowId,
      expectedWorkflowVersion: 4,
      statusLabel: "Reviewer Assigned",
      statusTone: "review",
      actorLabel: "Reviewer fund-controller / Operator ops-user",
      submittedLabel: "Submitted May 08, 15:05 UTC",
      decidedLabel: "Decision pending",
      rationale: "Pending final ledger validation before close sign-off.",
      reviewer: "fund-controller",
      evidenceLabel: "1 evidence link",
      evidenceHref: "/accounting/approvals",
      evidenceRouteLabel: "Open approval evidence",
      decisionGuardLabel: "Close workflow has unresolved ledger blockers.",
      approveWorkflowReportPackId: null,
      canApproveWorkflow: false,
      approveWorkflowDisabledReason: "Close workflow has unresolved ledger blockers.",
      rejectWorkflowReasonCode: "BrowserApprovalDecisionReview",
      canRejectWorkflow: true,
      rejectWorkflowDisabledReason: null
    });
    expect(vm.financialOperationsQueue.items[6]).toMatchObject({
      kindLabel: "Evidence package",
      title: "Close package manifest",
      statusLabel: "Missing",
      statusTone: "blocked",
      dueLabel: "0/1 categories complete",
      actionLabel: "Publish the close package manifest and retain the evidence hash.",
      routeHref: "/accounting/operations-continuity"
    });
    expect(vm.evidencePackages).toHaveLength(5);
    expect(vm.evidencePackages[0]).toMatchObject({
      id: "accounting-record-2026-05",
      label: "Accounting record evidence",
      statusLabel: "Review Required",
      statusTone: "review",
      readinessLabel: "3/8 categories complete",
      evidenceLabel: "1 evidence link",
      evidenceHref: "/accounting/operations-continuity",
      evidenceRouteLabel: "Open retained evidence",
      routeHref: "/accounting/operations-continuity"
    });
    expect(vm.evidencePackages[2]).toMatchObject({
      id: "close-package:fund-alpha:2026-05",
      label: "Close package manifest",
      statusLabel: "Missing",
      statusTone: "blocked",
      evidenceRouteLabel: "No local evidence route",
      requiredActionsLabel: "Publish the close package manifest and retain the evidence hash."
    });
    expect(vm.evidencePackages[4]).toMatchObject({
      id: "period-lock-reopen:fund-alpha:2026-05",
      label: "Period lock and reopen evidence",
      statusLabel: "Missing",
      statusTone: "blocked",
      readinessLabel: "1/2 categories complete",
      requiredActionsLabel: "Close the workflow and retain the period-lock package before evidence release.",
      routeHref: "/accounting/operations-continuity"
    });
    expect(vm.checklist).toHaveLength(1);
    expect(vm.checklist[0]).toMatchObject({
      id: "close-gate-ledgerposting",
      label: "Ledger posting controller check",
      gateLabel: "Ledger Posting",
      ownerLabel: "fund-controller",
      requiredEvidence: "Validated journal draft, retained ledger hash, and controller approval evidence.",
      approvalLabel: "2 control approvals required",
      evidenceLabel: "ledger-evidence-1",
      remediationHref: "/accounting/ledger",
      remediationLabel: "Open remediation",
      acknowledgementLabel: "Ledger validation is still required.",
      acknowledgementCommandPostureLabel: "Acknowledgement blocked",
      acknowledgementGuardLabel: "Expected workflow version 4",
      canAcknowledge: false,
      acknowledgeLabel: "Acknowledge",
      acknowledgeDisabledReason: "Ledger validation is still required.",
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
    expect(vm.closeGovernance).toMatchObject({
      statusLabel: "Open",
      statusTone: "neutral",
      lockLabel: "Period remains open",
      closeAuditLabel: "No close audit event",
      reopenAuditLabel: "No governed reopen recorded",
      commandGuardLabel: "Close and reopen command decisions remain enforced by the shared workflow API."
    });
  });

  it("marks acknowledgeable checklist tasks with the shared command guard", () => {
    const acknowledgeableDetail: OperationsContinuityWorkflow = {
      ...detail,
      closeChecklist: [
        {
          ...detail.closeChecklist[0]!,
          status: "Ready",
          blockingReason: null,
          canAcknowledge: true,
          acknowledgedAtUtc: null,
          acknowledgedBy: null
        }
      ]
    };

    const vm = buildOperationsContinuityScreenViewModel({
      workflows: [summary],
      selectedWorkflowId: workflowId,
      detail: acknowledgeableDetail,
      loading: false,
      detailLoading: false,
      error: null,
      detailError: null,
      refresh: vi.fn(),
      selectWorkflow: vi.fn()
    });

    expect(vm.checklist[0]).toMatchObject({
      id: "close-gate-ledgerposting",
      acknowledgementLabel: "Ready for acknowledgement",
      acknowledgementCommandPostureLabel: "Acknowledgement command ready",
      acknowledgementGuardLabel: "Expected workflow version 4",
      workflowId,
      expectedWorkflowVersion: 4,
      canAcknowledge: true,
      acknowledgeLabel: "Acknowledge",
      acknowledgeDisabledReason: null,
      acknowledgeAriaLabel: "Acknowledge close checklist task Ledger posting controller check"
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
          escalationLevel: "Level 2",
          escalationReason: "Aged cash variance past controller SLA",
          escalatedAtUtc: "2026-05-08T15:20:00Z",
          slaState: "Warning",
          slaDueAtUtc: "2026-05-09T16:00:00Z",
          materiality: 2,
          rootCauseCode: "BrokerCashTiming",
          approvalState: "ReadyForSignoff",
          blockedOutputs: ["Report package release", "Close sign-off review"],
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
    expect(vm.breakCases).toHaveLength(1);
    expect(vm.breakCases[0]).toMatchObject({
      id: "recon-break-42",
      caseLabel: "recon-break-42",
      categoryLabel: "CashBalance / cash-balance-check",
      statusLabel: "Resolved",
      statusTone: "ready",
      severityLabel: "Warning",
      severityTone: "review",
      ownerLabel: "fund-controller",
      dueLabel: "May 09, 00:00 UTC",
      slaLabel: "SLA Warning due May 09, 16:00 UTC",
      escalationLabel: "Level 2 at May 08, 15:20 UTC: Aged cash variance past controller SLA",
      varianceLabel: "Expected 125,000.25 / Actual 124,998.25 / Variance -2",
      materialityLabel: "Materiality 2",
      sourceLabel: "ledger vs custodian",
      rootCauseLabel: "Root cause Broker Cash Timing",
      symbolLabel: "No security",
      evidenceLabel: "1 retained evidence link",
      evidenceHref: "/accounting/reconciliation/recon-break-42/evidence",
      evidenceRouteLabel: "Open retained evidence",
      approvalLabel: "Approval Ready For Signoff",
      blockedOutputsLabel: "Blocks Report package release, Close sign-off review",
      actionLabel: "Accept custodian statement evidence and close the reconciliation break.",
      caseworkHref: `/accounting/exceptions?workflowId=${workflowId}&breakId=recon-break-42`,
      caseworkRouteLabel: "Open casework",
      commandPostureLabel: "Resolution retained",
      commandGuardLabel: "Expected workflow version 4",
      workflowId,
      expectedWorkflowVersion: 4,
      canAssign: false,
      assignLabel: "Assigned",
      assignDisabledReason: "Break resolution has already been retained.",
      canResolve: false,
      resolveLabel: "Resolved",
      resolveDisabledReason: "Break resolution has already been retained.",
      routeHref: "/accounting/reconciliation/recon-break-42/evidence",
      routeLabel: "Open break evidence"
    });
    expect(vm.reconciliationLanes).toHaveLength(7);
    expect(vm.reconciliationLanes[0]).toMatchObject({
      id: "cash-reconciliation",
      label: "Cash reconciliation",
      statusLabel: "Ready",
      statusTone: "ready",
      breakCountLabel: "No open breaks",
      evidenceLabel: "1 evidence link",
      evidenceHref: "/accounting/reconciliation/cash",
      evidenceRouteLabel: "Open retained evidence",
      routeHref: "/accounting/reconciliation",
      requiredActionsLabel: "No required actions"
    });
    expect(vm.reconciliationLanes.find((lane) => lane.id === "mbs-factor-reconciliation")).toMatchObject({
      statusLabel: "Review Required",
      statusTone: "review",
      breakCountLabel: "1 open break",
      evidenceLabel: "1 evidence link",
      evidenceHref: "/accounting/reconciliation/recon-break-factor-1",
      evidenceRouteLabel: "Open retained evidence",
      requiredActionsLabel: "Resolve or assign MBS factor reconciliation breaks and retain evidence."
    });
  });

  it("enables approval submission command metadata after exceptions and report-pack readiness clear", () => {
    const reportPackEvidence = {
      evidenceId: "report-pack-submit-evidence",
      label: "Report-pack readiness evidence",
      route: "/workstation/reporting/report-packs/report-pack-2026-05/evidence",
      source: "operations-continuity",
      capturedAtUtc: "2026-05-08T16:00:00Z"
    };
    const approvalReadyDetail: OperationsContinuityWorkflow = {
      ...detail,
      breakCases: detail.breakCases.map((breakCase) => ({
        ...breakCase,
        status: "Resolved"
      })),
      approvals: [],
      approvalState: "Pending",
      reportPackReadiness: {
        isReady: true,
        reportPackId: "report-pack-2026-05",
        blockingReason: null,
        evidenceLinks: [reportPackEvidence]
      },
      closePackage: null
    };

    const vm = buildOperationsContinuityScreenViewModel({
      workflows: [summary],
      selectedWorkflowId: workflowId,
      detail: approvalReadyDetail,
      loading: false,
      detailLoading: false,
      error: null,
      detailError: null,
      refresh: vi.fn(),
      selectWorkflow: vi.fn()
    });

    expect(vm.commandSpine.rows[3]).toMatchObject({
      id: "approve-results",
      workflowId,
      expectedWorkflowVersion: 4,
      submitApprovalReportPackId: "report-pack-2026-05",
      submitApprovalReviewer: "fund-controller",
      submitApprovalEvidenceLinks: expect.arrayContaining([reportPackEvidence]),
      submitApprovalChecklistControlApprovals: [],
      canSubmitApproval: true,
      submitApprovalDisabledReason: null
    });
  });

  it("enables workflow approval decision metadata when report-pack and checklist evidence are retained", () => {
    const reportPackEvidence: OperationsEvidenceLink = {
      evidenceId: "report-pack-approval-decision-evidence",
      label: "Report-pack approval decision evidence",
      route: "/workstation/reporting/report-packs/report-pack-2026-05/evidence",
      source: "operations-continuity",
      capturedAtUtc: "2026-05-10T17:25:00Z"
    };
    const approvalGates: OperationsGate[] = [
      {
        ...gates[0],
        status: "Passed",
        completedAtUtc: "2026-05-10T17:00:00Z",
        completedBy: "operations-lead"
      },
      {
        ...gates[1],
        status: "Passed",
        blockers: [],
        completedAtUtc: "2026-05-10T17:10:00Z",
        completedBy: "ledger-lead"
      },
      {
        gateKey: "Approval",
        displayName: "Approval",
        status: "InProgress",
        isRequired: true,
        description: "Reviewer decision is pending.",
        blockers: [],
        nextActions: [],
        completedAtUtc: null,
        completedBy: null
      }
    ];
    const closeChecklist: OperationsCloseChecklistTask[] = [
      {
        taskId: "close-gate-brokeringest",
        gate: "BrokerIngest",
        label: "Broker ingest close gate",
        owner: "operations-lead",
        requiredEvidence: "Broker ingest evidence is retained.",
        dueDate: "2026-05-10",
        requiredApprovalCount: 1,
        expiresOn: "2026-05-14",
        status: "Done",
        blockingReason: null,
        evidencePointer: "broker-ingest-evidence",
        remediationRoute: "/workstation/accounting/operations-continuity",
        canAcknowledge: false,
        acknowledgedAtUtc: "2026-05-10T17:00:00Z",
        acknowledgedBy: "operations-lead"
      },
      {
        taskId: "close-gate-ledgerposting",
        gate: "LedgerPosting",
        label: "Ledger posting close gate",
        owner: "ledger-lead",
        requiredEvidence: "Ledger posting evidence is retained.",
        dueDate: "2026-05-10",
        requiredApprovalCount: 1,
        expiresOn: "2026-05-14",
        status: "Done",
        blockingReason: null,
        evidencePointer: "ledger-posting-evidence",
        remediationRoute: "/workstation/accounting/ledger",
        canAcknowledge: false,
        acknowledgedAtUtc: "2026-05-10T17:10:00Z",
        acknowledgedBy: "ledger-lead"
      },
      {
        taskId: "close-gate-approval",
        gate: "Approval",
        label: "Approval close gate",
        owner: "fund-controller",
        requiredEvidence: "Reviewer approval and retained report-pack evidence.",
        dueDate: "2026-05-10",
        requiredApprovalCount: 2,
        expiresOn: "2026-05-14",
        status: "Pending",
        blockingReason: null,
        evidencePointer: reportPackEvidence.evidenceId,
        remediationRoute: "/workstation/accounting/approvals",
        canAcknowledge: false,
        acknowledgedAtUtc: null,
        acknowledgedBy: null
      }
    ];
    const approvalDecisionReadyDetail: OperationsContinuityWorkflow = {
      ...detail,
      status: "ApprovalPending",
      gates: approvalGates,
      ledgerPostingState: "Complete",
      reconciliationState: "Complete",
      approvalState: "ReviewerAssigned",
      breakCases: [],
      approvals: [
        {
          approvalId: "approval-close-2026-05",
          status: "ReviewerAssigned",
          operator: "ops-user",
          reviewer: "fund-controller",
          rationale: "Pending final close sign-off against retained report-pack evidence.",
          submittedAtUtc: "2026-05-10T17:30:00Z",
          decidedAtUtc: null,
          evidenceLinks: [reportPackEvidence]
        }
      ],
      reportPackReadiness: {
        isReady: true,
        reportPackId: "report-pack-2026-05",
        blockingReason: null,
        evidenceLinks: [reportPackEvidence]
      },
      closeChecklist,
      closePackage: null
    };

    const vm = buildOperationsContinuityScreenViewModel({
      workflows: [summary],
      selectedWorkflowId: workflowId,
      detail: approvalDecisionReadyDetail,
      loading: false,
      detailLoading: false,
      error: null,
      detailError: null,
      refresh: vi.fn(),
      selectWorkflow: vi.fn()
    });

    expect(vm.workflowApprovalHistory[0]).toMatchObject({
      id: "approval-close-2026-05",
      workflowId,
      expectedWorkflowVersion: 4,
      reviewer: "fund-controller",
      decisionGuardLabel: "Approval decision guard clear",
      approveWorkflowReportPackId: "report-pack-2026-05",
      approveWorkflowEvidenceLinks: expect.arrayContaining([reportPackEvidence]),
      approveWorkflowChecklistControlApprovals: expect.arrayContaining([
        {
          taskId: "close-gate-brokeringest",
          approvedBy: "operations-lead",
          approvedAtUtc: "2026-05-10T17:00:00Z"
        },
        {
          taskId: "close-gate-approval",
          approvedBy: "fund-controller",
          approvedAtUtc: "2026-05-10T17:30:00Z"
        }
      ]),
      canApproveWorkflow: true,
      approveWorkflowDisabledReason: null,
      rejectWorkflowEvidenceLinks: expect.arrayContaining([reportPackEvidence]),
      rejectWorkflowReasonCode: "BrowserApprovalDecisionReview",
      canRejectWorkflow: true,
      rejectWorkflowDisabledReason: null
    });
  });

  it("enables close package publication when shared close readiness and checklist-control approvals are retained", () => {
    const reportPackEvidence: OperationsEvidenceLink = {
      evidenceId: "report-pack-close-evidence",
      label: "Report-pack retained manifest",
      route: "/workstation/reporting/report-packs/report-pack-2026-05/evidence",
      source: "operations-continuity",
      capturedAtUtc: "2026-05-10T18:00:00Z"
    };
    const closeChecklist: OperationsCloseChecklistTask[] = [
      {
        ...detail.closeChecklist[0],
        status: "Done",
        blockingReason: null,
        acknowledgedAtUtc: "2026-05-10T17:10:00Z",
        acknowledgedBy: "ledger-lead",
        requiredApprovalCount: 1
      },
      {
        taskId: "close-gate-approval",
        gate: "Approval",
        label: "Approval close gate",
        owner: "fund-controller",
        requiredEvidence: "Approved workflow and retained report-pack evidence.",
        dueDate: "2026-05-10",
        requiredApprovalCount: 2,
        expiresOn: "2026-05-14",
        status: "Done",
        blockingReason: null,
        evidencePointer: reportPackEvidence.evidenceId,
        remediationRoute: "/workstation/accounting/approvals",
        canAcknowledge: false,
        acknowledgedAtUtc: null,
        acknowledgedBy: null
      }
    ];
    const closeReadyDetail: OperationsContinuityWorkflow = {
      ...detail,
      status: "ReadyForClose",
      approvalState: "Approved",
      approvals: [
        {
          approvalId: "approval-close-2026-05",
          status: "Approved",
          operator: "ops-user",
          reviewer: "fund-controller",
          rationale: "Approved close package publication from retained report-pack evidence.",
          submittedAtUtc: "2026-05-10T17:30:00Z",
          decidedAtUtc: "2026-05-10T17:45:00Z",
          evidenceLinks: [reportPackEvidence]
        }
      ],
      reportPackReadiness: {
        isReady: true,
        reportPackId: "report-pack-2026-05",
        blockingReason: null,
        evidenceLinks: [reportPackEvidence]
      },
      closeChecklist,
      closeReadiness: {
        isReadyToClose: true,
        severity: "Info",
        score: 100,
        components: [],
        blockers: [],
        nextActions: []
      },
      closePackage: null
    };

    const vm = buildOperationsContinuityScreenViewModel({
      workflows: [summary],
      selectedWorkflowId: workflowId,
      detail: closeReadyDetail,
      loading: false,
      detailLoading: false,
      error: null,
      detailError: null,
      refresh: vi.fn(),
      selectWorkflow: vi.fn()
    });

    expect(vm.commandSpine.rows[4]).toMatchObject({
      id: "produce-evidence",
      workflowId,
      expectedWorkflowVersion: 4,
      closeWorkflowReportPackId: "report-pack-2026-05",
      closeWorkflowEvidenceLinks: expect.arrayContaining([reportPackEvidence]),
      closeWorkflowChecklistControlApprovals: expect.arrayContaining([
        {
          taskId: "close-gate-ledgerposting",
          approvedBy: "ledger-lead",
          approvedAtUtc: "2026-05-10T17:10:00Z"
        },
        {
          taskId: "close-gate-approval",
          approvedBy: "fund-controller",
          approvedAtUtc: "2026-05-10T17:45:00Z"
        }
      ]),
      canCloseWorkflow: true,
      closeWorkflowLabel: "Publish close package",
      closeWorkflowDisabledReason: null,
      closeWorkflowAriaLabel: "Publish close package for 2026-05"
    });
  });

  it("fails closed when reconciliation lane evidence has no local route", () => {
    const workflowWithExternalLaneEvidence: OperationsContinuityWorkflow = {
      ...detail,
      reconciliationLanes: [
        {
          laneId: "bank-reconciliation",
          label: "Bank reconciliation",
          status: "ReviewRequired",
          isReady: false,
          breakCount: 1,
          summary: "Bank reconciliation is waiting for retained bank statement evidence.",
          routeHint: "/workstation/accounting/reconciliation",
          evidenceLinks: [
            {
              evidenceId: "external-bank-evidence-1",
              label: "External bank statement evidence",
              route: "https://evidence.example/bank-reconciliation",
              source: "external-vault",
              capturedAtUtc: "2026-05-08T15:36:00Z"
            }
          ],
          requiredActions: ["Retain the bank statement evidence inside the Meridian evidence vault."]
        }
      ]
    };

    const vm = buildOperationsContinuityScreenViewModel({
      workflows: [summary],
      selectedWorkflowId: workflowId,
      detail: workflowWithExternalLaneEvidence,
      loading: false,
      detailLoading: false,
      error: null,
      detailError: null,
      refresh: vi.fn(),
      selectWorkflow: vi.fn()
    });

    expect(vm.reconciliationLanes[0]).toMatchObject({
      id: "bank-reconciliation",
      evidenceLabel: "1 evidence link",
      evidenceHref: null,
      evidenceRouteLabel: "No local evidence route",
      routeHref: "/accounting/reconciliation",
      routeLabel: "Open lane"
    });
  });

  it("fails closed when workflow approval evidence has no local route", () => {
    const workflowWithExternalApprovalEvidence: OperationsContinuityWorkflow = {
      ...detail,
      approvals: [
        {
          ...detail.approvals[0]!,
          evidenceLinks: [
            {
              evidenceId: "external-approval-evidence-1",
              label: "External approval evidence",
              route: "https://evidence.example/approval-close-2026-05",
              source: "external-vault",
              capturedAtUtc: "2026-05-08T15:06:00Z"
            }
          ]
        }
      ]
    };

    const vm = buildOperationsContinuityScreenViewModel({
      workflows: [summary],
      selectedWorkflowId: workflowId,
      detail: workflowWithExternalApprovalEvidence,
      loading: false,
      detailLoading: false,
      error: null,
      detailError: null,
      refresh: vi.fn(),
      selectWorkflow: vi.fn()
    });

    expect(vm.workflowApprovalHistory[0]).toMatchObject({
      id: "approval-close-2026-05",
      evidenceLabel: "1 evidence link",
      evidenceHref: null,
      evidenceRouteLabel: "No local evidence route"
    });
    expect(vm.financialOperationsQueue.items.find((item) => item.id === "approval:approval-close-2026-05")).toMatchObject({
      routeHref: null,
      routeLabel: "No local route"
    });
  });

  it("fails closed when a break-case retained evidence link has no local route", () => {
    const workflowWithExternalBreakEvidence: OperationsContinuityWorkflow = {
      ...detail,
      breakCases: [
        {
          breakId: "recon-break-external",
          checkId: "income-accrual-check",
          category: "IncomeAccrual",
          severity: "Warning",
          status: "Open",
          owner: null,
          dueDate: null,
          expectedSource: "ledger",
          actualSource: "agent-bank",
          expectedAmount: 2400,
          actualAmount: 0,
          variance: -2400,
          securityId: null,
          symbol: "BOND1",
          suggestedAction: "Assign income accrual break and retain local evidence before approval.",
          evidenceLinks: [
            {
              evidenceId: "external-break-evidence-1",
              label: "External income accrual support",
              route: "https://evidence.example/recon-break-external",
              source: "external-vault",
              capturedAtUtc: "2026-05-08T15:34:00Z"
            }
          ],
          escalationLevel: null,
          escalationReason: null,
          escalatedAtUtc: null,
          slaState: null,
          slaDueAtUtc: null,
          materiality: null,
          rootCauseCode: null,
          approvalState: null,
          blockedOutputs: null
        }
      ]
    };

    const vm = buildOperationsContinuityScreenViewModel({
      workflows: [summary],
      selectedWorkflowId: workflowId,
      detail: workflowWithExternalBreakEvidence,
      loading: false,
      detailLoading: false,
      error: null,
      detailError: null,
      refresh: vi.fn(),
      selectWorkflow: vi.fn()
    });

    expect(vm.breakCases[0]).toMatchObject({
      id: "recon-break-external",
      evidenceLabel: "1 retained evidence link",
      evidenceHref: null,
      evidenceRouteLabel: "No local evidence route",
      caseworkHref: `/accounting/exceptions?workflowId=${workflowId}&breakId=recon-break-external`,
      caseworkRouteLabel: "Open casework",
      commandPostureLabel: "Assignment and escalation ready",
      commandGuardLabel: "Expected workflow version 4",
      workflowId,
      expectedWorkflowVersion: 4,
      assignmentOwner: "browser-operator",
      canAssign: true,
      assignLabel: "Assign to me",
      assignDisabledReason: null,
      canResolve: false,
      resolveLabel: "Resolve break",
      resolveDisabledReason: "Assign the break before resolving it.",
      routeHref: null,
      routeLabel: "No local route"
    });
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
    expect(vm.commandSpine.emptyText).toBe("Loading Financial Operations command spine...");
    expect(vm.financialOperationsQueue.emptyText).toBe("Loading Financial Operations operator queue...");
    expect(vm.timelineEmptyText).toBe("Loading workflow timeline.");
  });

  it("projects the private-capital close cockpit from the shared operations aggregate", () => {
    const vm = buildOperationsContinuityScreenViewModel({
      workflows: [summary],
      selectedWorkflowId: workflowId,
      detail,
      loading: false,
      detailLoading: false,
      closeCockpit,
      closeCockpitLoading: false,
      closeCockpitError: null,
      error: null,
      detailError: null,
      refresh: vi.fn(),
      selectWorkflow: vi.fn()
    });

    expect(vm.closeCockpit).toMatchObject({
      statusLabel: "Review Required",
      statusTone: "blocked",
      summaryLabel: "2/5 lanes ready across 1 close workflow.",
      scopeLabel: `Fund profile fund-alpha / Ledger book ledger-main / Fund account ${fundAccountId} / Period 2026-05 / Entity entity-master`,
      readinessLabel: "72% readiness",
      laneCountLabel: "2 ready / 1 blocked lanes",
      proofLaneSummaryLabel: "2/5 proof lanes ready; review NAV support, evidence package, period lock",
      blockerCountLabel: "1 close blocker",
      fundEventCountLabel: "3 fund events",
      reportOutputCountLabel: "1/2 report outputs delivered",
      nextActionLabel: "Resolve Ledger Posting blockers",
      nextActionHref: "/accounting/ledger",
      nextActionDisabled: false,
      liveCapabilitiesLabel: "workflow-readiness, capital-account-evidence",
      plannedCapabilitiesLabel: "tax-support-drilldown"
    });
    expect(vm.closeCockpit.lanes).toHaveLength(5);
    expect(vm.closeCockpit.lanes[0]).toMatchObject({
      id: "partner-capital-tie-outs",
      statusLabel: "Ready",
      statusTone: "ready",
      evidenceLabel: "2 evidence links",
      routeHref: "/accounting/private-capital/capital-account-subledger",
      requiredActionsLabel: "No required actions"
    });
    expect(vm.closeCockpit.lanes[1]).toMatchObject({
      id: "expense-fee-allocation",
      statusLabel: "Ready",
      statusTone: "ready",
      evidenceLabel: "2 evidence links",
      routeHref: "/accounting/private-capital/fund-events/management-fee",
      requiredActionsLabel: "No required actions"
    });
    expect(vm.closeCockpit.lanes[3]).toMatchObject({
      id: "close-package",
      statusLabel: "Blocked",
      statusTone: "blocked",
      requiredActionsLabel: "Publish the close package manifest"
    });
    expect(vm.closeCockpit.workflows[0]).toMatchObject({
      id: workflowId,
      periodLabel: `${summary.periodId} / ${fundAccountId}`,
      readinessLabel: "72% ready",
      readinessTone: "blocked",
      blockersLabel: "1 blocker",
      checklistLabel: "2 open checklist items",
      workflowHref: "/accounting/operations-continuity"
    });
    expect(vm.closeCockpit.evidencePackages).toHaveLength(2);
    expect(vm.closeCockpit.evidencePackages[0]).toMatchObject({
      id: "private-capital:fund-event-accounting",
      statusLabel: "Ready",
      statusTone: "ready",
      readinessLabel: "4/4 categories complete",
      evidenceLabel: "3 evidence links",
      evidenceHref: "/accounting/private-capital/fund-events",
      evidenceRouteLabel: "Open retained evidence",
      routeHref: "/accounting/private-capital/fund-events",
      requiredActionsLabel: "No required actions"
    });
    expect(vm.closeCockpit.evidencePackages[1]).toMatchObject({
      id: "private-capital:nav-support",
      statusLabel: "Review Required",
      statusTone: "review",
      readinessLabel: "1/3 categories complete",
      evidenceLabel: "1 evidence link",
      evidenceHref: "/portfolio/nav/support-package",
      evidenceRouteLabel: "Open retained evidence",
      routeHref: "/portfolio/nav",
      requiredActionsLabel: "Retain complete NAV support package evidence before close sign-off."
    });
    expect(vm.closeCockpit.approvalHistory).toHaveLength(1);
    expect(vm.closeCockpit.approvalHistory[0]).toMatchObject({
      id: "approval-close-2026-05",
      workflowLabel: `${summary.periodId} / ${fundAccountId}`,
      statusLabel: "Reviewer Assigned",
      statusTone: "review",
      actorLabel: "Reviewer fund-controller / Operator ops-user",
      decidedLabel: "May 08, 15:05 UTC",
      evidenceLabel: "1 evidence link",
      evidenceHref: "/accounting/approvals",
      evidenceRouteLabel: "Open evidence",
      workflowHref: "/accounting/operations-continuity"
    });
    expect(vm.closeCockpit.navSupportPackages).toHaveLength(1);
    expect(vm.closeCockpit.navSupportPackages[0]).toMatchObject({
      id: "nav-support:fund-alpha:2026-05",
      label: "NAV support package",
      statusLabel: "Review Required",
      statusTone: "review",
      componentSummaryLabel: "2/4 components ready; review Pricing, Shadow NAV",
      shadowNavLabel: "$1,250,000.00 shadow NAV",
      evidenceLabel: "1 evidence link",
      requiredActionsLabel: "Retain NAV support package for positions, cash, pricing, and shadow NAV evidence.",
      routeHref: "/portfolio/nav"
    });
  });

  it("projects the source-backed close calendar for workflow control", () => {
    const vm = buildOperationsContinuityScreenViewModel({
      workflows: [summary],
      selectedWorkflowId: workflowId,
      detail,
      loading: false,
      detailLoading: false,
      closeCalendar,
      closeCalendarLoading: false,
      closeCalendarError: null,
      error: null,
      detailError: null,
      refresh: vi.fn(),
      selectWorkflow: vi.fn()
    });

    expect(vm.closeCalendar).toMatchObject({
      title: "Close calendar",
      statusLabel: "Blocked",
      statusTone: "blocked",
      summaryLabel: "0/1 calendar item ready; 2 open checklist items.",
      scopeLabel: `Selected 2026-05 / ${fundAccountId}`,
      freshnessLabel: "Generated May 10, 18:45 UTC",
      itemCountLabel: "1 calendar item",
      blockerCountLabel: "1 blocker",
      checklistCountLabel: "2 open checklist items",
      approvalCountLabel: "1/2 approvals complete",
      nextDueLabel: "Next due May 12, 2026: Ledger posting controller check"
    });
    expect(vm.closeCalendar.rows[0]).toMatchObject({
      id: workflowId,
      periodLabel: `2026-05 / ${fundAccountId}`,
      statusLabel: "Ledger Posting Draft",
      statusTone: "review",
      dueLabel: "May 12, 2026",
      taskLabel: "Ledger posting controller check",
      ownerLabel: "fund-controller",
      readinessLabel: "72% ready",
      readinessTone: "blocked",
      blockerLabel: "1 blocker",
      checklistLabel: "2 open checklist items",
      approvalLabel: "1/2 approvals complete",
      routeHref: "/accounting/operations-continuity"
    });
    expect(vm.financialOperationsQueue.items.find((item) => item.id === `close-calendar:${workflowId}`)).toMatchObject({
      kindLabel: "Close calendar",
      title: `2026-05 / ${fundAccountId}: Ledger posting controller check`,
      detail: "1 blocker; 2 open checklist items; 1/2 approvals complete",
      statusLabel: "72% ready",
      statusTone: "blocked",
      ownerLabel: "fund-controller",
      dueLabel: "May 12, 2026",
      evidenceLabel: "Calendar status: Ledger Posting Draft",
      actionLabel: "Resolve close-calendar blockers before approval or close lock.",
      routeHref: "/accounting/operations-continuity"
    });
  });

  it("fails closed in the operator queue when the close calendar cannot be loaded", () => {
    const vm = buildOperationsContinuityScreenViewModel({
      workflows: [summary],
      selectedWorkflowId: workflowId,
      detail,
      loading: false,
      detailLoading: false,
      closeCalendar: null,
      closeCalendarLoading: false,
      closeCalendarError: "Close calendar projection could not be loaded.",
      error: null,
      detailError: null,
      refresh: vi.fn(),
      selectWorkflow: vi.fn()
    });

    expect(vm.closeCalendar).toMatchObject({
      statusLabel: "Unavailable",
      statusTone: "blocked",
      errorText: "Close calendar projection could not be loaded."
    });
    expect(vm.financialOperationsQueue.items.find((item) => item.id === "close-calendar:error")).toMatchObject({
      kindLabel: "Close calendar",
      title: `2026-05 / ${fundAccountId}: Close calendar unavailable`,
      statusLabel: "Unavailable",
      statusTone: "blocked",
      ownerLabel: "Operations control",
      dueLabel: "Next due task unavailable",
      evidenceLabel: "Close calendar evidence unavailable",
      actionLabel: "Close calendar projection could not be loaded.",
      routeHref: "/accounting/operations-continuity"
    });
  });

  it("fails closed when the private-capital close cockpit cannot be loaded", () => {
    const vm = buildOperationsContinuityScreenViewModel({
      workflows: [summary],
      selectedWorkflowId: workflowId,
      detail,
      loading: false,
      detailLoading: false,
      closeCockpit: null,
      closeCockpitLoading: false,
      closeCockpitError: "Private-capital close cockpit could not be loaded.",
      error: null,
      detailError: null,
      refresh: vi.fn(),
      selectWorkflow: vi.fn()
    });

    expect(vm.closeCockpit).toMatchObject({
      statusLabel: "Unavailable",
      statusTone: "blocked",
      scopeLabel: `Selected ${summary.periodId} / ${fundAccountId}`,
      readinessLabel: "Readiness unavailable",
      nextActionDisabled: true,
      lanes: [],
      workflows: []
    });
    expect(vm.closeCockpit.errorText).toBe("Private-capital close cockpit could not be loaded.");
    expect(vm.closeCockpit.lanesEmptyText).toBe("Private-capital cockpit lanes could not be loaded.");
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
          requiredEvidence: "Published report pack manifest and retained evidence hash.",
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
    expect(vm.checklist[0]).toMatchObject({
      id: "close-gate-ledgerposting",
      acknowledgementLabel: "Acknowledged by fund-controller at May 09, 15:30 UTC",
      acknowledgementCommandPostureLabel: "Acknowledgement retained",
      acknowledgementGuardLabel: "Expected workflow version 4",
      canAcknowledge: false,
      acknowledgeLabel: "Acknowledged",
      acknowledgeDisabledReason: "Checklist acknowledgement has already been retained."
    });
    expect(vm.checklist[1]).toMatchObject({
      id: "close-gate-reportpack",
      acknowledgementCommandPostureLabel: "Acknowledgement retained",
      acknowledgementGuardLabel: "Expected workflow version 4"
    });
  });

  it("projects governed close-package publication from shared workflow metadata", () => {
    const publishedDetail: OperationsContinuityWorkflow = {
      ...detail,
      status: "Closed",
      timeline: [
        ...detail.timeline,
        {
          auditId: "f4d29af8-90dd-46c1-9d7a-6f7160d103bb",
          occurredAtUtc: "2026-05-10T18:45:00Z",
          workflowId,
          fundAccountId,
          periodId: "2026-05",
          eventType: "workflow-closed",
          fromState: "ReadyForClose",
          toState: "Closed",
          gate: "Approval",
          fromGateStatus: "Passed",
          toGateStatus: "Passed",
          actor: "fund-controller",
          rationale: "Controller sign-off after report pack and checklist evidence were retained.",
          correlationId: "close-command-2026-05",
          references: [],
          previousHash: "casehash-decision-accepted-202605",
          currentHash: "closehash-package-202605"
        }
      ],
      closePackage: {
        closePackageId: "close-package-2026-05",
        reportPackId: "report-pack-may-2026",
        retainedManifestId: "close-package-2026-05-manifest",
        retainedManifestRoute: "/workstation/accounting/operations-continuity/79f1f386-0bb1-4aef-9a85-fb9d6de8e1f6/close-package/close-package-2026-05-manifest",
        evidenceHash: "b5f6c7d8e9a00112233445566778899aabbccddeeff00112233445566778899",
        publishedAtUtc: "2026-05-10T18:45:00Z",
        publishedBy: "fund-controller",
        signOffRationale: "Controller sign-off after report pack and checklist evidence were retained.",
        evidenceLinks: [
          {
            evidenceId: "close-package-evidence-1",
            label: "Close package retained manifest",
            route: "/workstation/reporting/report-packs/report-pack-may-2026",
            source: "operations-continuity",
            capturedAtUtc: "2026-05-10T18:44:00Z"
          }
        ],
        checklistControlApprovals: [
          {
            taskId: "close-gate-ledgerposting",
            approvedBy: "fund-controller",
            approvedAtUtc: "2026-05-10T18:40:00Z"
          },
          {
            taskId: "close-gate-reportpack",
            approvedBy: "fund-admin",
            approvedAtUtc: "2026-05-10T18:42:00Z"
          }
        ]
      }
    };

    const vm = buildOperationsContinuityScreenViewModel({
      workflows: [summary],
      selectedWorkflowId: workflowId,
      detail: publishedDetail,
      loading: false,
      detailLoading: false,
      error: null,
      detailError: null,
      refresh: vi.fn(),
      selectWorkflow: vi.fn()
    });

    expect(vm.selectedDetail?.metadata).toContainEqual({
      label: "Close package",
      value: "close-package-2026-05 signed by fund-controller at May 10, 18:45 UTC"
    });
    expect(vm.closePackage).toMatchObject({
      statusLabel: "Published",
      statusTone: "ready",
      packageIdLabel: "close-package-2026-05",
      reportPackLabel: "Report pack report-pack-may-2026",
      manifestLabel: "close-package-2026-05-manifest",
      manifestHref: "/accounting/operations-continuity/79f1f386-0bb1-4aef-9a85-fb9d6de8e1f6/close-package/close-package-2026-05-manifest",
      evidenceHashLabel: "b5f6c7d8e9a00112233445566778899aabbccddeeff00112233445566778899",
      publishedLabel: "Published May 10, 18:45 UTC",
      signerLabel: "Signed by fund-controller",
      rationaleLabel: "Controller sign-off after report pack and checklist evidence were retained.",
      evidenceLabel: "1 retained evidence link",
      approvalLabel: "2 checklist control approvals"
    });
    expect(vm.closeGovernance).toMatchObject({
      statusLabel: "Locked",
      statusTone: "ready",
      lockLabel: "Closed period is locked",
      closeAuditLabel: "Workflow Closed f4d29af8 / closehash-pa",
      closePackageLabel: "close-package-2026-05 / b5f6c7d8e9a0",
      reopenAuditLabel: "No governed reopen recorded",
      commandGuardLabel: "Mutations must use the governed reopen command with incident metadata, justification, approval reference, and impact summary.",
      workflowId,
      expectedWorkflowVersion: 4,
      canReopenWorkflow: true,
      reopenWorkflowDisabledReason: null,
      reopenWorkflowAriaLabel: "Reopen governed period for 2026-05"
    });
    expect(vm.closeGovernance.reopenWorkflowEvidenceLinks).toEqual(expect.arrayContaining([
      expect.objectContaining({ evidenceId: "close-package-evidence-1" })
    ]));
  });

  it("projects governed reopen evidence from the workflow timeline", () => {
    const reopenedDetail: OperationsContinuityWorkflow = {
      ...detail,
      status: "ReconciliationActive",
      timeline: [
        ...detail.timeline,
        {
          auditId: "f4d29af8-90dd-46c1-9d7a-6f7160d103bb",
          occurredAtUtc: "2026-05-10T18:45:00Z",
          workflowId,
          fundAccountId,
          periodId: "2026-05",
          eventType: "workflow-closed",
          fromState: "ReadyForClose",
          toState: "Closed",
          gate: "Approval",
          fromGateStatus: "Passed",
          toGateStatus: "Passed",
          actor: "fund-controller",
          rationale: "Controller sign-off after report pack and checklist evidence were retained.",
          correlationId: "close-command-2026-05",
          references: [],
          previousHash: "casehash-decision-accepted-202605",
          currentHash: "closehash-package-202605"
        },
        {
          auditId: "8da22949-41f5-43bf-b744-4f7e1f53d235",
          occurredAtUtc: "2026-05-11T13:15:00Z",
          workflowId,
          fundAccountId,
          periodId: "2026-05",
          eventType: "workflow-reopened",
          fromState: "Closed",
          toState: "ReconciliationActive",
          gate: "Reconciliation",
          fromGateStatus: "Passed",
          toGateStatus: "ReviewRequired",
          actor: "ops-admin",
          rationale: "Reopened after custodian correction incident required ledger restatement.",
          correlationId: "incident-2026-05-close-restatement",
          references: [
            {
              evidenceId: "reopen-incident-evidence-1",
              label: "Governed reopen approval",
              route: "/workstation/accounting/operations-continuity/incidents/incident-2026-05-close-restatement",
              source: "operations-continuity",
              capturedAtUtc: "2026-05-11T13:14:00Z"
            }
          ],
          previousHash: "closehash-package-202605",
          currentHash: "reopenhash-incident-202605"
        }
      ]
    };

    const vm = buildOperationsContinuityScreenViewModel({
      workflows: [summary],
      selectedWorkflowId: workflowId,
      detail: reopenedDetail,
      loading: false,
      detailLoading: false,
      error: null,
      detailError: null,
      refresh: vi.fn(),
      selectWorkflow: vi.fn()
    });

    expect(vm.selectedDetail?.metadata).toContainEqual({
      label: "Period lock",
      value: "Reopened by ops-admin at May 11, 13:15 UTC"
    });
    expect(vm.closeGovernance).toMatchObject({
      statusLabel: "Reopened",
      statusTone: "review",
      lockLabel: "Period reopened under governance",
      closeAuditLabel: "Workflow Closed f4d29af8 / closehash-pa",
      reopenAuditLabel: "Workflow Reopened by ops-admin at May 11, 13:15 UTC / reopenhash-i",
      reopenIncidentLabel: "Incident incident-2026-05-close-restatement",
      reopenEvidenceLabel: "1 reopen evidence link",
      reopenRationaleLabel: "Reopened after custodian correction incident required ledger restatement.",
      commandGuardLabel: "Close can proceed only after refreshed gate evidence and the reopen incident trail remain linked."
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
      getWorkflow: vi.fn().mockResolvedValue(detail),
      getCloseCalendar: vi.fn().mockResolvedValue(closeCalendar),
      getCloseCockpit: vi.fn().mockResolvedValue(closeCockpit)
    };

    const { result } = renderHook(() => useOperationsContinuityScreenViewModel(services));

    await waitFor(() => {
      expect(result.current.loading).toBe(false);
      expect(result.current.selectedWorkflowId).toBe(workflowId);
      expect(result.current.timeline).toHaveLength(1);
    });
    expect(services.getWorkflow).toHaveBeenCalledWith(workflowId, expect.objectContaining({ signal: expect.any(AbortSignal) }));
    expect(services.getCloseCalendar).toHaveBeenCalledWith(
      { fundAccountId, periodId: "2026-05" },
      expect.objectContaining({ signal: expect.any(AbortSignal) })
    );
    expect(services.getCloseCockpit).toHaveBeenCalledWith(
      { fundAccountId, periodId: "2026-05" },
      expect.objectContaining({ signal: expect.any(AbortSignal) })
    );
  });
});
