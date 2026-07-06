import { Component, type ErrorInfo, type ReactNode } from "react";
import { RefreshCcw } from "lucide-react";
import { Button } from "@/components/ui/button";
import { cn } from "@/lib/utils";

/**
 * Inline error surface for a single region. Deliberately compact — one failed
 * panel should degrade to a legible, recoverable message without blanking the
 * screen around it. Pair with {@link RegionErrorBoundary} (render-time throws)
 * or drive it directly from a view-model error flag.
 */
export interface RegionErrorStateProps {
  message: string;
  detail?: string | null;
  onRetry?: () => void;
  retryLabel?: string;
  className?: string;
}

export function RegionErrorState({
  message,
  detail,
  onRetry,
  retryLabel = "Retry",
  className
}: RegionErrorStateProps) {
  return (
    <div
      role="alert"
      className={cn(
        "flex flex-col gap-2 rounded-[2px] border border-danger/30 bg-danger/10 px-3.5 py-3 text-sm text-danger",
        className
      )}
    >
      <div className="font-semibold">{message}</div>
      {detail ? <div className="font-mono text-xs leading-5 text-danger/80">{detail}</div> : null}
      {onRetry ? (
        <div>
          <Button type="button" size="sm" variant="outline" onClick={onRetry}>
            <RefreshCcw className="h-3.5 w-3.5" aria-hidden="true" />
            {retryLabel}
          </Button>
        </div>
      ) : null}
    </div>
  );
}

export interface RegionErrorBoundaryProps {
  children: ReactNode;
  /**
   * Custom fallback. Receives the caught error and a `reset` callback that clears
   * the boundary so the region can re-mount its children. Defaults to
   * {@link RegionErrorState}.
   */
  fallback?: (context: { error: Error; reset: () => void }) => ReactNode;
  /** Label woven into the default message, e.g. "reconciliation table". */
  regionLabel?: string;
  /** Reported to telemetry/logging hooks; the boundary never swallows silently. */
  onError?: (error: Error, info: ErrorInfo) => void;
  /**
   * Changing any value here after an error clears the boundary. Feed it a retry
   * counter or the request version so a successful refresh re-mounts the region.
   */
  resetKeys?: readonly unknown[];
}

interface RegionErrorBoundaryState {
  error: Error | null;
}

/**
 * Catches render-time errors thrown by a region's children and contains them to
 * that region. Without this, a single throwing panel takes down the whole screen
 * — the opposite of the honest, independently-resolving layout we want.
 */
export class RegionErrorBoundary extends Component<RegionErrorBoundaryProps, RegionErrorBoundaryState> {
  state: RegionErrorBoundaryState = { error: null };

  static getDerivedStateFromError(error: Error): RegionErrorBoundaryState {
    return { error };
  }

  componentDidCatch(error: Error, info: ErrorInfo): void {
    this.props.onError?.(error, info);
  }

  componentDidUpdate(previous: RegionErrorBoundaryProps): void {
    if (this.state.error && !areResetKeysEqual(previous.resetKeys, this.props.resetKeys)) {
      this.reset();
    }
  }

  reset = (): void => {
    this.setState({ error: null });
  };

  render(): ReactNode {
    const { error } = this.state;
    if (!error) {
      return this.props.children;
    }

    if (this.props.fallback) {
      return this.props.fallback({ error, reset: this.reset });
    }

    const label = this.props.regionLabel ?? "This section";
    return (
      <RegionErrorState
        message={`${label} hit an unexpected error.`}
        detail={error.message || undefined}
        onRetry={this.reset}
        retryLabel="Reload section"
      />
    );
  }
}

function areResetKeysEqual(
  previous: readonly unknown[] | undefined,
  next: readonly unknown[] | undefined
): boolean {
  if (previous === next) {
    return true;
  }

  if (!previous || !next || previous.length !== next.length) {
    return false;
  }

  return previous.every((value, index) => Object.is(value, next[index]));
}
