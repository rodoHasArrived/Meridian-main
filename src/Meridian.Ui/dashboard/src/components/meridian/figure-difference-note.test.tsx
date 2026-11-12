import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { explainFigureDifference, FigureDifferenceNote, type FigureReference } from "./figure-difference-note";

const reference: FigureReference = { label: "Overview", value: "100", measure: "Market value", basis: "Observed marks", scope: "Account A", currency: "USD", asOf: "2026-11-10" };

describe("figure differences", () => {
  it("flags unexplained differences instead of inventing an explanation", () => {
    expect(explainFigureDifference(reference, { ...reference, value: "101" })).toContain("has not been reconciled");
  });
  it("does not compare exposure with market value as equivalent totals", () => {
    expect(explainFigureDifference(reference, { ...reference, value: "101", measure: "Gross exposure", scope: "Run B" })).toContain("different measure, population");
  });
  it("makes missing context explicit and exposes both operands", () => {
    render(<FigureDifferenceNote left={reference} right={{ ...reference, label: "Attribution", value: "101", basis: "" }} />);
    expect(screen.getByLabelText("Figure comparison note")).toHaveTextContent("comparison context is incomplete");
    expect(screen.getByText("Compare figure bases")).toBeInTheDocument();
  });
  it("does not add a difference note for matching values and currency", () => {
    expect(explainFigureDifference(reference, { ...reference })).toBeNull();
  });
});
