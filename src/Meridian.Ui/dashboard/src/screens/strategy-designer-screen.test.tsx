import { render, screen, within } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { StrategyDesignerScreen } from "@/screens/strategy-designer-screen";

describe("StrategyDesignerScreen", () => {
  it("renders GET Strategy Builder endpoints as links and POST endpoints as reference-only backend rows", () => {
    render(<StrategyDesignerScreen />);

    const panel = screen.getByTestId("strategy-builder-backtest-proof");

    expect(
      within(panel).getByRole("link", {
        name: "GET /api/workstation/strategy/designer/templates for Templates"
      })
    ).toHaveAttribute("href", "/api/workstation/strategy/designer/templates");

    expect(
      within(panel).getByRole("link", {
        name: "GET /api/workstation/strategy/designer/field-catalog for Field catalog"
      })
    ).toHaveAttribute("href", "/api/workstation/strategy/designer/field-catalog");

    expect(
      within(panel).queryByRole("link", {
        name: "POST /api/workstation/strategy/designer/run-backtest for Run backtest"
      })
    ).toBeNull();

    expect(
      within(panel).getByRole("group", {
        name: "Reference-only POST /api/workstation/strategy/designer/run-backtest for Run backtest"
      })
    ).toHaveTextContent("Reference");
  });
});
