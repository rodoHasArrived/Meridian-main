import { describe, expect, it } from "vitest";
import { screen, within } from "@testing-library/react";
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
    expect(screen.getByRole("button", { name: /clear strategy canvas/i })).toBeDisabled();
    expect(screen.getByRole("button", { name: /clear strategy canvas/i })).toHaveAttribute(
      "title",
      "No strategy legs to clear."
    );
    expect(screen.getByRole("status", { name: "Strategy leg detail empty state" })).toHaveTextContent("No legs on canvas");
  });

  it("appends a leg when a palette block is activated and renders payoff polyline", async () => {
    const user = userEvent.setup();
    renderWithRouter(<StrategyDesignerScreen />);

    await user.click(screen.getByLabelText("Add Long Call block"));

    expect(screen.getByText(/Canvas · 1 leg/)).toBeInTheDocument();
    expect(screen.getByTestId("strategy-designer-payoff-polyline")).toBeInTheDocument();
    expect(screen.getByTestId("strategy-designer-participation-list")).toBeInTheDocument();
    const detail = screen.getByRole("region", { name: /selected strategy leg detail for long call/i });
    expect(detail).toHaveTextContent("Selected leg");
    expect(detail).toHaveTextContent("Break-even");
    expect(detail).toHaveTextContent("Payoff at spot");
  });

  it("exposes keyboard-operable selection state for canvas legs", async () => {
    const user = userEvent.setup();
    renderWithRouter(<StrategyDesignerScreen />);

    await user.click(screen.getByRole("button", { name: /load sample/i }));

    const firstLeg = screen.getByRole("button", { name: /selected long call/i });
    const secondLeg = screen.getByRole("button", { name: /select short call/i });
    expect(firstLeg).toHaveAttribute("aria-pressed", "true");
    expect(firstLeg).toHaveAttribute("aria-controls", "strategy-designer-selected-leg-detail");
    expect(firstLeg).toHaveAttribute("aria-expanded", "true");
    expect(secondLeg).toHaveAttribute("aria-pressed", "false");
    expect(secondLeg).toHaveAttribute("aria-expanded", "false");
    expect(screen.getByRole("region", { name: /selected strategy leg detail for long call/i })).toHaveTextContent("Long exposure");

    secondLeg.focus();
    await user.keyboard("{Enter}");

    expect(screen.getByRole("button", { name: /select long call/i })).toHaveAttribute("aria-pressed", "false");
    expect(screen.getByRole("button", { name: /selected short call/i })).toHaveAttribute("aria-pressed", "true");
    const detail = screen.getByRole("region", { name: /selected strategy leg detail for short call/i });
    expect(within(detail).getByText("Short exposure")).toBeInTheDocument();
    expect(within(detail).getByText("Break-even")).toBeInTheDocument();
  });

  it("lets keyboard users select a canvas leg from the leg row and updates the detail panel", async () => {
    const user = userEvent.setup();
    renderWithRouter(<StrategyDesignerScreen />);

    await user.click(screen.getByRole("button", { name: /load sample/i }));

    const secondLegRow = screen.getByRole("listitem", { name: "Short Call · 110, Short Call, leg 2 of 2" });
    secondLegRow.focus();
    await user.keyboard(" ");

    expect(screen.getByRole("button", { name: /selected short call/i })).toHaveAttribute("aria-expanded", "true");
    expect(screen.getByRole("region", { name: /selected strategy leg detail for short call/i })).toHaveTextContent("Short exposure");
  });

  it("associates canvas leg field labels with stable view-model ids", async () => {
    const user = userEvent.setup();
    const { container } = renderWithRouter(<StrategyDesignerScreen />);

    await user.click(screen.getByRole("button", { name: /load sample/i }));

    const directionLabel = container.querySelector('label[for="strategy-leg-sample-long-call-direction"]');
    const directionInput = container.querySelector("#strategy-leg-sample-long-call-direction");
    const quantityLabel = container.querySelector('label[for="strategy-leg-sample-long-call-quantity"]');
    const quantityInput = container.querySelector("#strategy-leg-sample-long-call-quantity");
    const premiumLabel = container.querySelector('label[for="strategy-leg-sample-long-call-premium"]');
    const premiumInput = container.querySelector("#strategy-leg-sample-long-call-premium");

    expect(directionLabel).toHaveTextContent("Direction");
    expect(directionInput).toHaveAccessibleName("Direction for Long Call · 100");
    expect(quantityLabel).toHaveTextContent("Quantity");
    expect(quantityInput).toHaveAccessibleName("Quantity for Long Call · 100");
    expect(premiumLabel).toHaveTextContent("Premium");
    expect(premiumInput).toHaveAccessibleName("Premium for Long Call · 100");
  });

  it("reorders canvas legs with keyboard-operable move commands", async () => {
    const user = userEvent.setup();
    renderWithRouter(<StrategyDesignerScreen />);

    await user.click(screen.getByRole("button", { name: /load sample/i }));

    const moveFirstUp = screen.getByRole("button", { name: "Move Long Call · 100 up" });
    expect(moveFirstUp).toBeDisabled();
    expect(moveFirstUp).toHaveAttribute("title", "Long Call · 100 is already the first leg.");

    await user.click(screen.getByRole("button", { name: "Move Long Call · 100 down" }));

    const legs = screen.getAllByRole("listitem")
      .filter((item) => item.hasAttribute("data-leg-id"))
      .map((item) => item.getAttribute("aria-label"));
    expect(legs).toEqual([
      "Short Call · 110, Short Call, leg 1 of 2",
      "Long Call · 100, Long Call, leg 2 of 2"
    ]);
    expect(screen.getByRole("button", { name: "Move Long Call · 100 down" })).toBeDisabled();
    expect(screen.getByRole("button", { name: "Move Long Call · 100 down" })).toHaveAttribute(
      "title",
      "Long Call · 100 is already the last leg."
    );
  });

  it("keeps spot price draft validation in the view model", async () => {
    const user = userEvent.setup();
    renderWithRouter(<StrategyDesignerScreen />);

    const spotPrice = screen.getByRole("spinbutton", { name: /underlying spot price/i });
    await user.clear(spotPrice);
    await user.type(spotPrice, "-3");
    await user.tab();

    expect(spotPrice).toHaveValue(100);
  });

  it("loads a two-leg sample strategy on demand and reports break-even in the caption", async () => {
    const user = userEvent.setup();
    renderWithRouter(<StrategyDesignerScreen />);

    await user.click(screen.getByRole("button", { name: /load sample/i }));

    expect(screen.getByText(/Canvas · 2 legs/)).toBeInTheDocument();
    expect(screen.getByTestId("strategy-designer-payoff-polyline")).toBeInTheDocument();
    expect(screen.getByTestId("strategy-designer-payoff")).toHaveTextContent(/Break-even/i);
  });

  it("requires confirmation before clearing the canvas back to the empty state", async () => {
    const user = userEvent.setup();
    renderWithRouter(<StrategyDesignerScreen />);

    await user.click(screen.getByRole("button", { name: /load sample/i }));
    expect(screen.getByText(/Canvas · 2 legs/)).toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: /clear strategy canvas/i }));
    expect(screen.getByText(/Canvas · 2 legs/)).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /confirm clear strategy canvas and remove 2 legs/i })).toHaveTextContent(
      "Confirm clear"
    );

    await user.click(screen.getByRole("button", { name: /confirm clear strategy canvas and remove 2 legs/i }));

    expect(screen.getByText(/Canvas · 0 legs/)).toBeInTheDocument();
    expect(screen.queryByTestId("strategy-designer-payoff-polyline")).toBeNull();
  });
});
