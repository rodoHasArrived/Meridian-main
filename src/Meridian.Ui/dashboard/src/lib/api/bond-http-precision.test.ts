import { afterEach, expect, it, vi } from "vitest";
import { getBondAnalytics } from "./bond-analytics.api";
import { bondDecimalText } from "../bond-decimal-format";

afterEach(() => vi.unstubAllGlobals());

it("keeps fixed-income HTTP decimal strings exact through response parsing and browser formatting", async () => {
  // The matching TestServer endpoint test verifies these exact wire values are strings.
  const wire = '{"cleanPrice":"98.50000000000000000000000001","yieldToMaturity":"0.0500000000000000000000000001","cashFlows":[{"interest":"9007199254740993.123456789"}]}';
  vi.stubGlobal("fetch", vi.fn().mockResolvedValue(new Response(wire, { status: 200 })));
  const result = await getBondAnalytics("precision-bond", { asOf: "2026-11-12T00:00:00Z", settlementDate: "2026-11-13", cleanPrice: "98.50000000000000000000000001" });
  expect(typeof result.cleanPrice).toBe("string");
  expect(bondDecimalText(result.cleanPrice)).toBe("98.50000000000000000000000001");
  expect(bondDecimalText(result.yieldToMaturity)).toBe("0.0500000000000000000000000001");
  expect(bondDecimalText(result.cashFlows[0].interest)).toBe("9,007,199,254,740,993.123456789");
});
