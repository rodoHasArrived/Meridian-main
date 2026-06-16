import { describe, expect, it } from "vitest";
import { buildOperationsContinuityScreenViewModel } from "@/screens/operations-continuity-screen.view-model";
import { buildReconciliationBreakQueueState } from "@/screens/accounting-screen.view-model";
import { useReportingScreenViewModel } from "@/screens/reporting-screen.view-model";
import { renderHook } from "@testing-library/react";
import type {
  GovernanceReportingSummary,
  OperationsContinuityWorkflow,
  OperationsContinuityWorkflowSummary,
  OperationsGate,
  ReconciliationBreakQueueItem
} from "@/types";

const workflowId = "w4-close-2026-05";
const fundAccountId = "fund-account-alpha";

type W4AcceptanceEvidenceCategory =
  | "ReconciliationCasework"
  | "AccountingCloseChecklist"
  | "ReportingReviewApproval"
  | "GovernedReportPackPublication"
  | "RestatementReadiness"
  | "EvidenceVaultManifestExportSupport";

type W4AcceptanceEvidenceRole = "Acceptance" | "Support";

interface W4AcceptanceEvidenceContract {
  readonly category: W4AcceptanceEvidenceCategory;
  readonly role: W4AcceptanceEvidenceRole;
  readonly evidenceId: string;
  readonly route: string;
}

const requiredW4AcceptanceCategories: readonly W4AcceptanceEvidenceCategory[] = [
  "ReconciliationCasework",
  "AccountingCloseChecklist",
  "ReportingReviewApproval",
  "GovernedReportPackPublication",
  "RestatementReadiness"
];

const requiredW4SupportCategories: readonly W4AcceptanceEvidenceCategory[] = [
  "EvidenceVaultManifestExportSupport"
];

const closedGates: OperationsGate[] = [
  {
    gateKey: "BrokerIngest",
    displayName: "Broker intake",
    status: "Passed",
    isRequired: true,
    description: "Broker and custodian statements are normalized.",
    blockers: [],
    nextActions: [],
    completedAtUtc: "2026-05-28T13:00:00Z",
    completedBy: "ops-controller"
  },
  {
    gateKey: "Reconciliation",
    displayName: "Reconciliation",
    status: "Passed",
    isRequired: true,
    description: "Open casework is resolved and signed off.",
    blockers: [],
    nextActions: [],
    completedAtUtc: "2026-05-28T14:00:00Z",
    completedBy: "fund-controller"
  },
  {
    gateKey: "Approval",
    displayName: "Approval",
    status: "Passed",
    isRequired: true,
    description: "Controller approval is retained for close and publication.",
    blockers: [],
    nextActions: [],
    completedAtUtc: "2026-05-28T15:00:00Z",
    completedBy: "fund-controller"
  }
];

const closeSummary: OperationsContinuityWorkflowSummary = {
  workflowId,
  fundAccountId,
  periodId: "2026-05",
  securityMasterSnapshotId: "security-snapshot-2026-05",
  brokerSource: "custodian",
  status: "ReadyForClose",
  version: 7,
  createdAtUtc: "2026-05-28T12:00:00Z",
  updatedAtUtc: "2026-05-28T15:10:00Z",
  gates: closedGates,
  nextActions: [
    {
      code: "OPEN_REPORT_PACK_APPROVAL",
      label: "Open report-pack approval",
      route: "/workstation/reporting/report-packs",
      gate: "Approval"
    }
  ]
};

