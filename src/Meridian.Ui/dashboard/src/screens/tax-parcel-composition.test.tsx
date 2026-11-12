import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { axe } from "jest-axe";
import { TaxParcelComposition, taxParcelCoordinates } from "./tax-parcel-composition";
import type { LedgerDisposalTaxParcel } from "@/types/ledger-tax-results";
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
