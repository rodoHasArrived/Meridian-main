import { describe, expect, it } from "vitest";
import {
  resolvePrimaryOperatorWorkflowStepId,
  resolveWorkflowContinuityRoute
} from "@/app-shell.workflow-continuity";

describe("workflow continuity route resolution", () => {
  it.each([
    ["/", "Daily Control Tower", "Today"],
    ["/trading", "Trading Controls", "Trading cockpit"],
    ["/portfolio", "Trading Controls", "Exposure"],
    ["/strategy", "Research To Paper", "Run library"]
  ])("keeps a route-matched root workflow for %s", (pathname, title, activeLabel) => {
    const resolution = resolveWorkflowContinuityRoute(pathname, "");

    expect(resolution.mode).toBe("matched");
    expect(resolution.trail?.title).toBe(title);
    expect(resolution.trail?.steps[resolution.activeStepIndex ?? -1]?.label).toBe(activeLabel);
  });

  it.each([
    "/accounting",
    "/reporting",
    "/data",
    "/settings"
  ])("uses task choice for the unmatched workspace root %s", (pathname) => {
    expect(resolveWorkflowContinuityRoute(pathname, "")).toEqual({
      mode: "choose-task",
      trail: null,
      activeStepIndex: null
    });
  });

  it("hides continuity for an unknown workstation route", () => {
    expect(resolveWorkflowContinuityRoute("/unknown", "")).toEqual({
      mode: "hidden",
      trail: null,
      activeStepIndex: null
    });
  });

  it("preserves the hash-targeted provider setup deep link", () => {
    const resolution = resolveWorkflowContinuityRoute("/settings", "#alpaca-provider-setup");

    expect(resolution.mode).toBe("matched");
    expect(resolution.trail?.title).toBe("Market Data To Paper");
    expect(resolution.trail?.steps[resolution.activeStepIndex ?? -1]).toMatchObject({
      label: "Provider setup",
      href: "/settings#alpaca-provider-setup"
    });
  });

  it("preserves the route-specific Reporting handoff", () => {
    const resolution = resolveWorkflowContinuityRoute("/reporting/report-packs", "");

    expect(resolution.mode).toBe("matched");
    expect(resolution.trail?.title).toBe("Daily Control Tower");
    expect(resolution.trail?.steps[resolution.activeStepIndex ?? -1]).toMatchObject({
      label: "Reports",
      href: "/reporting/report-packs"
    });
  });

  it("does not invent Import as the primary step for unmatched routes", () => {
    expect(resolvePrimaryOperatorWorkflowStepId("/")).toBeNull();
    expect(resolvePrimaryOperatorWorkflowStepId("/settings")).toBeNull();
    expect(resolvePrimaryOperatorWorkflowStepId("/unknown")).toBeNull();
    expect(resolvePrimaryOperatorWorkflowStepId("/data")).toBe("import");
    expect(resolvePrimaryOperatorWorkflowStepId("/reporting")).toBe("report");
  });
});
