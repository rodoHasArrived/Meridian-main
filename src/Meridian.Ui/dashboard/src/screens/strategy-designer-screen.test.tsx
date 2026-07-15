import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it } from "vitest";
import { StrategyDesignerScreen } from "@/screens/strategy-designer-screen";

describe("StrategyDesignerScreen", () => {
  it("reserves the primary desktop width for the cell authoring canvas", () => {
    render(<StrategyDesignerScreen />);

    expect(screen.getByTestId("strategy-designer-primary-workbench"))
      .toHaveClass("xl:grid-cols-[280px_minmax(0,1fr)]");
  });

  it("keeps proof and analysis secondary until requested", async () => {
    const user = userEvent.setup();
    render(<StrategyDesignerScreen />);

    const proofDisclosure = screen.getByText("Templates and backtest proof").closest("details");
    const analysisDisclosure = screen.getByText("Transition and payoff analysis").closest("details");
    expect(proofDisclosure).not.toHaveAttribute("open");
    expect(analysisDisclosure).not.toHaveAttribute("open");

    await user.click(screen.getByText("Templates and backtest proof"));
    expect(proofDisclosure).toHaveAttribute("open");

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