const closeDetail: OperationsContinuityWorkflow = {
  ...closeSummary,
  brokerIntakeState: "Complete",
  securityMasterState: "Complete",
  ledgerPostingState: "Complete",
  reconciliationState: "Complete",
  approvalState: "Approved",
  timeline: [
    {
      auditId: "audit-close-approved",
      occurredAtUtc: "2026-05-28T15:10:00Z",
      workflowId,
      fundAccountId,
      periodId: "2026-05",
      eventType: "close-approval-approved",
      fromState: "ApprovalPending",
      toState: "ReadyForClose",
      gate: "Approval",
      fromGateStatus: "ReviewRequired",
      toGateStatus: "Passed",
      actor: "fund-controller",
      rationale: "Close checklist, reconciliation sign-off, and report pack evidence are retained.",
      correlationId: "w4-acceptance-close",
      references: [
        {
          evidenceId: "close-approval-evidence",
          label: "Close approval packet",
          route: "/workstation/reporting/report-packs/report-pack-2026-05",
          source: "operations-continuity",
          capturedAtUtc: "2026-05-28T15:10:00Z"
        }
      ],
      previousHash: "casehash-signed-off",
      currentHash: "closehash-approved-202605"
    }
  ],
  breakCases: [
    {
      breakId: "recon-break-42",
      checkId: "cash-balance-check",
      category: "CashBalance",
      severity: "Warning",
      status: "SignedOff",
      owner: "fund-controller",
      dueDate: "2026-05-28",
      expectedSource: "ledger",
      actualSource: "custodian",
      expectedAmount: 125000.25,
      actualAmount: 124998.25,
      variance: -2,
      securityId: null,
      symbol: null,
      suggestedAction: "Retain signed-off case evidence in the close package.",
      evidenceLinks: [
        {
          evidenceId: "case-signed-off-evidence",
          label: "Signed-off reconciliation case",
          route: "/workstation/accounting/reconciliation/recon-break-42/evidence",
          source: "reconciliation-casework",
          capturedAtUtc: "2026-05-28T14:30:00Z"
        }
      ]
    }
  ],
  ledgerPreview: {
    previewId: "ledger-preview-2026-05",
    status: "Validated",
    ledgerBatchId: "ledger-batch-2026-05",
    generatedAtUtc: "2026-05-28T13:15:00Z",
    evidenceLinks: []
  },
  approvals: [
    {
      approvalId: "approval-close-2026-05",
      status: "Approved",
      operator: "ops-controller",
      reviewer: "fund-controller",
      rationale: "Approved after reconciliation casework and report-pack evidence review.",
      submittedAtUtc: "2026-05-28T14:45:00Z",
      decidedAtUtc: "2026-05-28T15:05:00Z",
      evidenceLinks: [
        {
          evidenceId: "approval-evidence-1",
          label: "Controller sign-off",
          route: "/workstation/accounting/approvals/approval-close-2026-05",
          source: "ops-continuity",
          capturedAtUtc: "2026-05-28T15:05:00Z"
        }
      ]
    }
  ],
  reportPackReadiness: {
    isReady: true,
    reportPackId: "report-pack-2026-05",
    blockingReason: null,
    evidenceLinks: [
      {
        evidenceId: "report-pack-ready-evidence",
        label: "Approved report pack manifest",
        route: "/workstation/reporting/report-packs/report-pack-2026-05/manifest",
        source: "reporting",
        capturedAtUtc: "2026-05-28T15:00:00Z"
      }
    ]
  },
  closeChecklist: [
    {
      taskId: "close-gate-reconciliation",
      gate: "Reconciliation",
      label: "Reconciliation casework signed off",
      owner: "fund-controller",
      requiredEvidence: "Signed-off reconciliation case and retained custodian statement evidence.",
      dueDate: "2026-05-28",
      requiredApprovalCount: 1,
      expiresOn: "2026-05-31",
      status: "Complete",
      blockingReason: null,
      evidencePointer: "case-signed-off-evidence",
      remediationRoute: "/workstation/accounting/reconciliation/recon-break-42",
      canAcknowledge: false,
      acknowledgedAtUtc: "2026-05-28T14:30:00Z",
      acknowledgedBy: "fund-controller"
    },
    {
      taskId: "close-gate-report-pack",
      gate: "Approval",
      label: "Report pack approved for publication",
      owner: "fund-controller",
      requiredEvidence: "Approved report-pack manifest retained for close publication.",
      dueDate: "2026-05-28",
      requiredApprovalCount: 1,
      expiresOn: "2026-05-31",
      status: "Complete",
      blockingReason: null,
      evidencePointer: "report-pack-ready-evidence",
      remediationRoute: "/workstation/reporting/report-packs",
      canAcknowledge: false,
      acknowledgedAtUtc: "2026-05-28T15:00:00Z",
      acknowledgedBy: "fund-controller"
    }
  ],
  closeReadiness: {
    isReadyToClose: true,
    severity: "Ready",
    score: 100,
    components: [],
    blockers: [],
    nextActions: []
  },
  closePackage: {
    closePackageId: "close-package-2026-05",
    reportPackId: "report-pack-2026-05",
    retainedManifestId: "close-package-2026-05-manifest",
    retainedManifestRoute: "/workstation/accounting/operations-continuity/w4-close-2026-05/close-package/close-package-2026-05-manifest",
    evidenceHash: "b5f6c7d8e9a00112233445566778899aabbccddeeff00112233445566778899",
    publishedAtUtc: "2026-05-28T15:10:00Z",
    publishedBy: "fund-controller",
    signOffRationale: "Close package published after reconciliation casework, report pack approval, and checklist control approvals.",
    evidenceLinks: [
      {
        evidenceId: "close-package-manifest",
        label: "Retained close package manifest",
        route: "/workstation/accounting/operations-continuity/w4-close-2026-05/close-package/close-package-2026-05-manifest",
        source: "operations-continuity",
        capturedAtUtc: "2026-05-28T15:10:00Z"
      }
    ],
    checklistControlApprovals: [
      {
        taskId: "close-gate-reconciliation",
        approvedBy: "fund-controller",
        approvedAtUtc: "2026-05-28T14:30:00Z"
      },
      {
        taskId: "close-gate-report-pack",
        approvedBy: "fund-controller",
        approvedAtUtc: "2026-05-28T15:00:00Z"
      }
    ]
  },
  evidenceLinks: [
    {
      evidenceId: "close-workflow-snapshot",
      label: "Close workflow snapshot",
      route: "/workstation/accounting/operations-continuity/w4-close-2026-05",
      source: "ops-continuity",
      capturedAtUtc: "2026-05-28T15:10:00Z"
    }
  ],
  blockers: []
};

