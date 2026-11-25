import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { axe } from "jest-axe";
import { TaxParcelComposition, taxParcelCoordinates } from "./tax-parcel-composition";
import type { LedgerDisposalTaxParcel, LedgerDisposalTaxResult } from "@/types/ledger-tax-results";
const parcel: LedgerDisposalTaxParcel = { lotId: "retained-lot", acquiredDate: "2026-01-01", holdingPeriodStart: "2024-01-01", holdingPeriodDays: 900, holdingPeriodCarried: true, character: "ShortTerm", quantity: "0.000000001", proceeds: "1", costBasis: "2", economicGainOrLoss: "-9007199254740993.01", recognizedGainOrLoss: null, deferredLoss: null };
it("keeps exact retained amounts and missing allocations together after keyboard selection", async () => {
  const { container } = render(<TaxParcelComposition parcels={[parcel]} currency="USD" state="Provisional" />);
  const mark = screen.getByRole("button", { name: /Inspect parcel retained-lot/ });
  mark.focus(); await userEvent.keyboard("{Enter}");
  const selected = screen.getByRole("region", { name: "Selected parcel composition" });
  expect(within(selected).getByText("-9,007,199,254,740,993.01 USD")).toBeInTheDocument();
  expect(within(selected).getAllByText("Missing evidence")).toHaveLength(2);
  expect(selected).toHaveTextContent("Carried holding period");
  expect(screen.getByRole("table")).toHaveTextContent("Short term");
  expect((await axe(container)).violations).toEqual([]);
});
it("uses exact decimal ratios without overflowing and retains coincident parcels", () => {
  const plot = taxParcelCoordinates([parcel, { ...parcel, lotId: "other" }, { ...parcel, economicGainOrLoss: "0.000000000001" }]);
  expect(plot.points).toHaveLength(3);
  expect(plot.points[0]).toEqual(plot.points[1]);
  expect(plot.points.every((point) => Number.isFinite(point.x) && Number.isFinite(point.y))).toBe(true);
});

function disposal(mutationBatchId: string): LedgerDisposalTaxResult {
  return { mutationBatchId, journalEntryId: "journal", saleDate: "2026-11-10", accountName: "Investments", symbol: "ABC", reliefMethod: "FIFO",
    policyRevision: "retained-policy-v7", recordedAt: "2026-11-11T00:00:00Z", state: "Provisional", stateReason: "Replacement window remains open",
    canChange: true, reEvaluationRequired: false, replacementWindowEnd: "2026-12-10", character: "ShortTerm",
    economicGainOrLoss: parcel.economicGainOrLoss, recognizedGainOrLoss: null, deferredLoss: null, parcels: [parcel] };
}

it("keeps supplied tax character, provisional reason and policy identity beside selected amounts", async () => {
  const context = disposal("retained-batch");
  render(<TaxParcelComposition parcels={context.parcels} currency="EUR" state="Provisional" disposal={context} />);
  await userEvent.click(screen.getByRole("button", { name: "retained-lot" }));
  const selected = screen.getByRole("region", { name: "Selected parcel composition" });
  expect(selected).toHaveTextContent("Retained tax character: Short term");
  expect(selected).toHaveTextContent("Replacement window remains open");
  expect(selected).toHaveTextContent("retained-policy-v7");
  expect(selected).toHaveTextContent("retained-batch");
  expect(selected).toHaveTextContent("This retained result can change");
});

it("does not carry a same-lot selection across a different retained disposal", async () => {
  const context = disposal("first-batch");
  const { rerender } = render(<TaxParcelComposition parcels={[parcel]} currency="EUR" state="Provisional" disposal={context} />);
  await userEvent.click(screen.getByRole("button", { name: "retained-lot" }));
  expect(screen.getByRole("region", { name: "Selected parcel composition" })).toHaveTextContent("first-batch");
  rerender(<TaxParcelComposition parcels={[parcel]} currency="EUR" state="Provisional" disposal={disposal("other-batch")} />);
  expect(screen.queryByRole("region", { name: "Selected parcel composition" })).not.toBeInTheDocument();
});

it("discloses unavailable coordinates and still plots a reported zero without discarding table evidence", () => {
  const rows = [{ ...parcel, holdingPeriodDays: Number.NaN }, { ...parcel, lotId: "zero", holdingPeriodDays: 0, economicGainOrLoss: "0" }];
  const points = taxParcelCoordinates(rows);
  expect(points.omitted).toBe(1);
  expect(points.points[0].available).toBe(false);
  expect(points.points[1]).toMatchObject({ available: true, y: points.zeroY });
  render(<TaxParcelComposition parcels={rows} currency="EUR" state="Provisional" />);
  expect(screen.getByRole("status")).toHaveTextContent("1 parcels have unavailable plot coordinates");
  expect(screen.getByRole("button", { name: "zero" })).toBeInTheDocument();
  expect(screen.getByText("Unavailable")).toBeInTheDocument();
});
