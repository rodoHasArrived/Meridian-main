// Meridian "Concrete" data-furniture library — NEW shared table + surface components.
//
// Natively re-implemented from the Meridian Design System handoff (React 18 + TypeScript
// + Tailwind 3) for the browser workstation. These live alongside the pre-existing
// domain-specific `data/*` files but form an independent design-system surface that
// Wave 2 screens adopt. Design tokens are owned by sibling U1; every consumer reads them
// with a hex fallback so this library is independently mergeable.
//
// This barrel intentionally does NOT re-export the older domain components in this
// directory (provider drawers, symbol-universe manager, …) — those are separate surfaces.

export {
  Pagination,
  type PaginationProps,
} from "./pagination";
export { EmptyState, type EmptyStateProps, type EmptyStateIcon } from "./empty-state";
export {
  Skeleton,
  SkeletonTable,
  type SkeletonProps,
  type SkeletonTableProps,
} from "./skeleton";
export { KeyValueGrid, type KeyValueGridProps, type KeyValueGridItem } from "./key-value-grid";
export {
  EntitySummary,
  type EntitySummaryProps,
  type EntitySummaryItem,
} from "./entity-summary";
export { MetricCard, type MetricCardProps, type MetricCardTone } from "./metric-card";
export {
  useTableState,
  type TableState,
  type TableSort,
  type TableFilters,
  type SortDirection,
} from "./use-table-state";
