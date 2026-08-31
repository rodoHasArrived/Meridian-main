import React from "react";

export interface TreeNode {
  /** Stable unique id */
  id: string | number;
  /** Row label */
  label: React.ReactNode;
  /** Optional leading icon/glyph */
  icon?: React.ReactNode;
  /** Optional right-aligned meta (count, amount) */
  meta?: React.ReactNode;
  /** Child nodes */
  children?: TreeNode[];
}

export interface TreeViewProps {
  /** Root nodes */
  items: TreeNode[];
  /** Ids expanded on first render */
  defaultExpanded?: (string | number)[];
  /** Currently selected node id */
  selectedId?: string | number;
  /** Called with the clicked node */
  onSelect?: (node: TreeNode) => void;
}

export declare function TreeView(props: TreeViewProps): JSX.Element;
