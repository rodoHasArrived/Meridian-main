import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { EntitySummary, isDataLikeValue } from "@/components/meridian/ui-kit-primitives";
import { FormRow, FormSectionLabel } from "@/components/ui/form";

// B4: labels are the third tier of the type scale. One- or two-word labels may be tracked caps
// in the data face; a longer label is a phrase and reads in sentence case in the body face.
describe("label tier", () => {
  it("keeps tracked caps for a short form label and sentence case for a phrase", () => {
    render(
      <>
        <FormRow label="Ledger book" labelFor="book"><input id="book" /></FormRow>
        <FormRow label="Search by account, amount, journal ID" labelFor="search"><input id="search" /></FormRow>
      </>
    );

    const short = screen.getByText("Ledger book");
    expect(short).toHaveClass("type-label", "type-label--caps");
    const phrase = screen.getByText("Search by account, amount, journal ID");
    expect(phrase).toHaveClass("type-label");
    expect(phrase).not.toHaveClass("type-label--caps");
    // The label still names its control.
    expect(screen.getByLabelText("Search by account, amount, journal ID")).toHaveAttribute("id", "search");
  });

  it("applies the same rule to form section labels", () => {
    render(
      <>
        <FormSectionLabel>Routing</FormSectionLabel>
        <FormSectionLabel>Broker statement import settings</FormSectionLabel>
      </>
    );
    expect(screen.getByText("Routing")).toHaveClass("type-label--caps");
    expect(screen.getByText("Broker statement import settings")).not.toHaveClass("type-label--caps");
  });
});

describe("prose in the body face", () => {
  it("sets figures and identifiers in the data face and phrases in the body face", () => {
    render(
      <EntitySummary
        eyebrow="Break"
        title="Cash sweep mismatch"
        subtitle="Broker statement vs. ledger"
        description="The statement line has no matching ledger posting."
        ariaLabel="Selected break"
        status={null}
        fields={[
          { label: "Variance", value: "$1,250.00" },
          { label: "Break ID", value: "brk-2026-0042" },
          { label: "Owner", value: "Awaiting reviewer assignment" }
        ]}
      />
    );
    expect(screen.getByText("$1,250.00")).toHaveClass("entity-summary-value--data");
    expect(screen.getByText("brk-2026-0042")).toHaveClass("entity-summary-value--data");
    expect(screen.getByText("Awaiting reviewer assignment")).not.toHaveClass("entity-summary-value--data");
  });

  it("recognizes figures and identifiers but not phrases or dates written out", () => {
    for (const value of ["1,234", "-$512.00", "(500.00)", "12.5%", "+0.25", "500.00 USD", "je-cash-1", "2026-06-30", "ACC_1001"]) {
      expect(isDataLikeValue(value), value).toBe(true);
    }
    for (const value of ["Posted", "Not retained", "Jun 30, 2026, 18:05 UTC", "Cash sweep", "", "—"]) {
      expect(isDataLikeValue(value), value).toBe(false);
    }
  });
});
