import { afterEach, describe, expect, it, vi } from "vitest";
import { getAssetMarketValuation } from "./asset-market-valuation.api";
import { marketValuationFixture, marketValuationQuery } from "@/test/asset-market-valuation-fixture";
afterEach(() => vi.unstubAllGlobals());
const respond = (changes = {}) =>
  vi.stubGlobal(
    "fetch",
    vi.fn().mockResolvedValue(new Response(JSON.stringify({ ...marketValuationFixture, ...changes }), { status: 200 }))
  );
describe("market valuation read adapter", () => {
  it("sends only retained identities and preserves exact server decimal text", async () => {
    respond({ marketValue: "12345678901234567890.12345678", unrealizedPnl: "0.0000000000000000000000000001" });
    const controller = new AbortController();
    const result = await getAssetMarketValuation("sec-aapl", marketValuationQuery, "USD", {
      signal: controller.signal
    });
    expect(result.marketValue).toBe("12345678901234567890.12345678");
    const [url, options] = vi.mocked(fetch).mock.calls[0]!;
    const query = new URL(String(url), "https://test.invalid").searchParams;
    expect(query.get("economicStateId")).toBe("state-1");
    expect(query.get("economicStateVersion")).toBe("2");
    expect(query.get("accountId")).toBe("account-1");
    expect(query.has("price")).toBe(false);
    expect(query.has("quantity")).toBe(false);
    expect(options?.signal).toBe(controller.signal);
  });
  it.each([
    "securityId",
    "positionId",
    "ledgerBookId",
    "accountId",
    "economicStateId",
    "valuationDate",
    "economicStateVersion"
  ])("rejects a foreign %s", async (field) => {
    respond({ [field]: field === "economicStateVersion" ? 99 : "foreign" });
    await expect(getAssetMarketValuation("sec-aapl", marketValuationQuery, "USD")).rejects.toThrow("does not match");
  });
  it.each([
    { marketValue: 1406 },
    { unrealizedPnl: "1e-28" },
    { currency: "EUR" },
    { isStaleFallback: true },
    { status: "Blocked" }
  ])("withholds incompatible or precision-losing values %j", async (changes) => {
    respond(changes);
    await expect(getAssetMarketValuation("sec-aapl", marketValuationQuery, "USD")).rejects.toThrow();
  });
  it("retains a partial market value and explicit P/L blocker", async () => {
    respond({
      status: "Partial",
      unrealizedPnl: null,
      blockers: [
        { metric: "UnrealizedPnl", code: "CarryingBasisUnknown", reason: "Accrued-interest basis is not supplied." }
      ]
    });
    expect((await getAssetMarketValuation("sec-aapl", marketValuationQuery, "USD")).marketValue).toBe("1406.00");
  });
  it("propagates service failures and rejects incomplete input before a request", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(new Response("{}", { status: 503 })));
    await expect(getAssetMarketValuation("sec-aapl", marketValuationQuery, "USD")).rejects.toMatchObject({
      status: 503
    });
    await expect(
      getAssetMarketValuation("sec-aapl", { ...marketValuationQuery, accountId: "" }, "USD")
    ).rejects.toThrow("required");
    expect(fetch).toHaveBeenCalledTimes(1);
  });
  it.each([{ selectedSource: {} }, { observedAsOf: "not a timestamp" }, { isStaleFallback: "false" }, { inputs: [] }])("withholds malformed source metadata %j", async (changes) => {
    respond(changes);
    await expect(getAssetMarketValuation("sec-aapl", marketValuationQuery, "USD")).rejects.toThrow();
  });

});
