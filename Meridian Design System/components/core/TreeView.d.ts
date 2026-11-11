import React from "react";

export interface TreeNode {
  /** Stable unique id */
  id: string | number;
  /** Row label */
  label: React.ReactNode;
  /** Plain-text form of `label`, used for typeahead when `label` is not a string */
  text?: string;
  /** Optional leading icon/glyph */
  icon?: React.ReactNode;
  /** Optional right-aligned meta (count, amount) */
  meta?: React.ReactNode;
  /** Child nodes. Omit with `hasChildren` to lazy-load on expand. */
  children?: TreeNode[];
  /** Declares a branch before its children are loaded (lazy trees) */
  hasChildren?: boolean;
  /** Child count shown on a collapsed branch; inferred from `children` when present */
  childCount?: number;
  /** Renders a LOADING mark and sets `aria-busy` while children resolve */
  loading?: boolean;
  /** Not selectable; skipped by keyboard motion and typeahead */
  disabled?: boolean;
}

export interface TreeViewProps extends Omit<React.HTMLAttributes<HTMLDivElement>, "onSelect" | "onToggle"> {
  /** Root nodes */
  items: TreeNode[];
  /** Ids expanded on first render (uncontrolled) */
  defaultExpanded?: (string | number)[];
  /** Expanded ids — pass to control expansion (URL/store-backed screens) */
  expanded?: (string | number)[];
  /** Fires with the next full expanded-id list on any expand/collapse */
  onExpandedChange?: (ids: (string | number)[]) => void;
  /** Currently selected node id */
  selectedId?: string | number;
  /** Row click or Enter. Selection is independent of expansion. */
  onSelect?: (node: TreeNode) => void;
  /** Caret click, double-click, Space, or Arrow expand/collapse — use to lazy-load children */
  onToggle?: (node: TreeNode, open: boolean) => void;
  /** Hairline depth guides (default true) */
  guides?: boolean;
  /** Copy shown when `items` is empty */
  emptyLabel?: React.ReactNode;
}

/**
 * Expandable hierarchy — chart of accounts, security taxonomies, file trees. Implements the full
 * ARIA tree keyboard model over a flat DOM (level/setsize/posinset announced per row): Up/Down,
 * Right expand-then-descend, Left collapse-then-ascend, Home/End, `*` expand siblings, printable
 * typeahead, Enter select, Space toggle. Row height tracks `--theme-row-height`, so the tree re-densifies
 * with `DensityToggle`; override just this component with `--tree-row-height`.
 */
export declare function TreeView(props: TreeViewProps): JSX.Element;
