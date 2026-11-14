import { describe, expect, it } from "vitest";
import { normalizeDesignSystemSeverity } from "@/design-system/status";
import { normalizeSeverity, humanizeStatus, severityKeys, severityLabels } from "./status";

describe("normalizeSeverity", () => {
  it("collapses platform status strings onto the five canonical severities", () => {
    expect(normalizeSeverity("Ready")).toBe("ready");
    expect(normalizeSeverity("Passed")).toBe("ready");
    expect(normalizeSeverity("ReviewRequired")).toBe("review");
    expect(normalizeSeverity("InProgress")).toBe("review");
    expect(normalizeSeverity("Stale")).toBe("action");
    expect(normalizeSeverity("Warning")).toBe("action");
    expect(normalizeSeverity("Blocked")).toBe("blocked");
    expect(normalizeSeverity("Critical")).toBe("blocked");
  });

  it("is punctuation- and case-insensitive", () => {
    expect(normalizeSeverity("review-required")).toBe("review");
    expect(normalizeSeverity("Review_Required")).toBe("review");
    expect(normalizeSeverity("LIVE / LINKED")).toBe("ready");
  });

  it("keeps info for explicit unknown, not-started, empty, or nullish input", () => {
    expect(normalizeSeverity("NotStarted")).toBe("info");
    expect(normalizeSeverity("Unknown")).toBe("info");
    expect(normalizeSeverity("")).toBe("info");
    expect(normalizeSeverity("--")).toBe("info");
    expect(normalizeSeverity(undefined)).toBe("info");
    expect(normalizeSeverity(null)).toBe("info");
  });

  it("escalates an unrecognised status to action instead of looking informational", () => {
    expect(normalizeSeverity("gibberish")).toBe("action");
    expect(normalizeSeverity("Weird-99")).toBe("action");
  });

  it("shares the design-system alias vocabulary", () => {
    const aliases: Record<string, string> = {
      Breached: "blocked", Matched: "ready", Resolved: "ready", OnTrack: "ready", SignedOff: "ready",
      AwaitingApproval: "review", Reopened: "review", BreaksDetected: "action", AtRisk: "action",
      Partial: "action", NotReady: "info", Skipped: "info", Paused: "info",
      // Aliases the operations vocabulary already had keep their tone.
      LiveLinked: "ready", ReviewerAssigned: "review", SourceBacked: "review",
      ExceptionsOpen: "action", ReviewRequiredSoon: "action", Danger: "blocked", Pending: "info"
    };
    for (const [status, severity] of Object.entries(aliases)) {
      expect(normalizeSeverity(status)).toBe(severity);
      expect(normalizeSeverity(status)).toBe(normalizeDesignSystemSeverity(status));
    }
  });

  it("only ever returns a known severity key with a label", () => {
    for (const s of ["Ready", "x", "", "Blocked", "Weird-99"]) {
      const sev = normalizeSeverity(s);
      expect(severityKeys).toContain(sev);
      expect(severityLabels[sev]).toBeTruthy();
    }
  });
});

describe("humanizeStatus", () => {
  it("splits camelCase and separators into words", () => {
    expect(humanizeStatus("ReviewRequired")).toBe("Review Required");
    expect(humanizeStatus("NotStarted")).toBe("Not Started");
    expect(humanizeStatus("in_progress")).toBe("In progress");
    expect(humanizeStatus("")).toBe("");
  });
});
