import { useState } from "react";
import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { FamilyOwnershipGraph } from "./family-ownership-graph";
import { buildFamilyOfficeScreenViewModel, FAMILY_OFFICE_DEMO_ENTITY_STRUCTURE } from "./family-office-screen.view-model";

function OwnershipHarness() {
  const [selectedNodeId, setSelectedNodeId] = useState("unavailable-bookmark");
  const { ownershipGraph } = buildFamilyOfficeScreenViewModel(selectedNodeId, FAMILY_OFFICE_DEMO_ENTITY_STRUCTURE);
  return <FamilyOwnershipGraph graph={ownershipGraph} table={false} onSelect={setSelectedNodeId} />;
}

it("keeps a keyboard entry point when a bookmarked selection is unavailable", async () => {
  const user = userEvent.setup();
  render(<OwnershipHarness />);
  const graph = screen.getByRole("group", { name: "Family office ownership graph" });
  const nodes = within(graph).getAllByRole("button");

  expect(nodes.every(node => node.getAttribute("aria-pressed") === "false")).toBe(true);
  expect(nodes.filter(node => node.tabIndex === 0)).toEqual([nodes[0]]);
  await user.tab();
  expect(nodes[0]).toHaveFocus();
  expect(nodes[0]).toHaveAttribute("aria-pressed", "false");

  await user.keyboard("{ArrowDown}");
  expect(nodes[1]).toHaveFocus();
  expect(nodes[1]).toHaveAttribute("aria-pressed", "true");
  expect(nodes[1]).toHaveAttribute("tabindex", "0");
  expect(nodes[0]).toHaveAttribute("tabindex", "-1");
});
