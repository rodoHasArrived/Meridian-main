import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { AccountingValue, isAccountingFigure } from "@/screens/accounting-screen.accounting-value";

describe("AccountingValue", () => {
  it("keeps the data face for figures and the body face for prose and dates", () => {
    for (const figure of ["+$500.00", "4", "12.5%", "2026-05", "(1,250.00)", "−$3.10"]) {
      expect(isAccountingFigure(figure), figure).toBe(true);
    }
    for (const text of ["Unassigned", "Packet available for review", "1 source record", "Jan 1, 2026, 00:00 UTC", ""]) {
      expect(isAccountingFigure(text), text).toBe(false);
    }
  });

  it("renders a figure in tabular monospace and prose without it", () => {
    render(<><AccountingValue label="Variance" value="+$500.00" /><AccountingValue label="Owner" value="Unassigned" /></>);
    expect(screen.getByText("+$500.00")).toHaveClass("font-mono", "tabular-nums");
    expect(screen.getByText("Unassigned")).not.toHaveClass("font-mono");
  });
});
