import { beforeEach, describe, expect, it, vi } from "vitest";
import { apiPostJson } from "@/lib/api";
import { createCanonicalAmortizationDraft, prepareCanonicalAmortization, type CanonicalAmortizationRequest } from "./canonical-amortization.api";
vi.mock("@/lib/api", () => ({ apiPostJson: vi.fn() }));
const request: CanonicalAmortizationRequest = { ledgerBookId: "BOOK-1", periodId: "period-1", fundProfileId: "fund-1", accountingBasis: "Primary", taxLotRecordId: "lot-1", expectedPeriodVersion: 3, asOfDate: "2026-11-01", securityEvidence: { evidenceId: "reference" }, eventEvidence: { evidenceId: "event" }, assetAccountPath: "Assets:Bonds", incomeAccountPath: "Income:Amortization", amortizeRemainingFace: false, automaticYieldToWorst: false };
const scope = { ledgerBookId: "book-1", periodId: "period-1", fundProfileId: "fund-1", accountingBasis: "Primary" };
const response = () => ({ scope, preparationFingerprint: "hash", instruction: { asOfDate: "2026-11-01", expectedLot: { taxLotRecordId: "lot-1" } }, projection: { transactionCostBasis: "999.99999999999999999999", functionalCostBasis: "999", transactionMovement: "1", functionalMovement: "1" }, lines: [{ debit: "1", credit: "0" }] });
beforeEach(() => vi.resetAllMocks());
describe("Canonical amortization API", () => {
  it("preserves exact amounts and prohibits fixture fallback", async () => {
    vi.mocked(apiPostJson).mockResolvedValue(response());
    const result = await prepareCanonicalAmortization(request, { allowDevelopmentFallback: true });
    expect(result.projection.transactionCostBasis).toBe("999.99999999999999999999");
    expect(apiPostJson).toHaveBeenCalledWith("/api/ledger/books/BOOK-1/amortization/prepare", request, { allowDevelopmentFallback: false });
  });
  it.each(["ledgerBookId", "periodId", "fundProfileId", "accountingBasis"])("refuses a foreign %s", async field => {
    const result = response(); result.scope = { ...scope, [field]: "foreign" };
    vi.mocked(apiPostJson).mockResolvedValue(result);
    await expect(prepareCanonicalAmortization(request)).rejects.toThrow("selected scope");
  });
  it("refuses a changed lot and numeric money", async () => {
    const result = response(); result.instruction.expectedLot.taxLotRecordId = "other-lot";
    vi.mocked(apiPostJson).mockResolvedValueOnce(result).mockResolvedValueOnce({ ...response(), projection: { ...response().projection, functionalMovement: 1 } });
    await expect(prepareCanonicalAmortization(request)).rejects.toThrow("selected scope");
    await expect(prepareCanonicalAmortization(request)).rejects.toThrow("exact decimal strings");
  });
  it("submits only the original identifiers, reviewed fingerprint and description", async () => {
    vi.mocked(apiPostJson).mockResolvedValue({ spine: { scope }, candidate: { journalEntryId: "journal-1" } });
    await createCanonicalAmortizationDraft(request, "reviewed-hash", "Reviewed occurrence");
    expect(apiPostJson).toHaveBeenCalledWith("/api/ledger/books/BOOK-1/amortization/drafts", { request, expectedPreparationFingerprint: "reviewed-hash", description: "Reviewed occurrence" }, { allowDevelopmentFallback: false });
  });
  it("rejects a journal response outside reviewed scope", async () => {
    vi.mocked(apiPostJson).mockResolvedValue({ spine: { scope: { ...scope, periodId: "other" } } });
    await expect(createCanonicalAmortizationDraft(request, "hash", "Reviewed")).rejects.toThrow("reviewed scope");
  });
});
