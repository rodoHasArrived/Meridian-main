import type { FamilyOfficeEntityStructure } from "@/screens/family-office-screen.view-model";

export interface OwnershipMapNode { id: string; name: string; kind: string; currency?: string; recordAsOf?: string; valuationDate?: string; x: number; y: number }
export interface OwnershipMapEdge { id: string; from: string; to: string; relation: string; percent: number | null; fromDate?: string; toDate?: string; source?: string; legal: boolean; holding: boolean }
export interface OwnershipMap { nodes: OwnershipMapNode[]; edges: OwnershipMapEdge[]; warnings: string[]; asOf: string; width: number; height: number }

export function buildOwnershipMap(structure: FamilyOfficeEntityStructure): OwnershipMap {
  const source = structure.ownershipSource;
  const nodes: OwnershipMapNode[] = (source?.graph.nodes ?? []).map(node => ({ id: node.nodeId, name: node.displayName, kind: node.nodeType, currency: node.currency, x: 0, y: 0 }));
  const edges: OwnershipMapEdge[] = (source?.graph.edges ?? []).map(edge => ({ id: edge.edgeId, from: edge.sourceNodeId, to: edge.targetNodeId, relation: edge.relationshipType, percent: edge.ownershipPercent ?? null, fromDate: edge.effectiveFrom, toDate: edge.effectiveTo, source: edge.sourceDocumentId, legal: edge.relationshipType.toLowerCase() === "owns", holding: false }));
  const warnings: string[] = [];
  const known = new Set(nodes.map(node => node.id));
  // Keep missing graph endpoints visible as unresolved records; never invent ownership.
  for (const edge of edges) for (const id of [edge.from, edge.to]) if (!known.has(id)) {
    nodes.push({ id, name: `Unresolved node ${id}`, kind: "Unresolved", x: 0, y: 0 });
    known.add(id);
    warnings.push(`Relationship ${edge.id} refers to an unresolved structure node.`);
  }
  const assets = [...(source?.publicAssets ?? []).map(asset => ({ id: `asset:${asset.assetId}`, name: asset.displayName, owner: asset.entityId, currency: asset.currency, kind: asset.assetClass, asOf: asset.asOfDate, valuationDate: asset.valuationDate })), ...(source?.privateAssets ?? []).map(asset => ({ id: `private:${asset.privateAssetId}`, name: asset.displayName, owner: asset.entityId, currency: asset.currency, kind: asset.assetType, asOf: asset.asOfDate, valuationDate: asset.valuationDate }))];
  for (const asset of assets) {
    nodes.push({ id: asset.id, name: asset.name, kind: `Asset · ${asset.kind}`, currency: asset.currency, recordAsOf: asset.asOf, valuationDate: asset.valuationDate, x: 0, y: 0 });
    if (known.has(asset.owner)) edges.push({ id: `holding:${asset.id}`, from: asset.owner, to: asset.id, relation: "Held in asset register", percent: null, legal: false, holding: true });
    else warnings.push(`${asset.name} has no resolved holding entity in this structure.`);
  }
  const asOf = source?.graph.asOfDate ?? structure.asOfDate;
  // The service selects effective relationships. DateOnly display fields cannot reproduce
  // timestamp boundaries, so the browser must not refilter those authoritative edges.
  const activeEdges = edges;
  const structuralEdges = activeEdges.filter(edge => edge.legal || edge.holding);
  const incoming = new Map(nodes.map(node => [node.id, 0]));
  for (const edge of structuralEdges) incoming.set(edge.to, (incoming.get(edge.to) ?? 0) + 1);
  const levels = new Map<string, number>();
  const queue = nodes.filter(node => !incoming.get(node.id)).map(node => node.id);
  for (const id of queue) levels.set(id, 0);
  let cursor = 0;
  while (cursor < queue.length) {
    const id = queue[cursor++];
    for (const edge of structuralEdges) if (edge.from === id) {
      levels.set(edge.to, Math.max(levels.get(edge.to) ?? 0, (levels.get(id) ?? 0) + 1));
      const remaining = (incoming.get(edge.to) ?? 1) - 1;
      incoming.set(edge.to, remaining);
      if (remaining === 0) queue.push(edge.to);
    }
  }
  const rows = new Map<number, number>();
  for (const node of nodes) {
    const level = levels.get(node.id) ?? 0;
    const row = rows.get(level) ?? 0;
    node.x = 24 + level * 300;
    node.y = 70 + row * 110;
    rows.set(level, row + 1);
  }
  return { nodes, edges, warnings, asOf, width: Math.max(800, ...nodes.map(node => node.x + 240)), height: Math.max(180, ...nodes.map(node => node.y + 100)) };
}

/** Trace only supplied legal Owns edges, optionally ending with an asset register holding. */
export function ownershipPaths(map: OwnershipMap, selectedId: string): { paths: OwnershipMapEdge[][]; highlighted: Set<string>; cycle: boolean; truncated: boolean } {
  const eligible = map.edges.filter(edge => edge.legal || edge.holding);
  const result: OwnershipMapEdge[][] = [];
  let cycle = false;
  let truncated = false;
  let visits = 0;
  function visit(id: string, suffix: OwnershipMapEdge[], seen: Set<string>) {
    if (++visits > 2000 || result.length >= 50) { truncated = true; return; }
    if (seen.has(id)) { cycle = true; if (suffix.length) result.push(suffix); return; }
    const parents = eligible.filter(edge => edge.to === id);
    if (!parents.length) { if (suffix.length) result.push(suffix); return; }
    const nextSeen = new Set(seen).add(id);
    for (const edge of parents) visit(edge.from, [edge, ...suffix], nextSeen);
  }
  visit(selectedId, [], new Set());
  return { paths: result, highlighted: new Set(result.flat().map(edge => edge.id)), cycle, truncated };
}

export function ownershipEdgeLabel(edge: OwnershipMapEdge): string {
  return `${edge.relation}${edge.legal ? ` · ${edge.percent == null ? "percentage unreported" : `${edge.percent}%`}` : ""}`;
}
