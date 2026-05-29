import { screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { axe } from "jest-axe";
import { describe, expect, it } from "vitest";
import { AlternativesScreen } from "@/screens/alternatives-screen";
import { renderWithRouter } from "@/test/render";

describe("AlternativesScreen", () => {
  it("renders private asset records with explicit warnings and opens the detail drawer", async () => {
    const user = userEvent.setup();
    renderWithRouter(<AlternativesScreen />, { initialEntries: ["/portfolio/alternatives"] });

    expect(screen.getByRole("heading", { name: "Private Alternatives Register" })).toBeInTheDocument();
    expect(screen.getByText(/Records may be loaded from fixtures or statement imports/)).toBeInTheDocument();

    const table = screen.getByRole("table", { name: "Private asset list" });
    expect(within(table).getByText("Aurora Growth Fund V")).toBeInTheDocument();
    expect(within(table).getAllByText("statement imported").length).toBeGreaterThan(0);
    expect(screen.getAllByText("manual valuation").length).toBeGreaterThan(0);
    expect(screen.getAllByText("unreconciled").length).toBeGreaterThan(0);
    expect(screen.getAllByText("missing source document").length).toBeGreaterThan(0);
    expect(screen.getAllByText("stale NAV").length).toBeGreaterThan(0);

    await user.click(within(table).getByRole("row", { name: /Harbor Industrial Development/ }));

    const drawer = screen.getByRole("dialog", { name: "Private asset detail drawer" });
    expect(drawer).toHaveAttribute("id", "alternatives-asset-detail-drawer");
    expect(within(drawer).getByRole("heading", { name: "Harbor Industrial Development" })).toBeInTheDocument();
    expect(within(drawer).getByRole("region", { name: "Commitment summary" })).toBeInTheDocument();
    expect(within(drawer).getByRole("region", { name: "Valuation history" })).toBeInTheDocument();
    expect(within(drawer).getByRole("region", { name: "Document/evidence panel" })).toBeInTheDocument();
    expect(within(drawer).getByRole("region", { name: "Capital activity timeline" })).toBeInTheDocument();
    expect(within(drawer).getByRole("region", { name: "Stale data warnings" })).toHaveTextContent("149 days old");
  });

  it("has no basic accessibility violations", async () => {
    const { container } = renderWithRouter(<AlternativesScreen />, { initialEntries: ["/portfolio/alternatives"] });

    const results = await axe(container);
    expect(results.violations).toHaveLength(0);
  });
});
