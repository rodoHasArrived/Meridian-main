import { cleanup, renderHook } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { buildOperationsContinuityScreenViewModel } from "@/screens/operations-continuity-screen.view-model";
import { buildOperationsRecordReleaseViewModel } from "@/screens/operations-record-release-screen.view-model";
import { useReportingScreenViewModel } from "@/screens/reporting-screen.view-model";
import type {
  AccountingReportingSummary,
  DataWorkspaceResponse,
  OperationsContinuityWorkflow,
  OperationsContinuityWorkflowSummary,
  OperationsGate
} from "@/types";

afterEach(() => {
  cleanup();
});

const workflowId = "workflow-ops-record-2026-05";
const fundAccountId = "fund-account-1";

const readyGates: OperationsGate[] = [
  buildGate("BrokerIngest", "Broker intake"),
  buildGate("SecurityMaster", "Security Master"),
  buildGate("LedgerPosting", "Ledger posting"),
  buildGate("Reconciliation", "Reconciliation"),
  buildGate("Approval", "Approval")
];

const readySummary: OperationsContinuityWorkflowSummary = {
  workflowId,
  fundAccountId,
  periodId: "2026-05",
  securityMasterSnapshotId: "security-master-snapshot-1",
  brokerSource: "custodian",
  status: "Closed",
  version: 7,
  createdAtUtc: "2026-05-01T00:00:00Z",
  updatedAtUtc: "2026-05-08T15:30:00Z",
  gates: readyGates,
  nextActions: []
};

const readyDetail: OperationsContinuityWorkflow = {
  ...readySummary,
  brokerIntakeState: "Complete",
  securityMasterState: "Complete",
  ledgerPostingState: "Complete",
  reconciliationState: "Complete",
  approvalState: "Approved",
  timeline: [],
  breakCases: [],
  ledgerPreview: {
    previewId: "ledger-preview-1",
    status: "Posted",
    ledgerBatchId: "ledger-batch-1",
    generatedAtUtc: "2026-05-08T15:00:00Z",
    evidenceLinks: []
  },
  approvals: [],
  reportPackReadiness: {
    isReady: true,
    reportPackId: "report-pack-2026-05",
    blockingReason: null,
    evidenceLinks: []
  },
  closeChecklist: [],
  closeReadiness: null,
  closePackage: {
    closePackageId: "close-package-2026-05",
    reportPackId: "report-pack-2026-05",
    retainedManifestId: "manifest-2026-05",
    retainedManifestRoute: "/workstation/reporting/report-packs/report-pack-2026-05/manifest",
    evidenceHash: "sha256:ready",
    publishedAtUtc: "2026-05-08T15:30:00Z",
    publishedBy: "controller",
    signOffRationale: "Source, ledger, reconciliation, approval, and report-pack evidence reviewed.",
    evidenceLinks: [
      {
        evidenceId: "close-manifest",
        label: "Close manifest",
        route: "/workstation/reporting/report-packs/report-pack-2026-05/manifest",
        source: "operations-continuity",
        capturedAtUtc: "2026-05-08T15:30:00Z"
      }
    ],
    checklistControlApprovals: []
  },
  accountingRecordSummary: {
    recordId: "accounting-record-2026-05",
    isAuditReady: true,
    completeCategoryCount: 8,
    requiredCategoryCount: 8,
    summary: "Accounting record is complete and audit ready.",
    evidenceCategories: [
      buildEvidenceCategory("source-records", "Retained source records"),
      buildEvidenceCategory("normalized-activity", "Normalized activity"),
      buildEvidenceCategory("ledger-evidence", "Ledger evidence"),
      buildEvidenceCategory("report-pack", "Report pack")
    ],
    evidenceLinks: [],
    auditPackReadiness: {
      isComplete: true,
      generatedInSeconds: 2.4,
      slaTargetSeconds: 60,
      slaMet: true,
      missingEvidenceCategories: [],
      warnings: [],
      evidenceCategorySummaries: []
    }
  },
  evidenceLinks: [],
  blockers: []
};

