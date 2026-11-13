import { useId, useMemo, useRef, useState, type KeyboardEvent } from "react";
import { DenseDataTable, type DenseDataTableColumn } from "@/components/meridian/ui-kit-primitives";
import type { FamilyOfficeOwnershipEdge, FamilyOfficeOwnershipGraphViewModel } from "./family-office-screen.view-model";
import { layoutOwnershipGraph, ownershipPathEdges } from "./ownership-graph-layout";

export function FamilyOwnershipGraph({ graph, table, onSelect }: { graph: FamilyOfficeOwnershipGraphViewModel; table: boolean; onSelect: (id: string) => void }) {
  const marker = useId().replace(/:/g, "");
  const layout = useMemo(() => layoutOwnershipGraph(graph.nodes, graph.edges), [graph.nodes, graph.edges]);
  const highlighted = ownershipPathEdges(graph.selectedNodeId, layout.edges);
  // Keep keyboard entry available without inventing a selection for a missing bookmark.
  const tabStopNodeId = graph.nodes.find(node => node.isSelected)?.id ?? graph.nodes[0]?.id;
  const refs = useRef<Record<string, HTMLButtonElement | null>>({});
  const [selectedEdge, setSelectedEdge] = useState<string | null>(null);
  const edgeDetail = graph.edges.find((edge) => edge.id === selectedEdge);
  const relationshipColumns: DenseDataTableColumn<FamilyOfficeOwnershipEdge>[] = [
    { id: "from", label: "From", render: edge => graph.nodes.find(node => node.id === edge.fromId)?.label },
    { id: "to", label: "To", render: edge => graph.nodes.find(node => node.id === edge.toId)?.label },
    { id: "relationship", label: "Relationship", render: edge => edge.relationshipType },
    { id: "percentage", label: "Percentage", render: edge => edge.relationshipType === "Owns" ? edge.ownershipPercent == null ? "Unrecorded" : `${edge.ownershipPercent}%` : "Not a legal ownership percentage" },
    { id: "dates", label: "Effective dates", render: edge => `${edge.effectiveFrom ?? "Unrecorded"} → ${edge.effectiveTo ?? "Open ended"}` },
    { id: "inspect", label: "Inspect", render: edge => <button className="underline" onClick={() => { setSelectedEdge(edge.id); onSelect(edge.toId); }}>Inspect relationship</button> }
  ];
  const keyDown = (event: KeyboardEvent<HTMLButtonElement>, id: string) => {
    const index = graph.nodes.findIndex((node) => node.id === id);
    const next = event.key === "Home" ? 0 : event.key === "End" ? graph.nodes.length - 1
      : ["ArrowRight", "ArrowDown"].includes(event.key) ? Math.min(graph.nodes.length - 1, index + 1)
      : ["ArrowLeft", "ArrowUp"].includes(event.key) ? Math.max(0, index - 1) : null;
    if (next === null) return;
    event.preventDefault(); const nextId = graph.nodes[next].id; onSelect(nextId); refs.current[nextId]?.focus();
  };
  return <>
    <p className="text-xs text-muted-foreground">Retained structure snapshot. Select a node to highlight retained legal paths. Asset associations do not establish legal title; economic exposure is separate.</p>
    {layout.gapCount ? <p role="status">Some relationships have unavailable endpoints. Ownership paths are incomplete.</p> : null}
    {layout.cyclic.size ? <p role="status" className="text-warning">Ownership cycle — review required. Cycle links remain visible; effective ownership is not calculated.</p> : null}
    {table ? <div className="overflow-auto">
      <DenseDataTable columns={relationshipColumns} rows={layout.edges} getRowId={edge => edge.id}
        ariaLabel="Family office ownership relationship table" emptyText="No retained relationships are available."
        tableId="family-ownership-relationships" tableClassName="min-w-[800px]" />
      <div className="mt-3 flex flex-wrap gap-2" aria-label="Ownership nodes">{graph.nodes.map((node) => <button key={node.id} className="rounded border border-border p-2" aria-pressed={node.isSelected} onClick={() => onSelect(node.id)}>{node.label}</button>)}</div>
    </div> : <div role="group" aria-label={graph.ariaLabel} aria-describedby="family-office-graph-keyboard-instructions" className="overflow-auto rounded border border-border">
      <div className="relative" style={{ width: layout.width, height: layout.height }}>
        <svg width={layout.width} height={layout.height} className="absolute inset-0" aria-label="Retained ownership connections" role="img">
          <defs><marker id={marker} markerWidth="8" markerHeight="8" refX="7" refY="4" orient="auto"><path d="M0,0 L8,4 L0,8 Z" fill="currentColor" /></marker></defs>
          {layout.edges.map((edge, index) => {
            const from = layout.positions.get(edge.fromId)!; const to = layout.positions.get(edge.toId)!;
            const isForward = to.x > from.x; const x1 = from.x + 224; const y1 = from.y + 52; const y2 = to.y + 52;
            const track = isForward ? x1 + 26 + (index % 5) * 12 : Math.max(x1, to.x + 224) + 28 + (index % 5) * 12;
            const path = isForward ? `M${x1},${y1} H${track} V${y2} H${to.x}` : `M${x1},${y1} H${track} V${to.y - 18} H${to.x + 112} V${to.y}`;
            const textX = isForward ? (x1 + to.x) / 2 : track; const textY = isForward ? y2 - 8 : to.y - 24;
            return <g key={edge.id} className={highlighted.has(edge.id) ? "text-primary" : "text-muted-foreground"}>
              <path d={path} fill="none" stroke="currentColor" strokeWidth={highlighted.has(edge.id) ? 3 : 1.5} strokeDasharray={edge.relationshipType === "Owns" ? undefined : "5 4"} markerEnd={`url(#${marker})`} />
              <text x={textX} y={textY} textAnchor="middle" fill="currentColor" fontSize="10"><title>{edge.label}</title>{edge.relationshipType === "Owns" ? edge.ownershipPercent == null ? "Owns · ?%" : `Owns · ${edge.ownershipPercent}%` : edge.relationshipType === "AssetAssociation" ? "Association" : edge.relationshipType}</text>
            </g>;
          })}
        </svg>
        {graph.nodes.map((node) => { const pos = layout.positions.get(node.id)!; return <button key={node.id} ref={(element) => { refs.current[node.id] = element; }} type="button" style={{ left: pos.x, top: pos.y, width: 224, height: 104 }} className={`absolute overflow-hidden rounded border bg-background p-3 text-left text-xs focus-visible:outline focus-visible:outline-2 focus-visible:outline-primary ${node.isSelected ? "border-primary ring-2 ring-primary" : "border-border"}`} aria-label={node.selectAriaLabel} aria-pressed={node.isSelected} aria-controls={node.detailPanelId} tabIndex={node.id === tabStopNodeId ? 0 : -1} onClick={() => onSelect(node.id)} onKeyDown={(event) => keyDown(event, node.id)}>
          <span className="block text-muted-foreground">{node.type}{layout.cyclic.has(node.id) ? " · Cycle" : ""}</span><span className="mt-1 block font-semibold">{node.label}</span><span className="mt-1 block text-muted-foreground">{node.type === "asset" ? "Association only" : "Select for relationships"}</span>
        </button>; })}
      </div>
    </div>}
    <details className="rounded border border-border p-3"><summary className="cursor-pointer text-sm">Inspect retained relationships ({layout.edges.length})</summary><ul className="mt-2 space-y-2">{layout.edges.map((edge) => <li key={edge.id}><button className="text-left text-xs underline" onClick={() => setSelectedEdge(edge.id)}>{edge.label}</button></li>)}</ul></details>
    {edgeDetail ? <section aria-label="Selected relationship detail" className="rounded border border-border p-3 text-sm"><h3 className="font-semibold">{edgeDetail.label}</h3><p>Effective from {edgeDetail.effectiveFrom ?? "date unrecorded"}; {edgeDetail.effectiveTo ? `ends before ${edgeDetail.effectiveTo}` : "no end date retained"}.</p><p>{edgeDetail.notes ? `Notes: ${edgeDetail.notes}` : "No relationship notes retained."}</p><p>{edgeDetail.sourceDocumentId ? `Source document: ${edgeDetail.sourceDocumentId}` : "No source document identity retained."}</p></section> : null}
  </>;
}
