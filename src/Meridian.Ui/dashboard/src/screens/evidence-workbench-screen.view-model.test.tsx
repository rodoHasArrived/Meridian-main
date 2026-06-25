import { act, cleanup, render, renderHook, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import { ApiError } from "@/lib/api-errors";
import {
  exportEvidenceManifest,
  getEvidencePacket,
  getEvidenceSubjects,
  intakeEvidenceVaultDocument,
  listEvidenceVaultDocuments,
  listEvidenceVaultRequestLists,
  reviewEvidenceVaultDocument,
  validateEvidencePacket
} from "@/lib/api";
import { EvidenceWorkbenchScreen } from "@/screens/evidence-workbench-screen";
import {
  buildEvidenceLineageDetail,
  buildEvidenceLineagePanel,
  buildEvidenceNodeDetail,
  buildEvidenceProofChainPanel,
  buildEvidenceWorkbenchViewModel,
  groupNodes,
  mapStatusTone,
  useEvidenceWorkbenchViewModel
} from "@/screens/evidence-workbench-screen.view-model";
import type {
  EvidenceCompleteness,
  EvidenceNode,
  EvidencePacket,
  EvidencePacketExportResponse,
  EvidenceSubject,
  EvidenceVaultDocumentEntry,
  EvidenceVaultIntakeResponse,
  EvidenceVaultDocumentReviewResponse,
  EvidenceVaultRequestListEntry,
  WorkflowAction
} from "@/types";

vi.mock("@/lib/api", () => ({
  getEvidenceSubjects: vi.fn(),
  getEvidencePacket: vi.fn(),
  validateEvidencePacket: vi.fn(),
  exportEvidenceManifest: vi.fn(),
  intakeEvidenceVaultDocument: vi.fn(),
  listEvidenceVaultDocuments: vi.fn(),
  listEvidenceVaultRequestLists: vi.fn(),
  reviewEvidenceVaultDocument: vi.fn()
}));

beforeEach(() => {
  vi.clearAllMocks();
  vi.mocked(listEvidenceVaultRequestLists).mockResolvedValue([]);
  vi.mocked(listEvidenceVaultDocuments).mockResolvedValue([]);
  vi.mocked(intakeEvidenceVaultDocument).mockResolvedValue(defaultIntakeResponse);
  vi.mocked(reviewEvidenceVaultDocument).mockResolvedValue(defaultReviewResponse);
});

afterEach(() => {
  cleanup();
});

const subject: EvidenceSubject = {
  subjectId: "run-1",
  subjectKind: "strategy-run",
  label: "Momentum strategy run",
  workspace: "Strategy",
  route: "/strategy?runId=run-1",
  pageTag: "StrategyRuns"
};

const readyNode: EvidenceNode = {
  evidenceId: "strategy-run:run-1:detail",
  subject,
  kind: "strategy-run-detail",
  status: "Ready",
  freshness: { asOf: "2026-05-09T12:00:00Z", isStale: false, reason: null },
  sourceSystem: "StrategyRunReadService",
  summary: "Run detail is available.",
  artifactRefs: [
    {
      artifactId: "artifact-run-detail",
      kind: "json",
      path: "artifacts/evidence/strategy-run/run-1/detail.json",
      route: null,
      generatedAt: "2026-05-09T12:01:00Z",
      hash: "sha256:run-detail",
      retained: true,
      canonicalSubjectKind: "run",
      canonicalSubjectId: "run-1"
    }
  ],
  relatedWorkItemIds: []
};

const staleNode: EvidenceNode = {
  evidenceId: "strategy-run:run-1:replay",
  subject,
  kind: "paper-replay",
  status: "Stale",
  freshness: { asOf: "2026-04-30T12:00:00Z", isStale: true, reason: "Evidence is older than seven days." },
  sourceSystem: "TradingOperatorReadinessService",
  summary: "Replay verification is stale.",
  artifactRefs: [],
  relatedWorkItemIds: []
};

const blockedNode: EvidenceNode = {
  evidenceId: "strategy-run:run-1:provider-trust",
  subject,
  kind: "provider-trust",
  status: "ReviewRequired",
  freshness: { asOf: "2026-05-09T12:00:00Z", isStale: false, reason: null },
  sourceSystem: "Dk1TrustGateReadinessService",
  summary: "Provider sample review is pending.",
  artifactRefs: [],
  relatedWorkItemIds: ["provider-trust:sample-review", "provider-trust:sample-review"]
};

const completeness: EvidenceCompleteness = {
  score: 33,
  status: "Blocked",
  requiredIds: [readyNode.evidenceId, staleNode.evidenceId, blockedNode.evidenceId, "strategy-run:run-1:ledger"],
  readyIds: [readyNode.evidenceId],
  missingIds: ["strategy-run:run-1:ledger"],
  staleIds: [staleNode.evidenceId],
  blockingWorkItemIds: ["provider-trust:sample-review"],
  validationIssues: [
    {
      code: "orphan-evidence",
      severity: "Critical",
      message: "Evidence node 'strategy-run:run-1:unlinked-approval' is not linked into the packet graph.",
      evidenceId: "strategy-run:run-1:unlinked-approval",
      evidenceKind: "approval",
      sourceSystem: "OperationsContinuityWorkflowService"
    },
    {
      code: "evidence-sla-breached",
      severity: "Warning",
      message: "Replay evidence is outside the replay freshness window.",
      evidenceId: staleNode.evidenceId,
      evidenceKind: staleNode.kind,
      sourceSystem: staleNode.sourceSystem
    }
  ],
  blockingIssueCount: 1,
  warningIssueCount: 1,
  orphanEvidenceIds: ["strategy-run:run-1:unlinked-approval"],
  slaPolicies: [
    {
      policyId: "replay-check-freshness",
      evidenceKind: "paper-replay",
      workflowKind: "paper-readiness",
      freshnessMinutes: 10080,
      breachSeverity: "Warning",
      requiredForAssurance: true,
      description: "Replay checks must be fresh for assurance."
    }
  ],
  slaAssessments: [
    {
      policyId: "replay-check-freshness",
      evidenceId: staleNode.evidenceId,
      evidenceKind: staleNode.kind,
      sourceSystem: staleNode.sourceSystem,
      ageMinutes: 12960,
      freshnessMinutes: 10080,
      isBreached: true,
      severity: "Warning",
      message: "Replay evidence is outside the replay freshness window."
    }
  ],
  assuranceScore: {
    score: 45,
    status: "Stale",
    components: [
      {
        componentId: "replay",
        label: "Replay freshness",
        score: 40,
        status: "Stale",
        detail: "Paper replay evidence is stale."
      },
      {
        componentId: "ledger",
        label: "Ledger evidence",
        score: 0,
        status: "Missing",
        detail: "Ledger evidence is missing."
      }
    ],
    slaAssessments: [
      {
        policyId: "replay-check-freshness",
        evidenceId: staleNode.evidenceId,
        evidenceKind: staleNode.kind,
        sourceSystem: staleNode.sourceSystem,
        ageMinutes: 12960,
        freshnessMinutes: 10080,
        isBreached: true,
        severity: "Warning",
        message: "Replay evidence is outside the replay freshness window."
      }
    ]
  }
};

const vaultRequestListEntry: EvidenceVaultRequestListEntry = {
  requestListId: "request-list:auditrequestlist:audit:close-2026-05",
  requestListKind: "AuditRequestList",
  requestListKindCode: "Audit",
  targetKind: "audit",
  targetId: "close-2026-05",
  highestSeverity: "Critical",
  status: "Open",
  requestCount: 2,
  openRequestCount: 2,
  requestIds: [
    "support-request:missingevidence:audit-support",
    "support-request:blockedworkitem:audit-request"
  ],
  evidenceKinds: ["audit-history", "source-document"],
  blockedOutputs: ["report-pack/close-2026-05"],
  summary: "audit/close-2026-05 has 2 frozen requests; 2 open requests remain.",
  vaultId: "ev-request-list-demo",
  subjectKind: subject.subjectKind,
  subjectId: subject.subjectId,
  manifestRoute: "/workstation/evidence/strategy-run/run-1/manifest.json",
  retainedAt: "2026-05-09T12:35:00Z",
  supportRequests: [
    {
      requestId: "support-request:missingevidence:audit-support",
      requestKind: "MissingEvidence",
      evidenceId: "audit-support",
      evidenceKind: "audit-history",
      severity: "Critical",
      status: "Open",
      summary: "Audit support package is missing.",
      sourceSystem: "test",
      workItemId: "audit-request:close-2026-05",
      blockedOutput: "report-pack/close-2026-05"
    },
    {
      requestId: "support-request:blockedworkitem:audit-request",
      requestKind: "BlockedWorkItem",
      evidenceId: "source-node",
      evidenceKind: "source-document",
      severity: "Warning",
      status: "Open",
      summary: "Audit request work item blocks close publication.",
      sourceSystem: "test",
      workItemId: "audit-request:close-2026-05",
      blockedOutput: "report-pack/close-2026-05"
    }
  ]
};

const vaultDocumentEntry: EvidenceVaultDocumentEntry = {
  document: {
    documentId: "doc:ev-request-list-demo",
    fileName: "operating-bank-statement.csv",
    classification: "BankEvidence",
    sourceHashSha256: "d".repeat(64),
    receivedAt: "2026-05-09T13:00:00Z",
    sourceChannel: "upload",
    sourceRecord: {
      sourceHashSha256: "d".repeat(64),
      receivedAt: "2026-05-09T13:00:00Z",
      sourceChannel: "upload",
      channelKind: "Upload",
      actor: "fund-controller",
      tenantId: "tenant-alpha",
      scope: "fund-alpha",
      sourceSystem: "operator-upload",
      sourceReference: "file://operating-bank-statement.csv",
      receiptHash: "d".repeat(64)
    },
    actor: "fund-controller",
    tenantId: "tenant-alpha",
    scope: "fund-alpha",
    extractionStatus: "NeedsReview",
    extractedFields: [
      {
        fieldName: "endingCash",
        extractedValue: "1250000.00",
        expectedValue: "1250000.00",
        confidenceScore: 0.98,
        reviewState: "NeedsReview",
        validationStatus: "ReviewRequired",
        validationMessage: "Controller must confirm the bank ending cash.",
        linkedRecordKind: "close-task",
        linkedRecordId: "close-task:cash-support"
      }
    ],
    objectLinks: [
      {
        linkKind: "CloseTask",
        objectId: "close-task:cash-support",
        label: "Cash support",
        route: "/workstation/accounting/close/tasks/cash-support",
        relationship: "blocks-close-readiness"
      }
    ],
    reviewerState: {
      status: "NeedsReview",
      reviewer: "fund-controller",
      reviewedAt: null,
      notes: "Statement amount needs review."
    },
    auditTrail: [
      {
        recordedAt: "2026-05-09T13:00:00Z",
        actor: "fund-controller",
        action: "DocumentIntakeRetained",
        summary: "Retained BankEvidence document.",
        correlationId: "ev-request-list-demo"
      }
    ],
    contentType: "text/csv",
    sourceSystem: "operator-upload",
    sourceReference: "file://operating-bank-statement.csv",
    vaultId: "ev-request-list-demo",
    artifactId: "artifact-bank-statement",
    manifestRoute: "/workstation/evidence/strategy-run/run-1/manifest.json",
    extractorId: "manual-metadata-v1",
    authority: {
      canSupport: true,
      canBlock: true,
      canSuggest: true,
      canLink: true,
      canApprove: false,
      canPost: false,
      canCertify: false,
      canRelease: false,
      boundary: "Evidence documents can support, block, suggest, and link; they cannot approve, post, certify, or release."
    }
  },
  vaultId: "ev-request-list-demo",
  subjectKind: subject.subjectKind,
  subjectId: subject.subjectId,
  manifestRoute: "/workstation/evidence/strategy-run/run-1/manifest.json",
  retainedAt: "2026-05-09T13:00:00Z",
  storageKind: "file-bundle",
  openRequestCount: 2,
  supportRequests: vaultRequestListEntry.supportRequests
};

const acceptedVaultDocumentEntry: EvidenceVaultDocumentEntry = {
  ...vaultDocumentEntry,
  document: {
    ...vaultDocumentEntry.document,
    extractionStatus: "Accepted",
    reviewerState: {
      status: "Accepted",
      reviewer: "evidence-workbench-operator",
      reviewedAt: "2026-05-09T13:10:00Z",
      notes: "Evidence Workbench operator accepted this retained document."
    },
    auditTrail: [
      ...vaultDocumentEntry.document.auditTrail,
      {
        recordedAt: "2026-05-09T13:10:00Z",
        actor: "evidence-workbench-operator",
        action: "DocumentReviewRecorded",
        summary: "Document review state set to Accepted.",
        correlationId: "evidence-workbench:ev-request-list-demo:doc:ev-request-list-demo:Accepted"
      }
    ]
  }
};

const defaultReviewResponse: EvidenceVaultDocumentReviewResponse = {
  entry: acceptedVaultDocumentEntry,
  auditEvent: acceptedVaultDocumentEntry.document.auditTrail[1]
};

const defaultIntakeResponse: EvidenceVaultIntakeResponse = {
  intakeId: "intake:ev-uploaded-close-document",
  subjectKind: subject.subjectKind,
  subjectId: subject.subjectId,
  intakeChannel: "upload",
  fileName: "close-bank-evidence.csv",
  relativePath: "workstation/evidence/_vault/ev-uploaded-close-document/artifacts/close-bank-evidence.csv",
  contentHashSha256: "e".repeat(64),
  sizeBytes: 4,
  capturedAt: "2026-05-09T13:05:00Z",
  capture: {
    captureChannel: "upload",
    sourceSystem: "operator-upload",
    receivedAt: "2026-05-09T13:05:00Z",
    receivedBy: "fund-controller",
    sourceReference: "close-bank-evidence.csv",
    receiptHash: "e".repeat(64)
  },
  extractedFields: [],
  vaultIdentity: {
    vaultId: "ev-uploaded-close-document",
    subjectKind: subject.subjectKind,
    subjectId: subject.subjectId,
    manifestPath: "workstation/evidence/_vault/ev-uploaded-close-document/intake-manifest.json",
    manifestRoute: "/workstation/evidence/_vault/ev-uploaded-close-document/intake-manifest.json",
    retainedAt: "2026-05-09T13:05:00Z",
    contentHashSha256: "f".repeat(64),
    schemaVersion: 1,
    storageKind: "file-bundle",
    artifacts: [],
    supportRequests: [],
    documents: [],
    manifestSnapshot: null
  },
  document: {
    documentId: "doc:ev-uploaded-close-document",
    fileName: "close-bank-evidence.csv",
    classification: "BankEvidence",
    sourceHashSha256: "e".repeat(64),
    receivedAt: "2026-05-09T13:05:00Z",
    sourceChannel: "upload",
    actor: "fund-controller",
    tenantId: "tenant-alpha",
    scope: "fund-alpha",
    extractionStatus: "NeedsReview",
    objectLinks: [
      {
        linkKind: "CloseTask",
        objectId: "close-task:cash-support",
        label: "close-task:cash-support",
        relationship: "supports"
      }
    ],
    reviewerState: {
      status: "NeedsReview",
      reviewer: "fund-controller",
      reviewedAt: "2026-05-09T13:05:00Z",
      notes: "Manual Evidence Workbench intake."
    },
    auditTrail: []
  }
};

const evidenceActions: WorkflowAction[] = [
  {
    actionId: "workflow.evidence.open-packet",
    label: "Open Evidence Packet",
    detail: "Open the reusable evidence packet for the selected workflow subject.",
    targetPageTag: "EvidenceWorkbench",
    tone: "Primary",
    workItemKind: null,
    routePrefixes: ["/api/workstation/evidence"],
    routeContains: [],
    aliases: []
  },
  {
    actionId: "workflow.evidence.validate",
    label: "Validate Evidence",
    detail: "Validate evidence completeness without mutating source workflows.",
    targetPageTag: "EvidenceWorkbench",
    tone: "Warning",
    workItemKind: null,
    routePrefixes: ["/api/workstation/evidence"],
    routeContains: [],
    aliases: []
  },
  {
    actionId: "workflow.evidence.export-manifest",
    label: "Export Evidence Manifest",
    detail: "Write a manifest-only evidence export for audit review.",
    targetPageTag: "EvidenceWorkbench",
    tone: "Primary",
    workItemKind: null,
    routePrefixes: ["/api/workstation/evidence"],
    routeContains: [],
    aliases: []
  }
];

const packet: EvidencePacket = {
  subject,
  generatedAt: "2026-05-09T12:30:00Z",
  nodes: [readyNode, staleNode, blockedNode],
  edges: [
    {
      fromId: readyNode.evidenceId,
      toId: staleNode.evidenceId,
      relationship: "requires",
      reason: "Replay evidence supports the run."
    }
  ],
  completeness,
  actions: evidenceActions,
  warnings: ["DK1 sample review is pending."],
  proofChain: {
    coveragePercent: 44,
    status: "ReviewRequired",
    coveredLayerCount: 4,
    totalLayerCount: 9,
    summary: "4 of 9 v0.18 proof-chain layers have evidence; 1 blocked or missing, 1 stale, 1 review-required.",
    layers: [
      {
        layer: "Source",
        label: "Source",
        status: "Ready",
        coveragePercent: 100,
        requiredEvidenceIds: [readyNode.evidenceId],
        evidenceIds: [readyNode.evidenceId],
        readyEvidenceIds: [readyNode.evidenceId],
        reviewEvidenceIds: [],
        missingEvidenceIds: [],
        evidenceKinds: [readyNode.kind],
        summary: "Source has 1 evidence node(s), 1 ready, 0 review-required or blocked, and 0 missing."
      },
      {
        layer: "Normalization",
        label: "Normalization",
        status: "Missing",
        coveragePercent: 0,
        requiredEvidenceIds: [],
        evidenceIds: [],
        readyEvidenceIds: [],
        reviewEvidenceIds: [],
        missingEvidenceIds: [],
        evidenceKinds: [],
        summary: "No Normalization evidence is present in this packet."
      },
      {
        layer: "Ledger",
        label: "Ledger",
        status: "Blocked",
        coveragePercent: 0,
        requiredEvidenceIds: ["strategy-run:run-1:ledger"],
        evidenceIds: [],
        readyEvidenceIds: [],
        reviewEvidenceIds: [],
        missingEvidenceIds: ["strategy-run:run-1:ledger"],
        evidenceKinds: [],
        summary: "No Ledger evidence is present in this packet."
      },
      {
        layer: "Close",
        label: "Close",
        status: "ReviewRequired",
        coveragePercent: 0,
        requiredEvidenceIds: [blockedNode.evidenceId],
        evidenceIds: [blockedNode.evidenceId],
        readyEvidenceIds: [],
        reviewEvidenceIds: [blockedNode.evidenceId],
        missingEvidenceIds: [],
        evidenceKinds: [blockedNode.kind],
        summary: "Close has 1 evidence node(s), 0 ready, 1 review-required or blocked, and 0 missing."
      }
    ]
  }
};

function createDeferred<T>() {
  let resolve!: (value: T) => void;
  let reject!: (reason?: unknown) => void;
  const promise = new Promise<T>((promiseResolve, promiseReject) => {
    resolve = promiseResolve;
    reject = promiseReject;
  });

  return { promise, resolve, reject };
}

describe("Evidence Workbench view model", () => {
  it("groups evidence by lifecycle stage and surfaces blocked packet gaps", () => {
    const vm = buildEvidenceWorkbenchViewModel({
      selectedSubjectKind: "strategy-run",
      selectedSubjectId: "run-1",
      loading: false,
      error: null,
      subjects: [subject],
      packet,
      requestLists: [vaultRequestListEntry],
      exportBusy: false,
      exportResult: null,
      validateBusy: false,
      validationResult: null,
      exportManifest: vi.fn(),
      validatePacket: vi.fn()
    });

    expect(vm.title).toBe("Momentum strategy run");
    expect(vm.scoreLabel).toBe("33% complete");
    expect(vm.statusTone).toBe("danger");
    expect(vm.assurancePanel).toMatchObject({
      scoreLabel: "45% assurance",
      statusLabel: "Stale",
      statusTone: "warning",
      summaryLabel: "2 components, 1 SLA breach",
      orphanSummaryLabel: "1 orphan node",
      orphanTone: "danger",
      noOrphanRuleLabel: "No-orphan rule breached",
      validationIssueLabel: "1 blocking, 1 warning"
    });
    expect(vm.assurancePanel.componentRows[0]).toMatchObject({
      id: "replay",
      label: "Replay freshness",
      scoreLabel: "40%",
      statusLabel: "Stale",
      statusTone: "warning",
      detail: "Paper replay evidence is stale."
    });
    expect(vm.assurancePanel.breachedSlaRows[0]).toMatchObject({
      policyLabel: "Replay Check Freshness",
      evidenceId: staleNode.evidenceId,
      ageLabel: "12960 min",
      freshnessLabel: "10080 min limit",
      severityLabel: "Warning",
      breached: true,
      tone: "warning",
      message: "Replay evidence is outside the replay freshness window."
    });
    expect(vm.proofChainPanel).toMatchObject({
      title: "Operational Evidence Graph",
      summaryLabel: "4 of 9 v0.18 proof-chain layers have evidence; 1 blocked or missing, 1 stale, 1 review-required.",
      statusLabel: "Review Required",
      statusTone: "warning",
      coverageLabel: "4/9 layers, 44% coverage",
      hasLayers: true
    });
    expect(vm.proofChainPanel.rows.map((row) => row.id)).toEqual(["Source", "Normalization", "Ledger", "Close"]);
    expect(vm.proofChainPanel.rows[2]).toMatchObject({
      label: "Ledger",
      statusLabel: "Blocked",
      statusTone: "danger",
      coverageLabel: "0%",
      readyLabel: "0 ready nodes",
      missingLabel: "1 missing node",
      kindsLabel: "No evidence kinds"
    });
    expect(vm.requestListPanel).toMatchObject({
      title: "Evidence Vault request lists",
      summaryLabel: "1 open request list",
      scopeLabel: "Strategy Run run-1",
      hasRows: true
    });
    expect(vm.requestListPanel.rows[0]).toMatchObject({
      requestListKindLabel: "Audit Request List",
      targetLabel: "Audit close-2026-05",
      highestSeverityLabel: "Critical",
      highestSeverityTone: "danger",
      statusLabel: "Open",
      requestCountLabel: "2 support requests",
      openRequestCountLabel: "2 open requests",
      evidenceKindsLabel: "Audit History, Source Document",
      blockedOutputsLabel: "report-pack/close-2026-05",
      subjectLabel: "Strategy Run run-1",
      vaultLabel: "ev-request-list-demo",
      manifestHref: "/workstation/evidence/strategy-run/run-1/manifest.json",
      retainedLabel: "Retained May 9, 12:35 UTC",
      supportRequestSummaryLabel: "2 support requests"
    });
    expect(vm.requestListPanel.rows[0]?.supportRequestRows[0]).toMatchObject({
      requestKindLabel: "Missing Evidence",
      evidenceLabel: "audit-support",
      severityLabel: "Critical",
      statusLabel: "Open",
      workItemLabel: "audit-request:close-2026-05"
    });
    expect(vm.generatedLabel).toBe("May 9, 12:30 UTC");
    expect(vm.missingEvidence).toEqual(["strategy-run:run-1:ledger"]);
    expect(vm.staleEvidence).toEqual([staleNode.evidenceId]);
    expect(vm.orphanEvidence).toEqual(["strategy-run:run-1:unlinked-approval"]);
    expect(vm.slaBreaches).toEqual(["Replay evidence is outside the replay freshness window."]);
    expect(vm.relatedWorkItemIds).toEqual(["provider-trust:sample-review"]);
    expect(vm.nodeGroups.map((group) => group.id)).toEqual(["run-lifecycle", "readiness", "provider-trust"]);
    expect(vm.nodeGroups[0]).toMatchObject({
      tableLabel: "Run Lifecycle evidence nodes",
      detailPanelId: "evidence-node-run-lifecycle-selected-detail",
      defaultSelectedNodeId: readyNode.evidenceId,
      summaryLabel: "1 node; select a row to inspect retained artifacts, freshness, and work items."
    });
    expect(vm.nodeGroups[0].rows[0]).toMatchObject({
      kindLabel: "Strategy Run Detail",
      statusLabel: "Ready",
      freshnessLabel: "Fresh as of May 9, 12:00 UTC",
      artifactCountLabel: "1 artifact",
      workItemCountLabel: "0 work items",
      selectAriaLabel: "Inspect evidence node Strategy Run Detail strategy-run:run-1:detail"
    });
    expect(vm.sourceWorkflowHref).toBe("/strategy?runId=run-1");
    expect(vm.packetActionsSummaryLabel).toBe("3 workflow actions");
    expect(vm.packetActions.map((action) => action.control)).toEqual(["link", "validate", "export"]);
    expect(vm.packetActions[0]).toMatchObject({
      href: "/reporting/evidence?subjectKind=strategy-run&subjectId=run-1",
      tone: "primary",
      targetLabel: "Evidence Workbench"
    });
    expect(vm.packetActions[1]).toMatchObject({
      commandLabel: "Validate",
      ariaLabel: "Validate evidence for Momentum strategy run",
      tone: "warning"
    });
    expect(vm.validateCommand).toMatchObject({
      label: "Validate",
      ariaLabel: "Validate selected evidence packet for Momentum strategy run",
      disabled: false,
      disabledReason: null
    });
    expect(vm.exportCommand).toMatchObject({
      label: "Export manifest",
      ariaLabel: "Export selected evidence manifest for Momentum strategy run",
      disabled: false,
      disabledReason: null
    });
    expect(vm.exportResultDetail).toBeNull();
    expect(vm.lineagePanel).toMatchObject({
      hasRows: true,
      defaultSelectedRowId: "strategy-run:run-1:detail:requires:strategy-run:run-1:replay:0",
      detailPanelId: "evidence-lineage-selected-edge-detail",
      summaryLabel: "1 edge",
      tableLabel: "Evidence lineage edges for Momentum strategy run"
    });
    expect(vm.lineagePanel.rows[0]).toMatchObject({
      relationshipLabel: "Requires",
      ariaLabel: "Requires from strategy-run:run-1:detail to strategy-run:run-1:replay. Replay evidence supports the run.",
      selectAriaLabel: "Inspect lineage edge: Requires from strategy-run:run-1:detail to strategy-run:run-1:replay"
    });
  });

  it("keeps older packets usable when proof-chain coverage is absent", () => {
    const panel = buildEvidenceProofChainPanel(null);

    expect(panel).toMatchObject({
      title: "Operational Evidence Graph",
      summaryLabel: "Proof-chain coverage was not returned by this packet.",
      statusLabel: "Unknown",
      statusTone: "muted",
      coverageLabel: "No proof-chain coverage",
      hasLayers: false,
      rows: []
    });
  });

  it("keeps loading and empty subject states explicit", () => {
    const loading = buildEvidenceWorkbenchViewModel({
      selectedSubjectKind: null,
      selectedSubjectId: null,
      loading: true,
      error: null,
      subjects: [],
      packet: null,
      exportBusy: false,
      exportResult: null,
      validateBusy: false,
      validationResult: null,
      exportManifest: vi.fn(),
      validatePacket: vi.fn()
    });

    expect(loading.loading).toBe(true);
    expect(loading.showSubjectPicker).toBe(true);
    expect(loading.hasSelection).toBe(false);
    expect(loading.hasSubjects).toBe(false);
    expect(loading.loadingLabel).toBe("Loading evidence subjects.");
    expect(loading.subjectsSummaryLabel).toBe("0 subjects");
    expect(loading.subjectEmptyTitle).toBe("No evidence subjects returned");
    expect(loading.subjectEmptyActionHref).toBe("/trading/readiness");
    expect(loading.title).toBe("Evidence Workbench");
    expect(loading.reloadCommand).toMatchObject({
      label: "Retrying",
      ariaLabel: "Retry loading evidence subjects",
      busy: true,
      disabled: true,
      disabledReason: "Evidence load is already running."
    });
    expect(loading.openSubjectHref(subject)).toBe("/reporting/evidence?subjectKind=strategy-run&subjectId=run-1");
  });

  it("keeps load failures recoverable without rendering them as empty evidence", () => {
    const vm = buildEvidenceWorkbenchViewModel({
      selectedSubjectKind: null,
      selectedSubjectId: null,
      loading: false,
      error: { summary: "Evidence API unavailable", details: [] },
      subjects: [],
      packet: null,
      exportBusy: false,
      exportResult: null,
      validateBusy: false,
      validationResult: null,
      exportManifest: vi.fn(),
      validatePacket: vi.fn()
    });

    expect(vm.showSubjectPicker).toBe(false);
    expect(vm.subjectEmptyTitle).toBe("No evidence subjects returned");
    expect(vm.reloadCommand).toMatchObject({
      label: "Retry",
      ariaLabel: "Retry loading evidence subjects",
      busy: false,
      disabled: false,
      disabledReason: null
    });
  });

  it("falls back to page-tag routing when evidence subjects omit a direct route", () => {
    const packetWithoutRoute: EvidencePacket = {
      ...packet,
      subject: {
        ...subject,
        route: null,
        pageTag: "FundReportPack",
        workspace: "Reporting"
      }
    };
    const vm = buildEvidenceWorkbenchViewModel({
      selectedSubjectKind: "report-pack",
      selectedSubjectId: "close-pack",
      loading: false,
      error: null,
      subjects: [packetWithoutRoute.subject],
      packet: packetWithoutRoute,
      exportBusy: false,
      exportResult: null,
      validateBusy: false,
      validationResult: null,
      exportManifest: vi.fn(),
      validatePacket: vi.fn()
    });

    expect(vm.sourceWorkflowHref).toBe("/reporting/report-packs");
    expect(vm.sourceWorkflowAriaLabel).toBe("Open source workflow for Momentum strategy run");
  });

  it("preserves parameterized accounting-record evidence targets without exposing route syntax", () => {
    const accountingRecordSubject: EvidenceSubject = {
      subjectId: "accounting-record-2026-05",
      subjectKind: "accounting-record",
      label: "May accounting record",
      workspace: "Accounting",
      route: null,
      pageTag: "EvidenceWorkbench:accounting-record/accounting-record-2026-05"
    };
    const accountingRecordPacket: EvidencePacket = {
      ...packet,
      subject: accountingRecordSubject,
      actions: [
        {
          actionId: "workflow.evidence.open-packet",
          label: "Open Accounting Record Evidence",
          detail: "Open the retained accounting-record evidence packet.",
          targetPageTag: "EvidenceWorkbench:accounting-record/accounting-record-2026-05",
          tone: "Primary",
          workItemKind: null,
          routePrefixes: ["/api/workstation/evidence/subjects/accounting-record/accounting-record-2026-05"],
          routeContains: [],
          aliases: []
        }
      ]
    };

    const vm = buildEvidenceWorkbenchViewModel({
      selectedSubjectKind: "accounting-record",
      selectedSubjectId: "accounting-record-2026-05",
      loading: false,
      error: null,
      subjects: [accountingRecordSubject],
      packet: accountingRecordPacket,
      exportBusy: false,
      exportResult: null,
      validateBusy: false,
      validationResult: null,
      exportManifest: vi.fn(),
      validatePacket: vi.fn()
    });

    const expectedHref = "/reporting/evidence?subjectKind=accounting-record&subjectId=accounting-record-2026-05";
    expect(vm.sourceWorkflowHref).toBe(expectedHref);
    expect(vm.packetActions[0]).toMatchObject({
      href: expectedHref,
      targetLabel: "Evidence Workbench"
    });
  });

  it("preserves report-pack delivery evidence targets with direct package routes", () => {
    const deliverySubject: EvidenceSubject = {
      subjectId: "11111111-1111-1111-1111-111111111111:22222222-2222-2222-2222-222222222222",
      subjectKind: "report-pack-delivery",
      label: "Report-pack delivery Board reporting committee 1",
      workspace: "Reporting",
      route: "/reporting/report-packs?reportId=11111111-1111-1111-1111-111111111111&deliveryAttemptId=22222222-2222-2222-2222-222222222222",
      pageTag: "EvidenceWorkbench"
    };
    const deliveryPacket: EvidencePacket = {
      ...packet,
      subject: deliverySubject,
      actions: [
        {
          actionId: "workflow.evidence.open-packet",
          label: "Open Delivery Evidence",
          detail: "Open retained delivery evidence for the selected report-pack package.",
          targetPageTag: "EvidenceWorkbench",
          tone: "Primary",
          workItemKind: null,
          routePrefixes: ["/api/workstation/evidence/subjects/report-pack-delivery"],
          routeContains: [],
          aliases: []
        }
      ]
    };

    const vm = buildEvidenceWorkbenchViewModel({
      selectedSubjectKind: "report-pack-delivery",
      selectedSubjectId: deliverySubject.subjectId,
      loading: false,
      error: null,
      subjects: [deliverySubject],
      packet: deliveryPacket,
      exportBusy: false,
      exportResult: null,
      validateBusy: false,
      validationResult: null,
      exportManifest: vi.fn(),
      validatePacket: vi.fn()
    });

    const expectedWorkbenchHref = "/reporting/evidence?subjectKind=report-pack-delivery&subjectId=11111111-1111-1111-1111-111111111111%3A22222222-2222-2222-2222-222222222222";
    expect(vm.sourceWorkflowHref).toBe(deliverySubject.route);
    expect(vm.openSubjectHref(deliverySubject)).toBe(expectedWorkbenchHref);
    expect(vm.packetActions[0]).toMatchObject({
      href: expectedWorkbenchHref,
      targetLabel: "Evidence Workbench"
    });
  });

  it("preserves operating scope in source-workflow, subject, and packet-action links", () => {
    const vm = buildEvidenceWorkbenchViewModel({
      selectedSubjectKind: "strategy-run",
      selectedSubjectId: "run-1",
      operatingScope: {
        label: "Operating scope",
        summary: "Subject: MSFT / Account: fund-1 / Run: run-1 / Provider: Alpaca / Window: 2026-05-01 to 2026-05-15",
        subjectSymbol: "MSFT",
        fundAccountId: "fund-1",
        runId: "run-1",
        provider: "Alpaca",
        hasScope: true,
        clearAriaLabel: "Clear operating scope",
        items: [],
        queryParams: [
          { key: "symbol", value: "MSFT", scopeKey: "symbol" },
          { key: "fundAccountId", value: "fund-1", scopeKey: "fundAccountId" },
          { key: "runId", value: "run-1", scopeKey: "runId" },
          { key: "provider", value: "Alpaca", scopeKey: "provider" },
          { key: "from", value: "2026-05-01", scopeKey: "window" },
          { key: "to", value: "2026-05-15", scopeKey: "window" }
        ]
      },
      loading: false,
      error: null,
      subjects: [subject],
      packet,
      exportBusy: false,
      exportResult: null,
      validateBusy: false,
      validationResult: null,
      exportManifest: vi.fn(),
      validatePacket: vi.fn()
    });

    expect(vm.sourceWorkflowHref).toBe("/strategy?runId=run-1&symbol=MSFT&provider=Alpaca&from=2026-05-01&to=2026-05-15");
    expect(vm.openSubjectHref(subject))
      .toBe("/reporting/evidence?subjectKind=strategy-run&subjectId=run-1&symbol=MSFT&fundAccountId=fund-1&runId=run-1&provider=Alpaca&from=2026-05-01&to=2026-05-15");
    expect(vm.packetActions[0]).toMatchObject({
      href: "/reporting/evidence?subjectKind=strategy-run&subjectId=run-1&symbol=MSFT&fundAccountId=fund-1&runId=run-1&provider=Alpaca&from=2026-05-01&to=2026-05-15"
    });
  });

  it("normalizes subject routes to canonical workstation paths before falling back to page tags", () => {
    const legacyRoutePacket: EvidencePacket = {
      ...packet,
      subject: {
        ...subject,
        route: "/workstation/governance/reconciliation?runId=run-1#break-7",
        pageTag: "FundReportPack",
        workspace: "Reporting"
      }
    };
    const unsafeRoutePacket: EvidencePacket = {
      ...packet,
      subject: {
        ...subject,
        route: "https://example.test/workstation/strategy",
        pageTag: "FundReportPack",
        workspace: "Reporting"
      }
    };
    const apiRoutePacket: EvidencePacket = {
      ...packet,
      subject: {
        ...subject,
        route: "/api/workstation/evidence/strategy-run/run-1",
        pageTag: "FundReportPack",
        workspace: "Reporting"
      }
    };

    const build = (nextPacket: EvidencePacket) => buildEvidenceWorkbenchViewModel({
      selectedSubjectKind: nextPacket.subject.subjectKind,
      selectedSubjectId: nextPacket.subject.subjectId,
      loading: false,
      error: null,
      subjects: [nextPacket.subject],
      packet: nextPacket,
      exportBusy: false,
      exportResult: null,
      validateBusy: false,
      validationResult: null,
      exportManifest: vi.fn(),
      validatePacket: vi.fn()
    });

    expect(build(legacyRoutePacket).sourceWorkflowHref).toBe("/accounting/reconciliation?runId=run-1#break-7");
    expect(build(unsafeRoutePacket).sourceWorkflowHref).toBe("/reporting/report-packs");
    expect(build(apiRoutePacket).sourceWorkflowHref).toBe("/reporting/report-packs");
  });

  it("maps ready, review, blocked, missing, stale, and unknown statuses to accessible tones", () => {
    expect(mapStatusTone("Ready")).toBe("success");
    expect(mapStatusTone("ReviewRequired")).toBe("warning");
    expect(mapStatusTone("Stale")).toBe("warning");
    expect(mapStatusTone("Blocked")).toBe("danger");
    expect(mapStatusTone("Missing")).toBe("danger");
    expect(mapStatusTone("Unknown")).toBe("muted");
  });

  it("counts ready and review evidence inside stage groups", () => {
    const groups = groupNodes([readyNode, blockedNode]);

    expect(groups).toEqual([
      expect.objectContaining({
        id: "run-lifecycle",
        readyCount: 1,
        reviewCount: 0,
        tableLabel: "Run Lifecycle evidence nodes",
        hasRows: true,
        defaultSelectedNodeId: readyNode.evidenceId
      }),
      expect.objectContaining({
        id: "provider-trust",
        readyCount: 0,
        reviewCount: 1,
        tableLabel: "Provider Trust evidence nodes",
        hasRows: true,
        defaultSelectedNodeId: blockedNode.evidenceId
      })
    ]);

    expect(groups[1].rows[0]).toMatchObject({
      kindLabel: "Provider Trust",
      statusLabel: "Review Required",
      workItemCountLabel: "1 work item"
    });
    expect(buildEvidenceNodeDetail(groups[0].rows[0])).toMatchObject({
      eyebrow: "Selected evidence node",
      title: "Strategy Run Detail",
      subtitle: readyNode.evidenceId,
      artifactRows: [
        expect.objectContaining({
          kind: "Json",
          target: "artifacts/evidence/strategy-run/run-1/detail.json",
          retainedLabel: "Retained",
          hashLabel: "sha256:run-detail",
          canonicalSubjectLabel: "Run run-1"
        })
      ],
      workItemEmptyText: "No related operator work items are attached to this node."
    });
  });

  it("builds accessible empty and populated lineage presentation state", () => {
    const emptyPanel = buildEvidenceLineagePanel([], subject);

    expect(emptyPanel).toMatchObject({
      hasRows: false,
      defaultSelectedRowId: null,
      detailPanelId: "evidence-lineage-selected-edge-detail",
      summaryLabel: "0 edges",
      tableLabel: "Evidence lineage edges for Momentum strategy run",
      emptyTitle: "No lineage edges",
      emptyRole: "status",
      emptyAriaLive: "polite"
    });

    const populatedPanel = buildEvidenceLineagePanel([
      {
        fromId: "provider-trust:sample-review",
        relationship: "blocks_readiness",
        toId: "strategy-run:run-1:promotion",
        reason: "Provider evidence must be reviewed first."
      }
    ], subject);

    expect(populatedPanel.hasRows).toBe(true);
    expect(populatedPanel.rows[0]).toMatchObject({
      relationshipLabel: "Blocks Readiness",
      reason: "Provider evidence must be reviewed first."
    });
    expect(buildEvidenceLineageDetail(populatedPanel.rows[0])).toMatchObject({
      eyebrow: "Selected lineage edge",
      title: "Blocks Readiness",
      subtitle: "provider-trust:sample-review to strategy-run:run-1:promotion",
      description: "Provider evidence must be reviewed first.",
      fields: [
        { label: "From node", value: "provider-trust:sample-review" },
        { label: "Relationship", value: "Blocks Readiness" },
        { label: "To node", value: "strategy-run:run-1:promotion" },
        {
          label: "Edge ID",
          value: "provider-trust:sample-review:blocks_readiness:strategy-run:run-1:promotion:0"
        }
      ]
    });
  });

  it("serializes validation and export commands for the selected evidence packet", () => {
    const validating = buildEvidenceWorkbenchViewModel({
      selectedSubjectKind: "strategy-run",
      selectedSubjectId: "run-1",
      loading: false,
      error: null,
      subjects: [subject],
      packet,
      exportBusy: false,
      exportResult: null,
      validateBusy: true,
      validationResult: null,
      exportManifest: vi.fn(),
      validatePacket: vi.fn()
    });

    expect(validating.validateCommand).toMatchObject({
      busy: true,
      disabled: true,
      disabledReason: "Evidence validation is already running."
    });
    expect(validating.exportCommand).toMatchObject({
      disabled: true,
      disabledReason: "Evidence validation is already running."
    });
    expect(validating.packetActions.find((action) => action.control === "export")).toMatchObject({
      disabled: true,
      disabledReason: "Evidence validation is already running."
    });

    const exporting = buildEvidenceWorkbenchViewModel({
      selectedSubjectKind: "strategy-run",
      selectedSubjectId: "run-1",
      loading: false,
      error: null,
      subjects: [subject],
      packet,
      exportBusy: true,
      exportResult: null,
      validateBusy: false,
      validationResult: null,
      exportManifest: vi.fn(),
      validatePacket: vi.fn()
    });

    expect(exporting.exportCommand).toMatchObject({
      busy: true,
      disabled: true,
      disabledReason: "Evidence export is already running."
    });
    expect(exporting.validateCommand).toMatchObject({
      disabled: true,
      disabledReason: "Evidence export is already running."
    });
    expect(exporting.packetActions.find((action) => action.control === "validate")).toMatchObject({
      disabled: true,
      disabledReason: "Evidence export is already running."
    });
  });

  it("ignores stale validation results after the selected subject changes", async () => {
    const validation = createDeferred<EvidenceCompleteness>();
    const nextSubject: EvidenceSubject = {
      ...subject,
      subjectId: "run-2",
      label: "Rebalanced strategy run"
    };
    const nextPacket: EvidencePacket = {
      ...packet,
      subject: nextSubject,
      completeness: { ...completeness, score: 80 }
    };
    const services = {
      getSubjects: vi.fn().mockResolvedValue([subject, nextSubject]),
      getPacket: vi.fn(async (_subjectKind: string, subjectId: string) => subjectId === "run-2" ? nextPacket : packet),
      validatePacket: vi.fn(() => validation.promise),
      exportManifest: vi.fn()
    };
    const { result, rerender } = renderHook(
      ({ search }) => useEvidenceWorkbenchViewModel(search, services),
      { initialProps: { search: "?subjectKind=strategy-run&subjectId=run-1" } }
    );

    await waitFor(() => expect(result.current.hasPacket).toBe(true));

    await act(async () => {
      void result.current.validatePacket();
    });

    expect(result.current.validateBusy).toBe(true);

    rerender({ search: "?subjectKind=strategy-run&subjectId=run-2" });

    await waitFor(() => expect(result.current.selectedSubjectId).toBe("run-2"));
    await waitFor(() => expect(result.current.hasPacket).toBe(true));
    expect(result.current.validateBusy).toBe(false);
    const validationCalls = services.validatePacket.mock.calls as unknown as Array<[string, string, { signal?: AbortSignal }]>;
    expect(validationCalls[0]?.[2]?.signal?.aborted).toBe(true);

    await act(async () => {
      validation.resolve({ ...completeness, score: 50 });
      await validation.promise;
    });

    expect(result.current.selectedSubjectId).toBe("run-2");
    expect(result.current.title).toBe("Rebalanced strategy run");
    expect(result.current.validationResult).toBeNull();
    expect(result.current.scoreLabel).toBe("80% complete");
  });

  it("applies route queue filters to Evidence Vault request-list and document indexes", async () => {
    vi.mocked(getEvidenceSubjects).mockResolvedValue([subject]);
    vi.mocked(getEvidencePacket).mockResolvedValue(packet);

    const { result } = renderHook(
      () => useEvidenceWorkbenchViewModel(
        "?subjectKind=strategy-run&subjectId=run-1&requestListFamily=Audit&documentClassification=BankEvidence&documentReviewStatus=NeedsReview"
      )
    );

    await waitFor(() => expect(result.current.hasPacket).toBe(true));

    expect(result.current.queueFilters).toMatchObject({
      requestListFamily: "Audit",
      documentClassification: "BankEvidence",
      documentReviewStatus: "NeedsReview"
    });
    expect(listEvidenceVaultRequestLists).toHaveBeenCalledWith(
      {
        subjectKind: "strategy-run",
        subjectId: "run-1",
        requestListKindCode: "Audit",
        status: "Open",
        maxResults: 25
      },
      expect.objectContaining({ signal: expect.any(Object) })
    );
    expect(listEvidenceVaultDocuments).toHaveBeenCalledWith(
      {
        subjectKind: "strategy-run",
        subjectId: "run-1",
        classification: "BankEvidence",
        reviewStatus: "NeedsReview",
        maxResults: 25
      },
      expect.objectContaining({ signal: expect.any(Object) })
    );
  });

  it("keeps only the newest same-subject validation and export command results", async () => {
    const firstValidation = createDeferred<EvidenceCompleteness>();
    const secondValidation = createDeferred<EvidenceCompleteness>();
    const firstExport = createDeferred<EvidencePacketExportResponse>();
    const secondExport = createDeferred<EvidencePacketExportResponse>();
    const services = {
      getSubjects: vi.fn().mockResolvedValue([subject]),
      getPacket: vi.fn().mockResolvedValue(packet),
      validatePacket: vi.fn()
        .mockReturnValueOnce(firstValidation.promise)
        .mockReturnValueOnce(secondValidation.promise),
      exportManifest: vi.fn()
        .mockReturnValueOnce(firstExport.promise)
        .mockReturnValueOnce(secondExport.promise)
    };
    const { result } = renderHook(
      ({ search }) => useEvidenceWorkbenchViewModel(search, services),
      { initialProps: { search: "?subjectKind=strategy-run&subjectId=run-1" } }
    );

    await waitFor(() => expect(result.current.hasPacket).toBe(true));

    await act(async () => {
      void result.current.validatePacket();
      void result.current.validatePacket();
    });
    expect(services.validatePacket).toHaveBeenCalledTimes(2);
    const validationCalls = services.validatePacket.mock.calls as unknown as Array<[string, string, { signal?: AbortSignal }]>;
    expect(validationCalls[0]?.[2]?.signal?.aborted).toBe(true);
    expect(validationCalls[1]?.[2]?.signal?.aborted).toBe(false);

    await act(async () => {
      firstValidation.resolve({ ...completeness, score: 41 });
      await Promise.resolve();
    });
    expect(result.current.validationResult).toBeNull();
    expect(result.current.validateBusy).toBe(true);

    await act(async () => {
      secondValidation.resolve({ ...completeness, score: 91 });
      await secondValidation.promise;
    });
    expect(result.current.validationResult?.score).toBe(91);
    expect(result.current.validateBusy).toBe(false);

    await act(async () => {
      void result.current.exportManifest();
      void result.current.exportManifest();
    });
    expect(services.exportManifest).toHaveBeenCalledTimes(2);
    const exportCalls = services.exportManifest.mock.calls as unknown as Array<[string, string, { signal?: AbortSignal }]>;
    expect(exportCalls[0]?.[2]?.signal?.aborted).toBe(true);
    expect(exportCalls[1]?.[2]?.signal?.aborted).toBe(false);

    await act(async () => {
      firstExport.resolve({
        subjectKind: "strategy-run",
        subjectId: "run-1",
        generatedAt: "2026-05-09T12:35:00Z",
        manifestPath: "stale-manifest.json",
        manifestRoute: "/workstation/evidence/stale-manifest.json",
        evidenceCount: 1,
        warningCount: 0,
        retained: true
      });
      await Promise.resolve();
    });
    expect(result.current.exportResult).toBeNull();
    expect(result.current.exportBusy).toBe(true);

    await act(async () => {
      secondExport.resolve({
        subjectKind: "strategy-run",
        subjectId: "run-1",
        generatedAt: "2026-05-09T12:36:00Z",
        manifestPath: "fresh-manifest.json",
        manifestRoute: "/workstation/evidence/fresh-manifest.json",
        evidenceCount: 3,
        warningCount: 1,
        retained: true,
        vaultIdentity: {
          vaultId: "ev-1234567890abcdef12345678",
          subjectKind: "strategy-run",
          subjectId: "run-1",
          manifestPath: "fresh-manifest.json",
          manifestRoute: "/workstation/evidence/fresh-manifest.json",
          retainedAt: "2026-05-09T12:36:00Z",
          contentHashSha256: "0".repeat(64),
          schemaVersion: 1,
          storageKind: "file-bundle",
          artifacts: [
            {
              artifactId: "statement-artifact",
              kind: "broker-statement",
              relativePath: "workstation/evidence/_vault/ev-1234567890abcdef12345678/artifacts/broker-statement.csv",
              contentHashSha256: "a".repeat(64),
              sizeBytes: 2048,
              retainedAt: "2026-05-09T12:36:00Z",
              sourcePath: "C:/statement.csv",
              sourceRoute: "/api/workstation/reconciliation/statement-runs/import-1",
              canonicalSubjectKind: "report",
              canonicalSubjectId: "report-pack-1",
              capture: {
                captureChannel: "Upload",
                sourceSystem: "Evidence Vault upload",
                receivedAt: "2026-05-09T12:30:00Z",
                receivedBy: "ops-user",
                sourceReference: "portal-upload:broker-statement",
                receiptHash: "a".repeat(64)
              },
              extractedFields: [
                {
                  fieldName: "cashAmount",
                  extractedValue: "0",
                  expectedValue: "0",
                  confidenceScore: 0.98,
                  reviewState: "Reviewed",
                  validationStatus: "Ready",
                  validationMessage: "Cash amount matched.",
                  linkedRecordKind: "reconciliation-case",
                  linkedRecordId: "case-1"
                }
              ]
            }
          ],
          requestLists: [
            {
              requestListId: "request-list:auditrequestlist:audit:close-2026-05",
              requestListKind: "AuditRequestList",
              targetKind: "audit",
              targetId: "close-2026-05",
              highestSeverity: "Critical",
              status: "Open",
              requestCount: 2,
              requestIds: [
                "support-request:missingevidence:audit-support",
                "support-request:blockedworkitem:audit-support:audit-request-close-2026-05"
              ],
              evidenceKinds: ["audit-history"],
              blockedOutputs: ["report-pack/close-2026-05"],
              summary: "audit/close-2026-05 has 2 frozen requests; 2 open requests remain."
            }
          ],
          supportRequests: [
            {
              requestId: "support-request:missingevidence:audit-support",
              requestKind: "MissingEvidence",
              evidenceId: "audit-support",
              evidenceKind: "audit-history",
              severity: "Critical",
              status: "Open",
              summary: "Audit support package is missing.",
              sourceSystem: "test",
              workItemId: null,
              blockedOutput: "report-pack/close-2026-05"
            },
            {
              requestId: "support-request:blockedworkitem:audit-support:audit-request-close-2026-05",
              requestKind: "BlockedWorkItem",
              evidenceId: "audit-support",
              evidenceKind: "audit-history",
              severity: "Critical",
              status: "Open",
              summary: "Work item 'audit-request:close-2026-05' blocks evidence support.",
              sourceSystem: "test",
              workItemId: "audit-request:close-2026-05",
              blockedOutput: "report-pack/close-2026-05"
            }
          ],
          manifestSnapshot: {
            manifestId: "manifest-close-2026-05-audit",
            frozenAt: "2026-05-09T12:36:00Z",
            packageKind: "report-pack",
            packageId: "close-2026-05",
            contentHashSha256: "f".repeat(64),
            documents: [],
            requests: [],
            objectLinks: [],
            packageKindCode: "AuditPacket"
          }
        }
      });
      await secondExport.promise;
    });
    expect(result.current.exportResult?.manifestPath).toBe("fresh-manifest.json");
    expect(result.current.exportResultDetail).toMatchObject({
      title: "Manifest retained",
      manifestPath: "fresh-manifest.json",
      summaryLabel: "3 nodes, 1 warning, Audit Packet, 1 retained artifact, 1 request list, 2 support requests",
      routeHref: "/workstation/evidence/fresh-manifest.json",
      routeLabel: "Open manifest",
      routeAriaLabel: "Open retained evidence manifest at fresh-manifest.json",
      vaultIdLabel: "ev-1234567890abcdef12345678",
      storageKindLabel: "File Bundle",
      manifestPackageFamilyLabel: "Audit Packet",
      artifactSummaryLabel: "1 retained artifact",
      artifactRows: [
        expect.objectContaining({
          id: "statement-artifact",
          kind: "Broker Statement",
          relativePath: "workstation/evidence/_vault/ev-1234567890abcdef12345678/artifacts/broker-statement.csv",
          sizeLabel: "2.0 KiB",
          hashLabel: "a".repeat(64),
          sourceLabel: "/api/workstation/reconciliation/statement-runs/import-1",
          canonicalSubjectLabel: "Report report-pack-1",
          captureLabel: "Upload via Evidence Vault upload; received May 9, 12:30 UTC; reference portal-upload:broker-statement",
          extractionLabel: "1 extracted field; 1 validated; 1 reviewed",
          retainedLabel: "Retained May 9, 12:36 UTC"
        })
      ],
      requestListSummaryLabel: "1 request list",
      requestListRows: [
        expect.objectContaining({
          id: "request-list:auditrequestlist:audit:close-2026-05",
          requestListKindLabel: "Audit Request List",
          targetLabel: "Audit close-2026-05",
          highestSeverityLabel: "Critical",
          highestSeverityTone: "danger",
          statusLabel: "Open",
          requestCountLabel: "2 support requests",
          evidenceKindsLabel: "Audit History",
          blockedOutputsLabel: "report-pack/close-2026-05",
          summary: "audit/close-2026-05 has 2 frozen requests; 2 open requests remain."
        })
      ],
      supportRequestSummaryLabel: "2 support requests",
      supportRequestRows: [
        expect.objectContaining({
          id: "support-request:missingevidence:audit-support",
          requestKindLabel: "Missing Evidence",
          evidenceLabel: "audit-support",
          evidenceKindLabel: "Audit History",
          severityLabel: "Critical",
          severityTone: "danger",
          statusLabel: "Open",
          summary: "Audit support package is missing.",
          sourceLabel: "test",
          workItemLabel: "No work item",
          blockedOutputLabel: "report-pack/close-2026-05"
        }),
        expect.objectContaining({
          id: "support-request:blockedworkitem:audit-support:audit-request-close-2026-05",
          requestKindLabel: "Blocked Work Item",
          evidenceLabel: "audit-support",
          workItemLabel: "audit-request:close-2026-05",
          blockedOutputLabel: "report-pack/close-2026-05"
        })
      ]
    });
    expect(result.current.exportBusy).toBe(false);
  });
});

describe("EvidenceWorkbenchScreen", () => {
  it("renders subject query route, validation result, and manifest export result", async () => {
    const validation: EvidenceCompleteness = { ...completeness, score: 50 };
    const exportResponse: EvidencePacketExportResponse = {
      subjectKind: "strategy-run",
      subjectId: "run-1",
      generatedAt: "2026-05-09T12:35:00Z",
      manifestPath: "workstation/evidence/strategy-run/run-1/manifest.json",
      manifestRoute: "/workstation/evidence/strategy-run/run-1/manifest.json",
      evidenceCount: 3,
      warningCount: 1,
      retained: true,
      vaultIdentity: {
        vaultId: "ev-abcdefabcdefabcdefabcdef",
        subjectKind: "strategy-run",
        subjectId: "run-1",
        manifestPath: "workstation/evidence/strategy-run/run-1/manifest.json",
        manifestRoute: "/workstation/evidence/strategy-run/run-1/manifest.json",
        retainedAt: "2026-05-09T12:35:00Z",
        contentHashSha256: "0".repeat(64),
        schemaVersion: 1,
        storageKind: "file-bundle",
        artifacts: [
          {
            artifactId: "statement-artifact",
            kind: "broker-statement",
            relativePath: "workstation/evidence/_vault/ev-abcdefabcdefabcdefabcdef/artifacts/broker-statement.csv",
            contentHashSha256: "b".repeat(64),
            sizeBytes: 1024,
            retainedAt: "2026-05-09T12:35:00Z",
            sourcePath: null,
            sourceRoute: "/api/workstation/reconciliation/statement-runs/import-1",
            canonicalSubjectKind: "report",
            canonicalSubjectId: "report-pack-1",
            capture: {
              captureChannel: "Upload",
              sourceSystem: "Evidence Vault upload",
              receivedAt: "2026-05-09T12:30:00Z",
              receivedBy: "ops-user",
              sourceReference: "portal-upload:broker-statement",
              receiptHash: "b".repeat(64)
            },
            extractedFields: [
              {
                fieldName: "cashAmount",
                extractedValue: "0",
                expectedValue: "0",
                confidenceScore: 0.98,
                reviewState: "Reviewed",
                validationStatus: "Ready",
                validationMessage: "Cash amount matched.",
                linkedRecordKind: "reconciliation-case",
                linkedRecordId: "case-1"
              }
            ]
          }
        ],
        requestLists: [
          {
            requestListId: "request-list:auditrequestlist:audit:close-2026-05",
            requestListKind: "AuditRequestList",
            targetKind: "audit",
            targetId: "close-2026-05",
            highestSeverity: "Critical",
            status: "Open",
            requestCount: 1,
            requestIds: ["support-request:missingevidence:audit-support"],
            evidenceKinds: ["audit-history"],
            blockedOutputs: ["report-pack/close-2026-05"],
            summary: "audit/close-2026-05 has 1 frozen request; 1 open request remains."
          }
        ],
        supportRequests: [
          {
            requestId: "support-request:missingevidence:audit-support",
            requestKind: "MissingEvidence",
            evidenceId: "audit-support",
            evidenceKind: "audit-history",
            severity: "Critical",
            status: "Open",
            summary: "Audit support package is missing.",
            sourceSystem: "test",
            workItemId: "audit-request:close-2026-05",
            blockedOutput: "report-pack/close-2026-05"
          }
        ],
        manifestSnapshot: {
          manifestId: "manifest-close-2026-05-audit",
          frozenAt: "2026-05-09T12:35:00Z",
          packageKind: "report-pack",
          packageId: "close-2026-05",
          contentHashSha256: "0".repeat(64),
          documents: [],
          requests: [],
          objectLinks: [],
          packageKindCode: "AuditPacket"
        }
      }
    };
    vi.mocked(getEvidenceSubjects).mockResolvedValue([subject]);
    vi.mocked(getEvidencePacket).mockResolvedValue(packet);
    vi.mocked(listEvidenceVaultRequestLists).mockResolvedValue([vaultRequestListEntry]);
    vi.mocked(listEvidenceVaultDocuments).mockResolvedValue([vaultDocumentEntry]);
    vi.mocked(validateEvidencePacket).mockResolvedValue(validation);
    vi.mocked(exportEvidenceManifest).mockResolvedValue(exportResponse);
    const user = userEvent.setup();

    renderEvidenceRoute("/reporting/evidence?subjectKind=strategy-run&subjectId=run-1");

    expect(await screen.findByText("Momentum strategy run")).toBeInTheDocument();
    expect(listEvidenceVaultRequestLists).toHaveBeenCalledWith(
      {
        subjectKind: "strategy-run",
        subjectId: "run-1",
        status: "Open",
        maxResults: 25
      },
      expect.objectContaining({ signal: expect.any(Object) })
    );
    expect(listEvidenceVaultDocuments).toHaveBeenCalledWith(
      {
        subjectKind: "strategy-run",
        subjectId: "run-1",
        maxResults: 25
      },
      expect.objectContaining({ signal: expect.any(Object) })
    );
    const requestListPanel = screen.getByRole("region", { name: "Evidence Vault request lists" });
    const queueFilterPanel = screen.getByRole("region", { name: "Evidence Vault queue filters" });
    expect(queueFilterPanel).toHaveTextContent("Queue filters");
    expect(screen.getByLabelText("Request list family filter")).toBeInTheDocument();
    expect(screen.getByLabelText("Document classification filter")).toBeInTheDocument();
    expect(screen.getByLabelText("Document review status filter")).toBeInTheDocument();
    expect(requestListPanel).toHaveTextContent("1 open request list");
    expect(requestListPanel).toHaveTextContent("Strategy Run run-1");
    expect(requestListPanel).toHaveTextContent("Audit close-2026-05");
    expect(requestListPanel).toHaveTextContent("Audit family");
    expect(requestListPanel).toHaveTextContent("2 open requests");
    expect(requestListPanel).toHaveTextContent("ev-request-list-demo");
    expect(requestListPanel).toHaveTextContent("Audit support package is missing.");
    expect(screen.getByRole("link", { name: /open retained manifest for request list/i })).toHaveAttribute(
      "href",
      "/workstation/evidence/strategy-run/run-1/manifest.json"
    );
    const documentPanel = screen.getByRole("region", { name: "Evidence Vault documents" });
    expect(documentPanel).toHaveTextContent("1 need review");
    expect(documentPanel).toHaveTextContent("operating-bank-statement.csv");
    expect(documentPanel).toHaveTextContent("Bank Evidence");
    expect(documentPanel).toHaveTextContent("Close Task close-task:cash-support");
    expect(documentPanel).toHaveTextContent("fund-controller");
    expect(documentPanel).toHaveTextContent("tenant-alpha / fund-alpha");
    expect(screen.getByRole("link", { name: /open retained manifest for document operating-bank-statement.csv/i })).toHaveAttribute(
      "href",
      "/workstation/evidence/strategy-run/run-1/manifest.json"
    );
    const selectedDocument = screen.getByRole("region", {
      name: "Selected Evidence Vault document: operating-bank-statement.csv"
    });
    expect(selectedDocument).toHaveTextContent("Statement amount needs review.");
    expect(selectedDocument).toHaveTextContent("Document Intake Retained");
    expect(selectedDocument).toHaveTextContent("manual-metadata-v1");
    expect(selectedDocument).toHaveTextContent("support, block, suggest, link; cannot approve, post, certify, release");
    expect(selectedDocument).toHaveTextContent(`Upload receipt ${"d".repeat(64)}`);
    expect(selectedDocument).toHaveTextContent("Source record actor");
    expect(selectedDocument).toHaveTextContent("Review fields");
    expect(selectedDocument).toHaveTextContent("Ending Cash");
    expect(selectedDocument).toHaveTextContent("Extracted: 1250000.00");
    expect(selectedDocument).toHaveTextContent("98% confidence");
    expect(selectedDocument).toHaveTextContent("Cash support");
    expect(selectedDocument).toHaveTextContent("blocks-close-readiness");
    expect(selectedDocument).toHaveTextContent("Audit support package is missing.");
    expect(screen.getByRole("link", { name: /open linked close task cash support/i })).toHaveAttribute(
      "href",
      "/accounting/close/tasks/cash-support"
    );
    vi.mocked(listEvidenceVaultDocuments).mockResolvedValue([acceptedVaultDocumentEntry]);
    await user.click(within(selectedDocument).getByRole("button", { name: "Accept" }));
    await waitFor(() => expect(reviewEvidenceVaultDocument).toHaveBeenCalledTimes(1));
    expect(reviewEvidenceVaultDocument).toHaveBeenCalledWith(
      "ev-request-list-demo",
      "doc:ev-request-list-demo",
      expect.objectContaining({
        status: "Accepted",
        reviewer: "evidence-workbench-operator",
        confirmedFields: expect.arrayContaining([
          expect.objectContaining({
            fieldName: "endingCash",
            confirmedValue: "1250000.00",
            sourceFieldName: "endingCash"
          }),
          expect.objectContaining({
            fieldName: "sourceHashSha256",
            confirmedValue: "d".repeat(64),
            sourceFieldName: "sourceHashSha256"
          })
        ])
      }),
      expect.objectContaining({ signal: expect.any(Object) })
    );
    await waitFor(() => expect(screen.getByRole("region", {
      name: "Selected Evidence Vault document: operating-bank-statement.csv"
    })).toHaveTextContent("Document Review Recorded"));
    expect(screen.getByRole("region", { name: "Evidence document intake" })).toHaveTextContent("strategy-run/run-1");
    const file = new File(["cash"], "close-bank-evidence.csv", { type: "text/csv" });
    await user.upload(screen.getByLabelText("Document file"), file);
    const classificationSelect = screen.getByRole("combobox", { name: /^Document classification$/ });
    expect(within(classificationSelect).getByRole("option", { name: "Bank Statement" })).toBeInTheDocument();
    expect(within(classificationSelect).getByRole("option", { name: "Admin Package" })).toBeInTheDocument();
    expect(within(classificationSelect).getByRole("option", { name: "Tax Audit Support" })).toBeInTheDocument();
    const extractionStatusSelect = screen.getByRole("combobox", { name: "Extraction status" });
    expect(within(extractionStatusSelect).getByRole("option", { name: "Pending" })).toBeInTheDocument();
    expect(within(extractionStatusSelect).queryByRole("option", { name: "Accepted" })).not.toBeInTheDocument();
    expect(screen.getByRole("option", { name: "Email" })).toBeInTheDocument();
    expect(screen.getByRole("option", { name: "Sftp" })).toBeInTheDocument();
    expect(screen.getByRole("option", { name: "Api" })).toBeInTheDocument();
    expect(screen.getByRole("option", { name: "Portal Download" })).toBeInTheDocument();
    expect(screen.getByRole("option", { name: "Fund" })).toBeInTheDocument();
    await user.selectOptions(classificationSelect, "BankStatement");
    await user.selectOptions(screen.getByLabelText("Extraction status"), "Pending");
    await user.selectOptions(screen.getByLabelText("Reviewer state"), "NeedsReview");
    await user.type(screen.getByLabelText("Actor"), "fund-controller");
    await user.type(screen.getByLabelText("Tenant"), "tenant-alpha");
    await user.type(screen.getByLabelText("Scope"), "fund-alpha");
    await user.type(screen.getByLabelText("Source system"), "operator-upload");
    await user.type(screen.getByLabelText("Source reference"), "close-bank-evidence.csv");
    await user.selectOptions(screen.getByLabelText("Linked object kind"), "Fund");
    await user.type(screen.getByLabelText("Linked object id"), "fund-alpha");
    await user.click(screen.getByRole("button", { name: /retain document for momentum strategy run/i }));
    await waitFor(() => expect(intakeEvidenceVaultDocument).toHaveBeenCalledTimes(1));
    expect(intakeEvidenceVaultDocument).toHaveBeenCalledWith(
      expect.objectContaining({
        subjectKind: "strategy-run",
        subjectId: "run-1",
        intakeChannel: "upload",
        fileName: "close-bank-evidence.csv",
        contentBase64: "Y2FzaA==",
        contentType: "text/csv",
        sourceSystem: "operator-upload",
        sourceReference: "close-bank-evidence.csv",
        receivedBy: "fund-controller",
        classification: "BankStatement",
        actor: "fund-controller",
        tenantId: "tenant-alpha",
        scope: "fund-alpha",
        extractionStatus: "Pending",
        intakeChannelKind: "Upload",
        reviewerState: expect.objectContaining({
          status: "NeedsReview",
          reviewer: "fund-controller"
        }),
        objectLinks: [
          expect.objectContaining({
            linkKind: "Fund",
            objectId: "fund-alpha",
            relationship: "supports"
          })
        ],
        intakeSource: expect.objectContaining({
          sourceKind: "UploadedContent",
          displayName: "close-bank-evidence.csv"
        })
      }),
      expect.objectContaining({ signal: expect.any(Object) })
    );
    expect(await screen.findByText(/Retained close-bank-evidence.csv as intake:ev-uploaded-close-document/i)).toBeInTheDocument();
    await waitFor(() => expect(listEvidenceVaultDocuments).toHaveBeenCalledTimes(3));
    expect(screen.getByText("Missing evidence")).toBeInTheDocument();
    expect(screen.getByRole("region", { name: "Operational Evidence Graph" })).toHaveTextContent(
      "4/9 layers, 44% coverage"
    );
    expect(screen.getByRole("list", { name: "Operational evidence proof-chain layers" })).toHaveTextContent(
      "No Normalization evidence is present in this packet."
    );
    expect(screen.getByRole("list", { name: "Operational evidence proof-chain layers" })).toHaveTextContent(
      "1 missing node"
    );
    expect(screen.getByRole("region", { name: "Meridian Assurance" })).toHaveTextContent("45% assurance");
    expect(screen.getByRole("region", { name: "Meridian Assurance" })).toHaveTextContent("No-orphan rule breached");
    expect(screen.getByRole("list", { name: "Assurance score components" })).toHaveTextContent("Replay freshness");
    expect(screen.getByRole("list", { name: "Evidence SLA assessments" })).toHaveTextContent(
      "Replay evidence is outside the replay freshness window."
    );
    expect(screen.getByText("Orphan evidence")).toBeInTheDocument();
    expect(screen.getByText("strategy-run:run-1:unlinked-approval")).toBeInTheDocument();
    expect(screen.getByText("SLA breaches")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: /open source workflow for momentum strategy run/i })).toHaveAttribute(
      "href",
      "/strategy?runId=run-1"
    );
    expect(screen.getByRole("region", { name: "Evidence packet actions" })).toBeInTheDocument();
    expect(screen.getByRole("table", { name: "Run Lifecycle evidence nodes" })).toBeInTheDocument();
    expect(screen.getByRole("region", { name: "Selected evidence node: Strategy Run Detail" })).toHaveTextContent(
      "Run detail is available."
    );
    expect(screen.getByText("artifacts/evidence/strategy-run/run-1/detail.json")).toBeInTheDocument();
    expect(screen.getByRole("table", { name: /evidence lineage edges for momentum strategy run/i })).toBeInTheDocument();
    expect(screen.getAllByText("Requires").length).toBeGreaterThanOrEqual(1);
    expect(screen.getByRole("region", { name: "Selected lineage edge: Requires" })).toHaveTextContent(
      "Replay evidence supports the run."
    );
    expect(screen.getByRole("link", { name: /open evidence packet for momentum strategy run/i })).toHaveAttribute(
      "href",
      "/reporting/evidence?subjectKind=strategy-run&subjectId=run-1"
    );

    await user.click(screen.getByRole("button", { name: /validate selected evidence packet for momentum strategy run/i }));
    expect(await screen.findByText(/Validation returned 50% completeness/i)).toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: /export selected evidence manifest for momentum strategy run/i }));
    expect(await screen.findByText("Manifest retained")).toBeInTheDocument();
    expect(screen.getByText("workstation/evidence/strategy-run/run-1/manifest.json")).toBeInTheDocument();
    expect(screen.getByText("3 nodes, 1 warning, Audit Packet, 1 retained artifact, 1 request list, 1 support request")).toBeInTheDocument();
    expect(screen.getByText("ev-abcdefabcdefabcdefabcdef")).toBeInTheDocument();
    expect(screen.getAllByText("File Bundle").length).toBeGreaterThanOrEqual(1);
    expect(screen.getAllByText("Audit Packet").length).toBeGreaterThanOrEqual(1);
    expect(screen.getByRole("list", { name: "Retained vault artifacts" })).toBeInTheDocument();
    expect(screen.getByText("Broker Statement")).toBeInTheDocument();
    expect(screen.getByText("workstation/evidence/_vault/ev-abcdefabcdefabcdefabcdef/artifacts/broker-statement.csv")).toBeInTheDocument();
    expect(screen.getByText("Report report-pack-1")).toBeInTheDocument();
    expect(screen.getByText("Upload via Evidence Vault upload; received May 9, 12:30 UTC; reference portal-upload:broker-statement")).toBeInTheDocument();
    expect(screen.getByText("1 extracted field; 1 validated; 1 reviewed")).toBeInTheDocument();
    expect(screen.getByRole("list", { name: "Evidence vault request lists" })).toBeInTheDocument();
    expect(screen.getAllByText("Audit Request List").length).toBeGreaterThanOrEqual(2);
    expect(screen.getAllByText("Audit close-2026-05").length).toBeGreaterThanOrEqual(2);
    expect(screen.getAllByText("Audit family").length).toBeGreaterThanOrEqual(1);
    expect(screen.getByText("audit/close-2026-05 has 1 frozen request; 1 open request remains.")).toBeInTheDocument();
    expect(screen.getAllByText("Audit History").length).toBeGreaterThanOrEqual(1);
    expect(screen.getByRole("list", { name: "Evidence vault support requests" })).toBeInTheDocument();
    expect(screen.getAllByText("Missing Evidence").length).toBeGreaterThanOrEqual(1);
    expect(screen.getAllByText("audit-support").length).toBeGreaterThanOrEqual(1);
    expect(screen.getAllByText("Audit support package is missing.").length).toBeGreaterThanOrEqual(1);
    expect(screen.getAllByText("audit-request:close-2026-05").length).toBeGreaterThanOrEqual(1);
    expect(screen.getByRole("link", { name: /open retained evidence manifest/i })).toHaveAttribute(
      "href",
      "/workstation/evidence/strategy-run/run-1/manifest.json"
    );
  });

  it.each([
    ["LocalFile", "local-file", "LocalFile", "D:\\imports\\custodian-statement.csv", "CustodianFile"],
    ["ImportedFileReference", "imported-file-reference", "ImportedFileReference", "D:\\imports\\portal\\capital-notice.pdf", "CapitalNotice"]
  ] as const)("submits %s document references through the shared intake contract", async (sourceKind, intakeChannel, intakeChannelKind, sourcePath, classification) => {
    vi.mocked(getEvidenceSubjects).mockResolvedValue([subject]);
    vi.mocked(getEvidencePacket).mockResolvedValue(packet);
    const user = userEvent.setup();

    renderEvidenceRoute("/reporting/evidence?subjectKind=strategy-run&subjectId=run-1");

    expect(await screen.findByText("Momentum strategy run")).toBeInTheDocument();
    await user.selectOptions(screen.getByLabelText("Document source kind"), sourceKind);
    await user.type(screen.getByLabelText("Source file path"), sourcePath);
    await user.selectOptions(screen.getByRole("combobox", { name: /^Document classification$/ }), classification);
    await user.type(screen.getByLabelText("Actor"), "fund-controller");
    await user.type(screen.getByLabelText("Linked object id"), "close-task:cash-support");
    await user.click(screen.getByRole("button", { name: /retain document for momentum strategy run/i }));

    const fileName = sourcePath.split("\\").at(-1);
    await waitFor(() => expect(intakeEvidenceVaultDocument).toHaveBeenCalledTimes(1));
    expect(intakeEvidenceVaultDocument).toHaveBeenCalledWith(
      expect.objectContaining({
        subjectKind: "strategy-run",
        subjectId: "run-1",
        intakeChannel,
        fileName,
        contentBase64: null,
        contentType: null,
        sourceReference: sourcePath,
        receivedBy: "fund-controller",
        classification,
        actor: "fund-controller",
        intakeChannelKind,
        intakeSource: expect.objectContaining({
          sourceKind,
          path: sourcePath,
          displayName: fileName
        }),
        objectLinks: [
          expect.objectContaining({
            linkKind: "CloseTask",
            objectId: "close-task:cash-support"
          })
        ]
      }),
      expect.objectContaining({ signal: expect.any(Object) })
    );
  });

  it("submits adapter-seam source metadata with uploaded bytes instead of fetching the remote source", async () => {
    vi.mocked(getEvidenceSubjects).mockResolvedValue([subject]);
    vi.mocked(getEvidencePacket).mockResolvedValue(packet);
    const user = userEvent.setup();

    renderEvidenceRoute("/reporting/evidence?subjectKind=strategy-run&subjectId=run-1");

    expect(await screen.findByText("Momentum strategy run")).toBeInTheDocument();
    await user.selectOptions(screen.getByLabelText("Document source kind"), "PortalDownload");
    const file = new File(["portal"], "admin-package.csv", { type: "text/csv" });
    await user.upload(screen.getByLabelText("Document file"), file);
    await user.selectOptions(screen.getByRole("combobox", { name: /^Document classification$/ }), "AdminPackage");
    await user.type(screen.getByLabelText("Source system"), "fund-admin-portal");
    await user.type(screen.getByLabelText("Source reference"), "portal://fund-admin/fund-alpha/admin-package-202606");
    await user.type(screen.getByLabelText("Actor"), "fund-admin-operator");
    await user.type(screen.getByLabelText("Linked object id"), "fund-alpha");
    await user.selectOptions(screen.getByLabelText("Linked object kind"), "Fund");
    await user.click(screen.getByRole("button", { name: /retain document for momentum strategy run/i }));

    await waitFor(() => expect(intakeEvidenceVaultDocument).toHaveBeenCalledTimes(1));
    expect(intakeEvidenceVaultDocument).toHaveBeenCalledWith(
      expect.objectContaining({
        subjectKind: "strategy-run",
        subjectId: "run-1",
        intakeChannel: "portal-download",
        fileName: "admin-package.csv",
        contentBase64: "cG9ydGFs",
        contentType: "text/csv",
        sourceSystem: "fund-admin-portal",
        sourceReference: "portal://fund-admin/fund-alpha/admin-package-202606",
        receivedBy: "fund-admin-operator",
        classification: "AdminPackage",
        actor: "fund-admin-operator",
        intakeChannelKind: "PortalDownload",
        intakeSource: expect.objectContaining({
          sourceKind: "PortalDownload",
          path: null,
          uri: "portal://fund-admin/fund-alpha/admin-package-202606",
          displayName: "admin-package.csv"
        }),
        objectLinks: [
          expect.objectContaining({
            linkKind: "Fund",
            objectId: "fund-alpha"
          })
        ]
      }),
      expect.objectContaining({ signal: expect.any(Object) })
    );
  });

  it("lets keyboard and pointer users inspect lineage edge detail", async () => {
    const packetWithTwoEdges: EvidencePacket = {
      ...packet,
      edges: [
        ...packet.edges,
        {
          fromId: blockedNode.evidenceId,
          toId: "strategy-run:run-1:promotion",
          relationship: "blocks_readiness",
          reason: "Provider evidence must be reviewed first."
        }
      ]
    };
    vi.mocked(getEvidenceSubjects).mockResolvedValue([subject]);
    vi.mocked(getEvidencePacket).mockResolvedValue(packetWithTwoEdges);
    const user = userEvent.setup();

    renderEvidenceRoute("/reporting/evidence?subjectKind=strategy-run&subjectId=run-1");

    const secondEdge = await screen.findByRole("row", {
      name: /inspect lineage edge: blocks readiness from strategy-run:run-1:provider-trust/i
    });
    expect(secondEdge).toHaveAttribute("aria-controls", "evidence-lineage-selected-edge-detail");
    expect(secondEdge).toHaveAttribute("aria-expanded", "false");

    await user.click(secondEdge);

    expect(secondEdge).toHaveAttribute("aria-selected", "true");
    expect(secondEdge).toHaveAttribute("aria-expanded", "true");
    const detail = screen.getByRole("region", { name: "Selected lineage edge: Blocks Readiness" });
    expect(detail).toHaveTextContent("Provider evidence must be reviewed first.");
    expect(detail).toHaveTextContent("strategy-run:run-1:provider-trust");
  });

  it("lets keyboard and pointer users inspect evidence node detail", async () => {
    const promotionNode: EvidenceNode = {
      ...readyNode,
      evidenceId: "strategy-run:run-1:promotion",
      kind: "promotion-review",
      status: "ReviewRequired",
      summary: "Promotion review is waiting on provider trust evidence.",
      artifactRefs: [],
      relatedWorkItemIds: ["promotion:review"]
    };
    const packetWithTwoRunNodes: EvidencePacket = {
      ...packet,
      nodes: [readyNode, promotionNode, staleNode, blockedNode]
    };
    vi.mocked(getEvidenceSubjects).mockResolvedValue([subject]);
    vi.mocked(getEvidencePacket).mockResolvedValue(packetWithTwoRunNodes);
    const user = userEvent.setup();

    renderEvidenceRoute("/reporting/evidence?subjectKind=strategy-run&subjectId=run-1");

    const promotionRow = await screen.findByRole("row", {
      name: /inspect evidence node promotion review strategy-run:run-1:promotion/i
    });
    expect(promotionRow).toHaveAttribute("aria-controls", "evidence-node-run-lifecycle-selected-detail");
    expect(promotionRow).toHaveAttribute("aria-expanded", "false");

    promotionRow.focus();
    await user.keyboard("{Enter}");

    expect(promotionRow).toHaveAttribute("aria-selected", "true");
    expect(promotionRow).toHaveAttribute("aria-expanded", "true");
    const promotionDetail = screen.getByRole("region", { name: "Selected evidence node: Promotion Review" });
    expect(promotionDetail).toHaveTextContent("Promotion review is waiting on provider trust evidence.");
    expect(promotionDetail).toHaveTextContent("promotion:review");

    const readyRow = screen.getByRole("row", {
      name: /inspect evidence node strategy run detail strategy-run:run-1:detail/i
    });
    await user.click(readyRow);

    expect(readyRow).toHaveAttribute("aria-selected", "true");
    expect(screen.getByRole("region", { name: "Selected evidence node: Strategy Run Detail" })).toHaveTextContent(
      "artifacts/evidence/strategy-run/run-1/detail.json"
    );
  });

  it("renders broad subject selection route without loading a packet", async () => {
    vi.mocked(getEvidenceSubjects).mockResolvedValue([subject]);
    vi.mocked(getEvidencePacket).mockResolvedValue(packet);

    renderEvidenceRoute("/reporting/evidence");

    expect(await screen.findByRole("link", { name: /Momentum strategy run/i })).toHaveAttribute(
      "href",
      "/reporting/evidence?subjectKind=strategy-run&subjectId=run-1"
    );
    await waitFor(() => expect(getEvidencePacket).not.toHaveBeenCalled());
  });

  it("renders recoverable empty subject guidance when no subjects are available", async () => {
    vi.mocked(getEvidenceSubjects).mockResolvedValue([]);
    vi.mocked(getEvidencePacket).mockResolvedValue(packet);

    renderEvidenceRoute("/reporting/evidence");

    expect(await screen.findByText("No evidence subjects returned")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: /open readiness console/i })).toHaveAttribute(
      "href",
      "/trading/readiness"
    );
    await waitFor(() => expect(getEvidencePacket).not.toHaveBeenCalled());
  });

  it("lets operators retry a failed subject load without showing empty evidence copy", async () => {
    vi.mocked(getEvidenceSubjects)
      .mockRejectedValueOnce(new ApiError({
        path: "/api/workstation/evidence/subjects",
        status: 503,
        detail: "Evidence API unavailable",
        responseBody: "Evidence API unavailable"
      }))
      .mockResolvedValueOnce([subject]);
    vi.mocked(getEvidencePacket).mockResolvedValue(packet);
    const user = userEvent.setup();

    renderEvidenceRoute("/reporting/evidence");

    expect(await screen.findByRole("alert")).toHaveTextContent("Evidence API unavailable");
    expect(screen.getByText("Meridian service returned 503. Open diagnostics for technical details.")).toBeInTheDocument();
    expect(screen.queryByText("No evidence subjects returned")).not.toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: /retry loading evidence subjects/i }));

    expect(await screen.findByRole("link", { name: /Momentum strategy run/i })).toHaveAttribute(
      "href",
      "/reporting/evidence?subjectKind=strategy-run&subjectId=run-1"
    );
    await waitFor(() => expect(getEvidenceSubjects).toHaveBeenCalledTimes(2));
    await waitFor(() => expect(getEvidencePacket).not.toHaveBeenCalled());
  });

  it("keeps operating scope in evidence workbench links rendered from the route", async () => {
    vi.mocked(getEvidenceSubjects).mockResolvedValue([subject]);
    vi.mocked(getEvidencePacket).mockResolvedValue(packet);

    renderEvidenceRoute("/reporting/evidence?subjectKind=strategy-run&subjectId=run-1&symbol=MSFT&fundAccountId=fund-1&runId=run-1&provider=Alpaca&from=2026-05-01&to=2026-05-15");

    expect(await screen.findByText("Momentum strategy run")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: /open source workflow for momentum strategy run/i })).toHaveAttribute(
      "href",
      "/strategy?runId=run-1&symbol=MSFT&provider=Alpaca&from=2026-05-01&to=2026-05-15"
    );
    expect(screen.getByRole("link", { name: /open evidence packet for momentum strategy run/i })).toHaveAttribute(
      "href",
      "/reporting/evidence?subjectKind=strategy-run&subjectId=run-1&symbol=MSFT&fundAccountId=fund-1&runId=run-1&provider=Alpaca&from=2026-05-01&to=2026-05-15"
    );
  });

  it("renders structured validation errors for failed manifest export", async () => {
    vi.mocked(getEvidenceSubjects).mockResolvedValue([subject]);
    vi.mocked(getEvidencePacket).mockResolvedValue(packet);
    vi.mocked(exportEvidenceManifest).mockRejectedValue(
      new ApiError({
        path: "/api/workstation/evidence/strategy-run/run-1/export-manifest",
        status: 422,
        detail: "One or more validation errors occurred.",
        validationIssues: [
          {
            field: "includeWarnings",
            label: "includeWarnings",
            messages: ["Manifest-only export must keep warnings enabled."]
          }
        ],
        responseBody: "{\"detail\":\"One or more validation errors occurred.\"}"
      })
    );
    const user = userEvent.setup();

    renderEvidenceRoute("/reporting/evidence?subjectKind=strategy-run&subjectId=run-1");

    expect(await screen.findByText("Momentum strategy run")).toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: /export selected evidence manifest for momentum strategy run/i }));

    expect(await screen.findByRole("alert")).toHaveTextContent("One or more validation errors occurred.");
    expect(screen.getByText("Meridian service returned 422. Open diagnostics for technical details.")).toBeInTheDocument();
    expect(screen.getByText("includeWarnings: Manifest-only export must keep warnings enabled.")).toBeInTheDocument();
  });
});

function renderEvidenceRoute(initialEntry: string) {
  render(
    <MemoryRouter initialEntries={[initialEntry]}>
      <Routes>
        <Route path="/reporting/evidence" element={<EvidenceWorkbenchScreen />} />
      </Routes>
    </MemoryRouter>
  );
}
