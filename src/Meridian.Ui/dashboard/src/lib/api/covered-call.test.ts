import { beforeEach, describe, expect, it, vi } from "vitest";
import {
  cancelCoveredCallRun,
  getCoveredCallRunResult,
  listCoveredCallRuns,
  previewCoveredCallChain,
  startCoveredCallBacktest
} from "@/lib/api/covered-call";

describe("covered-call API client", () => {
  const fetchMock = vi.fn();

  beforeEach(() => {
    fetchMock.mockReset();
    fetchMock.mockResolvedValue({ ok: true, json: async () => ({}), text: async () => "{}" });
    vi.stubGlobal("fetch", fetchMock);
  });

  it("routes every request through the shared dashboard fetch helpers", async () => {
    const controller = new AbortController();

    await startCoveredCallBacktest(buildRequest(), controller.signal);
    await listCoveredCallRuns(25, controller.signal);
    await getCoveredCallRunResult("run / 1", controller.signal);
    await cancelCoveredCallRun("run / 1", controller.signal);
    await previewCoveredCallChain({
      underlyingSymbol: "SPY",
      asOf: "2026-01-15",
      minStrike: 500,
      maxDelta: 0.35,
      minDte: 7,
      maxDte: 45,
      minOpenInterest: 100,
      minVolume: 25,
      maxSpreadPct: 0.1
    }, controller.signal);

    expect(fetchMock).toHaveBeenNthCalledWith(1, "/api/strategies/covered-call/runs", expect.objectContaining({
      method: "POST",
      signal: controller.signal
    }));
    expect(fetchMock).toHaveBeenNthCalledWith(2, "/api/strategies/covered-call/runs?limit=25", expect.objectContaining({
      headers: { Accept: "application/json" },
      signal: controller.signal
    }));
    expect(fetchMock).toHaveBeenNthCalledWith(3, "/api/strategies/covered-call/runs/run%20%2F%201/result", expect.anything());
    expect(fetchMock).toHaveBeenNthCalledWith(4, "/api/strategies/covered-call/runs/run%20%2F%201/cancel", expect.objectContaining({
      method: "POST"
    }));
    expect(fetchMock).toHaveBeenNthCalledWith(5, "/api/strategies/covered-call/chain-preview", expect.objectContaining({
      method: "POST"
    }));
  });

  it("preserves shared backend problem details for covered-call errors", async () => {
    fetchMock.mockResolvedValueOnce({
      ok: false,
      status: 422,
      text: async () => JSON.stringify({
        title: "One or more validation errors occurred.",
        errors: {
          minStrike: ["Minimum strike must be positive."]
        }
      })
    });

    await expect(startCoveredCallBacktest(buildRequest())).rejects.toThrow(
      "Request failed for /api/strategies/covered-call/runs (422) - One or more validation errors occurred. minStrike: Minimum strike must be positive."
    );
  });

  it("rejects blank covered-call run identifiers before issuing a malformed fetch", async () => {
    await expect(getCoveredCallRunResult("   ")).rejects.toThrow("runId is required");
    expect(fetchMock).not.toHaveBeenCalled();
  });
});

function buildRequest() {
  return {
    underlyingSymbol: "SPY",
    from: "2026-01-01",
    to: "2026-01-31",
    initialCash: 100_000,
    overwriteRatio: 0.5,
    minStrike: 500,
    maxDelta: 0.35,
    minDte: 7,
    maxDte: 45,
    minIvPercentile: 50,
    minOpenInterest: 100,
    minVolume: 25,
    maxSpreadPct: 0.1,
    takeProfitCapture: 0.8,
    rollDelta: 0.55,
    exDivWindowDays: 7,
    scoringMode: "Basic" as const,
    depthBonusWeight: 0.05,
    riskFreeRate: 0.04,
    initialUnderlyingShares: 100,
    label: null
  };
}
