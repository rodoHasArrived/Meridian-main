import { beforeEach, describe, expect, it, vi } from "vitest";
import { ApiError } from "@/lib/api-errors";
import {
  approveSecurityMasterRevision,
  classifyWorkbenchWriteError,
  publishSecurityMasterRevision,
  resolveSourceConflict,
  submitSecurityMasterRevision,
  updateSecurityField
} from "@/lib/api/security-master-workbench.api";

const SECURITY_ID = "11111111-1111-1111-1111-111111111111";

describe("Security Master workbench API client", () => {
  const fetchMock = vi.fn();

  beforeEach(() => {
    fetchMock.mockReset();
    fetchMock.mockResolvedValue({ ok: true, json: async () => ({}), text: async () => "{}" });
    vi.stubGlobal("fetch", fetchMock);
  });

  it("routes each governed-write through the shared POST helper at the securityId-scoped path", async () => {
    await updateSecurityField(SECURITY_ID, {
      expectedVersion: 3,
      fieldPath: "EconomicDefinition.Coupon",
      newValue: "5.125",
      effectiveFrom: "2026-03-15T00:00:00Z",
      justification: "Vendor confirmation #4821."
    });
    await resolveSourceConflict(SECURITY_ID, {
      conflictId: "c-1",
      expectedVersion: 3,
      chosenWinnerSource: "golden-copy",
      reason: "Golden copy is authoritative."
    });
    await submitSecurityMasterRevision(SECURITY_ID, {
      revisionId: "r-1",
      workflowId: "w-1",
      expectedWorkflowVersion: 1,
      reviewer: "independent.reviewer"
    });
    await approveSecurityMasterRevision(SECURITY_ID, {
      revisionId: "r-1",
      workflowId: "w-1",
      expectedWorkflowVersion: 2,
      rationale: "Reviewed.",
      reportPackId: "rp-1"
    });
    await publishSecurityMasterRevision(SECURITY_ID, { revisionId: "r-1" });

    const base = `/api/security-master/${SECURITY_ID}/workbench`;
    expect(fetchMock).toHaveBeenNthCalledWith(1, `${base}/field`, expect.objectContaining({ method: "POST" }));
    expect(fetchMock).toHaveBeenNthCalledWith(2, `${base}/resolve-conflict`, expect.objectContaining({ method: "POST" }));
    expect(fetchMock).toHaveBeenNthCalledWith(3, `${base}/submit`, expect.objectContaining({ method: "POST" }));
    expect(fetchMock).toHaveBeenNthCalledWith(4, `${base}/approve`, expect.objectContaining({ method: "POST" }));
    expect(fetchMock).toHaveBeenNthCalledWith(5, `${base}/publish`, expect.objectContaining({ method: "POST" }));
  });

  it("does not send the server-derived actor in the field-edit body", async () => {
    await updateSecurityField(SECURITY_ID, {
      expectedVersion: 3,
      fieldPath: "EconomicDefinition.Coupon",
      newValue: "5.125",
      effectiveFrom: "2026-03-15T00:00:00Z",
      justification: "Vendor confirmation #4821."
    });

    const body = JSON.parse(fetchMock.mock.calls[0][1].body as string);
    expect(body).not.toHaveProperty("actor");
    expect(body.fieldPath).toBe("EconomicDefinition.Coupon");
  });
});

describe("classifyWorkbenchWriteError", () => {
  function apiError(status: number, body: Record<string, unknown>): ApiError {
    return new ApiError({ path: "/x", status, responseBody: JSON.stringify(body) });
  }

  it("extracts the current version from a 409 version-conflict", () => {
    const result = classifyWorkbenchWriteError(apiError(409, { error: "version-conflict", currentVersion: 7 }));
    expect(result).toEqual({ kind: "version-conflict", currentVersion: 7 });
  });

  it("treats other 409s as a revision-state conflict", () => {
    const result = classifyWorkbenchWriteError(apiError(409, { error: "revision-state-conflict", message: "not approved" }));
    expect(result).toEqual({ kind: "revision-state-conflict", message: "not approved" });
  });

  it("recognizes the workflow-required 422", () => {
    expect(classifyWorkbenchWriteError(apiError(422, { error: "workflow-required" }))).toEqual({ kind: "workflow-required" });
  });

  it("maps 403 and 401 to forbidden / unauthorized", () => {
    expect(classifyWorkbenchWriteError(apiError(403, {})).kind).toBe("forbidden");
    expect(classifyWorkbenchWriteError(apiError(401, {})).kind).toBe("unauthorized");
  });

  it("classifies non-API errors as unknown with a message", () => {
    const result = classifyWorkbenchWriteError(new Error("network down"));
    expect(result.kind).toBe("unknown");
    expect((result as { message: string }).message).toBe("network down");
  });
});
