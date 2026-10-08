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

const exactParcel = {
  quantity: "0.1234567890123456789012345678", proceeds: "9007199254740993.01", costBasis: "0.01",
  economicGainOrLoss: "9007199254740993", recognizedGainOrLoss: "9007199254740993", deferredLoss: "0"
};
const exactDisposal = {
  journalEntryId: scope.journalEntryId, economicGainOrLoss: "9007199254740993",
  recognizedGainOrLoss: "9007199254740993", deferredLoss: "0", parcels: [exactParcel]
};

it("retains exact decimal strings through JSON parsing, including large cents and fractional quantities", async () => {
  const wire = JSON.stringify({ ...scope, disposals: [exactDisposal] });
  vi.mocked(apiGetJson).mockResolvedValue(JSON.parse(wire));
  const result = await getLedgerJournalEntryTaxResults(scope);
  expect(result.disposals[0].economicGainOrLoss).toBe("9007199254740993");
  expect(result.disposals[0].parcels[0].proceeds).toBe("9007199254740993.01");
  expect(result.disposals[0].parcels[0].quantity).toBe("0.1234567890123456789012345678");
});

it.each(["economicGainOrLoss", "recognizedGainOrLoss", "deferredLoss"])(
  "rejects a rounded JSON number in disposal %s instead of coercing it to text", async (field) => {
    vi.mocked(apiGetJson).mockResolvedValue({ ...scope, disposals: [{ ...exactDisposal, [field]: JSON.parse("9007199254740993.01") }] });
    await expect(getLedgerJournalEntryTaxResults(scope)).rejects.toThrow("exact plain decimal text");
  }
);

it.each(["quantity", "proceeds", "costBasis", "economicGainOrLoss", "recognizedGainOrLoss", "deferredLoss"])(
  "rejects numeric parcel %s even when aggregate decimal strings are valid", async (field) => {
    vi.mocked(apiGetJson).mockResolvedValue({ ...scope, disposals: [{ ...exactDisposal, parcels: [{ ...exactParcel, [field]: 0.01 }] }] });
    await expect(getLedgerJournalEntryTaxResults(scope)).rejects.toThrow("exact plain decimal text");
  }
);

it.each(["1e-28", "NaN", "01.5", "1,000", "+1"])("rejects noncanonical wire text %j", async (value) => {
  vi.mocked(apiGetJson).mockResolvedValue({ ...scope, disposals: [{ ...exactDisposal, deferredLoss: value }] });
  await expect(getLedgerJournalEntryTaxResults(scope)).rejects.toThrow("exact plain decimal text");
});

it("keeps nullable missing evidence distinct from zero at both disposal and parcel levels", async () => {
  const result = { ...scope, disposals: [{ ...exactDisposal, economicGainOrLoss: null, recognizedGainOrLoss: null,
    deferredLoss: null, parcels: [{ ...exactParcel, recognizedGainOrLoss: null, deferredLoss: null }] }] };
  vi.mocked(apiGetJson).mockResolvedValue(result);
  expect(await getLedgerJournalEntryTaxResults(scope)).toBe(result);
});
