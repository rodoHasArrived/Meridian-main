/**
 * Loading placeholder with a subtle shimmer (reduced-motion safe). Use `block` for cards,
 * `text` for copy lines (multi-line via `lines`, last line shortened), `circle` for avatars.
 * `SkeletonTable` lays out a dense placeholder grid for tables that are still fetching.
 */
export interface SkeletonProps extends React.HTMLAttributes<HTMLSpanElement> {
  /** Shape. @default "block" */
  variant?: "block" | "text" | "circle";
  /** CSS width (number → px). */
  width?: number | string;
  /** CSS height (number → px). For `circle`, defaults to `width`. */
  height?: number | string;
  /** Line count when `variant="text"` (last line is shortened). @default 3 */
  lines?: number;
}
export declare function Skeleton(props: SkeletonProps): JSX.Element;

export interface SkeletonTableProps extends React.HTMLAttributes<HTMLTableElement> {
  /** Placeholder row count. @default 5 */
  rows?: number;
  /** Placeholder column count. @default 4 */
  columns?: number;
}
export declare function SkeletonTable(props: SkeletonTableProps): JSX.Element;
