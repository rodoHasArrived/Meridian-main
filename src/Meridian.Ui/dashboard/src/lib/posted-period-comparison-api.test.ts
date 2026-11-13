import { getJson } from "@/lib/api";
import { getPostedPeriodComparison, type PostedPeriodComparison } from "./posted-period-comparison-api";

vi.mock("@/lib/api", () => ({ getJson: vi.fn() }));

function response(): PostedPeriodComparison {
  return {
    status: "Ready", reason: null, generatedAt: "2026-11-11T00:00:00Z", asOf: "2026-11-11", ledgerBookId: "book-a", fundProfileId: "fund-a", bookName: "Primary", accountingBasis: "Primary", currency: "USD", accountingPolicyId: "policy", accountingPolicyVersion: "1",
    current: { periodId: "current", label: "Current", periodStart: "2026-11-01", periodEnd: "2026-11-30", rangeStart: "2026-11-01", rangeEnd: "2026-11-11", isPartial: true, status: "Open" },
    baseline: { periodId: "prior", label: "Prior", periodStart: "2026-10-01", periodEnd: "2026-10-31", rangeStart: "2026-10-01", rangeEnd: "2026-10-11", isPartial: true, status: "HardClosed" },
    lines: [{ accountKey: "cash", accountName: "Cash", accountType: "Asset", symbol: null, financialAccountId: null, currentDebit: "9007199254740993.123456789", currentCredit: "0", currentBalance: "9007199254740993.123456789", priorDebit: "0", priorCredit: "0", priorBalance: "0", change: "9007199254740993.123456789", percentChange: null, dimensions: null }],
    currentJournalIds: ["journal-a"], baselineJournalIds: [], populationHash: "source-cut-a", dimensions: null
  };
}

it("keeps exact decimal strings and sends the complete requested source scope without development fallback", async () => {
  vi.mocked(getJson).mockResolvedValue({ ...response(), dimensions: { entityId: "entity-a", accountId: "account-a" } });
  const signal = new AbortController().signal;
  const result = await getPostedPeriodComparison({ bookId: "book-a", periodId: "current", baselinePeriodId: "prior", asOf: "2026-11-11", dimensions: { entityId: "entity-a", accountId: "account-a" } }, { signal, allowDevelopmentFallback: true });
  expect(result.lines[0].currentBalance).toBe("9007199254740993.123456789");
  const [path, options] = vi.mocked(getJson).mock.calls.at(-1)!;
  const url = new URL(path, "http://localhost");
  expect(url.pathname).toBe("/api/ledger/books/book-a/periods/current/comparison");
  expect(url.searchParams.get("entityId")).toBe("entity-a");
  expect(url.searchParams.get("accountId")).toBe("account-a");
  expect(url.searchParams.get("baselinePeriodId")).toBe("prior");
  expect(options).toEqual({ signal, allowDevelopmentFallback: false });
});

it("refuses a comparison for another book or selected baseline", async () => {
  vi.mocked(getJson).mockResolvedValue({ ...response(), ledgerBookId: "foreign-book" });
  await expect(getPostedPeriodComparison({ bookId: "book-a", periodId: "current" })).rejects.toThrow("does not match");
  vi.mocked(getJson).mockResolvedValue(response());
  await expect(getPostedPeriodComparison({ bookId: "book-a", periodId: "current", baselinePeriodId: "other-baseline" })).rejects.toThrow("does not match");
});

it("refuses numeric JSON amounts or a missing source cut rather than rounding or claiming comparison readiness", async () => {
  const numeric = response();
  (numeric.lines[0] as unknown as { currentBalance: number }).currentBalance = 9007199254740992;
  vi.mocked(getJson).mockResolvedValue(numeric);
  await expect(getPostedPeriodComparison({ bookId: "book-a", periodId: "current" })).rejects.toThrow("exact amount");
  vi.mocked(getJson).mockResolvedValue({ ...response(), populationHash: "" });
  await expect(getPostedPeriodComparison({ bookId: "book-a", periodId: "current" })).rejects.toThrow("retained population");
});

it("does not display an unfiltered population under a selected dimension", async () => {
  vi.mocked(getJson).mockResolvedValue(response());
  await expect(getPostedPeriodComparison({ bookId: "book-a", periodId: "current", dimensions: { entityId: "selected-entity" } })).rejects.toThrow("dimension scope");
});

it("validates supported dimension aliases and does not accept an old as-of cutoff", async () => {
  vi.mocked(getJson).mockResolvedValue({ ...response(), dimensions: { fundId: "fund-a", bookId: "dimension-book", externalGlDimensions: { department: "treasury" } } });
  await expect(getPostedPeriodComparison({ bookId: "book-a", periodId: "current", asOf: "2026-11-11", dimensions: { fundProfileId: "fund-a", ledgerBookDimensionId: "dimension-book", externalGlDimensionKey: "department", externalGlDimensionValue: "treasury" } })).resolves.toEqual(expect.objectContaining({ status: "Ready" }));
  await expect(getPostedPeriodComparison({ bookId: "book-a", periodId: "current", asOf: "2026-11-12" })).rejects.toThrow("as-of cutoff");
});
