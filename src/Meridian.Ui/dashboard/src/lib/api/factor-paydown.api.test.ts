import { beforeEach, describe, expect, it, vi } from "vitest";
import { apiPostJson } from "@/lib/api";
import { createFactorPaydownDraft, prepareFactorPaydown } from "./factor-paydown.api";
import type { FactorPaydownRequest } from "@/types/factor-paydown";
vi.mock("@/lib/api", () => ({ apiPostJson: vi.fn() }));
const request = { ledgerBookId: "BOOK-1", periodId: "period-1", fundProfileId: "fund-1", securityId: "security-1", bookPositionId: "position-1", accountingBasis: "Primary", priorFactor: "0.9999999999999999999999999999", newFactor: "0.9" } as FactorPaydownRequest;
const response = () => ({ preparationFingerprint: "hash", instruction: { scope: { ...request, ledgerBookId: "book-1" }, lots: [] }, components: { "lot.principal": "123456789012345.6789012345678" } });
beforeEach(() => vi.resetAllMocks());
describe("Factor paydown API boundary", () => {
  it("requests authoritative preparation and preserves exact decimal inputs", async () => {
    vi.mocked(apiPostJson).mockResolvedValue(response());
    const result = await prepareFactorPaydown(request, { allowDevelopmentFallback: true });
    expect(apiPostJson).toHaveBeenCalledWith("/api/ledger/books/BOOK-1/factor-paydowns/prepare", request, { allowDevelopmentFallback: false });
    expect(result.components["lot.principal"]).toBe("123456789012345.6789012345678");
  });
  it.each(["ledgerBookId", "periodId", "fundProfileId", "securityId", "bookPositionId", "accountingBasis"])("refuses a foreign %s response", async (field) => {
    const result = response(); result.instruction.scope = { ...result.instruction.scope, [field]: "foreign" };
    vi.mocked(apiPostJson).mockResolvedValue(result);
    await expect(prepareFactorPaydown(request)).rejects.toThrow("does not match the selected scope");
  });
  it("refuses rounded numeric financial amounts", async () => {
    vi.mocked(apiPostJson).mockResolvedValue({ ...response(), components: { "lot.principal": 1.2345 } });
    await expect(prepareFactorPaydown(request)).rejects.toThrow("exact decimal strings");
  });
  it("refuses to claim a draft from a different retained scope", async () => {
    vi.mocked(apiPostJson).mockResolvedValue({ spine: { scope: { ...response().instruction.scope, periodId: "foreign" } }, candidate: { journalEntryId: "draft-1" } });
    await expect(createFactorPaydownDraft(request, "hash", "Reviewed")).rejects.toThrow("does not match the reviewed scope");
  });
  it("sends original exact inputs and reviewed fingerprint without roundtripping numeric instructions", async () => {
    vi.mocked(apiPostJson).mockResolvedValue({ spine: { scope: response().instruction.scope }, candidate: { journalEntryId: "draft-1" } });
    await createFactorPaydownDraft(request, "reviewed-hash", "Reviewed principal", { allowDevelopmentFallback: true });
    expect(apiPostJson).toHaveBeenCalledWith("/api/ledger/books/BOOK-1/factor-paydowns/draft", { request, expectedPreparationFingerprint: "reviewed-hash", description: "Reviewed principal" }, { allowDevelopmentFallback: false });
  });
});
