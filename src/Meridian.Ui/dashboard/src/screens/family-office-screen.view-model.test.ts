import { describe, expect, it } from "vitest";
import { buildFamilyOfficeScreenViewModel, selectAdjacentFamilyOfficeNode } from "@/screens/family-office-screen.view-model";

const requiredPanelLabels = [
  "Total family net worth",
  "Entity breakdown",
  "Asset-class breakdown",
  "Cash and liabilities",
  "Private assets",
  "Unfunded commitments",
  "Unresolved reconciliation breaks",
  "Stale valuation warnings"
];

describe("buildFamilyOfficeScreenViewModel", () => {
  it("keeps route metadata, labels, empty states, and disabled reasons in the view model seam", () => {
    const vm = buildFamilyOfficeScreenViewModel();

    expect(vm.route).toMatchObject({
      path: "/portfolio/family-office",
      workspaceLabel: "Portfolio",
      label: "Family office",
      disabledReason: null
    });
    expect(vm.route.emptyState).toContain("Family office data is not connected yet");
    expect(vm.statusChips.map((chip) => chip.value)).toContain("/portfolio/family-office");
    expect(vm.panels.map((panel) => panel.label)).toEqual(requiredPanelLabels);
    expect(vm.panels.every((panel) => panel.emptyState.length > 0 && panel.ariaLabel.includes(panel.value))).toBe(true);
  });

  it("builds a selectable ownership graph with an accessible table fallback contract", () => {
    const vm = buildFamilyOfficeScreenViewModel("beta-llc");

    expect(vm.ownershipGraph.selectedNodeId).toBe("beta-llc");
    expect(vm.ownershipGraph.selectedNode?.label).toBe("Beta Holdings LLC");
    expect(vm.ownershipGraph.keyboardInstructions).toContain("Arrow keys");
    expect(vm.ownershipGraph.tableFallbackLabel).toBe("Family office ownership table fallback");
    expect(vm.ownershipGraph.nodes.length).toBeGreaterThan(1);
    expect(vm.ownershipGraph.edges.length).toBe(vm.ownershipGraph.nodes.filter((node) => node.parentId !== null).length);
    expect(vm.ownershipGraph.nodes.every((node) => node.selectAriaLabel && node.detailPanelId)).toBe(true);
  });

  it("selects adjacent graph nodes deterministically for keyboard navigation", () => {
    expect(selectAdjacentFamilyOfficeNode("family-holdco", "next")).toBe("alpha-trust");
    expect(selectAdjacentFamilyOfficeNode("alpha-trust", "previous")).toBe("family-holdco");
    expect(selectAdjacentFamilyOfficeNode("private-funds", "last")).toBe("real-estate");
    expect(selectAdjacentFamilyOfficeNode("missing", "first")).toBe("family-holdco");
  });
});
