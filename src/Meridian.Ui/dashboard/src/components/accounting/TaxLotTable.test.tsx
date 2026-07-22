import { render, screen, within } from "@testing-library/react";
import { TaxLotTable, type TaxLot } from "./TaxLotTable";

const lots: TaxLot[] = [
  // acquired long ago → long-term, gain
  { id: "a", acquired: "2020-01-01", quantity: 10, costBasis: 1000, marketValue: 1500 },
  // recently acquired → short-term, loss
  { id: "b", acquired: "2026-06-01", quantity: 5, costBasis: 800, marketValue: 700 }
];

describe("TaxLotTable", () => {
  it("classifies holding period by days held vs. the long-term threshold", () => {
    render(<TaxLotTable lots={lots} currency="USD" asOf="2026-06-30" />);
    expect(screen.getByText("Long-term")).toBeInTheDocument();
    expect(screen.getByText("Short-term")).toBeInTheDocument();
  });

  it("computes unrealized gain per lot with a percent", () => {
    render(<TaxLotTable lots={lots} currency="USD" asOf="2026-06-30" />);
    // +500 on 1000 basis = +50.00%
    expect(screen.getByText("+50.00%")).toBeInTheDocument();
  });

  it("rolls up basis, value, and total gain in the footer", () => {
    render(<TaxLotTable lots={lots} currency="USD" asOf="2026-06-30" />);
    const footer = screen.getByText("2 lots").closest("tr")!;
    // total basis 1800, value 2200, gain +400
    expect(within(footer).getByText("$1,800.00")).toBeInTheDocument();
    expect(within(footer).getByText("$2,200.00")).toBeInTheDocument();
    expect(within(footer).getByText("+$400.00")).toBeInTheDocument();
  });
});
