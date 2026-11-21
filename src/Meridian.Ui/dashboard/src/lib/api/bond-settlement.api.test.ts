import { prepareBondSettlement } from "./bond-settlement.api";
import type { BondSettlementRequest } from "@/types/bond-settlement.types";

const request: BondSettlementRequest = {
  analytics: { securityId: "bond", context: { asOf: "2026-11-13T00:00:00Z", settlementDate: "2026-11-16", securityVersion: 7 }, cleanPrice: "99.123456789012345678", yieldConvention: "NominalPeriodic" },
  originalFace: "100000", bookedFactor: "0.9999999999999999", fees: "0", observedDirtyCash: "99123.46",
  sourceEvidenceId: "evidence", cashAccountId: "cash", accruedInterestAccountId: "interest", feesAccountId: null
};
afterEach(() => vi.unstubAllGlobals());
it("posts exact settlement decimal text and cancellation to the retained preparation service", async () => {
  const fetch = vi.fn().mockResolvedValue(new Response("{}")); vi.stubGlobal("fetch", fetch);
  const controller = new AbortController(); await prepareBondSettlement(request, { signal: controller.signal });
  const [path, options] = fetch.mock.calls[0]!;
  expect(path).toBe("/api/reference-data/bonds/settlement-consideration");
  expect(options.signal).toBe(controller.signal); expect(JSON.parse(options.body)).toEqual(request);
});
it("propagates unavailable responses instead of supplying simulated settlement", async () => {
  vi.stubGlobal("fetch", vi.fn().mockResolvedValue(new Response('{"error":"Unavailable"}', { status: 503 })));
  await expect(prepareBondSettlement(request)).rejects.toMatchObject({ status: 503 });
});
