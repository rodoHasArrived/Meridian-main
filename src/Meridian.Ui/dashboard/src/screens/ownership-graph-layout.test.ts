import { describe, expect, it } from "vitest";
import { buildFamilyOfficeScreenViewModel, FAMILY_OFFICE_DEMO_ENTITY_STRUCTURE } from "./family-office-screen.view-model";
import { layoutOwnershipGraph, ownershipPathEdges } from "./ownership-graph-layout";

const nodes = buildFamilyOfficeScreenViewModel(null, FAMILY_OFFICE_DEMO_ENTITY_STRUCTURE).ownershipGraph.nodes.slice(0, 3);
const [a, b, c] = nodes.map((node) => node.id);
const edge = (id: string, fromId: string, toId: string, relationshipType = "Owns") => ({ id, fromId, toId, relationshipType, ownershipPercent: 50, label: id });

describe("ownership graph", () => {
  it("keeps multiple parents with one child and stable positions regardless of input order", () => {
    const edges = [edge("ac", a, c), edge("bc", b, c)];
    const first = layoutOwnershipGraph(nodes, edges);
    const second = layoutOwnershipGraph([...nodes].reverse(), [...edges].reverse());
    expect([...first.positions].sort()).toEqual([...second.positions].sort());
    expect(first.positions.size).toBe(3);
    expect(first.positions.get(c)!.x).toBeGreaterThan(first.positions.get(a)!.x);
    expect(first.edges).toHaveLength(2);
  });
  it("terminates on cycles, retains cycle evidence and excludes unresolved endpoints", () => {
    const graph = layoutOwnershipGraph(nodes, [edge("ab", a, b), edge("ba", b, a), edge("missing", a, "unavailable")]);
    expect([...graph.cyclic].sort()).toEqual([a, b].sort());
    expect(graph.edges).toHaveLength(2);
    expect(graph.gapCount).toBe(1);
    expect([...ownershipPathEdges(a, graph.edges)].sort()).toEqual(["ab", "ba"]);
  });
  it("highlights Owns paths and asset associations, never custody or advice", () => {
    expect([...ownershipPathEdges(c, [edge("ac", a, c), edge("bc", b, c, "CustodiesFor")])]).toEqual(["ac"]);
  });
});
