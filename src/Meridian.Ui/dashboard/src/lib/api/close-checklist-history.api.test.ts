import { vi } from "vitest";
import { apiGetJson } from "@/lib/api";
import { getCloseChecklistHistory } from "./close-checklist-history.api";

vi.mock("@/lib/api", () => ({ apiGetJson: vi.fn() }));
const response = { workflowId: "workflow-1", asOf: "2026-11-11T12:00:00Z", status: "Ready", reason: null,
  coverageStart: null, coverageEnd: null, isPartial: false, missingAuditIds: [], versionGaps: [], observations: [] };

it("uses the exact shared workflow subject and prohibits demo proof fallback", async () => {
  vi.mocked(apiGetJson).mockResolvedValue(response);
  await getCloseChecklistHistory("workflow-1", { allowDevelopmentFallback: true });
  expect(apiGetJson).toHaveBeenCalledWith("/api/workstation/operations/continuity/workflow-1/checklist/history", { allowDevelopmentFallback: false });
  await expect(getCloseChecklistHistory("workflow-2")).rejects.toThrow(/does not match/);
});

it("refuses malformed retained counts instead of drawing a misleading success", async () => {
  vi.mocked(apiGetJson).mockResolvedValue({ ...response, observations: [{ auditId: "audit-1", recordedAtUtc: "2026-11-11T11:00:00Z", workflowVersion: 1, totalTasks: 5, remainingTasks: 0, blockedTasks: 1, segment: 0 }] });
  await expect(getCloseChecklistHistory("workflow-1")).rejects.toThrow(/incomplete/);
});
