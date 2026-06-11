import { beforeEach, describe, expect, it, vi } from "vitest";
import {
  closeOperationsContinuityWorkflow,
  reopenOperationsContinuityWorkflow,
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