const readyData: DataWorkspaceResponse = {
  metrics: [],
  providers: [
    {
      provider: "Custodian",
      status: "Healthy",
      capability: "Activity files",
      latency: "10ms",
      note: "Ready"
    }
  ],
  backfills: [],
  exports: [
    {
      exportId: "export-1",
      profile: "source-data",
      target: "accounting",
      status: "Ready",
      rows: "10k",
      updatedAt: "1m ago"
    }
  ]
};

const readyReporting: AccountingReportingSummary = {
  profileCount: 1,
  recommendedProfiles: ["excel"],
  profiles: [
    {
      id: "excel",
      name: "Excel",
      targetTool: "Excel",
      format: "Xlsx",
      description: "Board-ready workbook export.",
      loaderScript: true,
      dataDictionary: true
    }
  ],
  reportPackDistributions: [
    {
      distributionId: "board",
      recipient: "Board reporting committee",
      recipientRole: "Board",
      channel: "Board portal",
      state: "Ready",
      pendingItems: 0,
      pendingSummary: "No report-pack delivery items pending.",
      owner: "controller",
      dueAtUtc: null,
      lastSentAtUtc: "2026-05-08T15:45:00Z",
      route: "/reporting/report-packs?recipient=board"
    }
  ],
  summary: "1 governed report profile available.",
  templates: [
    {
      templateId: "board-pack",
      family: "BoardPack",
      name: "Board Pack",
      version: "1.0.0",
      sections: ["summary"],
      lifecycleStatus: "Approved"
    }
  ],
  recentRuns: [
    {
      runId: "board-pack-2026-05",
      templateId: "board-pack",
      family: "BoardPack",
      status: "Published",
      trigger: "Manual",
      asOfDate: "2026-05-31",
      attemptCount: 1,
      sectionCount: 1,
      lineageLinkedSections: 1,
      artifacts: [],
      auditActions: ["RunGenerated", "Published"],
      failureReason: null
    }
  ],
  workflowRecords: [
    {
      reportId: "report-pack-2026-05",
      fundProfileId: "fund-alpha",
      fundAccountId,
      period: "2026-05",
      templateId: { name: "board-pack", version: 1 },
      state: "Published",
      version: 1,
      createdAt: "2026-05-08T15:00:00Z",
      createdBy: "reporter",
      updatedAt: "2026-05-08T15:30:00Z",
      auditTrail: [],
      restatement: null,
      lineProvenance: [],
      publication: {
        manifestId: "manifest-2026-05",
        retainedManifestPath: "vault/report-packs/manifest-2026-05.json",
        evidenceHash: "sha256:ready",
        signedOffBy: "controller",
        signedOffAt: "2026-05-08T15:30:00Z",
        evidenceLinks: []
      }
    }
  ]
};

