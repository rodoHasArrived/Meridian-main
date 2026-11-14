import { describe, expect, it } from "vitest";
import { statusBadgeVariant, statusMetricTone, statusPanelClass, statusTextClass, toSeverity } from "@/design-system/status-styles";

describe("status styles", () => {
  it("maps every status vocabulary the screens use onto the five severities", () => {
    // Badge variants, readiness tones, server statuses and severities agree.
    expect(["success", "ready", "Posted", "positive", "trusted"].map(toSeverity)).toEqual(Array(5).fill("ready"));
    expect(["warning", "action", "Stale", "attention"].map(toSeverity)).toEqual(Array(4).fill("action"));
    expect(["review", "ReviewRequired", "InReview"].map(toSeverity)).toEqual(Array(3).fill("review"));
    expect(["danger", "blocked", "Failed", "critical", "destructive"].map(toSeverity)).toEqual(Array(5).fill("blocked"));
    expect(["muted", "outline", "default", "neutral", "info", "primary", "pending", null, undefined].map(toSeverity))
      .toEqual(Array(9).fill("info"));
  });

  it("escalates an unrecognised server state instead of hiding it as informational", () => {
    expect(toSeverity("SomethingNew")).toBe("action");
  });

  it("gives each severity one badge, panel, text and metric style", () => {
    expect(statusBadgeVariant("ready")).toBe("success");
    expect(statusBadgeVariant("review")).toBe(statusBadgeVariant("action"));
    expect(statusBadgeVariant("blocked")).toBe("danger");
    expect(statusBadgeVariant("info")).toBe("outline");
    expect(statusPanelClass("warning")).toBe(statusPanelClass("action"));
    expect(statusPanelClass("danger")).toContain("--severity-blocked-bd");
    expect(statusTextClass("success")).toContain("--severity-ready-fg");
    expect(statusTextClass("muted")).toBe("");
    expect(statusMetricTone("Failed")).toBe("danger");
    expect(statusMetricTone("default")).toBe("neutral");
  });
});
