import { beforeEach, describe, expect, it, vi } from "vitest";
import {
  assignOperationsContinuityBreakCase,
  closeOperationsContinuityWorkflow,
  reopenOperationsContinuityWorkflow,
  resolveOperationsContinuityBreakCase,
  resetDevelopmentFixtureUsage
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
