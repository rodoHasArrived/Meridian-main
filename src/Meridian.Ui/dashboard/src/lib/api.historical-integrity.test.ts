import { afterEach, describe, expect, it, vi } from "vitest";
import { fetchQuantData } from "@/lib/api";

afterEach(() => vi.unstubAllGlobals());

describe("quant historical query integrity", () => {
  it.each([
    { success: false },
    { success: true, isPartial: true },
    { success: true, fileFailures: [{ sourceFile: "AAPL/trades.jsonl.gz", error: "Invalid gzip" }] }
  ])("rejects incomplete historical data before constructing notebook bars: %j", async (outcome) => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(new Response(JSON.stringify({
      ...outcome, message: "Historical query incomplete.", bars: [], totalBars: 0
    }), { status: 200, headers: { "Content-Type": "application/json" } })));

    await expect(fetchQuantData({ symbol: "AAPL", from: "2026-01-01", to: "2026-01-02", interval: "minute" }))
      .rejects.toThrow("Historical query incomplete.");
  });
});
