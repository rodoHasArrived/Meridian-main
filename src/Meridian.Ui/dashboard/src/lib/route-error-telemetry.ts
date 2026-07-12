import type { ErrorInfo } from "react";

export const WORKSTATION_ROUTE_ERROR_EVENT = "meridian:workstation-route-error";

export interface WorkstationRouteErrorContext {
  routeKey: string;
  pathname: string;
  search: string;
  hash: string;
  workspaceLabel: string;
  routeLabel: string;
}

export interface WorkstationRouteErrorReport extends WorkstationRouteErrorContext {
  message: string;
  stack: string | null;
  componentStack: string | null;
}

export function reportWorkstationRouteError(
  context: WorkstationRouteErrorContext,
  error: Error,
  info: ErrorInfo
): WorkstationRouteErrorReport {
  const report: WorkstationRouteErrorReport = {
    ...context,
    message: error.message,
    stack: error.stack ?? null,
    componentStack: info.componentStack ?? null
  };

  if (typeof window !== "undefined") {
    window.dispatchEvent(new CustomEvent<WorkstationRouteErrorReport>(WORKSTATION_ROUTE_ERROR_EVENT, { detail: report }));
  }

  console.error("Meridian workstation route failed to render.", report, error);
  return report;
}
