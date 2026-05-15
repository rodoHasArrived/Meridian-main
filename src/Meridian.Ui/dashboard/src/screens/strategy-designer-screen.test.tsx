import { describe, expect, it } from "vitest";
import { screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";

import { StrategyDesignerScreen } from "@/screens/strategy-designer-screen";
import { renderWithRouter } from "@/test/render";

describe("StrategyDesignerScreen", () => {
  it("renders the Strategy Builder workbench with catalog, canvas, inspector, and proof panels", () => {
    renderWithRouter(<StrategyDesignerScreen />);

    expect(screen.getByRole("heading", { name: "Strategy Builder Workbench" })).toBeInTheDocument();
    expect(screen.getByTestId("strategy-builder-field-catalog")).toHaveTextContent("AMX private score");
    expect(screen.getByTestId("strategy-builder-canvas")).toHaveTextContent("Momentum score");
    expect(screen.getByTestId("strategy-builder-inspector")).toHaveTextContent("Liquid equity universe");
    expect(screen.getByTestId("strategy-builder-backtest-proof")).toHaveTextContent("Backtest only");
    expect(screen.getByTestId("strategy-builder-transition-map")).toHaveTextContent("Liquid equity universe -> Momentum score");
    expect(screen.getByTestId("strategy-designer-options-payoff-panel")).toBeInTheDocument();
  });

  it("filters field catalog results and surfaces disabled reasons from the view model", async () => {
    const user = userEvent.setup();
    renderWithRouter(<StrategyDesignerScreen />);

    await user.type(screen.getByRole("textbox", { name: "Search field catalog" }), "amx");

    expect(screen.getByTestId("strategy-builder-field-catalog")).toHaveTextContent("AMX_PRIVATE_SCORE");
    expect(screen.getByTestId("field-disabled-AMX_PRIVATE_SCORE")).toHaveTextContent("No Meridian canonical source");
    expect(screen.queryByText("63-day momentum")).not.toBeInTheDocument();
  });

  it("lets keyboard users select a strategy cell and updates inspector and trace state", async () => {
    const user = userEvent.setup();
    renderWithRouter(<StrategyDesignerScreen />);

    const row = screen.getByRole("row", { name: /select momentum score/i });
    row.focus();
    await user.keyboard("{Enter}");

    expect(row).toHaveAttribute("aria-selected", "true");
    const detail = screen.getByRole("region", { name: /strategy builder cell detail for momentum score/i });
    expect(detail).toHaveTextContent("MOMENTUM_63D - VOLATILITY_20D");
    expect(screen.getByTestId("strategy-builder-backtest-proof")).toHaveTextContent("Momentum score");
  });

  it("loads the options payoff template and preserves the payoff panel sample", async () => {
    const user = userEvent.setup();
    renderWithRouter(<StrategyDesignerScreen />);

    await user.click(screen.getByRole("button", { name: "Load Options payoff template" }));

    expect(screen.getByTestId("strategy-builder-canvas")).toHaveTextContent("Options payoff panel");
    expect(screen.getByTestId("strategy-builder-backtest-proof")).toHaveTextContent("strategy-design:");

    await user.click(screen.getByRole("button", { name: /load sample bull call spread/i }));

    expect(screen.getByTestId("strategy-designer-payoff-polyline")).toBeInTheDocument();
    expect(screen.getByTestId("strategy-designer-participation-list")).toBeInTheDocument();
  });

  it("keeps options payoff spot-price validation in the view model", async () => {
    const user = userEvent.setup();
    renderWithRouter(<StrategyDesignerScreen />);

    const spotPrice = screen.getByRole("spinbutton", { name: /underlying spot price/i });
    await user.clear(spotPrice);
    await user.type(spotPrice, "-3");
    await user.tab();

    expect(spotPrice).toHaveValue(100);
  });

  it("associates route actions with the designer endpoint catalog", () => {
    renderWithRouter(<StrategyDesignerScreen />);

    const proof = screen.getByTestId("strategy-builder-backtest-proof");
    expect(within(proof).getByRole("link", { name: /GET Templates/i })).toHaveAttribute(
      "href",
      "/api/workstation/strategy/designer/templates"
    );
    expect(within(proof).getByRole("link", { name: /POST Run backtest/i })).toHaveAttribute(
      "href",
      "/api/workstation/strategy/designer/run-backtest"
    );
  });
});
