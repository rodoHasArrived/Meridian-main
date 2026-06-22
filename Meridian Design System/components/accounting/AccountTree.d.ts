/**
 * Chart-of-accounts tree — an expandable account hierarchy with roll-up balances. A parent
 * with no explicit `balance` sums its descendants; parents render bold, leaves muted. Disclosure
 * triangles toggle branches; depth indents the name column; balances are mono and right-aligned.
 */
export interface AccountNode {
  /** Account code/number (mono prefix). Also the expand/select key — must be unique. */
  code: string;
  name: string;
  /** Explicit balance. Omit on a parent to roll up its children. */
  balance?: number | string;
  /** Optional classification tag (asset/liability/equity/revenue/expense) — for your own use. */
  type?: string;
  children?: AccountNode[];
}
export interface AccountTreeProps {
  nodes: AccountNode[];
  /** @default "USD" */
  currency?: string;
  /** Branches at or above this depth start expanded (root = 0). @default 1 */
  defaultExpandedDepth?: number;
  /** Code of the row to highlight with the cyan rail. */
  selectedCode?: string;
  /** Row click handler — makes rows selectable. */
  onSelect?: (node: AccountNode) => void;
  /** Header label over the balance column. @default "Balance" */
  valueLabel?: string;
}
export declare function AccountTree(props: AccountTreeProps): JSX.Element;
