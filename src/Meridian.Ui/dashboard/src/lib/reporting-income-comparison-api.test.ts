import { beforeEach, describe, expect, it, vi } from "vitest";
import { apiGetJson, apiPostJson } from "@/lib/api";
import { createIncomeComparison, getIncomeComparison, getIncomeComparisonCandidates, getIncomeContributionSupport } from "@/lib/reporting-income-comparison-api";
import { buildIncomeComparison, buildIncomeContributionSupport, incomeComparisonCandidates, publishedIncomeRun } from "@/test/reporting-income-comparison-fixtures";

vi.mock("@/lib/api", () => ({ apiGetJson: vi.fn(), apiPostJson: vi.fn() }));

const root = "/api/fund-structure/reporting/comparisons";
const request = { baselineRunId: "published-1", currentRunId: "restated-2", gridId: "income", metricColumn: "Interest" };

describe("retained investment-income comparison API", () => {
  beforeEach(() => {
    vi.resetAllMocks();
    vi.mocked(apiGetJson).mockResolvedValue(buildIncomeComparison());
    vi.mocked(apiPostJson).mockResolvedValue(buildIncomeComparison());
  });

  it("reads candidates authoritatively and preserves cancellation even when fallback is requested", async () => {
    const controller = new AbortController();
    vi.mocked(apiGetJson).mockResolvedValue([]);

    expect(await getIncomeComparisonCandidates({ signal: controller.signal, allowDevelopmentFallback: true })).toEqual([]);
    expect(apiGetJson).toHaveBeenCalledWith(`${root}/candidates`, { signal: controller.signal, allowDevelopmentFallback: false });
  });

  it("posts the explicitly selected retained pair and measure without development fallback", async () => {
    const controller = new AbortController();
    await createIncomeComparison(request, { signal: controller.signal, allowDevelopmentFallback: true });

    expect(apiPostJson).toHaveBeenCalledWith(root, request, { signal: controller.signal, allowDevelopmentFallback: false });
  });

  it("reads a saved comparison by its encoded identity without recalculating", async () => {
    await getIncomeComparison("comparison/June #1");

    expect(apiGetJson).toHaveBeenCalledWith(`${root}/comparison%2FJune%20%231`, { allowDevelopmentFallback: false });
    expect(apiPostJson).not.toHaveBeenCalled();
  });

  it("opens support under the exact comparison and encoded contribution identity", async () => {
    const controller = new AbortController();
    vi.mocked(apiGetJson).mockResolvedValue(buildIncomeContributionSupport());
    await getIncomeContributionSupport("comparison/1", "journal:late/accrual #17", { signal: controller.signal, allowDevelopmentFallback: true });

    expect(apiGetJson).toHaveBeenCalledWith(
      `${root}/comparison%2F1/contributions/journal%3Alate%2Faccrual%20%2317`,
      { signal: controller.signal, allowDevelopmentFallback: false }
    );
  });

  it.each([
    ["candidate discovery", () => getIncomeComparisonCandidates()],
    ["saved comparison", () => getIncomeComparison("comparison-1")],
    ["support record", () => getIncomeContributionSupport("comparison-1", "journal-17")]
  ])("preserves failed %s reads instead of substituting sample evidence", async (_label, read) => {
    vi.mocked(apiGetJson).mockRejectedValue(new Error("Retained store unavailable"));
    await expect(read()).rejects.toThrow("Retained store unavailable");
  });

  it("preserves retention failures without returning a locally explained result", async () => {
    vi.mocked(apiPostJson).mockRejectedValue(new Error("Retention failed"));
    await expect(createIncomeComparison(request)).rejects.toThrow("Retention failed");
  });

  it("returns original and restated candidate identities unchanged", async () => {
    vi.mocked(apiGetJson).mockResolvedValue(incomeComparisonCandidates);
    expect(await getIncomeComparisonCandidates()).toEqual(incomeComparisonCandidates);
  });

  it.each([
    { payload: {} },
    { payload: [null] },
    { payload: [{ ...publishedIncomeRun, grids: [{ gridId: "income", metrics: null }] }] }
  ])("rejects a malformed candidate response instead of rendering an invented selection", async ({ payload }) => {
    vi.mocked(apiGetJson).mockResolvedValue(payload);
    await expect(getIncomeComparisonCandidates()).rejects.toThrow("Invalid retained comparison candidates");
  });

  it.each([
    { payload: null },
    { payload: {} },
    { payload: buildIncomeComparison({ residualAmount: Number.NaN }) },
    { payload: buildIncomeComparison({ explainedAmount: Number.POSITIVE_INFINITY }) }
  ])("rejects malformed or non-finite retained comparison evidence", async ({ payload }) => {
    vi.mocked(apiGetJson).mockResolvedValue(payload);
    vi.mocked(apiPostJson).mockResolvedValue(payload);
    await expect(getIncomeComparison("comparison-1")).rejects.toThrow("Invalid retained income comparison");
    await expect(createIncomeComparison(request)).rejects.toThrow("Invalid retained income comparison");
  });

  it.each([
    { payload: {} },
    { payload: { ...buildIncomeContributionSupport(), currentRecords: [{ Income: { unsupported: "nested value" } }] } },
    { payload: { ...buildIncomeContributionSupport(), evidenceReferences: [null] } }
  ])("rejects malformed supporting records without replacing them with live evidence", async ({ payload }) => {
    vi.mocked(apiGetJson).mockResolvedValue(payload);
    await expect(getIncomeContributionSupport("comparison-1", "journal-17")).rejects.toThrow("Invalid retained contribution support");
  });
});
