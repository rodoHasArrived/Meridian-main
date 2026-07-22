import { describe, expect, it } from "vitest";
import { buildReportingTaskMode } from "@/screens/reporting-screen.task-mode-view-model";

describe("buildReportingTaskMode", () => {
  it.each([
    ["/reporting", "daily-reporting-cockpit", "Daily Reporting Cockpit"],
    ["/reporting/report-builder", "report-builder", "Report Builder"],
    ["/reporting/scheduled", "schedules", "Scheduled Reports"],
    ["/reporting/run-status", "run-status", "Run Status"],
    ["/reporting/report-packs", "report-pack-approval", "Report packs"],
    ["/reporting/exports", "exports", "Exports"],
    ["/reporting/governance", "governance", "Governance"]
  ] as const)("owns %s with the %s task mode", (pathname, id, label) => {
    expect(buildReportingTaskMode(pathname)).toMatchObject({ id, label, routeLabel: label });
  });

  it("normalizes query strings and trailing slashes before resolving schedules", () => {
    expect(buildReportingTaskMode("/reporting/scheduled/?scope=fund-alpha").id).toBe("schedules");
  });
});
