import { render, screen } from "@testing-library/react";
import { StatementTable, type StatementSection } from "./StatementTable";

const sections: StatementSection[] = [
  {
    label: "Revenue",
    rows: [{ label: "Trading fees", value: 12000 }],
    subtotal: { label: "Total revenue", value: 12000 }
  },
  {
    label: "Operating expenses",
    rows: [{ label: "Data providers", value: -4000 }],
    subtotal: { label: "Total expenses", value: -4000 }
  }
];

describe("StatementTable", () => {
  it("renders sections, line items, and a grand total", () => {
    render(<StatementTable sections={sections} total={{ label: "Net income", value: 8000 }} currency="USD" />);
    expect(screen.getByText("Revenue")).toBeInTheDocument();
    expect(screen.getByText("Net income")).toBeInTheDocument();
    expect(screen.getByText("$8,000.00")).toBeInTheDocument();
  });

  it("renders negatives in accounting parentheses", () => {
    render(<StatementTable sections={sections} currency="USD" />);
    expect(screen.getAllByText("($4,000.00)").length).toBeGreaterThan(0);
  });

  it("supports comparison columns via values maps", () => {
    render(
      <StatementTable
        columns={[
          { key: "cur", label: "2026" },
          { key: "prv", label: "2025" }
        ]}
        sections={[{ rows: [{ label: "Cash", values: { cur: 100, prv: 80 } }] }]}
        currency="USD"
      />
    );
    expect(screen.getByText("$100.00")).toBeInTheDocument();
    expect(screen.getByText("$80.00")).toBeInTheDocument();
  });

  it("uses the wrapper as a region and leaves table semantics to the table", () => {
    render(<StatementTable sections={sections} total={{ label: "Net income", value: 8000 }} currency="USD" />);
    expect(screen.getByRole("region", { name: "Financial statement" })).toBeInTheDocument();
    expect(screen.getByRole("table")).toBeInTheDocument();
  });

  it("makes the scroll region keyboard-focusable with an accessible name", () => {
    render(<StatementTable sections={sections} currency="USD" />);
    const region = screen.getByRole("region", { name: "Financial statement" });
    expect(region).toHaveAttribute("tabindex", "0");
  });

  it("renders a single full-width empty row when there are no sections", () => {
    render(<StatementTable sections={[]} columns={[{ key: "cur", label: "Current" }, { key: "prior", label: "Prior" }]} />);
    const cell = screen.getByText("No line items");
    expect(cell).toHaveAttribute("colspan", "3");
    expect(cell.closest("tbody")?.querySelectorAll("tr")).toHaveLength(1);
  });

  it("names the line-item header column", () => {
    render(<StatementTable sections={sections} currency="USD" />);
    const header = screen.getByRole("columnheader", { name: "Line item" });
    expect(header).toHaveAttribute("scope", "col");
  });
});
