import { screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { axe } from "jest-axe";
import { describe, expect, it } from "vitest";
import { FamilyOfficeScreen } from "@/screens/family-office-screen";
import { FAMILY_OFFICE_DEMO_ENTITY_STRUCTURE } from "@/screens/family-office-screen.view-model";
import { renderWithRouter } from "@/test/render";

describe("FamilyOfficeScreen", () => {
  it("renders an honest not-connected state by default", () => {
    renderWithRouter(<FamilyOfficeScreen />, { initialEntries: ["/portfolio/family-office"] });

    expect(screen.getByRole("heading", { name: "Family Office Portfolio" })).toBeInTheDocument();
    expect(screen.getByText("Family office data is not connected")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Set up family entities" })).toHaveAttribute("href", "/accounting/entity-setup");
    expect(screen.getByLabelText("Family office data confidence")).toHaveTextContent("Entity setup required");
    expect(screen.getAllByText("Set up family entities and connect portfolio, accounting, and private-asset sources to begin consolidated review.")).toHaveLength(1);
    expect(screen.queryByLabelText("Family office summary panels")).not.toBeInTheDocument();
  });

  it("renders family-office panels and accessible ownership graph controls", async () => {
    const user = userEvent.setup();
    renderWithRouter(<FamilyOfficeScreen entityStructure={FAMILY_OFFICE_DEMO_ENTITY_STRUCTURE} />, { initialEntries: ["/portfolio/family-office"] });

    expect(screen.getByRole("heading", { name: "Family Office Portfolio" })).toBeInTheDocument();
    expect(screen.getByLabelText("Family office data confidence")).toHaveTextContent("Meridian Family HoldCo");
    expect(screen.getByLabelText("Family office summary panels")).toBeInTheDocument();
    expect(screen.getAllByText("$128.4M").length).toBeGreaterThan(0);

    const graph = screen.getByRole("group", { name: "Family office ownership graph" });
    expect(within(graph).getByRole("button", { name: "Inspect ownership node Meridian Family HoldCo" })).toHaveAttribute("aria-pressed", "true");

    within(graph).getByRole("button", { name: "Inspect ownership node Meridian Family HoldCo" }).focus();
    await user.keyboard("{ArrowDown}");
    expect(within(graph).getByRole("button", { name: "Inspect ownership node Alpha Family Trust" })).toHaveAttribute("aria-pressed", "true");
    expect(screen.getByRole("region", { name: "Selected ownership detail" })).toHaveTextContent("Alpha Family Trust");
  });

  it("switches to the dense table fallback with selectable rows", async () => {
    const user = userEvent.setup();
    renderWithRouter(<FamilyOfficeScreen entityStructure={FAMILY_OFFICE_DEMO_ENTITY_STRUCTURE} />, { initialEntries: ["/portfolio/family-office"] });

    await user.click(screen.getByRole("button", { name: "Show ownership table fallback" }));

    const table = screen.getByRole("treegrid", { name: "Family office ownership table fallback" });
    expect(table).toBeInTheDocument();
    await user.click(within(table).getByRole("row", { name: "Inspect ownership node Beta Holdings LLC" }));
    expect(screen.getByRole("region", { name: "Selected ownership detail" })).toHaveTextContent("Beta Holdings LLC");
  });

  it("has no basic accessibility violations", async () => {
    const { container } = renderWithRouter(<FamilyOfficeScreen />, { initialEntries: ["/portfolio/family-office"] });

    const results = await axe(container);
    expect(results.violations).toHaveLength(0);
  });
});
