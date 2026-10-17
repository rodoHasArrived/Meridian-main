import { act, render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { axe } from "jest-axe";
import { getLedgerJournalEntryTaxResults } from "@/lib/ledger-tax-results-api";
import { JournalEntryTaxResults } from "@/screens/journal-entry-tax-results";
import type { LedgerDisposalTaxResult, LedgerJournalTaxResults } from "@/types/ledger-tax-results";

vi.mock("@/lib/ledger-tax-results-api", () => ({ getLedgerJournalEntryTaxResults: vi.fn() }));
const scope = { ledgerBookId: "book-1", periodId: "period-1", journalEntryId: "journal-1" };
const disposal: LedgerDisposalTaxResult = {
  mutationBatchId: "batch-1", journalEntryId: scope.journalEntryId, saleDate: "2026-08-03", accountName: "Investments",
  symbol: "AAPL", reliefMethod: "Fifo", policyRevision: "us-tax:2026.4/retained-17", recordedAt: "2026-08-03T16:00:00Z",
  state: "Provisional", stateReason: "Replacement acquisitions through September 2 can change the retained loss.",
  canChange: true, reEvaluationRequired: false, replacementWindowEnd: "2026-09-02", character: "Mixed",
  economicGainOrLoss: "-123.45", recognizedGainOrLoss: "-23.45", deferredLoss: "100",
  parcels: [
    { lotId: "lot-carried", acquiredDate: "2026-07-20", holdingPeriodStart: "2024-02-29", holdingPeriodDays: 886,
      holdingPeriodCarried: true, character: "LongTerm", quantity: "2", proceeds: "300", costBasis: "400",
      economicGainOrLoss: "-100", recognizedGainOrLoss: "0", deferredLoss: "100" },
    { lotId: "lot-short", acquiredDate: "2025-08-03", holdingPeriodStart: "2025-08-03", holdingPeriodDays: 365,
      holdingPeriodCarried: false, character: "ShortTerm", quantity: "1", proceeds: "100", costBasis: "123.45",
      economicGainOrLoss: "-23.45", recognizedGainOrLoss: "-23.45", deferredLoss: "0" }
  ]
};
function result(overrides: Partial<LedgerJournalTaxResults> = {}): LedgerJournalTaxResults {
  return { ...scope, functionalCurrency: "EUR", evaluatedAt: "2026-08-04T12:00:00Z", evidenceState: "Available", message: "Retained evidence", disposals: [disposal], ...overrides };
}
beforeEach(() => vi.resetAllMocks());

it("renders retained mixed character, carried holding start, exact revision and amounts without recomputation", async () => {
  vi.mocked(getLedgerJournalEntryTaxResults).mockResolvedValue(result({ disposals: [{
    ...disposal, state: "MissingEvidence", stateReason: "Legacy parcel allocation evidence is missing.",
    parcels: disposal.parcels.map((parcel) => ({ ...parcel, recognizedGainOrLoss: null, deferredLoss: null }))
  }] }));
  const { container } = render(<JournalEntryTaxResults {...scope} />);
  expect(await screen.findByText("Mixed parcels")).toBeInTheDocument();
  expect(screen.getByText("us-tax:2026.4/retained-17")).toBeInTheDocument();
  expect(screen.getByText("-123.45 EUR")).toBeInTheDocument();
  expect(screen.getAllByText("-23.45 EUR")[0]).toBeVisible();
  expect(screen.getAllByText("100.00 EUR")[0]).toBeVisible();
  expect(screen.getByText("Tax result needs evidence")).toBeInTheDocument();
  const summary = screen.getByText(/lot-carried/).closest("summary")!;
  await userEvent.click(summary);
  const parcel = summary.closest("details")!;
  expect(parcel).toHaveAttribute("open");
  expect(within(parcel).getByText("2024-02-29")).toBeInTheDocument();
  expect(within(parcel).getByText("Long term")).toBeInTheDocument();
  // Legacy mixed-parcel allocation gaps must remain unknown, even with a retained aggregate.
  expect(within(parcel).getAllByText("Missing evidence")).toHaveLength(2);
  expect((await axe(container)).violations).toEqual([]);
});

it("uses server character even if dates alone would suggest a different classification", async () => {
  vi.mocked(getLedgerJournalEntryTaxResults).mockResolvedValue(result({ disposals: [{ ...disposal, character: "ShortTerm", parcels: [] }] }));
  render(<JournalEntryTaxResults {...scope} />);
  expect(await screen.findByText("Short term")).toBeInTheDocument();
  expect(screen.queryByText("Long term")).not.toBeInTheDocument();
});

it("keeps a closed replacement window provisional until refresh receives a settled retained result", async () => {
  const closed = { ...disposal, replacementWindowEnd: "2020-09-02", reEvaluationRequired: true,
    stateReason: "Replacement window closed. Re-evaluation must be retained before this result settles." };
  vi.mocked(getLedgerJournalEntryTaxResults)
    .mockResolvedValueOnce(result({ disposals: [closed] }))
    .mockResolvedValueOnce(result({ disposals: [{ ...closed, state: "Settled", canChange: false, reEvaluationRequired: false, stateReason: "Replacement evidence is complete." }] }));
  render(<JournalEntryTaxResults {...scope} />);
  expect(await screen.findByText("Provisional")).toBeInTheDocument();
  expect(screen.getByText("Required — awaiting retained evidence")).toBeInTheDocument();
  await userEvent.click(screen.getByRole("button", { name: "Refresh retained results" }));
  expect(await screen.findByText("Settled")).toBeInTheDocument();
  expect(screen.getByText("Replacement evidence is complete.")).toBeInTheDocument();
  expect(screen.queryByText("This result can still change")).not.toBeInTheDocument();
  expect(getLedgerJournalEntryTaxResults).toHaveBeenCalledTimes(2);
});

it("distinguishes missing retained evidence from an available journal without disposals", async () => {
  vi.mocked(getLedgerJournalEntryTaxResults)
    .mockResolvedValueOnce(result({ evidenceState: "MissingEvidence", message: "Retained policy evidence is missing.", disposals: [] }))
    .mockResolvedValueOnce(result({ disposals: [], message: "No retained disposal for this posting." }));
  render(<JournalEntryTaxResults {...scope} />);
  expect(await screen.findByText("Missing evidence")).toBeInTheDocument();
  expect(screen.getByText("Retained policy evidence is missing.")).toBeInTheDocument();
  await userEvent.click(screen.getByRole("button", { name: "Refresh retained results" }));
  expect(await screen.findByText("No retained disposal for this posting.")).toBeInTheDocument();
  expect(screen.queryByText("Missing evidence")).not.toBeInTheDocument();
});

it("shows unavailable and retry after a failed read without displaying a fabricated tax result", async () => {
  vi.mocked(getLedgerJournalEntryTaxResults).mockRejectedValueOnce(new Error("offline")).mockResolvedValueOnce(result());
  render(<JournalEntryTaxResults {...scope} />);
  expect(await screen.findByRole("alert")).toHaveTextContent("Tax results unavailable");
  expect(screen.queryByText("Settled")).not.toBeInTheDocument();
  await userEvent.click(screen.getByRole("button", { name: "Retry tax results" }));
  expect(await screen.findByText("Provisional")).toBeInTheDocument();
});

it("keeps missing disposal applicability and amounts unknown instead of implying settled zero", async () => {
  vi.mocked(getLedgerJournalEntryTaxResults).mockResolvedValue(result({ disposals: [{
    ...disposal, state: "MissingEvidence", stateReason: "The retained policy revision is missing.",
    policyRevision: null, replacementWindowEnd: null, reEvaluationRequired: false, canChange: true,
    character: null, recognizedGainOrLoss: null, deferredLoss: null, parcels: []
  }] }));
  render(<JournalEntryTaxResults {...scope} />);
  expect(await screen.findByText("Tax result needs evidence")).toBeInTheDocument();
  expect(screen.getAllByText("Cannot determine — missing evidence")).toHaveLength(2);
  expect(screen.queryByText("Not applicable")).not.toBeInTheDocument();
  expect(screen.queryByText("Not required by current evidence")).not.toBeInTheDocument();
  expect(screen.queryByText("Settled under the retained policy")).not.toBeInTheDocument();
  expect(screen.queryByText("0.00 EUR")).not.toBeInTheDocument();
});

it("aborts and ignores late tax evidence when the selected journal changes", async () => {
  let resolveOld!: (value: LedgerJournalTaxResults) => void;
  vi.mocked(getLedgerJournalEntryTaxResults)
    .mockImplementationOnce(() => new Promise((resolve) => { resolveOld = resolve; }))
    .mockResolvedValueOnce(result({ journalEntryId: "journal-2", disposals: [], message: "No disposal for journal 2." }));
  const { rerender } = render(<JournalEntryTaxResults {...scope} />);
  const oldSignal = vi.mocked(getLedgerJournalEntryTaxResults).mock.calls[0][1]?.signal;
  expect(screen.getByRole("status")).toHaveTextContent("Loading retained tax results");
  rerender(<JournalEntryTaxResults {...scope} journalEntryId="journal-2" />);
  expect(await screen.findByText("No disposal for journal 2.")).toBeInTheDocument();
  expect(oldSignal?.aborted).toBe(true);
  await act(async () => resolveOld(result()));
  expect(screen.queryByText("us-tax:2026.4/retained-17")).not.toBeInTheDocument();
  expect(screen.getByText("No disposal for journal 2.")).toBeInTheDocument();
});

it("renders large cents, tiny amounts and fractional quantities exactly from retained decimal text", async () => {
  vi.mocked(getLedgerJournalEntryTaxResults).mockResolvedValue(result({ disposals: [{
    ...disposal, economicGainOrLoss: "9007199254740993.01", recognizedGainOrLoss: "9007199254740993.01", deferredLoss: "0",
    parcels: [{ ...disposal.parcels[0], quantity: "0.1234567890123456789012345678", proceeds: "9007199254740993.010000000001",
      costBasis: "0.000000000001", economicGainOrLoss: "9007199254740993.01", recognizedGainOrLoss: "9007199254740993.01", deferredLoss: "0" }]
  }] }));
  render(<JournalEntryTaxResults {...scope} />);
  expect((await screen.findAllByText("9,007,199,254,740,993.01 EUR"))[0]).toBeVisible();
  await userEvent.click(screen.getByText(/lot-carried/).closest("summary")!);
  expect(screen.getByText("0.1234567890123456789012345678")).toBeVisible();
  expect(screen.getByText("9,007,199,254,740,993.010000000001 EUR")).toBeVisible();
  expect(screen.getByText("0.000000000001 EUR")).toBeVisible();
  expect(screen.queryByText("9,007,199,254,740,994.00 EUR")).not.toBeInTheDocument();
});
