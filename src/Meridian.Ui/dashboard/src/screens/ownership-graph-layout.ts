import type { FamilyOfficeOwnershipEdge, FamilyOfficeOwnershipNode } from "./family-office-screen.view-model";

/** Pure deterministic layout. Relationship traversal never changes the server-authorized node set. */
export function layoutOwnershipGraph(nodes: FamilyOfficeOwnershipNode[], edges: FamilyOfficeOwnershipEdge[]) {
  const ids = [...new Set(nodes.map((node) => node.id))].sort();
  const allowed = new Set(ids);
  const closed = edges.filter((edge) => allowed.has(edge.fromId) && allowed.has(edge.toId));
  const ranked = closed.filter((edge) => edge.relationshipType === "Owns" || edge.relationshipType === "AssetAssociation");
  const forward = new Map(ids.map((id) => [id, [] as string[]]));
  const reverse = new Map(ids.map((id) => [id, [] as string[]]));
  for (const edge of ranked) { forward.get(edge.fromId)!.push(edge.toId); reverse.get(edge.toId)!.push(edge.fromId); }
  for (const neighbors of [...forward.values(), ...reverse.values()]) neighbors.sort();
  const visited = new Set<string>();
  const finish: string[] = [];
  for (const id of ids) {
    if (visited.has(id)) continue;
    const stack: Array<[string, boolean]> = [[id, false]];
    while (stack.length) {
      const [current, done] = stack.pop()!;
      if (done) { finish.push(current); continue; }
      if (visited.has(current)) continue;
      visited.add(current); stack.push([current, true]);
      for (const next of [...forward.get(current)!].reverse()) if (!visited.has(next)) stack.push([next, false]);
    }
  }
  const component = new Map<string, number>();
  const groups: string[][] = [];
  for (const id of finish.reverse()) {
    if (component.has(id)) continue;
    const group: string[] = [];
    const stack = [id];
    while (stack.length) {
      const current = stack.pop()!;
      if (component.has(current)) continue;
      component.set(current, groups.length); group.push(current);
      for (const next of reverse.get(current)!) if (!component.has(next)) stack.push(next);
    }
    groups.push(group.sort());
  }
  const ranks = groups.map(() => 0);
  // SCC condensation is a DAG; bounded relaxation also handles disconnected nodes.
  for (let pass = 0; pass < groups.length; pass++) {
    let changed = false;
    for (const edge of ranked) {
      const from = component.get(edge.fromId)!; const to = component.get(edge.toId)!;
      if (from !== to && ranks[to] < ranks[from] + 1) { ranks[to] = ranks[from] + 1; changed = true; }
    }
    if (!changed) break;
  }
  const labels = new Map(nodes.map((node) => [node.id, node.label]));
  const columns = new Map<number, string[]>();
  for (const id of ids) {
    const rank = ranks[component.get(id)!];
    columns.set(rank, [...(columns.get(rank) ?? []), id]);
  }
  for (const column of columns.values()) column.sort((a, b) => labels.get(a)!.localeCompare(labels.get(b)!, "en") || a.localeCompare(b));
  const orderedRanks = [...columns.keys()].sort((a, b) => a - b);
  for (const sweep of [orderedRanks, [...orderedRanks].reverse()]) {
    const order = new Map([...columns.values()].flatMap((column) => column.map((id, index) => [id, index] as const)));
    for (const rank of sweep) {
      const barycenter = (id: string) => {
        const neighbors = [...forward.get(id)!, ...reverse.get(id)!].filter((next) => ranks[component.get(next)!] !== rank);
        return neighbors.length ? neighbors.reduce((sum, next) => sum + (order.get(next) ?? 0), 0) / neighbors.length : order.get(id)!;
      };
      columns.get(rank)!.sort((a, b) => barycenter(a) - barycenter(b) || labels.get(a)!.localeCompare(labels.get(b)!, "en") || a.localeCompare(b));
    }
  }
  const positions = new Map<string, { x: number; y: number }>();
  for (const [rank, column] of columns) column.forEach((id, row) => positions.set(id, { x: 32 + rank * 352, y: 64 + row * 144 }));
  const cyclic = new Set(groups.filter((group) => group.length > 1).flat());
  for (const edge of ranked) if (edge.fromId === edge.toId) cyclic.add(edge.fromId);
  const maxRows = Math.max(1, ...[...columns.values()].map((column) => column.length));
  return { positions, edges: closed, cyclic, gapCount: edges.length - closed.length, width: Math.max(600, (Math.max(0, ...ranks) + 1) * 352), height: maxRows * 144 + 128 };
}

export function ownershipPathEdges(selectedId: string | null, edges: FamilyOfficeOwnershipEdge[]) {
  const highlighted = new Set<string>(); const visited = new Set<string>(); const queue = selectedId ? [selectedId] : [];
  while (queue.length) {
    const id = queue.pop()!; if (visited.has(id)) continue; visited.add(id);
    for (const edge of edges) if (edge.toId === id && (edge.relationshipType === "Owns" || edge.relationshipType === "AssetAssociation")) {
      highlighted.add(edge.id); queue.push(edge.fromId);
    }
  }
  return highlighted;
}