const signedOffBreak: ReconciliationBreakQueueItem = {
  breakId: "recon-break-42",
  runId: "run-close-2026-05",
  strategyName: "May close reconciliation",
  category: "CashBalance",
  status: "SignedOff",
  variance: -2,
  reason: "Custodian cash variance accepted after ledger adjustment evidence was attached.",
  assignedTo: "fund-controller",
  detectedAt: "2026-05-28T13:30:00Z",
  lastUpdatedAt: "2026-05-28T14:35:00Z",
  reviewedBy: "fund-controller",
  reviewedAt: "2026-05-28T14:00:00Z",
  resolvedBy: "fund-controller",
  resolvedAt: "2026-05-28T14:30:00Z",
  resolutionNote: "Accepted after custodian statement matched the ledger adjustment.",
  exceptionRoute: "fund-ops-review",
  toleranceProfileId: "cash-close-policy",
  toleranceBand: 5,
  requiredSignoffRole: "Fund controller",
  signoffStatus: "signed-off",
  fundAccountId,
  explainabilitySummary: "Casework accepted a small custodian cash variance with retained statement evidence.",
  routingTarget: "OperationsClose",
  routingDetail: "Close checklist handoff",
  recommendedAction: "Continue to report-pack approval.",
  assigneeId: "fund-controller",
  assigneeDisplayName: "Fund Controller",
  priority: "High",
  slaPolicyId: "monthly-close",
  slaDueAt: "2026-05-28T18:00:00Z",
  slaWarningAt: "2026-05-28T16:00:00Z",
  slaBreachedAt: null,
  slaState: "OnTrack",
  ageBand: "same-day",
  businessAgeHours: 1,
  rootCauseCode: "CUSTODIAN_TIMING",
  resolutionCode: "ACCEPTED_WITH_EVIDENCE",
  signedOffBy: "fund-controller",
  signedOffAt: "2026-05-28T14:35:00Z",
  signOffNote: "Signed off for close package.",
  reopenedBy: null,
  reopenedAt: null,
  reopenReason: null,
  version: 4,
  comments: null,
  evidenceLinks: ["case-signed-off-evidence", "custodian-statement-hash"],
  commentCount: 2,
  evidenceCount: 2,
  lastActivityAt: "2026-05-28T14:35:00Z",
  sourceType: "StatementRun",
  sourceSystem: "custodian",
  sourceReference: "statement-row-42",
  sourceImportId: "import-2026-05",
  sourceBreakId: "cash-balance-check",
  sourceFingerprint: "statement-hash:cash-close",
  lastCommentExcerpt: "Statement and ledger adjustment evidence retained.",
  relatedCaseCount: 0,
  slaBadgeLabel: "On track",
  slaBadgeTone: "success"
};

