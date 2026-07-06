import type { ReactNode } from "react";
import { RegionErrorBoundary, RegionErrorState } from "@/components/ui/error-boundary";
import type { RequestLifecyclePhase } from "@/hooks/use-request-lifecycle";
import { cn } from "@/lib/utils";

/**
 * Wraps a data region so it resolves honestly and independently: a shaped
 * skeleton on first load, the resolved content once a view-model settles, and a
 * contained error state (plus a per-region {@link RegionErrorBoundary}) when it
 * fails — so one bad panel degrades gracefully instead of blanking the screen.
 *
 * Reads the flags screens already expose. Pass either a `useRequestLifecycle`
 * `phase` or the plain `loading` boolean many view-models surface, plus an
 * `error` string. `hasData` is what keeps streaming honest: skeletons only stand
 * in on *first* load (no data yet). A background refresh over existing data keeps
 * the data on screen — the freshness chip, not a blanked pane, tells the operator
 * a refresh is in flight.
 *
 * @example
 * <AsyncRegion
 *   loading={view.loading}
 *   error={view.errorText}
 *   hasData={view.rows.length > 0}
 *   label="reconciliation table"
 *   onRetry={() => void view.refresh()}
 *   skeleton={<SkeletonGrid rows={6} columns={5} />}
 * >
 *   <ReconciliationTable rows={view.rows} />
 * </AsyncRegion>
 */
export interface AsyncRegionProps {
  /** Shaped placeholder shown during first load. Use a `Skeleton*` preset. */
  skeleton: ReactNode;
  /** Resolved content, rendered once data is available. */
  children: ReactNode;
  /** Lifecycle phase from `useRequestLifecycle`, when the region is driven by it. */
  phase?: RequestLifecyclePhase;
  /** Plain loading flag for view-models that expose `loading` directly. */
  loading?: boolean;
  /** Error text/flag. Replaces content only while there is no data to fall back to. */
  error?: string | null;
  /**
   * Whether resolved data already exists. Gates first-load skeletons and
   * error-state takeover: while true, background refreshes and transient errors
   * never blank the region.
   */
  hasData?: boolean;
  /** Optional node for the resolved-but-empty case (settled, no data, no error). */
  empty?: ReactNode;
  /** Retry handler surfaced in the error state and forwarded to the boundary. */
  onRetry?: () => void;
  /** Accessible region label, e.g. "close package detail". Announced while busy. */
  label?: string;
  className?: string;
}

function isLoadingState(phase: RequestLifecyclePhase | undefined, loading: boolean | undefined): boolean {
  return loading === true || phase === "running";
}

function isErrorState(phase: RequestLifecyclePhase | undefined, error: string | null | undefined): boolean {
  return Boolean(error) || phase === "failed";
}

export function AsyncRegion({
  skeleton,
  children,
  phase,
  loading,
  error,
  hasData = false,
  empty,
  onRetry,
  label = "content",
  className
}: AsyncRegionProps) {
  const busy = isLoadingState(phase, loading);
  const failed = isErrorState(phase, error);

  // First load: no data yet and a request is running — stand in with the shape.
  if (busy && !hasData) {
    return (
      <div className={className} role="status" aria-busy="true" aria-live="polite">
        <span className="sr-only">Loading {label}…</span>
        {skeleton}
      </div>
    );
  }

  // Failed with nothing to show. With data present we keep it on screen and let
  // the surface's own freshness/status affordances report the stale attempt.
  if (failed && !hasData) {
    return (
      <div className={className}>
        <RegionErrorState
          message={typeof error === "string" && error ? error : `${capitalize(label)} could not be loaded.`}
          onRetry={onRetry}
        />
      </div>
    );
  }

  return (
    <RegionErrorBoundary
      regionLabel={label}
      resetKeys={[hasData, busy]}
      fallback={({ error: caught, reset }) => (
        <div className={className}>
          <RegionErrorState
            message={`${capitalize(label)} hit an unexpected error.`}
            detail={caught.message || undefined}
            onRetry={onRetry ? () => { reset(); onRetry(); } : reset}
            retryLabel={onRetry ? "Retry" : "Reload section"}
          />
        </div>
      )}
    >
      <div className={cn(className)} aria-busy={busy || undefined}>
        {!hasData && empty ? empty : children}
      </div>
    </RegionErrorBoundary>
  );
}

function capitalize(value: string): string {
  return value.length > 0 ? value.charAt(0).toUpperCase() + value.slice(1) : value;
}
