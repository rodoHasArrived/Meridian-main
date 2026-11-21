import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it } from "vitest";
import { axe } from "jest-axe";
import { buildOwnershipMap, ownershipPaths } from "@/screens/family-office-ownership";
import { FamilyOfficeOwnershipMap } from "@/screens/family-office-ownership-map";
import { mapFamilyOfficeOverview } from "@/screens/family-office-screen.mapper";
import { buildFamilyOfficeOverviewFixture } from "@/screens/family-office-screen.mapper.test-fixture";

function structure() {
  const overview = buildFamilyOfficeOverviewFixture();
  overview.ownershipGraph = {
    asOfDate: "2026-11-12", evidenceLinks: [],
    nodes: ["A", "B", "C", "D"].map(nodeId => ({ nodeId, displayName: `Entity ${nodeId}`, nodeType: "Legal entity" })),
    edges: [
      { edgeId: "AC", sourceNodeId: "A", targetNodeId: "C", relationshipType: "Owns", ownershipPercent: 60, effectiveFrom: "2025-01-01", sourceDocumentId: "deed-1" },
      { edgeId: "BC", sourceNodeId: "B", targetNodeId: "C", relationshipType: "Owns", ownershipPercent: null },
      { edgeId: "DC", sourceNodeId: "D", targetNodeId: "C", relationshipType: "Advises", ownershipPercent: null }
    ]
  };
  overview.publicAssets = [];
  overview.privateAssets = [{ ...overview.privateAssets[0], privateAssetId: "asset-1", entityId: "C", displayName: "Private vehicle", evidenceCompleteness: "Complete", valuationDate: "2026-11-01" }];
  const mapped = mapFamilyOfficeOverview(overview);
  if (!mapped?.ownershipSource) throw new Error("Ownership fixture must retain its supplied graph.");
  return { ...mapped, ownershipSource: mapped.ownershipSource };
}

describe("faithful ownership relationships", () => {
  it("preserves multiple legal parents and all assets without turning advice into ownership", () => {
    const map = buildOwnershipMap(structure());
    const result = ownershipPaths(map, "private:asset-1");
    expect(result.paths).toHaveLength(2);
    expect([...result.highlighted].sort()).toEqual(["AC", "BC", "holding:private:asset-1"]);
    expect(map.edges.find(edge => edge.id === "AC")).toMatchObject({ percent: 60, fromDate: "2025-01-01", source: "deed-1" });
    expect(map.nodes.some(node => node.id === "private:asset-1")).toBe(true);
  });
  it("terminates cycles and retains unresolved endpoints as warnings", () => {
    const source = structure();
    source.ownershipSource.graph.edges.push({ edgeId: "CA", sourceNodeId: "C", targetNodeId: "A", relationshipType: "Owns" }, { edgeId: "missing", sourceNodeId: "Unknown", targetNodeId: "D", relationshipType: "Advises" });
    const map = buildOwnershipMap(source);
    expect(ownershipPaths(map, "C").cycle).toBe(true);
    expect(map.warnings).toHaveLength(1);
    expect(map.nodes.find(node => node.id === "Unknown")?.kind).toBe("Unresolved");
  });
  it("places a multiply owned subsidiary after every legal parent so direct links do not hide intermediate ownership", () => {
    const source = structure();
    source.ownershipSource.graph.edges.push({ edgeId: "AB", sourceNodeId: "A", targetNodeId: "B", relationshipType: "Owns", ownershipPercent: 100 });
    const map = buildOwnershipMap(source);
    const child = map.nodes.find(node => node.id === "C");
    const parent = map.nodes.find(node => node.id === "B");
    if (!child || !parent) throw new Error("Expected legal parent and child nodes in the fixture.");
    expect(child.x).toBeGreaterThan(parent.x);
  });
  it("selects an asset, exposes source/effective evidence in the table and supports keyboard map selection", async () => {
    const { container } = render(<FamilyOfficeOwnershipMap structure={structure()} />);
    await userEvent.selectOptions(screen.getByRole("combobox", { name: "Inspect entity or asset" }), "private:asset-1");
    const detail = screen.getByRole("region", { name: "Selected legal ownership paths" });
    expect(within(detail).getByText(/percentage unreported/)).toBeInTheDocument();
    expect(within(detail).queryByText(/Advises/)).not.toBeInTheDocument();
    const node = screen.getByRole("button", { name: "Entity C Legal entity; Inspect ownership record Entity C" });
    node.focus();
    await userEvent.keyboard("{Enter}");
    expect(node).toHaveAttribute("aria-pressed", "true");
    expect((await axe(container)).violations).toEqual([]);
    await userEvent.click(screen.getByRole("button", { name: "Show relationship table" }));
    expect(screen.getByRole("table", { name: "Ownership relationship evidence" })).toBeInTheDocument();
    expect(screen.getByText("deed-1")).toBeInTheDocument();
    expect((await axe(container)).violations).toEqual([]);
  });
});
