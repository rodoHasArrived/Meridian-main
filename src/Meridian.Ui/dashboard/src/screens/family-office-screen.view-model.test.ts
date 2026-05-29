import { describe, expect, it } from "vitest";
import {
  buildFamilyOfficeScreenViewModel,
  selectAdjacentFamilyOfficeNode,
  type FamilyOfficeEntityStructure
} from "@/screens/family-office-screen.view-model";

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

  it("derives panels and graph rows from the family-office entity structure", () => {
    const entityStructure: FamilyOfficeEntityStructure = {
      familyOfficeId: "root-family",
      displayName: "Root Family Office",
      baseCurrency: "USD",
      asOfDate: "2026-05-29",
      entities: [
        {
          entityId: "root-family",
          displayName: "Root Family Office",
          entityType: "family-office",
          parentEntityId: null,
          ownershipPercent: 100,
          netWorth: 10_000_000,
          cash: 1_500_000,
          liabilities: 500_000,
          privateAssetValue: 2_000_000,
          unfundedCommitments: 250_000,
          reconciliationBreakCount: 1,
          staleValuationWarningCount: 1,
          detail: "Root entity for custom structure."
        },
        {
          entityId: "trust-a",
          displayName: "Trust A",
          entityType: "trust",
          parentEntityId: "root-family",
          ownershipPercent: 60,
          netWorth: 6_000_000,
          cash: 900_000,
          liabilities: 100_000,
          privateAssetValue: 2_000_000,
          unfundedCommitments: 250_000,
          reconciliationBreakCount: 1,
          staleValuationWarningCount: 1,
          detail: "Trust A owns the private sleeve."
        }
      ],
      assetClassExposures: [
        { assetClass: "Cash", value: 1_500_000 },
        { assetClass: "Private equity", value: 2_000_000 }
      ],
      privateAssets: [
        {
          assetId: "trust-a-private-sleeve",
          entityId: "trust-a",
          displayName: "Trust A private sleeve",
          assetType: "Private equity",
          value: 2_000_000,
          valuationStatus: "stale"
        }
      ],
      commitments: [
        { commitmentId: "commitment-a", entityId: "trust-a", vehicleName: "PE Vehicle A", unfundedAmount: 250_000 }
      ]
    };

    const vm = buildFamilyOfficeScreenViewModel("trust-a", entityStructure);

    expect(vm.statusChips).toContainEqual({ label: "Entity source", value: "Root Family Office" });
    expect(vm.panels.find((panel) => panel.id === "total-family-net-worth")).toMatchObject({
      value: "$10.0M",
      detail: expect.stringContaining("Root Family Office")
    });
    expect(vm.panels.find((panel) => panel.id === "entity-breakdown")?.value).toBe("2 entities");
    expect(vm.panels.find((panel) => panel.id === "asset-class-breakdown")?.value).toBe("2 classes");
    expect(vm.panels.find((panel) => panel.id === "unfunded-commitments")?.value).toBe("$250.0K");
    expect(vm.ownershipGraph.nodes.map((node) => node.id)).toEqual(["root-family", "trust-a", "trust-a-private-sleeve"]);
    expect(vm.ownershipGraph.edges.map((edge) => edge.label)).toEqual([
      "60% ownership from Root Family Office to Trust A",
      "Trust A private sleeve is held by Trust A"
    ]);
  });

  it("selects adjacent graph nodes deterministically for keyboard navigation", () => {
    expect(selectAdjacentFamilyOfficeNode("family-holdco", "next")).toBe("alpha-trust");
    expect(selectAdjacentFamilyOfficeNode("alpha-trust", "previous")).toBe("family-holdco");
    expect(selectAdjacentFamilyOfficeNode("private-funds", "last")).toBe("real-estate");
    expect(selectAdjacentFamilyOfficeNode("missing", "first")).toBe("family-holdco");
  });
});
