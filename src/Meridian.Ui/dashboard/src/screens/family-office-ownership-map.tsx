import { useId, useMemo, useState } from "react";
import { Button } from "@/components/ui/button";
import { Select } from "@/components/ui/select";
import { DenseDataTable, type DenseDataTableColumn } from "@/components/meridian/ui-kit-primitives";
import { LocalDateText } from "@/components/ui/local-date-text";
import { buildOwnershipMap, ownershipEdgeLabel, ownershipPaths, type OwnershipMapEdge } from "@/screens/family-office-ownership";
import { formatPercent } from "@/lib/format";
import type { FamilyOfficeEntityStructure } from "@/screens/family-office-screen.view-model";

export function FamilyOfficeOwnershipMap({ structure }: { structure: FamilyOfficeEntityStructure }) {
  const map = useMemo(() => buildOwnershipMap(structure), [structure]);
  const [selectedId, setSelectedId] = useState("");
  const [table, setTable] = useState(false);
  const [legalOnly, setLegalOnly] = useState(false);
  const arrowId = useId().replace(/:/g, "");
  const selected = map.nodes.find(node => node.id === selectedId);
  const paths = ownershipPaths(map, selected?.id ?? "");
  const shownEdges = map.edges.filter(edge => !legalOnly || edge.legal || edge.holding);
  const names = new Map(map.nodes.map(node => [node.id, node.name]));
  const evidenceLinks = structure.ownershipSource?.graph.evidenceLinks ?? [];
  const columns: DenseDataTableColumn<OwnershipMapEdge>[] = [
    { id: "from", label: "From", rowHeader: true, render: edge => names.get(edge.from) },
    { id: "relationship", label: "Relationship", render: ownershipEdgeLabel },
    { id: "to", label: "To", render: edge => names.get(edge.to) },
    { id: "fromDate", label: "Effective from", render: edge => <LocalDateText value={edge.fromDate} fallback={edge.fromDate || "Unreported"} /> },
    { id: "toDate", label: "Effective to", render: edge => <LocalDateText value={edge.toDate} fallback={edge.toDate || "Open / unreported"} /> },
    { id: "source", label: "Source reference", render: edge => <span className="font-mono text-xs">{edge.source ?? "Unreported"}</span> }
  ];
  return <section aria-labelledby="ownership-map-title" className="space-y-4 rounded-sm border border-border p-4">
    <div className="flex flex-wrap items-center justify-between gap-3"><div><h2 id="ownership-map-title" className="text-base font-semibold">Legal ownership relationships</h2><p className="text-sm text-muted-foreground">Effective structure on <LocalDateText value={map.asOf} />. Financial balance dates remain independent.</p></div><Button size="sm" variant="outline" aria-pressed={table} onClick={() => setTable(!table)}>{table ? "Show relationship map" : "Show relationship table"}</Button></div>
    <p className="text-sm text-muted-foreground">Legal paths follow Owns links. Dashed links show other relationships and registered holdings. Economic exposure is not calculated.</p>
    <details className="text-xs text-muted-foreground"><summary>Ownership and date interpretation</summary><p className="mt-2">Arrows follow supplied direction. Asset-register holdings identify the registered entity without asserting a legal percentage. Holdings retain their own source dates and are not restated by historical structure selection. Effective dates and source references are available in the relationship table.</p></details>
    <details className="text-xs text-muted-foreground"><summary>Structure source evidence</summary><ul className="mt-2 space-y-1">{evidenceLinks.map(link => <li key={link.evidenceId}>{link.route?.startsWith("/") && !link.route.startsWith("//") ? <a href={link.route} className="text-primary underline">{link.label}</a> : link.label} · {link.sourceSystem} · {link.completenessStatus ?? "Completeness unreported"} · {link.sourceDocumentId ?? link.evidenceId}</li>)}</ul>{!evidenceLinks.length ? <p className="mt-2">No structure source evidence was supplied.</p> : null}</details>
    <div className="grid gap-3 sm:grid-cols-[minmax(16rem,1fr)_1fr]"><label className="text-sm">Inspect entity or asset<Select value={selected?.id ?? ""} onChange={event => setSelectedId(event.target.value)}><option value="">Select a record</option>{map.nodes.map(node => <option key={node.id} value={node.id}>{node.name} · {node.kind}</option>)}</Select></label><label className="flex items-center gap-2 text-sm"><input type="checkbox" checked={legalOnly} onChange={event => setLegalOnly(event.target.checked)} />Show legal ownership and registered holdings only</label></div>
    {map.warnings.length ? <details className="text-sm text-warning"><summary>{map.warnings.length} unresolved structure warning(s)</summary><ul className="list-disc pl-5">{map.warnings.map((warning, index) => <li key={index}>{warning}</li>)}</ul></details> : null}
    {table ? <DenseDataTable columns={columns} rows={shownEdges} getRowId={edge => edge.id} getRowAriaLabel={edge => `${names.get(edge.from)} ${ownershipEdgeLabel(edge)} ${names.get(edge.to)}`} getRowClassName={edge => paths.highlighted.has(edge.id) ? "bg-secondary" : ""} onRowSelect={edge => setSelectedId(edge.to)} emptyText="No relationship records are supplied." ariaLabel="Ownership relationship evidence" tableId="family-office-relationship-evidence" /> : <div className="max-h-[36rem] overflow-auto rounded-sm border border-border" role="region" aria-label="Directed ownership map" tabIndex={0}>
      <svg width={map.width} height={map.height} role="group" aria-label="Directed relationships; use the record selector or relationship table to inspect paths" className="text-foreground"><defs><marker id={arrowId} markerWidth="8" markerHeight="8" refX="7" refY="4" orient="auto"><path d="M0,0 L8,4 L0,8 Z" fill="currentColor" /></marker></defs>
        {shownEdges.map((edge, index) => {
          const from = map.nodes.find(node => node.id === edge.from); const to = map.nodes.find(node => node.id === edge.to);
          if (!from || !to) return null;
          const x1 = from.x + 210, y1 = from.y + 35, x2 = to.x, y2 = to.y + 35;
          const mid = x1 + (x2 - x1) / 2;
          const highlighted = paths.highlighted.has(edge.id);
          const backwards = to.x <= from.x;
          const aboveNodes = backwards || to.x - from.x > 300;
          const routeY = Math.min(from.y, to.y) - 22 - (index % 3) * 12;
          const path = aboveNodes
            ? `M ${from.x + 105} ${from.y} V ${routeY} H ${to.x + 105} V ${to.y}`
            : `M ${x1} ${y1} C ${mid} ${y1}, ${mid} ${y2}, ${x2} ${y2}`;
          const labelX = aboveNodes ? (from.x + to.x) / 2 + 105 : mid;
          const labelY = aboveNodes ? routeY - 16 : (y1 + y2) / 2 - 13;
          return <g key={edge.id} className={highlighted ? "text-primary" : "text-muted-foreground"}><title>{names.get(edge.from)} → {ownershipEdgeLabel(edge)} → {names.get(edge.to)}</title><path d={path} stroke="currentColor" fill="none" strokeWidth={highlighted ? 3 : 1.5} strokeDasharray={edge.legal ? undefined : "5 4"} markerEnd={`url(#${arrowId})`} />{edge.legal || highlighted ? <text x={labelX} y={labelY} fontSize="12" textAnchor="middle" fill="currentColor">{edge.holding ? "Registered holder" : edge.relation}{edge.legal ? <tspan x={labelX} dy="14" style={{ fontFamily: "var(--font-data)" }}>{edge.percent == null ? "Unreported %" : formatPercent(edge.percent, { maximumFractionDigits: 20 })}</tspan> : null}</text> : null}</g>;
        })}
        {map.nodes.map(node => {
          const visibleName = node.name.length > 27 ? `${node.name.slice(0, 25)}…` : node.name;
          const visibleKind = node.kind.slice(0, 31);
          const visibleLabel = [visibleName, visibleKind, node.currency ?? ""].filter(Boolean).join(" ");
          return <g key={node.id} transform={`translate(${node.x} ${node.y})`} role="button" tabIndex={0} aria-label={`${visibleLabel}; Inspect ownership record ${node.name}`} aria-pressed={node.id === selected?.id} onClick={() => setSelectedId(node.id)} onKeyDown={event => { if (event.key === "Enter" || event.key === " ") { event.preventDefault(); setSelectedId(node.id); } }} className="cursor-pointer focus-visible:outline-2 focus-visible:outline-primary"><rect width="210" height="70" rx="2" fill="var(--ws-surface-raised)" stroke="currentColor" strokeWidth={node.id === selected?.id ? 3 : 1} /><text x="10" y="23" fill="currentColor" fontSize="12"><title>{node.name}</title>{visibleName}{" "}</text><text x="10" y="45" fill="currentColor" fontSize="12">{visibleKind}{" "}</text><text x="10" y="60" fill="currentColor" fontSize="12" style={{ fontFamily: "var(--font-data)" }}>{node.currency ?? ""}</text></g>;
        })}
      </svg>
    </div>}
    <section className="space-y-2 rounded-sm border border-border p-3" aria-label="Selected legal ownership paths" aria-live="polite">
      {selected ? <><h3 className="font-semibold">{selected.name}</h3><p className="font-mono text-xs">{selected.id} · {selected.kind}</p>{selected.recordAsOf ? <p className="text-xs text-muted-foreground">Asset register as of <LocalDateText value={selected.recordAsOf} />; valuation <LocalDateText value={selected.valuationDate} fallback="unreported" />. This is the supplied registered holder, not a verified historical holding.</p> : null}{paths.cycle ? <p className="text-sm text-warning">A cycle is present. Paths stop at repeated nodes and do not establish a complete legal chain.</p> : null}{paths.truncated ? <p className="text-sm text-warning">Path review is limited to 50 paths / 2,000 traversal steps. Additional relationships remain in the table.</p> : null}{paths.paths.length ? <ol className="space-y-2 text-sm">{paths.paths.map((path, index) => <li key={index}>{path.map((edge, edgeIndex) => <span key={edge.id}>{edgeIndex === 0 ? names.get(edge.from) : ""} → [{ownershipEdgeLabel(edge)}; <LocalDateText value={edge.fromDate} fallback="start unreported" /> to <LocalDateText value={edge.toDate} fallback="open / unreported" />] → {names.get(edge.to)} </span>)}</li>)}</ol> : <p className="text-sm">No upstream legal ownership relationship is supplied for this record.</p>}</> : <p className="text-sm">Select an entity or asset to highlight its upstream legal relationships and inspect their effective dates.</p>}
    </section>
  </section>;
}
