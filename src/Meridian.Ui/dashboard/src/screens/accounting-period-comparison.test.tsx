import { act, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { MemoryRouter, useLocation } from "react-router-dom";
import { AccountingPeriodComparison } from "./accounting-period-comparison";
import type { PostedPeriodComparison } from "@/lib/posted-period-comparison-api";

function fixture(): PostedPeriodComparison {
  return {
    status: "Ready", reason: null, generatedAt: "2026-11-11T12:00:00Z", asOf: "2026-11-11",
    ledgerBookId: "book-a", fundProfileId: "fund-a", bookName: "Primary book", accountingBasis: "Primary", currency: "USD", accountingPolicyId: "policy-a", accountingPolicyVersion: "1",
    current: { periodId: "period-a", label: "November", periodStart: "2026-11-01", periodEnd: "2026-11-30", rangeStart: "2026-11-01", rangeEnd: "2026-11-11", isPartial: true, status: "Open" },
    baseline: { periodId: "period-b", label: "October", periodStart: "2026-10-01", periodEnd: "2026-10-31", rangeStart: "2026-10-01", rangeEnd: "2026-10-11", isPartial: true, status: "HardClosed" },
    lines: [{ accountKey: "cash", accountName: "Cash", accountType: "Asset", symbol: null, financialAccountId: null, currentDebit: "9007199254740993.12", currentCredit: "0", currentBalance: "9007199254740993.12", priorDebit: "0", priorCredit: "0", priorBalance: "0", change: "9007199254740993.12", percentChange: null, dimensions: null }],
    currentJournalIds: ["journal-a"], baselineJournalIds: [], populationHash: "retained-population-a", dimensions: null
  };
}
const options = [{ id: "period-a", label: "November", statusLabel: "Open" }, { id: "period-b", label: "October", statusLabel: "HardClosed" }];
function Route() { return <output aria-label="Current route">{useLocation().search}</output>; }

it("requests comparisons only when enabled and retains all decimal digits and actual ranges", async () => {
  const service = vi.fn().mockResolvedValue(fixture());
  render(<MemoryRouter initialEntries={["/accounting/ledger?entityId=entity-a"]}><AccountingPeriodComparison bookId="book-a" periodId="period-a" periodOptions={options} service={service} /><Route /></MemoryRouter>);
  expect(service).not.toHaveBeenCalled();
  fireEvent.click(screen.getByRole("button", { name: "Vs prior period" }));
  const table = await screen.findByRole("table", { name: "Posted period account comparison" });
  expect(table).toHaveTextContent("9,007,199,254,740,993.12");
  expect(table).toHaveTextContent("Not comparable · prior zero");
  expect(service).toHaveBeenCalledWith(expect.objectContaining({ dimensions: { entityId: "entity-a" } }), expect.objectContaining({ allowDevelopmentFallback: false }));
  expect(screen.getByText(/Current: November/)).toHaveTextContent("2026-11-01–2026-11-11");
  expect(screen.getByText(/Current: November/)).toHaveTextContent("2026-10-01–2026-10-11");
  fireEvent.change(screen.getByLabelText("Comparison baseline"), { target: { value: "period-b" } });
  expect(screen.getByLabelText("Current route")).toHaveTextContent("baselinePeriodId=period-b");
  expect(screen.getByLabelText("Current route")).toHaveTextContent("entityId=entity-a");
});

it("shows missing comparisons explicitly rather than substituting zero balances", async () => {
  const service = vi.fn().mockResolvedValue({ ...fixture(), status: "Unavailable", reason: "No completed prior fiscal period.", baseline: null, lines: [] });
  render(<MemoryRouter initialEntries={["/accounting/ledger?comparison=prior"]}><AccountingPeriodComparison bookId="book-a" periodId="period-a" periodOptions={options} service={service} /></MemoryRouter>);
  await screen.findByText("No completed prior fiscal period.");
  expect(screen.queryByRole("table", { name: "Posted period account comparison" })).not.toBeInTheDocument();
});

it("suppresses unsupported scope instead of requesting an unfiltered comparison", async () => {
  const service = vi.fn();
  render(<MemoryRouter initialEntries={["/accounting/ledger?comparison=prior&shareClassId=class-a"]}><AccountingPeriodComparison bookId="book-a" periodId="period-a" periodOptions={options} service={service} /></MemoryRouter>);
  expect(screen.getByText(/Share-class scope is not supported/)).toBeInTheDocument();
  expect(service).not.toHaveBeenCalled();
});

it("does not present the book's basis as the independently selected trial-balance basis", async () => {
  const service = vi.fn().mockResolvedValue(fixture());
  render(<MemoryRouter initialEntries={["/accounting/ledger?comparison=prior"]}><AccountingPeriodComparison bookId="book-a" periodId="period-a" expectedBasis="Tax" periodOptions={options} service={service} /></MemoryRouter>);
  await screen.findByText(/does not match the selected Tax basis/);
  expect(service).toHaveBeenCalledWith(expect.objectContaining({ accountingBasis: "Tax" }), expect.anything());
  expect(screen.queryByRole("table", { name: "Posted period account comparison" })).not.toBeInTheDocument();
});

it("does not issue an unfiltered request for an incomplete External GL dimension pair", () => {
  const service = vi.fn();
  render(<MemoryRouter initialEntries={["/accounting/ledger?comparison=prior&externalGlDimensionKey=department"]}><AccountingPeriodComparison bookId="book-a" periodId="period-a" periodOptions={options} service={service} /></MemoryRouter>);
  expect(screen.getByText(/dimension key and value must both be supplied/)).toBeInTheDocument();
  expect(service).not.toHaveBeenCalled();
});

it("hides previous-book comparison data immediately and ignores delayed responses", async () => {
  let finish!: (value: PostedPeriodComparison) => void;
  const service = vi.fn().mockImplementationOnce(() => new Promise((resolve) => { finish = resolve; })).mockResolvedValue({ ...fixture(), ledgerBookId: "book-b", status: "Unavailable", reason: "Current scope has no prior period.", baseline: null, lines: [] });
  const view = render(<MemoryRouter initialEntries={["/accounting/ledger?comparison=prior"]}><AccountingPeriodComparison bookId="book-a" periodId="period-a" periodOptions={options} service={service} /></MemoryRouter>);
  await waitFor(() => expect(service).toHaveBeenCalledTimes(1));
  view.rerender(<MemoryRouter initialEntries={["/accounting/ledger?comparison=prior"]}><AccountingPeriodComparison bookId="book-b" periodId="period-a" periodOptions={options} service={service} /></MemoryRouter>);
  await screen.findByText("Current scope has no prior period.");
  await act(async () => finish(fixture()));
  expect(screen.queryByText("9,007,199,254,740,993.12")).not.toBeInTheDocument();
  expect(screen.getByText("Current scope has no prior period.")).toBeInTheDocument();
});
