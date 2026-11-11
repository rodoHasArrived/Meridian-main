/**
 * Lineage graph — directed provenance and dependency with a **layered** layout
 * and orthogonal routing. Meridian's ownership, data-lineage, accounting-flow,
 * and workflow graphs all have a dominant direction, so force-directed layout
 * is prohibited: node order must be stable between refreshes and an operator
 * must be able to trace a path with their finger.
 *
 * Layers are derived by longest path (or pinned with `node.layer`); nodes are
 * real HTML buttons over an SVG edge layer, so labels ellipsis properly, focus
 * works, and selection is committed. Edge labels sit outside nodes. The text
 * `outline` beneath the viewport is the non-visual path and stays on by default.
 *
 * Density: past a readable topology, filter to a neighborhood, cluster by
 * stage, or switch to a matrix — never shrink labels or fade edges.
 *
 * @example
 * <LineageGraph
 *   nodes={[
 *     { id:"refinitiv", label:"Refinitiv rates", kind:"source", meta:"Source · live" },
 *     { id:"clean",     label:"Clean & normalize", kind:"transformation", meta:"Transformation" },
 *     { id:"curve",     label:"Curve builder v14.7", kind:"model", meta:"Model" },
 *     { id:"y10",       label:"10Y yield", kind:"field", meta:"Derived field · 4.45%" },
 *   ]}
 *   edges={[
 *     { from:"refinitiv", to:"clean", label:"feeds", dashed:true },
 *     { from:"clean", to:"curve", label:"normalizes" },
 *     { from:"curve", to:"y10", label:"derives" },
 *   ]}
 *   selectedId={node} onSelect={setNode} height={340} />
 */
export interface LineageNode {
  id: string;
  label: string;
  /** Drives the default meta line when `meta` is omitted. */
  kind?: "source" | "transformation" | "model" | "field" | "consumer" | "output" | "control";
  /** Second line — owner, version, value, freshness. */
  meta?: string;
  /** Pin a column instead of deriving it. */
  layer?: number;
}
export interface LineageEdge {
  from: string;
  to: string;
  /** Relationship verb, drawn outside the nodes ("feeds", "derives", "consumed by"). */
  label?: string;
  /** Dashed = data flow / indirect dependency. */
  dashed?: boolean;
}
export interface LineageGraphProps extends Omit<React.HTMLAttributes<HTMLDivElement>, "onSelect"> {
  nodes: LineageNode[];
  edges: LineageEdge[];
  selectedId?: string;
  onSelect?: (id: string) => void;
  /** Viewport height; the canvas scrolls at natural size rather than scaling down. */
  height?: number;
  nodeWidth?: number;
  nodeHeight?: number;
  columnGap?: number;
  rowGap?: number;
  /** Text outline of nodes and relationships — the non-visual path. Keep it on. */
  outline?: boolean;
  outlineTitle?: string;
}
export declare function LineageGraph(props: LineageGraphProps): JSX.Element;
