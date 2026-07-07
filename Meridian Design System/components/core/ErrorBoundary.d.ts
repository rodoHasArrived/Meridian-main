/**
 * ErrorBoundary — panel-level crash isolation. Wrap each independent workstation panel (chart,
 * table, inspector) so one render crash degrades to a quiet fault panel instead of blanking
 * the screen. "Try again" clears the error and re-renders the children.
 *
 * @example
 * <ErrorBoundary label="Equity curve">
 *   <EquityCurve data={series} />
 * </ErrorBoundary>
 */
export interface ErrorBoundaryProps {
  children?: React.ReactNode;
  /** Named in the fault panel: "<label> failed to render". @default "This panel" */
  label?: string;
  /** Replace the built-in fault panel entirely. */
  fallback?: React.ReactNode;
  /** Telemetry hook — fired once per catch with (error, info). */
  onError?: (error: Error, info: { componentStack: string }) => void;
}
export declare class ErrorBoundary extends React.Component<ErrorBoundaryProps> {}