const reporting: GovernanceReportingSummary = {
  profileCount: 1,
  recommendedProfiles: ["board-pack"],
  profiles: [
    {
      id: "board-pack",
      name: "Board Pack",
      targetTool: "Investor portal",
      format: "Pdf",
      description: "Governed board and investor report pack.",
      loaderScript: true,
      dataDictionary: true
    }
  ],
  reportPackTargets: ["board", "audit", "investor-portal"],
  summary: "Report-pack lifecycle is ready for approval and publication.",
  templates: [
    {
      templateId: "monthly-board-pack",
      family: "BoardPack",
      name: "Monthly Board Pack",
      version: "2.0.0",
      sections: ["nav", "performance", "reconciliation"]
    }
  ],
  recentRuns: [
    {
      runId: "report-run-review-2026-05",
      templateId: "monthly-board-pack",
      family: "BoardPack",
      status: "InReview",
      trigger: "CloseWorkflow",
      attemptCount: 1,
      sectionCount: 3,
      lineageLinkedSections: 3,
      artifacts: ["manifest.json"],
      auditActions: ["RunGenerated", "ReviewStarted"],
      failureReason: null
    },
    {
      runId: "report-run-published-2026-05",
      templateId: "monthly-board-pack",
      family: "BoardPack",
      status: "Published",
      trigger: "CloseWorkflow",
      attemptCount: 2,
      sectionCount: 3,
      lineageLinkedSections: 3,
      artifacts: ["manifest.json", "board-pack.pdf"],
      auditActions: ["ReviewCompleted", "ApprovalTransition", "Published"],
      failureReason: null
    }
  ],
  workflowRecords: [
    {
      reportId: "report-published-2026-05",
      fundProfileId: "fund-alpha",
      fundAccountId,
      period: "2026-05",
      templateId: { name: "monthly-board-pack", version: 2 },
      state: "Published",
      version: 1,
      createdAt: "2026-05-28T14:45:00Z",
      createdBy: "reporter",
      updatedAt: "2026-05-28T15:20:00Z",
      auditTrail: [
        {
          at: "2026-05-28T15:10:00Z",
          actor: "fund-controller",
          action: "approved",
          fromState: "InReview",
          toState: "Approved",
          note: "Close evidence reviewed."
        },
        {
          at: "2026-05-28T15:20:00Z",
          actor: "reporting-ops",
          action: "published",
          fromState: "Approved",
          toState: "Published",
          note: "Published to investor portal."
        }
      ],
      restatement: null,
      lineProvenance: [
        {
          lineKey: "nav.total",
          sourceKind: "ledger",
          sourceId: "ledger-batch-2026-05",
          evidenceId: "close-workflow-snapshot",
          runId: "run-close-2026-05",
          ledgerEntryId: "ledger-entry-nav",
          reconciliationCaseId: "recon-break-42",
          reportValue: "125000.25",
          sourceSessionId: null,
          reconciliationRunId: "recon-run-2026-05"
        }
      ],
      publication: {
        manifestId: "manifest-report-published-2026-05",
        retainedManifestPath: "/workstation/reporting/report-packs/report-published-2026-05/manifest",
        evidenceHash: "sha256:report-published-2026-05",
        signedOffBy: "reporting-ops",
        signedOffAt: "2026-05-28T15:20:00Z",
        evidenceLinks: [
          {
            evidenceId: "publication-evidence",
            label: "Publication manifest",
            route: "/workstation/reporting/report-packs/report-published-2026-05/manifest",
            source: "reporting",
            capturedAtUtc: "2026-05-28T15:20:00Z"
          }
        ]
      }
    }
  ]
};

const lifecycleW4AcceptanceEvidence: readonly W4AcceptanceEvidenceContract[] = [
  {
    category: "ReconciliationCasework",
    role: "Acceptance",
    evidenceId: "case-signed-off-evidence",
    route: "/accounting/reconciliation/recon-break-42/evidence"
  },
  {
    category: "AccountingCloseChecklist",
    role: "Acceptance",
    evidenceId: "close-package-manifest",
    route: "/accounting/operations-continuity/w4-close-2026-05/close-package/close-package-2026-05-manifest"
  },
  {
    category: "ReportingReviewApproval",
    role: "Acceptance",
    evidenceId: "approval-evidence-1",
    route: "/accounting/approvals/approval-close-2026-05"
  },
  {
    category: "GovernedReportPackPublication",
    role: "Acceptance",
    evidenceId: "publication-evidence",
    route: "/reporting/report-packs/report-published-2026-05/manifest"
  },
  {
    category: "RestatementReadiness",
    role: "Acceptance",
    evidenceId: "restatement-review-evidence",
    route: "/reporting/report-packs/report-run-review-2026-05/restatement"
  },
  {
    category: "EvidenceVaultManifestExportSupport",
    role: "Support",
    evidenceId: "report-pack-ready-evidence",
    route: "/reporting/report-packs/report-pack-2026-05/manifest"
  }
];