describe("operations record release view model", () => {
  it("builds one coherent ready path from source data to accounting record to report pack", () => {
    const continuity = buildOperationsContinuityScreenViewModel({
      workflows: [readySummary],
      selectedWorkflowId: workflowId,
      detail: readyDetail,
      loading: false,
      detailLoading: false,
      error: null,
      detailError: null,
      refresh: vi.fn(),
      selectWorkflow: vi.fn()
    });
    const reporting = renderHook(() => useReportingScreenViewModel(readyReporting, undefined, "/reporting/report-packs")).result.current;

    const vm = buildOperationsRecordReleaseViewModel({
      data: readyData,
      continuity,
      reporting
    });

    expect(vm.statusLabel).toBe("Release ready");
    expect(vm.rootAreaSummary).toBe("Root navigation remains Trading, Portfolio, Accounting, Reporting, Strategy, Data, and Settings.");
    expect(vm.steps.map((step) => [step.label, step.href, step.statusLabel])).toEqual([
      ["Source data", "/data/providers", "Providers ready"],
      ["Import and normalize", "/accounting/operations-continuity", "Passed"],
      ["Accounting record", "/reporting/evidence?subjectKind=accounting-record&subjectId=accounting-record-2026-05", "Audit ready"],
      ["Reconcile", "/accounting/reconciliation", "Passed"],
      ["Approve", "/accounting/approvals", "Passed"],
      ["Report pack", "/reporting/report-packs", "Publication ready"]
    ]);
    expect(vm.metrics.find((metric) => metric.id === "root-areas")).toMatchObject({
      value: "7",
      detail: "The release route is nested under Reporting, not exposed as a new root."
    });
    expect(vm.distributions).toHaveLength(1);
    expect(vm.templates).toHaveLength(1);
    expect(vm.recentRuns).toHaveLength(1);
  });

  it("fails closed when source, accounting-record, and report-pack payloads are absent", () => {
    const continuity = buildOperationsContinuityScreenViewModel({
      workflows: [],
      selectedWorkflowId: null,
      detail: null,
      loading: false,
      detailLoading: false,
      error: null,
      detailError: null,
      refresh: vi.fn(),
      selectWorkflow: vi.fn()
    });
    const reporting = renderHook(() => useReportingScreenViewModel(null, undefined, "/reporting/report-packs")).result.current;

    const vm = buildOperationsRecordReleaseViewModel({
      data: null,
      continuity,
      reporting
    });

    expect(vm.statusLabel).toBe("Release blocked");
    expect(vm.sourcePanel).toMatchObject({
      statusLabel: "Payload pending",
      tone: "blocked",
      rows: [],
      emptyText: "No source-data evidence is loaded."
    });
    expect(vm.accountingPanel).toMatchObject({
      statusLabel: "Not available",
      tone: "blocked",
      primaryHref: "/accounting/operations-continuity"
    });
    expect(vm.reportPackPanel).toMatchObject({
      statusLabel: "Recipients missing",
      tone: "blocked"
    });
    expect(vm.steps.map((step) => [step.label, step.tone])).toEqual([
      ["Source data", "blocked"],
      ["Import and normalize", "neutral"],
      ["Accounting record", "blocked"],
      ["Reconcile", "neutral"],
      ["Approve", "neutral"],
      ["Report pack", "blocked"]
    ]);
  });

  it("blocks release when source payload is missing even if downstream evidence is ready", () => {
    const continuity = buildOperationsContinuityScreenViewModel({
      workflows: [readySummary],
      selectedWorkflowId: workflowId,
      detail: readyDetail,
      loading: false,
      detailLoading: false,
      error: null,
      detailError: null,
      refresh: vi.fn(),
      selectWorkflow: vi.fn()
    });
    const reporting = renderHook(() => useReportingScreenViewModel(readyReporting, undefined, "/reporting/report-packs")).result.current;

    const vm = buildOperationsRecordReleaseViewModel({
      data: null,
      continuity,
      reporting
    });

    expect(vm.statusLabel).toBe("Release blocked");
    expect(vm.statusDetail).toBe("1 release step blocked; keep the demo path visible but do not call it closed.");
    expect(vm.sourcePanel).toMatchObject({
      statusLabel: "Payload pending",
      tone: "blocked"
    });
    expect(vm.steps.map((step) => [step.id, step.tone])).toEqual([
      ["source-data", "blocked"],
      ["broker-intake", "ready"],
      ["ledger", "ready"],
      ["reconcile", "ready"],
      ["approve", "ready"],
      ["report", "ready"]
    ]);
  });
});

function buildGate(gateKey: OperationsGate["gateKey"], displayName: string): OperationsGate {
  return {
    gateKey,
    displayName,
    status: "Passed",
    isRequired: true,
    description: `${displayName} evidence is complete.`,
    blockers: [],
    nextActions: [],
    completedAtUtc: "2026-05-08T15:00:00Z",
    completedBy: "controller"
  };
}

function buildEvidenceCategory(key: string, label: string) {
  return {
    key,
    label,
    isComplete: true,
    status: "Complete",
    routeHint: "/workstation/reporting/evidence",
    evidenceLinks: [
      {
        evidenceId: `${key}-evidence`,
        label: `${label} evidence`,
        route: "/workstation/reporting/evidence",
        source: "test",
        capturedAtUtc: "2026-05-08T15:00:00Z"
      }
    ],
    requiredEvidence: [label]
  };
}
