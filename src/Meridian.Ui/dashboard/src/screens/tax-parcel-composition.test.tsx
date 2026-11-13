import { fireEvent, render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { axe } from "jest-axe";
import { TaxParcelComposition, taxParcelPoints } from "./tax-parcel-composition";
import type { LedgerJournalTaxResults } from "@/types/ledger-tax-results";

function evidence(): LedgerJournalTaxResults {
  return { ledgerBookId: "book", periodId: "period", journalEntryId: "journal", functionalCurrency: "EUR", evaluatedAt: "2026-11-12T12:00:00Z", evidenceState: "Available", message: "Retained", disposals: [{
    mutationBatchId: "batch", journalEntryId: "journal", saleDate: "2026-11-10", accountName: "Investments", symbol: "ABC", reliefMethod: "FIFO", policyRevision: "retained-policy-v7", recordedAt: "2026-11-11T00:00:00Z",
    state: "Provisional", stateReason: "Replacement window remains open", canChange: true, reEvaluationRequired: false, replacementWindowEnd: "2026-12-10", character: "LongTerm", economicGainOrLoss: "-100.000000000000000001", recognizedGainOrLoss: null, deferredLoss: null,
    parcels: [{ lotId: "same-lot", acquiredDate: "2026-11-01", holdingPeriodStart: "2024-01-01", holdingPeriodDays: 1044, holdingPeriodCarried: true, character: "LongTerm", quantity: "1", proceeds: "100", costBasis: "200.000000000000000001", economicGainOrLoss: "-100.000000000000000001", recognizedGainOrLoss: null, deferredLoss: "100.000000000000000001" }]
  }] };
}

it("selects retained parcel evidence by keyboard and preserves exact decimal and provisional meanings", async () => {
  const { container } = render(<TaxParcelComposition result={evidence()} />);
  const point = screen.getByRole("button", { name: /Inspect parcel 1/ });
  point.focus();
  fireEvent.keyDown(point, { key: "Enter" });
  const detail = screen.getByRole("region", { name: "Selected tax parcel evidence" });
  expect(within(detail).getByText("-100.000000000000000001 EUR")).toBeInTheDocument();
  expect(within(detail).getByText("Missing evidence")).toBeInTheDocument();
  expect(within(detail).getByText("100.000000000000000001 EUR")).toBeInTheDocument();
  expect(within(detail).getByText(/Long term · Provisional/)).toHaveTextContent("Replacement window remains open");
  expect(within(detail).getByText("Carried from prior parcel")).toBeInTheDocument();
  expect(within(detail).getByText("retained-policy-v7")).toBeInTheDocument();
  expect((await axe(container)).violations).toEqual([]);
});

it("distinguishes repeated lot IDs across disposals and clears an obsolete selection", async () => {
  const result = evidence();
  result.disposals.push({ ...result.disposals[0], mutationBatchId: "other-batch", state: "Settled", stateReason: "Retained settled", canChange: false });
  expect(new Set(taxParcelPoints(result).points.map((point) => point.id)).size).toBe(2);
  const { rerender } = render(<TaxParcelComposition result={result} />);
  await userEvent.click(screen.getByRole("button", { name: "Select parcel 2: same-lot" }));
  expect(screen.getByRole("region", { name: "Selected tax parcel evidence" })).toHaveTextContent("other-batch");
  rerender(<TaxParcelComposition result={evidence()} />);
  expect(screen.queryByRole("region", { name: "Selected tax parcel evidence" })).not.toBeInTheDocument();
});

it("omits unrepresentable chart coordinates explicitly while keeping zero gains plottable", () => {
  const result = evidence();
  result.disposals[0].parcels.push({ ...result.disposals[0].parcels[0], lotId: "zero", economicGainOrLoss: "0", holdingPeriodDays: 0 });
  result.disposals[0].parcels[0].holdingPeriodDays = Number.NaN;
  const points = taxParcelPoints(result);
  expect(points.omitted).toBe(1);
  expect(points.points[0]).toMatchObject({ x: 0, y: 0 });
  render(<TaxParcelComposition result={result} />);
  expect(screen.getByRole("status")).toHaveTextContent("1 parcels have unavailable plot coordinates");
  expect(screen.getByRole("button", { name: /Select parcel 2: zero/ })).toBeInTheDocument();
});