describe("W4 acceptance browser parity", () => {
  it("blocks W4 Done evidence unless close checklist, signed reconciliation casework, and report-pack readiness hand off as one lifecycle", () => {
    const closeVm = buildOperationsContinuityScreenViewModel({
      workflows: [closeSummary],
      selectedWorkflowId: workflowId,
      detail: closeDetail,
      loading: false,
      detailLoading: false,
      error: null,
      detailError: null,
      refresh: async () => undefined,
      selectWorkflow: () => undefined
    });

    expect(closeVm.selectedDetail?.metadata).toEqual(expect.arrayContaining([
      { label: "Reconciliation", value: "Complete" },
      { label: "Approval", value: "Approved" },
      { label: "Report pack", value: "Ready: report-pack-2026-05" },
      { label: "Break cases", value: "1" },
      { label: "Close evidence", value: "4 close evidence links" }
    ]));
    expect(closeVm.checklist.map((item) => item.label)).toEqual([
      "Reconciliation casework signed off",
      "Report pack approved for publication"
    ]);
    expect(closeVm.checklist).toEqual(expect.arrayContaining([
      expect.objectContaining({ approvalLabel: "1 control approval required", evidenceLabel: "case-signed-off-evidence", remediationHref: "/accounting/reconciliation/recon-break-42" }),
      expect.objectContaining({ approvalLabel: "1 control approval required", evidenceLabel: "report-pack-ready-evidence", remediationHref: "/reporting/report-packs" })
    ]));
    expect(closeVm.nextAction).toMatchObject({
      title: "Open report-pack approval",
      href: "/reporting/report-packs",
      disabled: false
    });

    const acceptanceCategories = lifecycleW4AcceptanceEvidence
      .filter((item) => item.role === "Acceptance")
      .map((item) => item.category);
    const supportCategories = lifecycleW4AcceptanceEvidence
      .filter((item) => item.role === "Support")
      .map((item) => item.category);
    expect(acceptanceCategories).toEqual(requiredW4AcceptanceCategories);
    expect(supportCategories).toEqual(requiredW4SupportCategories);
    expect(lifecycleW4AcceptanceEvidence.map((item) => item.evidenceId)).toEqual([
      "case-signed-off-evidence",
      "close-package-manifest",
      "approval-evidence-1",
      "publication-evidence",
      "restatement-review-evidence",
      "report-pack-ready-evidence"
    ]);
    expect(lifecycleW4AcceptanceEvidence.every((item) => item.route.startsWith("/"))).toBe(true);

    const reconciliationVm = buildReconciliationBreakQueueState({
      breakQueue: [signedOffBreak],
      selectedBreakId: signedOffBreak.breakId,
      loading: false,
      loadError: null,
      action: null,
      actionError: null
    });

    expect(reconciliationVm.selectedDetail?.fields).toEqual(expect.arrayContaining([
      { label: "Required sign-off", value: "Sign-off: signed-off by Fund controller." },
      { label: "Decision note", value: "Accepted after custodian statement matched the ledger adjustment." },
      { label: "Evidence links", value: "2 evidence link(s)" },
      { label: "Resolution code", value: "ACCEPTED_WITH_EVIDENCE" },
      { label: "Routing", value: "OperationsClose" }
    ]));
    expect(reconciliationVm.selectedDetail?.routingActionHref).toBe("/accounting/operations-continuity");
    expect(reconciliationVm.selectedDetail?.analysisText).toContain("retained statement evidence");

    const { result } = renderHook(() => useReportingScreenViewModel(reporting, undefined, "/reporting/report-packs"));

    expect(result.current.workflowTaskPanel).toMatchObject({
      statusLabel: "Approval-ready",
      selectedProfileId: "board-pack",
      selectedSummary: "Board Pack is selected for report-pack approval using Pdf output to Investor portal."
    });
    expect(result.current.runStatusRows).toEqual(expect.arrayContaining([
      expect.objectContaining({ id: "report-run-review-2026-05", status: "InReview", lineageSummary: "3/3 sections linked", auditSummary: "RunGenerated → ReviewStarted" }),
      expect.objectContaining({ id: "report-run-published-2026-05", status: "Published", lineageSummary: "3/3 sections linked", auditSummary: "ReviewCompleted → ApprovalTransition → Published" })
    ]));
    expect(result.current.workflowTaskPanel?.restatementReview.fields).toEqual(expect.arrayContaining([
      expect.objectContaining({ label: "Workflow records", value: "1" }),
      expect.objectContaining({ label: "Publication", value: "No publication" })
    ]));
    expect(result.current.workflowTaskPanel?.actions.find((action) => action.id === "run")?.isDisabled).toBe(false);
    expect(result.current.workbenchActions[0]).toMatchObject({ href: "/reporting/evidence?subjectKind=report-pack&subjectId=board-pack" });
  });
});
