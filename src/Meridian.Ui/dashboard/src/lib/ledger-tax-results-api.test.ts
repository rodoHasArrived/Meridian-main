import { apiGetJson } from "@/lib/api";
import { getLedgerJournalEntryTaxResults } from "@/lib/ledger-tax-results-api";

vi.mock("@/lib/api", () => ({ apiGetJson: vi.fn() }));
const scope = { ledgerBookId: "book-1", periodId: "period/1", journalEntryId: "journal/1" };
beforeEach(() => vi.resetAllMocks());

it("reads the exact retained posting, preserves cancellation and prohibits demo fallback", async () => {
  const result = { ...scope, disposals: [] };
  vi.mocked(apiGetJson).mockResolvedValue(result);
  const signal = new AbortController().signal;
  expect(await getLedgerJournalEntryTaxResults(scope, { signal, allowDevelopmentFallback: true })).toBe(result);
  expect(apiGetJson).toHaveBeenCalledWith(
    "/api/ledger/periods/period%2F1/journal-entries/journal%2F1/tax-results",
    { signal, allowDevelopmentFallback: false }
  );
});

it.each(["ledgerBookId", "periodId", "journalEntryId"])("rejects a different %s before displaying tax evidence", async (field) => {
  vi.mocked(apiGetJson).mockResolvedValue({ ...scope, [field]: "other", disposals: [] });
  await expect(getLedgerJournalEntryTaxResults(scope)).rejects.toThrow("do not match");
});

it("rejects a disposal belonging to another journal even when the envelope matches", async () => {
  vi.mocked(apiGetJson).mockResolvedValue({ ...scope, disposals: [{ journalEntryId: "other" }] });
  await expect(getLedgerJournalEntryTaxResults(scope)).rejects.toThrow("do not match");
});

it("preserves a failed retained read as an error", async () => {
  vi.mocked(apiGetJson).mockRejectedValue(new Error("retained evidence offline"));
  await expect(getLedgerJournalEntryTaxResults(scope)).rejects.toThrow("retained evidence offline");
});
