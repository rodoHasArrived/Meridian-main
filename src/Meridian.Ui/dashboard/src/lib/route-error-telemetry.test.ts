import { describe, expect, it, vi } from "vitest";
import {
  WORKSTATION_ROUTE_ERROR_EVENT,
  reportWorkstationRouteError,
  type WorkstationRouteErrorReport
} from "@/lib/route-error-telemetry";

describe("route-error telemetry", () => {
  it("dispatches a route-scoped telemetry event before logging", () => {
    const reports: WorkstationRouteErrorReport[] = [];
    const listener = (event: Event) => {
      reports.push((event as CustomEvent<WorkstationRouteErrorReport>).detail);
    };
    const consoleError = vi.spyOn(console, "error").mockImplementation(() => undefined);

    window.addEventListener(WORKSTATION_ROUTE_ERROR_EVENT, listener);
    try {
      const report = reportWorkstationRouteError(
        {
          routeKey: "/accounting/ledger",
          pathname: "/accounting/ledger",
          search: "",
          hash: "",
          workspaceLabel: "Accounting",
          routeLabel: "Ledger Explorer"
        },
        new Error("Render exploded"),
        { componentStack: "Stack" }
      );

      expect(report).toMatchObject({ workspaceLabel: "Accounting", routeLabel: "Ledger Explorer", message: "Render exploded" });
      expect(reports).toHaveLength(1);
      expect(reports[0]).toMatchObject({ routeKey: "/accounting/ledger", componentStack: "Stack" });
      expect(consoleError).toHaveBeenCalled();
    } finally {
      window.removeEventListener(WORKSTATION_ROUTE_ERROR_EVENT, listener);
      consoleError.mockRestore();
    }
  });
});
