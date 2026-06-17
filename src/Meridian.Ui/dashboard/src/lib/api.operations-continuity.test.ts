import { beforeEach, describe, expect, it, vi } from "vitest";
import {
  acknowledgeOperationsContinuityChecklistTask,
  approveOperationsContinuitySecurityMasterOverride,
  assignOperationsContinuityBreakCase,
  closeOperationsContinuityWorkflow,
  draftOperationsContinuityLedger,
  getOperationsContinuityBreaks,
  getOperationsContinuityChecklist,
  getOperationsContinuityCloseReadiness,
  getOperationsContinuityLedgerPreview,
  getOperationsContinuityTimeline,
  importOperationsContinuityBrokerData,
  normalizeOperationsContinuityBrokerTransactions,
  postOperationsContinuityLedger,
  refreshOperationsContinuityGatePosture,
  reopenOperationsContinuityWorkflow,
  resolveOperationsContinuityBreakCase,
  resolveOperationsContinuitySecurityMasterMappings,
  runOperationsContinuityReconciliation,
  resetDevelopmentFixtureUsage,
  submitOperationsContinuityApproval,
  startOperationsContinuityWorkflow,
  validateOperationsContinuityLedger
} from "@/lib/api";

describe("operations continuity API command wiring", () => {
  const fetchMock = vi.fn();

  beforeEach(() => {
    fetchMock.mockReset();
    fetchMock.mockResolvedValue({
      ok: true,
      status: 200,
      headers: { get: () => null },
      json: async () => ({ success: true, workflow: {}, blockers: [], message: null })
    });
    vi.stubGlobal("fetch", fetchMock);
    resetDevelopmentFixtureUsage();
  });

  it("gets checklist detail from the shared close checklist endpoint", async () => {
    await getOperationsContinuityChecklist("workflow / 1");

    expect(fetchMock).toHaveBeenCalledWith(
      "/api/workstation/operations/continuity/workflow%20%2F%201/checklist",
      expect.objectContaining({
        headers: { Accept: "application/json" }
      })
    );
  });

  it("gets close readiness from the shared close readiness endpoint", async () => {
    await getOperationsContinuityCloseReadiness("workflow / 1");

    expect(fetchMock).toHaveBeenCalledWith(
      "/api/workstation/operations/continuity/workflow%20%2F%201/close-readiness",
      expect.objectContaining({
        headers: { Accept: "application/json" }
      })
    );
  });

  it("gets timeline, break, and ledger-preview evidence from the shared workflow read endpoints", async () => {
    await getOperationsContinuityTimeline("workflow / 1");
    await getOperationsContinuityBreaks("workflow / 1");
    await getOperationsContinuityLedgerPreview("workflow / 1");

    expect(fetchMock).toHaveBeenNthCalledWith(
      1,
      "/api/workstation/operations/continuity/workflow%20%2F%201/timeline",
      expect.objectContaining({
        headers: { Accept: "application/json" }
      })
    );
    expect(fetchMock).toHaveBeenNthCalledWith(
      2,
      "/api/workstation/operations/continuity/workflow%20%2F%201/breaks",
      expect.objectContaining({
        headers: { Accept: "application/json" }
      })
    );
    expect(fetchMock).toHaveBeenNthCalledWith(
      3,
      "/api/workstation/operations/continuity/workflow%20%2F%201/ledger-preview",
      expect.objectContaining({
        headers: { Accept: "application/json" }
      })
    );
  });

  it("posts receive, matching, ledger, reconciliation, and approval commands to shared operations endpoints", async () => {
    const startRequest = {
      fundAccountId: "11111111-1111-1111-1111-111111111111",
      periodId: "2026-05",
      actor: "browser-operator",
      brokerSource: "custodian",
      rationale: "Start the retained close workflow from browser command routing."
    };
    const transitionRequest = {
      expectedVersion: 2,
      actor: "browser-operator",
      rationale: "Retained activity evidence was reviewed.",
      correlationId: "ops-command-1",
      evidenceReferenceIds: ["evidence:activity-import"]
    };
    const postureRequest = {
      ...transitionRequest,
      providerAccountLinked: true,
      securityCoverageIssueCount: 1,
      openCriticalBreakCount: 0,
      reportPackReady: false
    };
    const securityRequest = {
      expectedVersion: 3,
      actor: "browser-operator",
      unresolvedInstrumentCount: 0,
      overrideRequestCount: 1,
      overridesApproved: false,
      missingAccountingTermCount: 0,
      rationale: "Security Master mappings were reviewed."
    };
    const overrideRequest = {
      expectedVersion: 4,
      actor: "browser-operator",
      overrideId: "override / 1",
      rationale: "Controller approved the retained Security Master override.",
      policyReference: "sm-override-policy-v1",
      expiresOn: "2026-06-30"
    };
    const ledgerDraftRequest = {
      expectedVersion: 5,
      actor: "browser-operator",
      previewId: "ledger-preview-1",
      isBalanced: true,
      ledgerBatchId: "ledger-batch-1",
      rationale: "Build retained ledger draft."
    };
    const ledgerValidateRequest = {
      expectedVersion: 6,
      actor: "browser-operator",
      isBalanced: true,
      periodOpen: true,
      rationale: "Validate balanced ledger draft."
    };
    const ledgerPostRequest = {
      expectedVersion: 7,
      actor: "browser-operator",
      ledgerBatchId: "ledger-batch-1",
      postingKind: "Originating",
      periodOpen: true,
      rationale: "Post the validated retained journal."
    };
    const reconciliationRequest = {
      expectedVersion: 8,
      actor: "browser-operator",
      sourceRunId: "statement-run-1",
      reconciliationRunId: "recon-run-1",
      expectedAccountingEventCount: 6,
      expectedJournalPreviewCount: 6,
      rationale: "Run shared reconciliation for the selected workflow."
    };
    const approvalSubmitRequest = {
      expectedVersion: 9,
      actor: "browser-operator",
      reviewer: "fund-controller",
      rationale: "Submit retained report-pack evidence for approval.",
      reportPackId: "report-pack-2026-05"
    };

    await startOperationsContinuityWorkflow(startRequest);
    await importOperationsContinuityBrokerData("workflow / 1", transitionRequest);
    await normalizeOperationsContinuityBrokerTransactions("workflow / 1", transitionRequest);
    await refreshOperationsContinuityGatePosture("workflow / 1", postureRequest);
    await resolveOperationsContinuitySecurityMasterMappings("workflow / 1", securityRequest);
    await approveOperationsContinuitySecurityMasterOverride("workflow / 1", "override / 1", overrideRequest);
    await draftOperationsContinuityLedger("workflow / 1", ledgerDraftRequest);
    await validateOperationsContinuityLedger("workflow / 1", ledgerValidateRequest);
    await postOperationsContinuityLedger("workflow / 1", ledgerPostRequest);
    await runOperationsContinuityReconciliation("workflow / 1", reconciliationRequest);
    await submitOperationsContinuityApproval("workflow / 1", approvalSubmitRequest);

    const expectedCalls = [
      ["/api/workstation/operations/continuity", startRequest],
      ["/api/workstation/operations/continuity/workflow%20%2F%201/broker/import", transitionRequest],
      ["/api/workstation/operations/continuity/workflow%20%2F%201/broker/normalize", transitionRequest],
      ["/api/workstation/operations/continuity/workflow%20%2F%201/posture/refresh", postureRequest],
      ["/api/workstation/operations/continuity/workflow%20%2F%201/security-master/resolve", securityRequest],
      [
        "/api/workstation/operations/continuity/workflow%20%2F%201/security-master/overrides/override%20%2F%201/approve",
        overrideRequest
      ],
      ["/api/workstation/operations/continuity/workflow%20%2F%201/ledger/draft", ledgerDraftRequest],
      ["/api/workstation/operations/continuity/workflow%20%2F%201/ledger/validate", ledgerValidateRequest],
      ["/api/workstation/operations/continuity/workflow%20%2F%201/ledger/post", ledgerPostRequest],
      ["/api/workstation/operations/continuity/workflow%20%2F%201/reconciliation/run", reconciliationRequest],
      ["/api/workstation/operations/continuity/workflow%20%2F%201/approval/submit", approvalSubmitRequest]
    ] as const;

    expectedCalls.forEach(([endpoint, request], index) => {
      expect(fetchMock).toHaveBeenNthCalledWith(
        index + 1,
        endpoint,
        expect.objectContaining({
          method: "POST",
          body: JSON.stringify(request)
        })
      );
    });
  });

  it("posts close requests to the governed workflow close endpoint", async () => {
    const request = {
      expectedVersion: 8,
      actor: "browser-operator",
      rationale: "Controller close package retained.",
      reportPackId: "report-pack-2026-05",
      checklistControlApprovals: [
        {
          taskId: "close-gate-reportpack",
          approvedBy: "fund-controller",
          approvedAtUtc: "2026-05-10T18:42:00Z"
        }
      ],
      correlationId: "close-command-2026-05",
      closePackageId: "close-package-2026-05",
      closePackageManifestId: "close-package-2026-05-manifest",
      closePackageEvidenceHash: "hash-2026-05",
      closePackageRetainedManifestRoute: "/workstation/accounting/operations-continuity/workflow-1/close-package"
    };

    await closeOperationsContinuityWorkflow("workflow / 1", request);

    expect(fetchMock).toHaveBeenCalledWith(
      "/api/workstation/operations/continuity/workflow%20%2F%201/close",
      expect.objectContaining({
        method: "POST",
        body: JSON.stringify(request)
      })
    );
  });

  it("posts checklist acknowledgement requests to the shared close checklist command endpoint", async () => {
    const request = {
      expectedVersion: 6,
      actor: "browser-operator",
      rationale: "Retained evidence pointer and control approval were reviewed.",
      correlationId: "ack-close-gate-ledgerposting"
    };

    await acknowledgeOperationsContinuityChecklistTask("workflow / 1", "task / 1", request);

    expect(fetchMock).toHaveBeenCalledWith(
      "/api/workstation/operations/continuity/workflow%20%2F%201/checklist/task%20%2F%201/acknowledge",
      expect.objectContaining({
        method: "POST",
        body: JSON.stringify(request)
      })
    );
  });

  it("posts break assignment requests to the shared operations continuity command endpoint", async () => {
    const request = {
      expectedVersion: 4,
      actor: "browser-operator",
      owner: "fund-controller",
      rationale: "Assign aged cash variance to controller review.",
      escalationLevel: "Level 2",
      escalationReason: "Aged cash variance past controller SLA",
      dueDate: "2026-05-09",
      correlationId: "assign-recon-break-42"
    };

    await assignOperationsContinuityBreakCase("workflow / 1", "break / 1", request);

    expect(fetchMock).toHaveBeenCalledWith(
      "/api/workstation/operations/continuity/workflow%20%2F%201/reconciliation/breaks/break%20%2F%201/assign",
      expect.objectContaining({
        method: "POST",
        body: JSON.stringify(request)
      })
    );
  });

  it("posts break resolution requests to the shared operations continuity command endpoint", async () => {
    const request = {
      expectedVersion: 5,
      actor: "browser-operator",
      resolutionStatus: "Resolved",
      rationale: "Accepted retained custodian statement evidence and closed the cash break.",
      correlationId: "resolve-recon-break-42",
      evidenceLinks: [
        {
          evidenceId: "recon-break-close-evidence-1",
          label: "Custodian statement case close evidence",
          route: "/workstation/accounting/reconciliation/recon-break-42/evidence",
          source: "operations-continuity",
          capturedAtUtc: "2026-05-08T15:34:00Z"
        }
      ]
    };

    await resolveOperationsContinuityBreakCase("workflow / 1", "break / 1", request);

    expect(fetchMock).toHaveBeenCalledWith(
      "/api/workstation/operations/continuity/workflow%20%2F%201/reconciliation/breaks/break%20%2F%201/resolve",
      expect.objectContaining({
        method: "POST",
        body: JSON.stringify(request)
      })
    );
  });

  it("posts governed reopen requests with incident metadata", async () => {
    const request = {
      expectedVersion: 9,
      actor: "browser-admin",
      rationale: "Reopen period to attach corrected custodian activity.",
      incidentId: "incident-2026-05-close-restatement",
      isGovernedAdmin: true,
      justification: "Controller approved exception remediation.",
      approvalReference: "admin-approval-42",
      impactSummary: "Ledger and report package will be regenerated with retained restatement evidence.",
      correlationId: "reopen-command-2026-05"
    };

    await reopenOperationsContinuityWorkflow("workflow / 1", request);

    expect(fetchMock).toHaveBeenCalledWith(
      "/api/workstation/operations/continuity/workflow%20%2F%201/reopen",
      expect.objectContaining({
        method: "POST",
        body: JSON.stringify(request)
      })
    );
  });
});
