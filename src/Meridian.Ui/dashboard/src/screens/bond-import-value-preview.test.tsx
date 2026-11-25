import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { axe } from "jest-axe";
import { describe, expect, it } from "vitest";
import { BondImportValuePreview } from "./bond-import-value-preview";

const mappedRecord = {
  security: { cusip: "9128285M8" },
  bondValueReconciliation: {
    schema: "bond-value-reconciliation-v1", providerValue: "98500", currency: "USD",
    originalFace: "100000", currentFactor: "1", quotation: "98.50", quotationConvention: "Per100",
    priceBasis: "Clean", marketValueBasis: "Clean", accruedInterest: "120.123456789012345678901234",
    tolerance: "0.01", normalizedValue: "98500", variance: "0", cleanConsideration: "98500",
    dirtyConsideration: "98620.123456789012345678901234", status: "Matched",
    explanation: "100000 original face × 1 factor × 98.50 / 100 = 98500 Clean consideration."
  }
};

describe("bond holding import value preview", () => {
  it("shows provider, normalized and variance together and exposes exact convention evidence", async () => {
    const user = userEvent.setup();
    const { container } = render(<BondImportValuePreview mappedRecords={[mappedRecord]} />);
    expect(screen.getByRole("table", { name: "Bond holding provider value and normalized consideration" })).toBeInTheDocument();
    const row = screen.getByRole("row", { name: /9128285M8/ });
    expect(within(row).getByRole("rowheader", { name: /9128285M8 Clean · USD/ })).toBeInTheDocument();
    expect(within(row).getAllByText("98,500")).toHaveLength(3);
    expect(within(row).getByRole("cell", { name: "0" })).toBeInTheDocument();
    await user.click(screen.getByText("Review conventions"));
    expect(screen.getByText("120.123456789012345678901234")).toBeVisible();
    expect(screen.getByText("98,620.123456789012345678901234")).toBeVisible();
    expect((await axe(container)).violations).toEqual([]);
  });

  it("shows unavailable values and remediation when conventions block a quarantined row", () => {
    render(<BondImportValuePreview quarantinedRecords={[{ quarantineRecordId: "Q-1", mappedRecord: {
      ...mappedRecord,
      bondValueReconciliation: { ...mappedRecord.bondValueReconciliation, normalizedValue: null,
        variance: null, status: "Blocked", explanation: "Review conventions: bond.currentFactor." }
    } }]} />);
    const row = screen.getByRole("row", { name: /9128285M8/ });
    expect(within(row).getAllByRole("cell", { name: "Unavailable" })).toHaveLength(2);
    expect(within(row).getByText("Blocked")).toBeInTheDocument();
  });

  it("omits non-bond rows and ignores unversioned provider normalization assertions", () => {
    const { container } = render(<BondImportValuePreview mappedRecords={[{ quantity: 100 }, {
      bondValueReconciliation: { normalizedValue: "1" }
    }]} />);
    expect(container).toBeEmptyDOMElement();
  });
});
