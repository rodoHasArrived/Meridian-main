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

    await user.type(screen.getByRole("textbox", { name: "Search Strategy Builder field catalog" }), "amx");

    expect(screen.getByTestId("strategy-builder-field-catalog")).toHaveTextContent("AMX_PRIVATE_SCORE");
    expect(screen.getByTestId("field-disabled-AMX_PRIVATE_SCORE")).toHaveTextContent("No Meridian canonical source");
    expect(screen.queryByText("63-day momentum")).not.toBeInTheDocument();
  });

  it("renders a VM-owned empty state when field catalog search has no matches", async () => {
    const user = userEvent.setup();
    renderWithRouter(<StrategyDesignerScreen />);

    const search = screen.getByRole("textbox", { name: "Search Strategy Builder field catalog" });
    expect(search).toHaveAccessibleDescription(/Filter by field ID/i);

    await user.type(search, "no-such-field");

    const catalog = screen.getByTestId("strategy-builder-field-catalog");
    expect(within(catalog).getByRole("status", { name: "Strategy Builder field catalog empty state" })).toHaveTextContent(
      'No field catalog entries match "no-such-field".'
    );
    expect(within(catalog).getByText(/0 fields match "no-such-field" out of \d+\./)).toBeInTheDocument();
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
    expect(spotPrice).toHaveAccessibleDescription(/non-negative decimal used to sample payoff/i);

    await user.clear(spotPrice);
    await user.type(spotPrice, "-3");
    await user.tab();

    expect(spotPrice).toHaveValue(100);
    expect(spotPrice).toHaveAttribute("aria-invalid", "true");
    expect(spotPrice).toHaveAttribute(
      "aria-errormessage",
      "strategy-designer-spot-price-feedback"
    );
    expect(screen.getByText(
      "Enter a non-negative spot price. Restored $100.00 for payoff sampling."
    )).toHaveAttribute("role", "status");

    await user.clear(spotPrice);
    await user.type(spotPrice, "125");

    expect(spotPrice).not.toHaveAttribute("aria-invalid");
    expect(screen.queryByText(/restored \$100\.00/i)).not.toBeInTheDocument();
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
