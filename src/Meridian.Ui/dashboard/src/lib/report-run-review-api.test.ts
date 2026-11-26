// @vitest-environment node
import { beforeEach, describe, expect, it, vi } from "vitest";
import { apiGetJson } from "@/lib/api";
import { getReportRunReview } from "@/lib/report-run-review-api";
vi.mock("@/lib/api", () => ({ apiGetJson: vi.fn() }));
const result = { reviewId: "a".repeat(64), runId: "run/1", version: 3, snapshotHash: "b".repeat(64),
  baselineRunId: null, baselineVersion: null, parameters: [], sourceChanges: [], lines: [],
  materialityOutcome: "NotAssessed", materialChangeCount: null, unavailableReasons: [] };
describe("report run review API", () => {
  beforeEach(() => vi.resetAllMocks());
  it("loads the exact run with browser fixture fallback disabled", async () => {
    vi.mocked(apiGetJson).mockResolvedValue(result);
    expect(await getReportRunReview("run/1")).toEqual(result);
    expect(apiGetJson).toHaveBeenCalledWith("/api/fund-structure/reporting/runs/run%2F1/review", { allowDevelopmentFallback: false });
  });
  it("rejects evidence for a different run", async () => {
    vi.mocked(apiGetJson).mockResolvedValue({ ...result, runId: "other" });
    await expect(getReportRunReview("run/1")).rejects.toThrow("invalid report review evidence");
  });
});
