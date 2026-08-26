import { describe, expect, it } from "vitest";
import { buildGettingStartedViewModel } from "./getting-started.view-model";
import type { ActivationOutcome, FirstRunStatus } from "./types";

const OUTCOMES: ActivationOutcome[] = [
  { key: "workspace-opened", label: "Open or create a workspace", actionLabel: "Open workspace", route: "/portfolio", isComplete: true, completedAtUtc: "2026-08-01T10:00:00Z" },
  { key: "data-imported", label: "Import sample or real data", actionLabel: "Import data", route: "/accounting/statement-import", isComplete: false, completedAtUtc: null },
  { key: "report-run", label: "Run one report", actionLabel: "Run report", route: "/reporting/run", isComplete: false, completedAtUtc: null }
];

function status(overrides: Partial<FirstRunStatus> = {}): FirstRunStatus {
  return {
    isComplete: true,
    goal: "monitor-investments",
    starterKitId: "personal-portfolio",
    dataChoice: "upload",
    workspace: {
      id: "primary",
      name: "Meridian Workspace",
      isSample: false,
      badge: "LOCAL",
      safetyMessage: "Local workspace data.",
      samplePackVersion: ""
    },
    starterKits: [],
    outcomes: OUTCOMES,
    recommendedActions: [],
    sampleWorkspace: null,
    ...overrides
  };
}

describe("buildGettingStartedViewModel", () => {
  it("stays hidden until first-run setup is finished", () => {
    expect(buildGettingStartedViewModel(status({ isComplete: false })).visible).toBe(false);
    expect(buildGettingStartedViewModel(null).visible).toBe(false);
    expect(buildGettingStartedViewModel(undefined).visible).toBe(false);
  });

  it("stays hidden when the host reports no outcomes to track", () => {
    expect(buildGettingStartedViewModel(status({ outcomes: [] })).visible).toBe(false);
  });

  it("counts host-recorded completions rather than page visits", () => {
    const model = buildGettingStartedViewModel(status());

    expect(model.completedCount).toBe(1);
    expect(model.totalCount).toBe(3);
    expect(model.triggerLabel).toBe("Getting started 1/3");
    expect(model.finished).toBe(false);
  });

  it("offers the first outstanding step as the next one, with its route", () => {
    const model = buildGettingStartedViewModel(status());

    expect(model.nextStep?.key).toBe("data-imported");
    expect(model.nextStep?.route).toBe("/accounting/statement-import");
    expect(model.nextStep?.actionLabel).toBe("Import data");
    expect(model.steps.filter((step) => step.isNext)).toHaveLength(1);
    expect(model.triggerAriaLabel).toContain("Import sample or real data");
  });

  it("keeps every outcome addressable so a completed step can still be revisited", () => {
    const model = buildGettingStartedViewModel(status());

    expect(model.steps.map((step) => step.route)).toEqual([
      "/portfolio",
      "/accounting/statement-import",
      "/reporting/run"
    ]);
    expect(model.steps[0].isComplete).toBe(true);
    expect(model.steps[0].completedAtUtc).toBe("2026-08-01T10:00:00Z");
  });

  it("reports a finished checklist with no next step", () => {
    const model = buildGettingStartedViewModel(status({
      outcomes: OUTCOMES.map((outcome) => ({ ...outcome, isComplete: true, completedAtUtc: "2026-08-01T10:00:00Z" }))
    }));

    expect(model.finished).toBe(true);
    expect(model.nextStep).toBeNull();
    expect(model.triggerLabel).toBe("Getting started 3/3");
    expect(model.triggerAriaLabel).toContain("every getting-started step is done");
  });
});
