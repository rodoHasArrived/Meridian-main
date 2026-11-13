import { afterEach, describe, expect, it, vi } from "vitest";
import { calculateBondWorkflow, getBondMonitoring, planAccountBondIncome, previewBondIncome } from "./bond-workflow.api";
import type { BondWorkflowRequest } from "@/types/bond-workflow.types";

afterEach(() => vi.unstubAllGlobals());
describe("authoritative bond workflow APIs", () => {
  it("preserves exact decimal input, explicit dates, identifiers and cancellation", async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response("{}", { status: 200 }));
    vi.stubGlobal("fetch", fetchMock); const controller = new AbortController();
    const request: BondWorkflowRequest = { securityId: "bond /id", context: { asOf: "2026-11-12T00:00:00Z", settlementDate: "2026-11-16" }, originalFace: "10000.123456789012345678", cleanPricePer100: "99.123456789012345678", quoteSettlementDate: "2026-11-13", fees: "1.25", priceBasis: "NominalClean", rateShiftBps: "-25", spreadShiftBps: "50" };
    await calculateBondWorkflow(request, { signal: controller.signal });
    const [path, options] = fetchMock.mock.calls[0];
    expect(path).toBe("/api/reference-data/bonds/bond%20%2Fid/workflow"); expect(options.method).toBe("POST");
    expect(JSON.parse(options.body)).toEqual(request); expect(options.signal).toBe(controller.signal);
  });
  it("encodes account and book scope on planning and monitoring routes", async () => {
    const fetchMock = vi.fn().mockImplementation(() => Promise.resolve(new Response("[]", { status: 200 })));
    vi.stubGlobal("fetch", fetchMock);
    await getBondMonitoring("book /id", "account /id");
    expect(fetchMock.mock.calls[0][0]).toBe("/api/ledger/books/book%20%2Fid/accounts/account%20%2Fid/bond-monitoring");
    await planAccountBondIncome("book", "account", { scope: { tenantId: "", companyId: "", accountId: "account", bookId: "book", baseCurrency: "" }, periodStart: "2026-11-12", periodEnd: "2027-11-11", context: { asOf: "2026-11-12T00:00:00Z" }, openingBalances: [], fxConversions: [], minimumCashThreshold: 0, bucketDays: 7 });
    expect(fetchMock.mock.calls[1][0]).toBe("/api/ledger/books/book/accounts/account/bond-income/preview");
    await previewBondIncome("book", "account", { ledgerBookId: "book", taxLotRecordId: "lot", throughDate: "2026-11-12", knownAt: "2026-11-12T10:00:00Z" });
    expect(fetchMock.mock.calls[2][0]).toBe("/api/ledger/books/book/accounts/account/bond-accounting/preview");
  });
  it("propagates service failures without seeded results", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(new Response('{"detail":"Retained source unavailable"}', { status: 503 })));
    await expect(getBondMonitoring("book", "account", { allowDevelopmentFallback: true })).rejects.toMatchObject({ status: 503 });
  });
  it("uses the authorized account route for account-specific pricing and requires its book", async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response("{}", { status: 200 })); vi.stubGlobal("fetch", fetchMock);
    const request: BondWorkflowRequest = { securityId: "bond /id", context: { asOf: "2026-11-12T00:00:00Z", settlementDate: "2026-11-16" }, originalFace: "10000", fees: "0", priceBasis: "NominalClean", rateShiftBps: "0", spreadShiftBps: "0", accountId: "account /id" };
    await expect(calculateBondWorkflow(request)).rejects.toThrow("authorized ledger book"); expect(fetchMock).not.toHaveBeenCalled();
    await calculateBondWorkflow(request, { ledgerBookId: "book /id" });
    expect(fetchMock.mock.calls[0][0]).toBe("/api/ledger/books/book%20%2Fid/accounts/account%20%2Fid/bond-workflows/bond%20%2Fid");
  });
});
