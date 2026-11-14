import * as React from "react";
export type ControlNodeType = "input" | "validation" | "reconciliation" | "calculation" | "journal" | "review" | "approval" | "attestation" | "report" | "external";
export type ControlNodeStatus = "not-started" | "ready" | "in-progress" | "waiting" | "blocked" | "failed" | "complete" | "reopened" | "superseded";
export type ControlEdgeType = "requires" | "produces" | "validates" | "blocks" | "invalidates" | "reopens" | "approves";
export interface ControlNode {
  id: string;
  label: string;
  type?: ControlNodeType;
  status?: ControlNodeStatus;
  owner?: string;
  due?: string;
  /** Why this node is blocked, in its own words. */
  blocker?: string;
  /** Pin the node to a layer. Omit to let longest-path depth place it deterministically. */
  layer?: number;
  [key: string]: unknown;
}
export interface ControlEdge {
  id?: string;
  from: string;
  to: string;
  /** Relationships are explicitly typed and labelled on the connector. Defaults to "requires". */
  type?: ControlEdgeType;
  blocked?: boolean;
}
export interface ControlDependencyGraphProps extends Omit<React.HTMLAttributes<HTMLDivElement>, "onSelect"> {
  nodes: ControlNode[];
  edges: ControlEdge[];
  width?: number;
  nodeWidth?: number;
  nodeHeight?: number;
  layerGap?: number;
  selectedId?: string | null;
  /** Selection is meant to drive an inspector, the close calendar, and resolution actions. */
  onSelect?: (node: ControlNode) => void;
  showEdgeLabels?: boolean;
  /** Highlight the longest requires-chain to the deepest incomplete node. */
  criticalPath?: boolean;
  /** "graph" or "outline" — outline is the export and degraded-session view. */
  view?: "graph" | "outline";
  defaultView?: "graph" | "outline";
  onViewChange?: (view: "graph" | "outline") => void;
  description?: string;
  title?: string;
  asOf?: string;
}
export declare function ControlDependencyGraph(props: ControlDependencyGraphProps): React.JSX.Element;
