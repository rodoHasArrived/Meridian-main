import { apiGetJson } from "@/lib/api";
import { getRecurringJournalQueue } from "./recurring-journals.api";

vi.mock("@/lib/api", () => ({ apiGetJson: vi.fn() }));
const scope = { fundProfileId: "fund/a", ledgerBookId: "book-a", entityId: "entity-a" };
beforeEach(() => vi.resetAllMocks());

it("uses explicit scope and disables development fallback", async () => {
  vi.mocked(apiGetJson).mockResolvedValue({ ...scope, occurrences: [] });
  await getRecurringJournalQueue(scope);
  expect(apiGetJson).toHaveBeenCalledWith(
    "/api/ledger/journal-automation/recurring/occurrences?fundProfileId=fund%2Fa&ledgerBookId=book-a&entityId=entity-a",
    expect.objectContaining({ allowDevelopmentFallback: false }));
});

it.each(["envelope", "row"])("refuses mismatched %s scope", async (kind) => {
  vi.mocked(apiGetJson).mockResolvedValue({ ...scope,
    entityId: kind === "envelope" ? "other" : scope.entityId,
    occurrences: kind === "row" ? [{ ...scope, entityId: "other" }] : [] });
  await expect(getRecurringJournalQueue(scope)).rejects.toThrow("does not match");
});

it("preserves a failed durable read as a failure", async () => {
  vi.mocked(apiGetJson).mockRejectedValue(new Error("Durable state unavailable"));
  await expect(getRecurringJournalQueue(scope)).rejects.toThrow("Durable state unavailable");
});
