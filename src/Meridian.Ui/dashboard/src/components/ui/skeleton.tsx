import { cn } from "@/lib/utils";

// NOTE: a second, CSS-in-JS Skeleton family lives in `components/data/skeleton.tsx` as
// part of the independent "Concrete" design-system surface (variant/width/lines props).
// The two APIs are incompatible by design — pick the family that matches the screen you
// are working in; do not mix them.

/**
 * Placeholder block that occupies the same footprint as content that has not
 * loaded yet. On a data-dense operator screen this is what preserves spatial
 * memory across navigations — the reconciliation table, the metric row, and the
 * evidence timeline keep their positions instead of collapsing behind a spinner.
 *
 * The pulse is the whole point, so it animates; `motion-reduce:animate-none`
 * holds it static for operators who opt out of motion (the global
 * `prefers-reduced-motion` rule in `styles/index.css` also neutralizes it).
 *
 * Skeletons are decorative — they carry `aria-hidden` so assistive tech is not
 * flooded with empty nodes. The accessible "loading" announcement belongs to the
 * wrapping region (see {@link AsyncRegion}), not to each block.
 */
export type SkeletonProps = React.HTMLAttributes<HTMLDivElement>;

export function Skeleton({ className, ...props }: SkeletonProps) {
  return (
    <div
      aria-hidden="true"
      className={cn(
        "animate-pulse rounded-[2px] bg-muted/70 motion-reduce:animate-none",
        className
      )}
      {...props}
    />
  );
}

/**
 * Stacked text lines. The last line is short by default so the block reads as a
 * paragraph rather than a solid rectangle.
 */
export interface SkeletonTextProps {
  lines?: number;
  className?: string;
  lastLineWidth?: string;
}

export function SkeletonText({ lines = 3, className, lastLineWidth = "w-2/3" }: SkeletonTextProps) {
  return (
    <div className={cn("space-y-2", className)} aria-hidden="true">
      {Array.from({ length: Math.max(1, lines) }).map((_, index) => (
        <Skeleton
          key={index}
          className={cn("h-3.5", index === lines - 1 ? lastLineWidth : "w-full")}
        />
      ))}
    </div>
  );
}

/**
 * A row of metric/summary cards — the shape at the top of most cockpit screens.
 * Mirrors the `grid gap-2 md:grid-cols-2 xl:grid-cols-4` layout the real metric
 * rows use so nothing jumps when values resolve.
 */
export interface SkeletonCardRowProps {
  cards?: number;
  className?: string;
}

export function SkeletonCardRow({ cards = 4, className }: SkeletonCardRowProps) {
  return (
    <div className={cn("grid gap-2 md:grid-cols-2 xl:grid-cols-4", className)} aria-hidden="true">
      {Array.from({ length: Math.max(1, cards) }).map((_, index) => (
        <div
          key={index}
          className="space-y-3 rounded-[2px] border border-border bg-card p-4"
        >
          <Skeleton className="h-3 w-24" />
          <Skeleton className="h-6 w-16" />
          <Skeleton className="h-3 w-20" />
        </div>
      ))}
    </div>
  );
}

/**
 * A tabular grid — header strip plus body rows. Use for reconciliation tables,
 * journal-entry grids, package histories, and any dense row/column surface.
 */
export interface SkeletonGridProps {
  rows?: number;
  columns?: number;
  className?: string;
  showHeader?: boolean;
}

export function SkeletonGrid({ rows = 5, columns = 4, className, showHeader = true }: SkeletonGridProps) {
  const columnCount = Math.max(1, columns);
  const template = { gridTemplateColumns: `repeat(${columnCount}, minmax(0, 1fr))` } as const;

  return (
    <div
      className={cn("overflow-hidden rounded-[2px] border border-border", className)}
      aria-hidden="true"
    >
      {showHeader ? (
        <div className="grid gap-3 border-b border-border/70 bg-secondary/20 px-3 py-2.5" style={template}>
          {Array.from({ length: columnCount }).map((_, index) => (
            <Skeleton key={index} className="h-3 w-16" />
          ))}
        </div>
      ) : null}
      <div className="divide-y divide-border/60">
        {Array.from({ length: Math.max(1, rows) }).map((_, rowIndex) => (
          <div key={rowIndex} className="grid gap-3 px-3 py-3" style={template}>
            {Array.from({ length: columnCount }).map((_, columnIndex) => (
              <Skeleton
                key={columnIndex}
                className={cn("h-3.5", columnIndex === 0 ? "w-3/4" : "w-1/2")}
              />
            ))}
          </div>
        ))}
      </div>
    </div>
  );
}

/**
 * A vertical timeline — dot + connector rail + event content. Use for evidence
 * timelines, audit trails, and workflow-history rails.
 */
export interface SkeletonTimelineProps {
  items?: number;
  className?: string;
}

export function SkeletonTimeline({ items = 4, className }: SkeletonTimelineProps) {
  const count = Math.max(1, items);

  return (
    <ol className={cn("space-y-4", className)} aria-hidden="true">
      {Array.from({ length: count }).map((_, index) => (
        <li key={index} className="flex gap-3">
          <div className="flex flex-col items-center">
            <Skeleton className="h-2.5 w-2.5 rounded-full" />
            {index < count - 1 ? <Skeleton className="mt-1 w-px flex-1" /> : null}
          </div>
          <div className="min-w-0 flex-1 space-y-2 pb-1">
            <Skeleton className="h-3.5 w-1/3" />
            <Skeleton className="h-3 w-2/3" />
          </div>
        </li>
      ))}
    </ol>
  );
}

/**
 * A stacked form — label + control pairs plus a trailing action. Use for setup,
 * sign-off, and adjustment drafts while their initial values settle.
 */
export interface SkeletonFormProps {
  fields?: number;
  className?: string;
  showAction?: boolean;
}

export function SkeletonForm({ fields = 4, className, showAction = true }: SkeletonFormProps) {
  return (
    <div className={cn("space-y-4", className)} aria-hidden="true">
      {Array.from({ length: Math.max(1, fields) }).map((_, index) => (
        <div key={index} className="space-y-1.5">
          <Skeleton className="h-3 w-28" />
          <Skeleton className="h-9 w-full rounded-[2px]" />
        </div>
      ))}
      {showAction ? <Skeleton className="h-9 w-32 rounded-[2px]" /> : null}
    </div>
  );
}
