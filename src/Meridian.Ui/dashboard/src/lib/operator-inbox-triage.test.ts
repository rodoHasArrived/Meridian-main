import { beforeEach, describe, expect, it, vi } from "vitest";
import { apiGetJson, apiPostJson } from "@/lib/api";
import { ApiError } from "@/lib/api-errors";
import { commandOperatorTriage, getOperatorTriageInbox, isActiveTriageSnooze, routeForTriageItem, safeTriageRoute, type OperatorTriageItem } from "@/lib/operator-inbox-triage";

vi.mock("@/lib/api", () => ({ apiGetJson: vi.fn(), apiPostJson: vi.fn() }));
const row: OperatorTriageItem = { workItemId: "break/1", label: "Cash break", detail: "Review cash evidence", workspace: "accounting", targetRoute: "/api/workstation/operator/inbox", kind: "ReconciliationBreak", targetPageTag: "FundReconciliation",
  tone: "Critical", createdAt: "2026-01-01T00:00:00Z", ownerId: null, dueAt: null, version: 2, sourceVersion: 3, acknowledgedAt: null, snoozedUntil: null,
  capabilities: { canAssign: true, canAcknowledge: true, canSnooze: false, canUnsnooze: false, disabledReason: null }, activity: [] };

describe("operator inbox authority client", () => {
  beforeEach(() => vi.resetAllMocks());
  it("uses authoritative reads with escaped account scope and rejects a malformed inbox", async () => {
    vi.mocked(apiGetJson).mockResolvedValue({ actorId: "operator", items: [row], persistenceAvailable: true });
    await getOperatorTriageInbox("account&other=1", { allowDevelopmentFallback: true });
    expect(apiGetJson).toHaveBeenCalledWith("/api/workstation/operator/triage?fundAccountId=account%26other%3D1", expect.objectContaining({ allowDevelopmentFallback: false }));
    vi.mocked(apiGetJson).mockResolvedValue({ actorId: "", items: [], persistenceAvailable: true });
    await expect(getOperatorTriageInbox()).rejects.toThrow("invalid authority response");
    vi.mocked(apiGetJson).mockResolvedValue({ actorId: "operator", items: [row, row], persistenceAvailable: true });
    await expect(getOperatorTriageInbox()).rejects.toThrow("invalid authority response");
  });
  it("preserves the reviewed command identity and treats a retained version refusal as known", async () => {
    const command = { commandId: "command-1", action: "Assign" as const, expectedVersion: 2, expectedSourceVersion: 3, reason: "Cash desk review", assigneeId: "cash-operator" };
    vi.mocked(apiPostJson).mockRejectedValue(new ApiError({ path: "triage", status: 409, responseBody: JSON.stringify({ status: "Conflict", workItemId: row.workItemId, error: "Version changed", replayed: false }) }));
    await expect(commandOperatorTriage(row.workItemId, command)).resolves.toMatchObject({ status: "Conflict", error: "Version changed" });
    expect(apiPostJson).toHaveBeenCalledWith("/api/workstation/operator/triage/break%2F1/actions", command, expect.objectContaining({ allowDevelopmentFallback: false }));
  });
  it("keeps interrupted command responses uncertain", async () => {
    vi.mocked(apiPostJson).mockRejectedValue(new Error("connection interrupted"));
    await expect(commandOperatorTriage(row.workItemId, { commandId: "same-command", action: "Acknowledge", expectedVersion: 2, reason: "Review started" })).rejects.toThrow("connection interrupted");
  });
  it("never hides a critical item behind an existing snooze", () => {
    expect(isActiveTriageSnooze({ ...row, snoozedUntil: "2030-01-01T00:00:00Z" }, Date.parse("2026-01-01"))).toBe(false);
    expect(isActiveTriageSnooze({ ...row, tone: "Warning", snoozedUntil: "2030-01-01T00:00:00Z" }, Date.parse("2026-01-01"))).toBe(true);
  });
  it("maps API targets through canonical browser routes and rejects external targets", () => {
    expect(safeTriageRoute("/api/workstation/operator/inbox")).toBeNull();
    expect(safeTriageRoute("//outside.example")).toBeNull();
    expect(safeTriageRoute("https://outside.example/accounting")).toBeNull();
    expect(routeForTriageItem(row)).toBe("/accounting/reconciliation");
  });
});
