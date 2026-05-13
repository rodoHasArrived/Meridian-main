import { describe, expect, it } from "vitest";
import { screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";

import { StrategyDesignerScreen } from "@/screens/strategy-designer-screen";
import { renderWithRouter } from "@/test/render";

describe("StrategyDesignerScreen", () => {
  it("renders the palette and empty canvas placeholder", () => {
    renderWithRouter(<StrategyDesignerScreen />);

    expect(screen.getByText("Visual Strategy Designer")).toBeInTheDocument();
    expect(screen.getByText("Block palette")).toBeInTheDocument();
    expect(screen.getByLabelText("Add Long Call block")).toBeInTheDocument();
    expect(screen.getByLabelText("Add Short Put block")).toBeInTheDocument();
    expect(screen.getByText(/Drop a leg from the palette/i)).toBeInTheDocument();
    expect(screen.getByTestId("strategy-designer-payoff")).toBeInTheDocument();
    expect(screen.getByTestId("strategy-designer-participation")).toBeInTheDocument();
    expect(screen.queryByTestId("strategy-designer-payoff-polyline")).toBeNull();
  });

  it("appends a leg when a palette block is activated and renders payoff polyline", async () => {
    const user = userEvent.setup();
    renderWithRouter(<StrategyDesignerScreen />);

    await user.click(screen.getByLabelText("Add Long Call block"));

    expect(screen.getByText(/Canvas · 1 leg/)).toBeInTheDocument();
    expect(screen.getByTestId("strategy-designer-payoff-polyline")).toBeInTheDocument();
    expect(screen.getByTestId("strategy-designer-participation-list")).toBeInTheDocument();
  });

  it("loads a two-leg sample strategy on demand and reports break-even in the caption", async () => {
    const user = userEvent.setup();
    renderWithRouter(<StrategyDesignerScreen />);

    await user.click(screen.getByRole("button", { name: /load sample/i }));

    expect(screen.getByText(/Canvas · 2 legs/)).toBeInTheDocument();
    expect(screen.getByTestId("strategy-designer-payoff-polyline")).toBeInTheDocument();
    expect(screen.getByText(/Break-even/i)).toBeInTheDocument();
  });

  it("clears the canvas back to the empty state", async () => {
    const user = userEvent.setup();
    renderWithRouter(<StrategyDesignerScreen />);

    await user.click(screen.getByRole("button", { name: /load sample/i }));
    expect(screen.getByText(/Canvas · 2 legs/)).toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: /clear canvas/i }));

    expect(screen.getByText(/Canvas · 0 legs/)).toBeInTheDocument();
    expect(screen.queryByTestId("strategy-designer-payoff-polyline")).toBeNull();
  });
});
