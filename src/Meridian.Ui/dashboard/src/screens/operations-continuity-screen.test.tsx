import { cleanup, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import {
  acknowledgeOperationsContinuityChecklistTask,
  approveOperationsContinuityWorkflow,
  assignOperationsContinuityBreakCase,
  closeOperationsContinuityWorkflow,
  getOperationsCloseCalendar,
  getPrivateCapitalCloseCockpit,
  getOperationsContinuityWorkflow,
  getOperationsContinuityWorkflows,
  reopenOperationsContinuityWorkflow,
  rejectOperationsContinuityWorkflow,
  resolveOperationsContinuityBreakCase,
  submitOperationsContinuityApproval
} from "@/lib/api";
import { OperationsContinuityScreen } from "@/screens/operations-continuity-screen";
import { OPERATIONS_CONTINUITY_WORKFLOW_DETAIL_PANEL_ID } from "@/screens/operations-continuity-screen.view-model";
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
import { requirePresent } from "@/test/fixtures";

vi.mock("@/lib/api", () => ({
  acknowledgeOperationsContinuityChecklistTask: vi.fn(),
  approveOperationsContinuityWorkflow: vi.fn(),
  assignOperationsContinuityBreakCase: vi.fn(),
  closeOperationsContinuityWorkflow: vi.fn(),
  getOperationsCloseCalendar: vi.fn(),
  getPrivateCapitalCloseCockpit: vi.fn(),
  getOperationsContinuityWorkflows: vi.fn(),
  getOperationsContinuityWorkflow: vi.fn(),
  reopenOperationsContinuityWorkflow: vi.fn(),
  rejectOperationsContinuityWorkflow: vi.fn(),
  resolveOperationsContinuityBreakCase: vi.fn(),
  submitOperationsContinuityApproval: vi.fn()
}));

afterEach(() => {
  cleanup();
  vi.clearAllMocks();
});

beforeEach(() => {
  vi.mocked(getOperationsContinuityWorkflows).mockResolvedValue([summary]);
  vi.mocked(getOperationsContinuityWorkflow).mockResolvedValue(detail);
  vi.mocked(getOperationsCloseCalendar).mockResolvedValue(closeCalendar);
  vi.mocked(getPrivateCapitalCloseCockpit).mockResolvedValue(closeCockpit);
  vi.mocked(assignOperationsContinuityBreakCase).mockResolvedValue({
    success: true,
    workflow: detail,
    blockers: [],
    message: "Break assignment retained."
  });
  vi.mocked(resolveOperationsContinuityBreakCase).mockResolvedValue({
    success: true,
    workflow: detail,
    blockers: [],
    message: "Break resolution retained."
  });
  vi.mocked(acknowledgeOperationsContinuityChecklistTask).mockResolvedValue({
    success: true,
    workflow: detail,
    blockers: [],
    message: "Checklist task acknowledged."
  });
  vi.mocked(submitOperationsContinuityApproval).mockResolvedValue({
    success: true,
    workflow: detail,
    blockers: [],
    message: "Workflow approval submitted."
  });
  vi.mocked(approveOperationsContinuityWorkflow).mockResolvedValue({
    success: true,
    workflow: detail,
    blockers: [],
    message: "Workflow approval approved."
  });
  vi.mocked(rejectOperationsContinuityWorkflow).mockResolvedValue({
    success: true,
    workflow: detail,
    blockers: [],
    message: "Workflow approval rejected."
  });
  vi.mocked(closeOperationsContinuityWorkflow).mockResolvedValue({
    success: true,
    workflow: detail,
    blockers: [],
    message: "Close package published."
  });
  vi.mocked(reopenOperationsContinuityWorkflow).mockResolvedValue({
    success: true,
    workflow: detail,
    blockers: [],
    message: "Governed reopen retained."
  });
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
          label: "Approval evidence",
          route: "/workstation/accounting/approvals",
          source: "operations-continuity",
          capturedAtUtc: "2026-05-08T15:05:00Z"
        }
      ]
    }
  ],
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
      },
      {
        artifactId: "reviewed-automation:missing-support-flag",
        artifactKind: "Missing support",
        title: "Missing support flag",
        status: "ReviewRequired",
        requiresHumanReview: true,
        confidencePercent: 72,
        sourceSummary: "Missing support flags are derived from incomplete evidence package categories.",
        suggestedOperatorAction: "Attach or waive missing support through governed human review.",
        blockedMaterialAction: "Cannot approve its own missing-support disposition.",
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
    },
    {
      packageId: "period-lock-reopen:fund-alpha:2026-05",
      label: "Period lock and reopen evidence",
      status: "ReviewRequired",
      isReady: false,
      summary: "Workflow was reopened after close package close-package-2026-05; 1 incident evidence link is retained and the period must be locked again after remediation.",
      routeHint: "/workstation/accounting/operations-continuity",
      completeCategoryCount: 1,
      requiredCategoryCount: 2,
      evidenceLinkCount: 1,
      evidenceLinks: [
        {
          evidenceId: "INC-123",
          label: "Workflow reopen incident",
          route: "/workstation/accounting",
          source: "incident",
          capturedAtUtc: "2026-05-09T11:00:00Z"
        }
      ],
      requiredActions: ["Complete reopened incident remediation and close the period again with retained evidence."]
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
      packageId: "private-capital:partner-capital-tie-out",
      label: "Partner capital tie-out evidence package",
      status: "Ready",
      isReady: true,
      summary: "Partner capital account tie-outs retain capital subledger, ledger, and investor statement evidence.",
      routeHint: "/workstation/accounting/private-capital/capital-account-subledger",
      completeCategoryCount: 3,
      requiredCategoryCount: 3,
      evidenceLinkCount: 2,
      evidenceLinks: [
        {
          evidenceId: "partner-capital-tie-out-evidence",
          label: "Partner capital tie-out evidence",
          route: "/workstation/accounting/private-capital/capital-account-subledger/tie-outs",
          source: "private-capital",
          capturedAtUtc: "2026-05-10T18:06:00Z"
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
    },
    {
      packageId: "private-capital:close-approval-audit",
      label: "Close approval and audit evidence",
      status: "ReviewRequired",
      isReady: false,
      summary: "Close approval and audit evidence still need checklist-control approval and close-package manifest support.",
      routeHint: "/workstation/accounting/operations-continuity",
      completeCategoryCount: 2,
      requiredCategoryCount: 4,
      evidenceLinkCount: 1,
      evidenceLinks: [
        {
          evidenceId: "close-approval-audit-evidence",
          label: "Close approval audit evidence",
          route: "/workstation/accounting/approvals",
          source: "operations-continuity",
          capturedAtUtc: "2026-05-10T18:12:00Z"
        }
      ],
      requiredActions: ["Retain checklist-control approval and close-package manifest evidence before sign-off."]
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

describe("OperationsContinuityScreen", () => {
  it("renders workflow list, detail gates, blockers, timeline, and enabled next action", async () => {
    renderScreen();

    expect(await screen.findByRole("heading", { name: "Operations continuity" })).toBeInTheDocument();
    const workflows = await screen.findByRole("treegrid", { name: "Operations continuity workflows" });
    expect(within(workflows).getByText("2026-05 close")).toBeInTheDocument();
    expect(workflows).not.toHaveTextContent(fundAccountId);
    const workflowRow = within(workflows).getByRole("row", { name: /open 2026-05 operations continuity workflow/i });
    expect(workflowRow).toHaveAttribute("aria-controls", OPERATIONS_CONTINUITY_WORKFLOW_DETAIL_PANEL_ID);
    expect(workflowRow).toHaveAttribute("aria-expanded", "true");
    expect(workflowRow).toHaveClass("bg-warning/5");
    const selectedWorkflow = screen.getByRole("region", { name: "Operations continuity detail for 2026-05 close workflow" });
    expect(selectedWorkflow).toHaveAttribute("id", OPERATIONS_CONTINUITY_WORKFLOW_DETAIL_PANEL_ID);
    expect(selectedWorkflow).toHaveTextContent("Broker source: Custodian.");
    expect(selectedWorkflow).not.toHaveTextContent(fundAccountId);
    const workflowSystemDetails = screen.getByText("Workflow system details").closest("details");
    expect(workflowSystemDetails).not.toHaveAttribute("open");
    expect(within(workflowSystemDetails as HTMLElement).getByText(fundAccountId)).toBeInTheDocument();

    expect(await screen.findByRole("heading", { name: "Gates" })).toBeInTheDocument();
    await waitFor(() => {
      expect(screen.getAllByText("Ledger posting requires a balanced and validated journal draft.")).toHaveLength(2);
    });
    const gates = screen.getByRole("table", { name: "Operations continuity gates" });
    const brokerIntakeGate = within(gates).getByRole("row", { name: "Broker intake gate, Passed, 0 blockers" });
    expect(within(brokerIntakeGate).getByText("May 08, 14:20 UTC by Operations user")).toBeInTheDocument();
    const completionIdentity = within(brokerIntakeGate).getByText("Completion identity").closest("details");
    expect(completionIdentity).not.toHaveAttribute("open");
    expect(within(completionIdentity as HTMLElement).getByText("Actor ID: ops-user")).toBeInTheDocument();
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
    expect(within(checklist).getByText("Acknowledgement blocked")).toBeInTheDocument();
    expect(within(checklist).getByText("Expected workflow version 4")).toBeInTheDocument();
    expect(within(checklist).getByRole("link", { name: "Open remediation for Ledger posting controller check" }))
      .toHaveAttribute("href", "/accounting/ledger");
    expect(await screen.findByText("Ledger Draft Blocked")).toBeInTheDocument();
    const breakCases = screen.getByRole("table", { name: "Operations continuity break assignment and escalation" });
    expect(within(breakCases).getByText("recon-break-42")).toBeInTheDocument();
    expect(within(breakCases).getByText("fund-controller")).toBeInTheDocument();
    expect(within(breakCases).getByText("Level 2 at May 08, 15:20 UTC: Aged cash variance past controller SLA")).toBeInTheDocument();
    expect(within(breakCases).getByText("Expected 125,000.25 / Actual 124,998.25 / Variance -2")).toBeInTheDocument();
    expect(within(breakCases).getByText("1 retained evidence link")).toBeInTheDocument();
    expect(within(breakCases).getByRole("link", { name: "Open retained evidence for break recon-break-42" }))
      .toHaveAttribute("href", "/accounting/reconciliation/recon-break-42/evidence");
    expect(within(breakCases).getByText("SLA Warning due May 09, 16:00 UTC")).toBeInTheDocument();
    expect(within(breakCases).getByText("Materiality 2")).toBeInTheDocument();
    expect(within(breakCases).getByText("Root cause Broker Cash Timing")).toBeInTheDocument();
    expect(within(breakCases).getByText("Approval Ready For Signoff")).toBeInTheDocument();
    expect(within(breakCases).getByText("Blocks Report package release, Close sign-off review")).toBeInTheDocument();
    expect(within(breakCases).getByText("Resolution command ready")).toBeInTheDocument();
    expect(within(breakCases).getByText("Expected workflow version 4")).toBeInTheDocument();
    expect(within(breakCases).getByRole("link", { name: "Open exception casework for break recon-break-42" }))
      .toHaveAttribute("href", `/accounting/exceptions?workflowId=${workflowId}&breakId=recon-break-42`);
    const reconciliationLaneCoverage = screen.getByRole("table", { name: "Operations continuity reconciliation lane coverage" });
    expect(within(reconciliationLaneCoverage).getByText("Cash reconciliation")).toBeInTheDocument();
    expect(within(reconciliationLaneCoverage).getByText("Position reconciliation")).toBeInTheDocument();
    expect(within(reconciliationLaneCoverage).getByText("Trade reconciliation")).toBeInTheDocument();
    expect(within(reconciliationLaneCoverage).getByText("Income reconciliation")).toBeInTheDocument();
    expect(within(reconciliationLaneCoverage).getByText("MBS factor reconciliation")).toBeInTheDocument();
    expect(within(reconciliationLaneCoverage).getByText("Bank reconciliation")).toBeInTheDocument();
    expect(within(reconciliationLaneCoverage).getByText("GL reconciliation support")).toBeInTheDocument();
    expect(within(reconciliationLaneCoverage).getByText("Resolve or assign MBS factor reconciliation breaks and retain evidence.")).toBeInTheDocument();
    expect(within(reconciliationLaneCoverage).getAllByText("1 evidence link")).toHaveLength(2);
    expect(within(reconciliationLaneCoverage).getByRole("link", { name: "Open retained evidence for reconciliation lane Cash reconciliation" }))
      .toHaveAttribute("href", "/accounting/reconciliation/cash");
    expect(within(reconciliationLaneCoverage).getByRole("link", { name: "Open retained evidence for reconciliation lane MBS factor reconciliation" }))
      .toHaveAttribute("href", "/accounting/reconciliation/recon-break-factor-1");
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
    expect(within(dashboard).getByText("Approve Results")).toBeInTheDocument();
    expect(within(dashboard).getByText("Produce Evidence")).toBeInTheDocument();
    expect(within(dashboard).getByText("Close Support")).toBeInTheDocument();
    expect(within(dashboard).getByText("Close package retained")).toBeInTheDocument();
    expect(within(dashboard).getByText("Assign, escalate, or resolve open exceptions and retain resolution evidence.")).toBeInTheDocument();
    expect(getOperationsCloseCalendar).toHaveBeenCalledWith(
      { fundAccountId, periodId: "2026-05" },
      expect.objectContaining({ signal: expect.any(AbortSignal) })
    );
    const closeCalendarSummary = screen.getByRole("list", { name: "Operations close calendar summary" });
    expect(within(closeCalendarSummary).getByText(`Selected 2026-05 / ${fundAccountId}`)).toBeInTheDocument();
    expect(within(closeCalendarSummary).getByText("Next due May 12, 2026: Ledger posting controller check")).toBeInTheDocument();
    expect(within(closeCalendarSummary).getByText("1/2 approvals complete")).toBeInTheDocument();
    const closeCalendarTable = screen.getByRole("table", { name: "Operations continuity close calendar" });
    expect(within(closeCalendarTable).getByText(`2026-05 / ${fundAccountId}`)).toBeInTheDocument();
    expect(within(closeCalendarTable).getByText("Ledger posting controller check")).toBeInTheDocument();
    expect(within(closeCalendarTable).getByText("fund-controller")).toBeInTheDocument();
    expect(within(closeCalendarTable).getByRole("link", { name: `Open close calendar workflow 2026-05 / ${fundAccountId}` }))
      .toHaveAttribute("href", "/accounting/operations-continuity");
    const commandSpine = screen.getByRole("table", { name: "Financial Operations command spine" });
    expect(within(commandSpine).getByText("Start/import/normalize activity")).toBeInTheDocument();
    expect(within(commandSpine).getByText("Run reconciliation and refresh posture")).toBeInTheDocument();
    expect(within(commandSpine).getByText("Assign, escalate, or resolve breaks")).toBeInTheDocument();
    expect(within(commandSpine).getByText("Submit or decide approval")).toBeInTheDocument();
    expect(within(commandSpine).getByText("Open evidence package routes")).toBeInTheDocument();
    expect(within(commandSpine).getByText("Review checklist and close readiness")).toBeInTheDocument();
    expect(within(commandSpine).getAllByText("Shared command guard clear")).toHaveLength(2);
    expect(within(commandSpine).getByRole("link", { name: "Open Financial Operations command stage Resolve Exceptions" }))
      .toHaveAttribute("href", "/accounting/reconciliation");
    const reviewedAutomation = screen.getByRole("list", { name: "Reviewed automation summary" });
    expect(within(reviewedAutomation).getByText("Stage: Report commentary and audit request list draft review")).toBeInTheDocument();
    expect(within(reviewedAutomation).getByText("Human review required")).toBeInTheDocument();
    expect(within(reviewedAutomation).getByText("Allowed: Draft report commentary, Draft audit request lists")).toBeInTheDocument();
    expect(within(reviewedAutomation).getByText("Prohibited: Approve workflow, Publish close package")).toBeInTheDocument();
    expect(within(reviewedAutomation).getByText("1 retained review evidence link")).toBeInTheDocument();
    expect(within(reviewedAutomation).getByText("Review drafted report commentary and audit request lists against retained evidence before submission.")).toBeInTheDocument();
    const reviewedAutomationQueue = screen.getByRole("table", { name: "Reviewed automation output queue" });
    expect(within(reviewedAutomationQueue).getByText("Report commentary draft")).toBeInTheDocument();
    expect(within(reviewedAutomationQueue).getByText("Audit request list draft")).toBeInTheDocument();
    expect(within(reviewedAutomationQueue).getByText("Missing support flag")).toBeInTheDocument();
    expect(within(reviewedAutomationQueue).getByText("84% confidence")).toBeInTheDocument();
    expect(within(reviewedAutomationQueue).getByText("Cannot publish reports or release support packages.")).toBeInTheDocument();
    const operatorQueue = screen.getByRole("table", { name: "Financial Operations operator queue" });
    expect(within(operatorQueue).getByText("recon-break-42")).toBeInTheDocument();
    expect(within(operatorQueue).getAllByText("approval-close-2026-05")).toHaveLength(2);
    expect(within(operatorQueue).getByText("CashBalance / cash-balance-check; Level 2 at May 08, 15:20 UTC: Aged cash variance past controller SLA")).toBeInTheDocument();
    expect(within(operatorQueue).getByText("Reconciliation lane")).toBeInTheDocument();
    expect(within(operatorQueue).getByText("MBS factor reconciliation")).toBeInTheDocument();
    expect(within(operatorQueue).getByText("1 open break; MBS factor reconciliation has 1 open break requiring controller review.")).toBeInTheDocument();
    expect(within(operatorQueue).getByText("Workflow blocker")).toBeInTheDocument();
    expect(within(operatorQueue).getByText("Ledger validation required")).toBeInTheDocument();
    expect(within(operatorQueue).getByText("Rule code: LEDGER_VALIDATION_REQUIRED")).toBeInTheDocument();
    expect(within(operatorQueue).getByText("Ledger Posting: Ledger posting requires a balanced and validated journal draft.")).toBeInTheDocument();
    expect(within(operatorQueue).getByText("Resolve Ledger Posting blocker and retain evidence.")).toBeInTheDocument();
    expect(within(operatorQueue).getByText("Ledger posting controller check")).toBeInTheDocument();
    expect(within(operatorQueue).getByText("Close calendar")).toBeInTheDocument();
    expect(within(operatorQueue).getByText(`2026-05 / ${fundAccountId}: Ledger posting controller check`)).toBeInTheDocument();
    expect(within(operatorQueue).getByText("Calendar status: Ledger Posting Draft")).toBeInTheDocument();
    expect(within(operatorQueue).getAllByText("Private-capital proof lane")).toHaveLength(3);
    expect(within(operatorQueue).getByText("NAV support")).toBeInTheDocument();
    expect(within(operatorQueue).getByText("Shadow NAV support package still needs retained positions, cash, and pricing evidence.")).toBeInTheDocument();
    expect(within(operatorQueue).getByText("Retain NAV support for positions, cash, and pricing")).toBeInTheDocument();
    expect(within(operatorQueue).getAllByText("NAV support package")).toHaveLength(2);
    expect(within(operatorQueue).getByText(/2\/4 components ready; review Pricing, Shadow NAV; \$1,250,000\.00 shadow NAV/)).toBeInTheDocument();
    expect(within(operatorQueue).getByText("Retain NAV support package for positions, cash, pricing, and shadow NAV evidence.")).toBeInTheDocument();
    expect(within(operatorQueue).getAllByText("Private-capital evidence package")).toHaveLength(2);
    expect(within(operatorQueue).getByText("NAV support evidence package")).toBeInTheDocument();
    expect(within(operatorQueue).getByText("NAV support evidence package still needs pricing and shadow NAV support.")).toBeInTheDocument();
    expect(within(operatorQueue).getByText("Retain complete NAV support package evidence before close sign-off.")).toBeInTheDocument();
    expect(within(operatorQueue).getByText("Close approval and audit evidence")).toBeInTheDocument();
    expect(within(operatorQueue).getByText("Close approval and audit evidence still need checklist-control approval and close-package manifest support.")).toBeInTheDocument();
    expect(within(operatorQueue).getByText("Retain checklist-control approval and close-package manifest evidence before sign-off.")).toBeInTheDocument();
    expect(within(operatorQueue).getByText("Private-capital approval")).toBeInTheDocument();
    expect(within(operatorQueue).getByText(`2026-05 / ${fundAccountId}: Pending final ledger validation before close sign-off.`)).toBeInTheDocument();
    expect(within(operatorQueue).getByText("Complete private-capital close approval")).toBeInTheDocument();
    expect(within(operatorQueue).getAllByText("Reviewed automation")).toHaveLength(3);
    expect(within(operatorQueue).getByText("Report commentary draft")).toBeInTheDocument();
    expect(within(operatorQueue).getByText("Report commentary: Draft commentary is generated from retained close, ledger, reconciliation, and report-pack evidence.; 84% confidence")).toBeInTheDocument();
    expect(within(operatorQueue).getByText("Review commentary against retained evidence before report approval or publication.; Cannot publish reports or release support packages.")).toBeInTheDocument();
    expect(within(operatorQueue).getByText("Audit request list draft")).toBeInTheDocument();
    expect(within(operatorQueue).getByText("Review each requested support item and assign an owner before audit release.; Cannot erase evidence or satisfy audit requests without retained support.")).toBeInTheDocument();
    expect(within(operatorQueue).getByText("Missing support flag")).toBeInTheDocument();
    expect(within(operatorQueue).getByText("Attach or waive missing support through governed human review.; Cannot approve its own missing-support disposition.")).toBeInTheDocument();
    expect(within(operatorQueue).getAllByText("Accounting-record evidence")).toHaveLength(6);
    expect(within(operatorQueue).getByText("Journal and ledger evidence")).toBeInTheDocument();
    expect(within(operatorQueue).getByText("Ledger validation is still required.; Requires journal preview, posted ledger batch, trial-balance support")).toBeInTheDocument();
    expect(within(operatorQueue).getByText("Retain journal preview, posted ledger batch, trial-balance support.")).toBeInTheDocument();
    expect(within(operatorQueue).getByText("Period lock and reopen evidence")).toBeInTheDocument();
    expect(within(operatorQueue).getByText("Complete reopened incident remediation and close the period again with retained evidence.")).toBeInTheDocument();
    expect(within(operatorQueue).getAllByText("Command stage")).toHaveLength(4);
    expect(within(operatorQueue).getByText("Match Records")).toBeInTheDocument();
    expect(within(operatorQueue).getByText("Run reconciliation and refresh posture; Cash, position, trade, income, MBS factor, bank, and GL reconciliation lanes are tracked from the shared workflow detail.")).toBeInTheDocument();
    expect(within(operatorQueue).getByText("Complete source-backed reconciliation lanes before approval.")).toBeInTheDocument();
    expect(within(operatorQueue).getByText("Close Support")).toBeInTheDocument();
    expect(within(operatorQueue).getByText("Review checklist and close readiness; Close checklist, period lock, and reopen evidence are governed by the shared workflow.")).toBeInTheDocument();
    expect(within(operatorQueue).getByText("Clear close readiness blockers and retain period-lock or reopen evidence.")).toBeInTheDocument();
    expect(within(operatorQueue).getByRole("link", { name: "Open Financial Operations queue item: recon-break-42" }))
      .toHaveAttribute("href", "/accounting/reconciliation/recon-break-42/evidence");
    const approvalQueueLinks = within(operatorQueue).getAllByRole("link", { name: "Open Financial Operations queue item: approval-close-2026-05" });
    expect(approvalQueueLinks).toHaveLength(2);
    approvalQueueLinks.forEach((link) => {
      expect(link).toHaveAttribute("href", "/accounting/approvals");
    });
    expect(within(operatorQueue).getByRole("link", { name: "Open Financial Operations queue item: MBS factor reconciliation" }))
      .toHaveAttribute("href", "/accounting/reconciliation");
    expect(within(operatorQueue).getByRole("link", { name: "Open Financial Operations queue item: Ledger validation required" }))
      .toHaveAttribute("href", "/accounting/operations-continuity");
    expect(within(operatorQueue).getByRole("link", { name: "Open Financial Operations queue item: Ledger posting controller check" }))
      .toHaveAttribute("href", "/accounting/ledger");
    expect(within(operatorQueue).getByRole("link", { name: "Open Financial Operations queue item: Journal and ledger evidence" }))
      .toHaveAttribute("href", "/accounting/ledger");
    expect(within(operatorQueue).getByRole("link", { name: `Open Financial Operations queue item: 2026-05 / ${fundAccountId}: Ledger posting controller check` }))
      .toHaveAttribute("href", "/accounting/operations-continuity");
    expect(within(operatorQueue).getByRole("link", { name: "Open Financial Operations queue item: NAV support" }))
      .toHaveAttribute("href", "/portfolio/nav");
    expect(within(operatorQueue).getByRole("link", { name: "Open Financial Operations queue item: NAV support package" }))
      .toHaveAttribute("href", "/portfolio/nav");
    expect(within(operatorQueue).getByRole("link", { name: "Open Financial Operations queue item: NAV support evidence package" }))
      .toHaveAttribute("href", "/portfolio/nav");
    expect(within(operatorQueue).getByRole("link", { name: "Open Financial Operations queue item: Close approval and audit evidence" }))
      .toHaveAttribute("href", "/accounting/operations-continuity");
    expect(within(operatorQueue).getByRole("link", { name: "Open Financial Operations queue item: Report commentary draft" }))
      .toHaveAttribute("href", "/reporting/report-packs/automation-review");
    expect(within(operatorQueue).getByRole("link", { name: "Open Financial Operations queue item: Period lock and reopen evidence" }))
      .toHaveAttribute("href", "/accounting/operations-continuity");
    expect(within(operatorQueue).getByRole("link", { name: "Open Financial Operations queue item: Match Records" }))
      .toHaveAttribute("href", "/accounting/reconciliation");
    expect(within(operatorQueue).getByRole("link", { name: "Open Financial Operations queue item: Close Support" }))
      .toHaveAttribute("href", "/accounting/operations-continuity");
    const workflowApprovalHistory = screen.getByRole("table", { name: "Operations continuity workflow approval history" });
    expect(within(workflowApprovalHistory).getByText("approval-close-2026-05")).toBeInTheDocument();
    expect(within(workflowApprovalHistory).getByText("Reviewer fund-controller / Operator ops-user")).toBeInTheDocument();
    expect(within(workflowApprovalHistory).getByText("Submitted May 08, 15:05 UTC")).toBeInTheDocument();
    expect(within(workflowApprovalHistory).getByText("Decision pending")).toBeInTheDocument();
    expect(within(workflowApprovalHistory).getByText("Pending final ledger validation before close sign-off.")).toBeInTheDocument();
    expect(within(workflowApprovalHistory).getByRole("link", { name: "Open approval evidence for workflow approval approval-close-2026-05" }))
      .toHaveAttribute("href", "/accounting/approvals");
    const evidencePackages = screen.getByRole("table", { name: "Operations continuity evidence packages" });
    expect(within(evidencePackages).getByText("Accounting record evidence")).toBeInTheDocument();
    expect(within(evidencePackages).getByText("Close package manifest")).toBeInTheDocument();
    expect(within(evidencePackages).getByText("Period lock and reopen evidence")).toBeInTheDocument();
    expect(within(evidencePackages).getAllByText("4/6 categories complete")).toHaveLength(2);
    expect(within(evidencePackages).getByText("1/2 categories complete")).toBeInTheDocument();
    expect(within(evidencePackages).getByText("Close package close-package-2026-05 retained manifest close-package-2026-05-manifest and evidence hash.")).toBeInTheDocument();
    expect(within(evidencePackages).getByText("Complete reopened incident remediation and close the period again with retained evidence.")).toBeInTheDocument();
    expect(within(evidencePackages).getByRole("link", { name: "Open retained evidence for package Accounting record evidence" }))
      .toHaveAttribute("href", "/accounting/reconciliation/recon-break-42/evidence");
    expect(within(evidencePackages).getByRole("link", { name: "Open evidence package Close package manifest" }))
      .toHaveAttribute("href", "/accounting/operations-continuity/79f1f386-0bb1-4aef-9a85-fb9d6de8e1f6/close-package/close-package-2026-05-manifest");
    expect(within(evidencePackages).getByRole("link", { name: "Open evidence package Audit support package" }))
      .toHaveAttribute("href", "/reporting/evidence");
    expect(within(evidencePackages).getByRole("link", { name: "Open evidence package Period lock and reopen evidence" }))
      .toHaveAttribute("href", "/accounting/operations-continuity");
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
    const cockpitEvidencePackages = screen.getByRole("table", { name: "Private-capital close cockpit evidence packages" });
    expect(within(cockpitEvidencePackages).getByText("Fund-event accounting evidence")).toBeInTheDocument();
    expect(within(cockpitEvidencePackages).getByText("Partner capital tie-out evidence package")).toBeInTheDocument();
    expect(within(cockpitEvidencePackages).getByText("NAV support evidence package")).toBeInTheDocument();
    expect(within(cockpitEvidencePackages).getByText("Close approval and audit evidence")).toBeInTheDocument();
    expect(within(cockpitEvidencePackages).getByText("4/4 categories complete")).toBeInTheDocument();
    expect(within(cockpitEvidencePackages).getByText("3/3 categories complete")).toBeInTheDocument();
    expect(within(cockpitEvidencePackages).getByText("1/3 categories complete")).toBeInTheDocument();
    expect(within(cockpitEvidencePackages).getByText("2/4 categories complete")).toBeInTheDocument();
    expect(within(cockpitEvidencePackages).getByText("Retain complete NAV support package evidence before close sign-off.")).toBeInTheDocument();
    expect(within(cockpitEvidencePackages).getByText("Retain checklist-control approval and close-package manifest evidence before sign-off.")).toBeInTheDocument();
    expect(within(cockpitEvidencePackages).getByRole("link", { name: "Open retained evidence for package Partner capital tie-out evidence package" }))
      .toHaveAttribute("href", "/accounting/private-capital/capital-account-subledger/tie-outs");
    expect(within(cockpitEvidencePackages).getByRole("link", { name: "Open retained evidence for package NAV support evidence package" }))
      .toHaveAttribute("href", "/portfolio/nav/support-package");
    expect(within(cockpitEvidencePackages).getByRole("link", { name: "Open retained evidence for package Close approval and audit evidence" }))
      .toHaveAttribute("href", "/accounting/approvals");
    expect(within(cockpitEvidencePackages).getByRole("link", { name: "Open evidence package Fund-event accounting evidence" }))
      .toHaveAttribute("href", "/accounting/private-capital/fund-events");
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
    expect(within(cockpitApprovalHistory).getByRole("link", { name: "Open private-capital close approval evidence approval-close-2026-05" }))
      .toHaveAttribute("href", "/accounting/approvals");
    expect(screen.getByRole("link", { name: "Open private-capital close cockpit action: Resolve Ledger Posting blockers" }))
      .toHaveAttribute("href", "/accounting/ledger");

    const nextAction = screen.getByRole("link", { name: "Open operations continuity next action: Resolve Ledger Posting blockers" });
    expect(nextAction).toHaveAttribute("href", "/accounting");
    expect(within(screen.getByRole("region", { name: "Recommended next action" })).getByText("Blocked")).toBeInTheDocument();
  });

  it("posts close checklist acknowledgement with the shared workflow guard", async () => {
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
    vi.mocked(getOperationsContinuityWorkflow).mockResolvedValue(acknowledgeableDetail);

    const user = userEvent.setup();
    renderScreen();

    const acknowledge = await screen.findByRole("button", {
      name: "Acknowledge close checklist task Ledger posting controller check"
    }, { timeout: 5000 });
    await user.click(acknowledge);

    await waitFor(() => {
      expect(acknowledgeOperationsContinuityChecklistTask).toHaveBeenCalledWith(
        workflowId,
        "close-gate-ledgerposting",
        {
          expectedVersion: 4,
          actor: "browser-operator",
          rationale: "Acknowledged close checklist task Ledger posting controller check after retained evidence review.",
          correlationId: "browser-checklist:close-gate-ledgerposting"
        }
      );
    });
    expect(await screen.findByText("Checklist task acknowledged.")).toBeInTheDocument();
  });

  it("submits approval from the command spine with retained report-pack evidence", async () => {
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
    vi.mocked(getOperationsContinuityWorkflow).mockResolvedValue(approvalReadyDetail);

    const user = userEvent.setup();
    renderScreen();

    await user.click(await screen.findByRole("button", { name: "Submit workflow approval for 2026-05" }));

    await waitFor(() => {
      expect(submitOperationsContinuityApproval).toHaveBeenCalledWith(
        workflowId,
        expect.objectContaining({
          expectedVersion: 4,
          actor: "browser-operator",
          reviewer: "fund-controller",
          rationale: "Submitted Approve Results approval from Operations Continuity command spine.",
          reportPackId: "report-pack-2026-05",
          correlationId: "browser-approval-submit:approve-results",
          evidenceLinks: expect.arrayContaining([reportPackEvidence]),
          checklistControlApprovals: [],
          actionOrigin: "HumanOperator"
        })
      );
    });
    expect(await screen.findByText("Workflow approval submitted.")).toBeInTheDocument();
  });

  it("surfaces command-spine approval submission failures", async () => {
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
        evidenceLinks: []
      },
      closePackage: null
    };
    vi.mocked(getOperationsContinuityWorkflow).mockResolvedValue(approvalReadyDetail);
    vi.mocked(submitOperationsContinuityApproval).mockRejectedValueOnce(new Error("Approval submission version conflict."));

    const user = userEvent.setup();
    renderScreen();

    await user.click(await screen.findByRole("button", { name: "Submit workflow approval for 2026-05" }));

    const alert = await screen.findByRole("alert");
    expect(within(alert).getByText("Approval submission version conflict.")).toBeInTheDocument();
  });

  it("approves workflow approval history rows with retained report-pack and checklist evidence", async () => {
    const reportPackEvidence: OperationsEvidenceLink = {
      evidenceId: "report-pack-approval-decision-evidence",
      label: "Report-pack approval decision evidence",
      route: "/workstation/reporting/report-packs/report-pack-2026-05/evidence",
      source: "operations-continuity",
      capturedAtUtc: "2026-05-10T17:25:00Z"
    };
    const approvalDecisionReadyDetail = createApprovalDecisionReadyDetail(reportPackEvidence);
    vi.mocked(getOperationsContinuityWorkflow).mockResolvedValue(approvalDecisionReadyDetail);

    const user = userEvent.setup();
    renderScreen();

    await user.click(await screen.findByRole("button", { name: "Approve workflow approval approval-close-2026-05" }));

    await waitFor(() => {
      expect(approveOperationsContinuityWorkflow).toHaveBeenCalledWith(
        workflowId,
        expect.objectContaining({
          expectedVersion: 4,
          actor: "browser-operator",
          reviewer: "fund-controller",
          rationale: "Approved workflow approval approval-close-2026-05 from Operations Continuity approval history.",
          reportPackId: "report-pack-2026-05",
          correlationId: "browser-approval-decision:approve:approval-close-2026-05",
          evidenceLinks: expect.arrayContaining([reportPackEvidence]),
          checklistControlApprovals: expect.arrayContaining([
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
          actionOrigin: "HumanOperator"
        })
      );
    });
    expect(await screen.findByText("Workflow approval approved.")).toBeInTheDocument();
  });

  it("rejects workflow approval history rows with reviewer evidence and reason code", async () => {
    vi.mocked(getOperationsContinuityWorkflow).mockResolvedValue({
      ...detail,
      approvalState: "ReviewerAssigned"
    });

    const user = userEvent.setup();
    renderScreen();

    const workflowApprovalHistory = await screen.findByRole("table", { name: "Operations continuity workflow approval history" });
    expect(within(workflowApprovalHistory).getByText("Close workflow has unresolved ledger blockers.")).toBeInTheDocument();
    const rejectButton = within(workflowApprovalHistory).getByRole("button", { name: "Reject workflow approval approval-close-2026-05" });
    expect(rejectButton).toBeEnabled();
    await user.click(rejectButton);

    await waitFor(() => {
      expect(rejectOperationsContinuityWorkflow).toHaveBeenCalledWith(
        workflowId,
        expect.objectContaining({
          expectedVersion: 4,
          actor: "browser-operator",
          reviewer: "fund-controller",
          rationale: "Rejected workflow approval approval-close-2026-05 from Operations Continuity approval history.",
          reasonCode: "BrowserApprovalDecisionReview",
          correlationId: "browser-approval-decision:reject:approval-close-2026-05",
          evidenceLinks: expect.arrayContaining(detail.approvals[0]!.evidenceLinks),
          actionOrigin: "HumanOperator"
        })
      );
    });
    expect(await screen.findByText("Workflow approval rejected.")).toBeInTheDocument();
  });

  it("surfaces workflow approval decision failures", async () => {
    const reportPackEvidence: OperationsEvidenceLink = {
      evidenceId: "report-pack-approval-decision-evidence",
      label: "Report-pack approval decision evidence",
      route: "/workstation/reporting/report-packs/report-pack-2026-05/evidence",
      source: "operations-continuity",
      capturedAtUtc: "2026-05-10T17:25:00Z"
    };
    const approvalDecisionReadyDetail = createApprovalDecisionReadyDetail(reportPackEvidence);
    vi.mocked(getOperationsContinuityWorkflow).mockResolvedValue(approvalDecisionReadyDetail);
    vi.mocked(approveOperationsContinuityWorkflow).mockRejectedValueOnce(new Error("Approval decision version conflict."));

    const user = userEvent.setup();
    renderScreen();

    await user.click(await screen.findByRole("button", { name: "Approve workflow approval approval-close-2026-05" }));

    const alert = await screen.findByRole("alert");
    expect(within(alert).getByText("Approval decision version conflict.")).toBeInTheDocument();
  });

  it("publishes the close package from the command spine with retained checklist-control approvals", async () => {
    const reportPackEvidence = {
      evidenceId: "report-pack-close-evidence",
      label: "Report-pack retained manifest",
      route: "/workstation/reporting/report-packs/report-pack-2026-05/evidence",
      source: "operations-continuity",
      capturedAtUtc: "2026-05-10T18:00:00Z"
    };
    const readyDetail = createCloseReadyDetail(reportPackEvidence);
    vi.mocked(getOperationsContinuityWorkflow).mockResolvedValue(readyDetail);

    const user = userEvent.setup();
    renderScreen();

    await user.click(await screen.findByRole("button", { name: "Publish close package for 2026-05" }));

    await waitFor(() => {
      expect(closeOperationsContinuityWorkflow).toHaveBeenCalledWith(
        workflowId,
        expect.objectContaining({
          expectedVersion: 4,
          actor: "browser-operator",
          rationale: "Published Produce Evidence close package from Operations Continuity command spine.",
          reportPackId: "report-pack-2026-05",
          correlationId: "browser-close-package:produce-evidence",
          evidenceLinks: expect.arrayContaining([reportPackEvidence]),
          checklistControlApprovals: expect.arrayContaining([
            {
              taskId: "close-gate-brokeringest",
              approvedBy: "operations-lead",
              approvedAtUtc: "2026-05-10T17:00:00Z"
            },
            {
              taskId: "close-gate-approval",
              approvedBy: "fund-controller",
              approvedAtUtc: "2026-05-10T17:45:00Z"
            }
          ]),
          actionOrigin: "HumanOperator"
        })
      );
    });
    expect(await screen.findByText("Close package published.")).toBeInTheDocument();
  });

  it("surfaces command-spine close package publication failures", async () => {
    const readyDetail = createCloseReadyDetail({
      evidenceId: "report-pack-close-evidence",
      label: "Report-pack retained manifest",
      route: "/workstation/reporting/report-packs/report-pack-2026-05/evidence",
      source: "operations-continuity",
      capturedAtUtc: "2026-05-10T18:00:00Z"
    });
    vi.mocked(getOperationsContinuityWorkflow).mockResolvedValue(readyDetail);
    vi.mocked(closeOperationsContinuityWorkflow).mockRejectedValueOnce(new Error("Close package publication version conflict."));

    const user = userEvent.setup();
    renderScreen();

    await user.click(await screen.findByRole("button", { name: "Publish close package for 2026-05" }));

    const alert = await screen.findByRole("alert");
    expect(within(alert).getByText("Close package publication version conflict.")).toBeInTheDocument();
  });

  it("reopens a closed period from close governance with entered incident metadata", async () => {
    const closeEvidence = {
      evidenceId: "close-package-2026-05-manifest",
      label: "Close package retained manifest",
      route: "/workstation/accounting/operations-continuity/close-package-2026-05-manifest",
      source: "operations-continuity",
      capturedAtUtc: "2026-05-10T18:45:00Z"
    };
    const closedDetail = createClosedWorkflowDetail(closeEvidence);
    vi.mocked(getOperationsContinuityWorkflow).mockResolvedValue(closedDetail);

    const user = userEvent.setup();
    renderScreen();

    fireEvent.change(await screen.findByLabelText("Incident id"), {
      target: { value: "incident-2026-05-close-restatement" }
    });
    fireEvent.change(screen.getByLabelText("Approval reference"), {
      target: { value: "admin-approval-42" }
    });
    fireEvent.change(screen.getByLabelText("Justification"), {
      target: { value: "Controller approved restatement remediation." }
    });
    fireEvent.change(screen.getByLabelText("Impact summary"), {
      target: { value: "Ledger and report package will be regenerated with retained evidence." }
    });
    await user.click(screen.getByRole("button", { name: "Reopen governed period for 2026-05" }));

    await waitFor(() => {
      expect(reopenOperationsContinuityWorkflow).toHaveBeenCalledWith(
        workflowId,
        expect.objectContaining({
          expectedVersion: 5,
          actor: "browser-admin",
          rationale: "Governed reopen requested from Operations Continuity close governance for Closed period is locked.",
          incidentId: "incident-2026-05-close-restatement",
          isGovernedAdmin: true,
          justification: "Controller approved restatement remediation.",
          approvalReference: "admin-approval-42",
          impactSummary: "Ledger and report package will be regenerated with retained evidence.",
          correlationId: "browser-governed-reopen:incident-2026-05-close-restatement",
          evidenceLinks: expect.arrayContaining([closeEvidence]),
          actionOrigin: "HumanOperator"
        })
      );
    });
    expect(await screen.findByText("Governed reopen retained.")).toBeInTheDocument();
  });

  it("posts break resolution with retained evidence and the shared workflow guard", async () => {
    const user = userEvent.setup();
    renderScreen();

    await user.click(await screen.findByRole("button", { name: "Resolve break recon-break-42" }));

    await waitFor(() => {
      expect(resolveOperationsContinuityBreakCase).toHaveBeenCalledWith(
        workflowId,
        "recon-break-42",
        {
          expectedVersion: 4,
          actor: "browser-operator",
          resolutionStatus: "Resolved",
          rationale: "Resolved break recon-break-42 after retained evidence review.",
          correlationId: "browser-break-resolve:recon-break-42",
          evidenceLinks: detail.breakCases[0]!.evidenceLinks,
          actionOrigin: "HumanOperator"
        }
      );
    });
    expect(await screen.findByText("Break resolution retained.")).toBeInTheDocument();
  });

  it("posts break assignment for unassigned exceptions with the shared workflow guard", async () => {
    const unassignedDetail: OperationsContinuityWorkflow = {
      ...detail,
      breakCases: [
        {
          ...detail.breakCases[0]!,
          owner: null
        }
      ]
    };
    vi.mocked(getOperationsContinuityWorkflow).mockResolvedValue(unassignedDetail);

    const user = userEvent.setup();
    renderScreen();

    await user.click(await screen.findByRole("button", { name: "Assign break recon-break-42 to browser operator" }));

    await waitFor(() => {
      expect(assignOperationsContinuityBreakCase).toHaveBeenCalledWith(
        workflowId,
        "recon-break-42",
        {
          expectedVersion: 4,
          actor: "browser-operator",
          owner: "browser-operator",
          rationale: "Assigned break recon-break-42 from Operations Continuity exception management.",
          escalationLevel: "Level 2",
          escalationReason: "Aged cash variance past controller SLA",
          dueDate: "2026-05-09",
          correlationId: "browser-break-assign:recon-break-42",
          actionOrigin: "HumanOperator"
        }
      );
    });
    expect(await screen.findByText("Break assignment retained.")).toBeInTheDocument();
  });

  it("surfaces break command failures", async () => {
    vi.mocked(resolveOperationsContinuityBreakCase).mockRejectedValueOnce(new Error("Break version conflict."));

    const user = userEvent.setup();
    renderScreen();

    await user.click(await screen.findByRole("button", { name: "Resolve break recon-break-42" }));

    const alert = await screen.findByRole("alert");
    expect(within(alert).getByText("Break version conflict.")).toBeInTheDocument();
  });

  it("surfaces close checklist acknowledgement command failures", async () => {
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
    vi.mocked(getOperationsContinuityWorkflow).mockResolvedValue(acknowledgeableDetail);
    vi.mocked(acknowledgeOperationsContinuityChecklistTask).mockRejectedValueOnce(new Error("Workflow version conflict."));

    const user = userEvent.setup();
    renderScreen();

    await user.click(await screen.findByRole("button", {
      name: "Acknowledge close checklist task Ledger posting controller check"
    }));

    const alert = await screen.findByRole("alert");
    expect(within(alert).getByText("Workflow version conflict.")).toBeInTheDocument();
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

function createApprovalDecisionReadyDetail(reportPackEvidence: OperationsEvidenceLink): OperationsContinuityWorkflow {
  const approvalGates: OperationsGate[] = [
    createPassedGate("BrokerIngest", "Broker intake", "2026-05-10T17:00:00Z", "operations-lead"),
    createPassedGate("SecurityMaster", "Security Master", "2026-05-10T17:05:00Z", "security-master-lead"),
    createPassedGate("LedgerPosting", "Ledger posting", "2026-05-10T17:10:00Z", "ledger-lead"),
    createPassedGate("Reconciliation", "Reconciliation", "2026-05-10T17:20:00Z", "reconciliation-lead"),
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

  return {
    ...detail,
    status: "ApprovalPending",
    gates: approvalGates,
    brokerIntakeState: "Complete",
    securityMasterState: "Complete",
    ledgerPostingState: "Complete",
    reconciliationState: "Complete",
    approvalState: "ReviewerAssigned",
    breakCases: detail.breakCases.map((breakCase) => ({
      ...breakCase,
      status: "Resolved"
    })),
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
    closeChecklist: [
      createAcknowledgedCloseTask("close-gate-brokeringest", "BrokerIngest", "Broker ingest close gate", "operations-lead", 1, "2026-05-10T17:00:00Z"),
      createAcknowledgedCloseTask("close-gate-securitymaster", "SecurityMaster", "Security Master close gate", "security-master-lead", 1, "2026-05-10T17:05:00Z"),
      createAcknowledgedCloseTask("close-gate-ledgerposting", "LedgerPosting", "Ledger posting close gate", "ledger-lead", 1, "2026-05-10T17:10:00Z"),
      createAcknowledgedCloseTask("close-gate-reconciliation", "Reconciliation", "Reconciliation close gate", "reconciliation-lead", 1, "2026-05-10T17:20:00Z"),
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
    ],
    closeReadiness: null,
    closePackage: null
  };
}

function createCloseReadyDetail(reportPackEvidence: OperationsEvidenceLink): OperationsContinuityWorkflow {
  const readyGates: OperationsGate[] = [
    createPassedGate("BrokerIngest", "Broker intake", "2026-05-10T17:00:00Z", "operations-lead"),
    createPassedGate("SecurityMaster", "Security Master", "2026-05-10T17:05:00Z", "security-master-lead"),
    createPassedGate("LedgerPosting", "Ledger posting", "2026-05-10T17:10:00Z", "ledger-lead"),
    createPassedGate("Reconciliation", "Reconciliation", "2026-05-10T17:20:00Z", "reconciliation-lead"),
    createPassedGate("Approval", "Approval", "2026-05-10T17:45:00Z", "fund-controller")
  ];
  const closeChecklist: OperationsCloseChecklistTask[] = [
    createAcknowledgedCloseTask("close-gate-brokeringest", "BrokerIngest", "Broker ingest close gate", "operations-lead", 1, "2026-05-10T17:00:00Z"),
    createAcknowledgedCloseTask("close-gate-securitymaster", "SecurityMaster", "Security Master close gate", "security-master-lead", 1, "2026-05-10T17:05:00Z"),
    createAcknowledgedCloseTask("close-gate-ledgerposting", "LedgerPosting", "Ledger posting close gate", "ledger-lead", 1, "2026-05-10T17:10:00Z"),
    createAcknowledgedCloseTask("close-gate-reconciliation", "Reconciliation", "Reconciliation close gate", "reconciliation-lead", 1, "2026-05-10T17:20:00Z"),
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

  return {
    ...detail,
    status: "ReadyForClose",
    gates: readyGates,
    brokerIntakeState: "Complete",
    securityMasterState: "Complete",
    ledgerPostingState: "Complete",
    reconciliationState: "Complete",
    approvalState: "Approved",
    breakCases: detail.breakCases.map((breakCase) => ({
      ...breakCase,
      status: "Resolved"
    })),
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
    closePackage: null,
    dashboardSummary: detail.dashboardSummary
      ? {
          ...detail.dashboardSummary,
          stage: "Produce Evidence",
          status: "Ready",
          isReady: true,
          readyMetricCount: 6,
          totalMetricCount: 6,
          summary: "Financial Operations dashboard is ready to publish the retained close package.",
          metrics: detail.dashboardSummary.metrics.map((metric) => {
            if (metric.metricId === "approve-results") {
              return {
                ...metric,
                value: "Approved",
                status: "Ready",
                detail: "Workflow approval is complete and retained.",
                evidenceLinks: [reportPackEvidence],
                requiredActions: []
              };
            }

            if (metric.metricId === "produce-evidence") {
              return {
                ...metric,
                value: "Ready to publish",
                status: "Ready",
                detail: "Close readiness is clear and retained report-pack evidence is ready.",
                evidenceLinks: [reportPackEvidence],
                requiredActions: []
              };
            }

            if (metric.metricId === "close-support") {
              return {
                ...metric,
                value: "100% ready",
                status: "Ready",
                detail: "Close checklist, period lock, and retained evidence are ready for publication.",
                evidenceLinks: [reportPackEvidence],
                requiredActions: []
              };
            }

            return {
              ...metric,
              status: "Ready",
              requiredActions: []
            };
          }),
          evidenceLinks: [reportPackEvidence],
          requiredActions: []
        }
      : null
  };
}

function createClosedWorkflowDetail(closeEvidence: OperationsEvidenceLink): OperationsContinuityWorkflow {
  const readyDetail = createCloseReadyDetail(closeEvidence);

  return {
    ...readyDetail,
    status: "Closed",
    version: 5,
    timeline: [
      ...readyDetail.timeline,
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
        references: [closeEvidence],
        previousHash: "readyhash-202605",
        currentHash: "closehash-package-202605"
      }
    ],
    closePackage: {
      closePackageId: "close-package-2026-05",
      reportPackId: "report-pack-2026-05",
      retainedManifestId: "close-package-2026-05-manifest",
      retainedManifestRoute: requirePresent(closeEvidence.route, "closeEvidence.route"),
      evidenceHash: "b5f6c7d8e9a00112233445566778899aabbccddeeff00112233445566778899",
      publishedAtUtc: "2026-05-10T18:45:00Z",
      publishedBy: "fund-controller",
      signOffRationale: "Controller sign-off after report pack and checklist evidence were retained.",
      evidenceLinks: [closeEvidence],
      checklistControlApprovals: [
        {
          taskId: "close-gate-approval",
          approvedBy: "fund-controller",
          approvedAtUtc: "2026-05-10T17:45:00Z"
        }
      ]
    }
  };
}

function createPassedGate(
  gateKey: OperationsGate["gateKey"],
  displayName: string,
  completedAtUtc: string,
  completedBy: string
): OperationsGate {
  return {
    gateKey,
    displayName,
    status: "Passed",
    isRequired: true,
    description: `${displayName} gate passed.`,
    blockers: [],
    nextActions: [],
    completedAtUtc,
    completedBy
  };
}

function createAcknowledgedCloseTask(
  taskId: string,
  gate: OperationsGate["gateKey"],
  label: string,
  owner: string,
  requiredApprovalCount: number,
  acknowledgedAtUtc: string
): OperationsCloseChecklistTask {
  return {
    taskId,
    gate,
    label,
    owner,
    requiredEvidence: `${label} evidence is retained.`,
    dueDate: "2026-05-10",
    requiredApprovalCount,
    expiresOn: "2026-05-14",
    status: "Done",
    blockingReason: null,
    evidencePointer: `${taskId}-evidence`,
    remediationRoute: "/workstation/accounting/operations-continuity",
    canAcknowledge: false,
    acknowledgedAtUtc,
    acknowledgedBy: owner
  };
}
