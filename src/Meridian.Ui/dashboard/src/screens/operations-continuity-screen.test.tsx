import { cleanup, render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import {
  getPrivateCapitalCloseCockpit,
  getOperationsContinuityWorkflow,
  getOperationsContinuityWorkflows
} from "@/lib/api";
import { OperationsContinuityScreen } from "@/screens/operations-continuity-screen";
import { OPERATIONS_CONTINUITY_WORKFLOW_DETAIL_PANEL_ID } from "@/screens/operations-continuity-screen.view-model";
import type {
  OperationsContinuityWorkflow,
  OperationsContinuityWorkflowSummary,
  OperationsGate,
  OperationsReconciliationLaneSummary,
  PrivateCapitalCloseCockpit
} from "@/types";

vi.mock("@/lib/api", () => ({
  getPrivateCapitalCloseCockpit: vi.fn(),
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
  vi.mocked(getPrivateCapitalCloseCockpit).mockResolvedValue(closeCockpit);
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
  breakCases: [
    {
      breakId: "recon-break-42",
      checkId: "cash-balance-check",
      category: "CashBalance",
      severity: "Warning",
      status: "InReview",
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
  ],
  reconciliationLanes,
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
  dashboardSummary: {
    dashboardId: "operations-dashboard:fund-alpha:2026-05",
    stage: "Resolve Exceptions",
    status: "Blocked",
    isReady: false,
    readyMetricCount: 3,
    totalMetricCount: 6,
    summary: "Financial Operations dashboard is in Resolve Exceptions with 3 metrics requiring review.",
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
        evidenceLinks: [],
        requiredActions: ["Complete source-backed reconciliation lanes before approval."]
      },
      {
        metricId: "resolve-exceptions",
        label: "Resolve Exceptions",
        value: "1 open",
        status: "Blocked",
        detail: "1 reconciliation break requires assignment, escalation, or resolution evidence.",
        routeHint: "/workstation/accounting/reconciliation",
        evidenceLinks: [
          {
            evidenceId: "recon-break-close-evidence-1",
            label: "Custodian statement case close evidence",
            route: "/workstation/accounting/reconciliation/recon-break-42/evidence",
            source: "operations-continuity",
            capturedAtUtc: "2026-05-08T15:34:00Z"
          }
        ],
        requiredActions: ["Assign, escalate, or resolve open exceptions and retain resolution evidence."]
      },
      {
        metricId: "approve-results",
        label: "Approve Results",
        value: "Pending",
        status: "ReviewRequired",
        detail: "Approval history is not complete for this workflow.",
        routeHint: "/workstation/accounting/approvals",
        evidenceLinks: [],
        requiredActions: ["Complete workflow approval and checklist-control approvals."]
      },
      {
        metricId: "produce-evidence",
        label: "Produce Evidence",
        value: "Close package retained",
        status: "Ready",
        detail: "Close package close-package-2026-05 retained manifest close-package-2026-05-manifest.",
        routeHint: "/workstation/reporting/report-packs",
        evidenceLinks: [],
        requiredActions: []
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
        evidenceId: "recon-break-close-evidence-1",
        label: "Custodian statement case close evidence",
        route: "/workstation/accounting/reconciliation/recon-break-42/evidence",
        source: "operations-continuity",
        capturedAtUtc: "2026-05-08T15:34:00Z"
      }
    ],
    requiredActions: [
      "Assign, escalate, or resolve open exceptions and retain resolution evidence.",
      "Complete workflow approval and checklist-control approvals."
    ]
  },
  evidencePackages: [
    {
      packageId: "accounting-record-2026-05",
      label: "Accounting record evidence",
      status: "ReviewRequired",
      isReady: false,
      summary: "Accounting record has 4 of 6 required evidence categories complete.",
      routeHint: "/workstation/accounting/operations-continuity",
      completeCategoryCount: 4,
      requiredCategoryCount: 6,
      evidenceLinkCount: 1,
      evidenceLinks: [
        {
          evidenceId: "recon-break-close-evidence-1",
          label: "Custodian statement case close evidence",
          route: "/workstation/accounting/reconciliation/recon-break-42/evidence",
          source: "operations-continuity",
          capturedAtUtc: "2026-05-08T15:34:00Z"
        }
      ],
      requiredActions: ["Complete all accounting-record evidence categories before publishing the evidence package."]
    },
    {
      packageId: "report-pack-may-2026",
      label: "Report pack evidence",
      status: "Ready",
      isReady: true,
      summary: "Report pack report-pack-may-2026 is linked for retained close evidence.",
      routeHint: "/workstation/reporting/report-packs",
      completeCategoryCount: 1,
      requiredCategoryCount: 1,
      evidenceLinkCount: 0,
      evidenceLinks: [],
      requiredActions: []
    },
    {
      packageId: "close-package-2026-05",
      label: "Close package manifest",
      status: "Ready",
      isReady: true,
      summary: "Close package close-package-2026-05 retained manifest close-package-2026-05-manifest and evidence hash.",
      routeHint: "/workstation/accounting/operations-continuity/79f1f386-0bb1-4aef-9a85-fb9d6de8e1f6/close-package/close-package-2026-05-manifest",
      completeCategoryCount: 1,
      requiredCategoryCount: 1,
      evidenceLinkCount: 0,
      evidenceLinks: [],
      requiredActions: []
    },
    {
      packageId: "audit-support:fund-alpha:2026-05",
      label: "Audit support package",
      status: "ReviewRequired",
      isReady: false,
      summary: "2 audit evidence categories are missing.",
      routeHint: "/workstation/reporting/evidence",
      completeCategoryCount: 4,
      requiredCategoryCount: 6,
      evidenceLinkCount: 1,
      evidenceLinks: [
        {
          evidenceId: "recon-break-close-evidence-1",
          label: "Custodian statement case close evidence",
          route: "/workstation/accounting/reconciliation/recon-break-42/evidence",
          source: "operations-continuity",
          capturedAtUtc: "2026-05-08T15:34:00Z"
        }
      ],
      requiredActions: ["Complete missing audit evidence categories before releasing the package."]
    }
  ],
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
      evidenceLinks: [],
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
    await waitFor(() => {
      expect(screen.getAllByText("Ledger posting requires a balanced and validated journal draft.")).toHaveLength(2);
    });
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
    const breakCases = screen.getByRole("table", { name: "Operations continuity break assignment and escalation" });
    expect(within(breakCases).getByText("recon-break-42")).toBeInTheDocument();
    expect(within(breakCases).getByText("fund-controller")).toBeInTheDocument();
    expect(within(breakCases).getByText("Level 2 at May 08, 15:20 UTC: Aged cash variance past controller SLA")).toBeInTheDocument();
    expect(within(breakCases).getByText("Expected 125,000.25 / Actual 124,998.25 / Variance -2")).toBeInTheDocument();
    expect(within(breakCases).getByText("1 retained evidence link")).toBeInTheDocument();
    expect(within(breakCases).getByText("SLA Warning due May 09, 16:00 UTC")).toBeInTheDocument();
    expect(within(breakCases).getByText("Materiality 2")).toBeInTheDocument();
    expect(within(breakCases).getByText("Root cause Broker Cash Timing")).toBeInTheDocument();
    expect(within(breakCases).getByText("Approval Ready For Signoff")).toBeInTheDocument();
    expect(within(breakCases).getByText("Blocks Report package release, Close sign-off review")).toBeInTheDocument();
    const reconciliationLaneCoverage = screen.getByRole("table", { name: "Operations continuity reconciliation lane coverage" });
    expect(within(reconciliationLaneCoverage).getByText("Cash reconciliation")).toBeInTheDocument();
    expect(within(reconciliationLaneCoverage).getByText("MBS factor reconciliation")).toBeInTheDocument();
    expect(within(reconciliationLaneCoverage).getByText("Bank reconciliation")).toBeInTheDocument();
    expect(within(reconciliationLaneCoverage).getByText("GL reconciliation support")).toBeInTheDocument();
    expect(within(reconciliationLaneCoverage).getByText("Resolve or assign MBS factor reconciliation breaks and retain evidence.")).toBeInTheDocument();
    expect(within(reconciliationLaneCoverage).getAllByText("1 evidence link")).toHaveLength(2);
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
    const closeGovernance = screen.getByLabelText("Period close lock and reopen evidence");
    expect(within(closeGovernance).getByText("Period remains open")).toBeInTheDocument();
    expect(within(closeGovernance).getByText("No close audit event")).toBeInTheDocument();
    expect(within(closeGovernance).getByText("No governed reopen recorded")).toBeInTheDocument();
    const closePackage = screen.getByLabelText("Close package publication summary");
    expect(within(closePackage).getByText("close-package-2026-05")).toBeInTheDocument();
    expect(within(closePackage).getByRole("link", { name: "Open retained close package manifest" }))
      .toHaveAttribute("href", "/accounting/operations-continuity/79f1f386-0bb1-4aef-9a85-fb9d6de8e1f6/close-package/close-package-2026-05-manifest");
    expect(within(closePackage).getByText("Signed by fund-controller")).toBeInTheDocument();
    expect(within(closePackage).getByText("1 checklist control approval")).toBeInTheDocument();

    expect(await screen.findByRole("heading", { name: "Private-capital close cockpit" })).toBeInTheDocument();
    expect(getPrivateCapitalCloseCockpit).toHaveBeenCalledWith(
      { fundAccountId, periodId: "2026-05" },
      expect.objectContaining({ signal: expect.any(AbortSignal) })
    );
    const cockpitSummary = screen.getByRole("list", { name: "Private-capital close cockpit summary" });
    expect(within(cockpitSummary).getByText("72% readiness")).toBeInTheDocument();
    expect(within(cockpitSummary).getByText("2 ready / 1 blocked lanes")).toBeInTheDocument();
    expect(within(cockpitSummary).getByText("2/5 proof lanes ready; review NAV support, evidence package, period lock")).toBeInTheDocument();
    expect(within(cockpitSummary).getByText("3 fund events")).toBeInTheDocument();
    expect(within(cockpitSummary).getByText("1/2 report outputs delivered")).toBeInTheDocument();
    const dashboardSummary = screen.getByRole("list", { name: "Financial Operations dashboard summary" });
    expect(within(dashboardSummary).getByText("Core flow: Resolve Exceptions")).toBeInTheDocument();
    expect(within(dashboardSummary).getByText("3/6 metrics ready")).toBeInTheDocument();
    expect(within(dashboardSummary).getByText("1 retained evidence link")).toBeInTheDocument();
    const dashboard = screen.getByRole("table", { name: "Financial Operations operational dashboard" });
    expect(within(dashboard).getByText("Receive Activity")).toBeInTheDocument();
    expect(within(dashboard).getByText("Match Records")).toBeInTheDocument();
    expect(within(dashboard).getByText("Resolve Exceptions")).toBeInTheDocument();
    expect(within(dashboard).getByText("Close package retained")).toBeInTheDocument();
    expect(within(dashboard).getByText("Assign, escalate, or resolve open exceptions and retain resolution evidence.")).toBeInTheDocument();
    const evidencePackages = screen.getByRole("table", { name: "Operations continuity evidence packages" });
    expect(within(evidencePackages).getByText("Accounting record evidence")).toBeInTheDocument();
    expect(within(evidencePackages).getByText("Close package manifest")).toBeInTheDocument();
    expect(within(evidencePackages).getAllByText("4/6 categories complete")).toHaveLength(2);
    expect(within(evidencePackages).getByText("Close package close-package-2026-05 retained manifest close-package-2026-05-manifest and evidence hash.")).toBeInTheDocument();
    expect(within(evidencePackages).getByRole("link", { name: "Open evidence package Close package manifest" }))
      .toHaveAttribute("href", "/accounting/operations-continuity/79f1f386-0bb1-4aef-9a85-fb9d6de8e1f6/close-package/close-package-2026-05-manifest");
    expect(within(evidencePackages).getByRole("link", { name: "Open evidence package Audit support package" }))
      .toHaveAttribute("href", "/reporting/evidence");
    const cockpitLanes = screen.getByRole("table", { name: "Private-capital close cockpit lanes" });
    expect(within(cockpitLanes).getByText("Partner capital account tie-outs")).toBeInTheDocument();
    expect(within(cockpitLanes).getByText("Expense, fee, and allocation review")).toBeInTheDocument();
    expect(within(cockpitLanes).getByText("NAV support")).toBeInTheDocument();
    expect(within(cockpitLanes).getByText("Evidence package")).toBeInTheDocument();
    expect(within(cockpitLanes).getByText("Publish the close package manifest")).toBeInTheDocument();
    expect(within(cockpitLanes).getByText("Close the workflow and retain period-lock evidence")).toBeInTheDocument();
    const cockpitWorkflows = screen.getByRole("table", { name: "Private-capital close cockpit workflows" });
    expect(within(cockpitWorkflows).getByText(`${summary.periodId} / ${fundAccountId}`)).toBeInTheDocument();
    expect(within(cockpitWorkflows).getByText("72% ready")).toBeInTheDocument();
    const navSupportPackages = screen.getByRole("table", { name: "Private-capital close cockpit NAV support packages" });
    expect(within(navSupportPackages).getByText("NAV support package")).toBeInTheDocument();
    expect(within(navSupportPackages).getByText("$1,250,000.00 shadow NAV")).toBeInTheDocument();
    expect(within(navSupportPackages).getByText("2/4 components ready; review Pricing, Shadow NAV")).toBeInTheDocument();
    expect(within(navSupportPackages).getByText("Retain NAV support package for positions, cash, pricing, and shadow NAV evidence.")).toBeInTheDocument();
    const cockpitApprovalHistory = screen.getByRole("table", { name: "Private-capital close cockpit approval history" });
    expect(within(cockpitApprovalHistory).getByText("approval-close-2026-05")).toBeInTheDocument();
    expect(within(cockpitApprovalHistory).getByText("Reviewer fund-controller / Operator ops-user")).toBeInTheDocument();
    expect(within(cockpitApprovalHistory).getByText("Pending final ledger validation before close sign-off.")).toBeInTheDocument();
    expect(within(cockpitApprovalHistory).getByText("1 evidence link")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Open private-capital close cockpit action: Resolve Ledger Posting blockers" }))
      .toHaveAttribute("href", "/accounting/ledger");

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
