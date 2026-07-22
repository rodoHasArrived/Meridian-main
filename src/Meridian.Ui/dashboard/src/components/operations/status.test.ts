import { describe, expect, it } from "vitest";
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

  it("falls back to info for unknown, empty, or nullish input", () => {
    expect(normalizeSeverity("NotStarted")).toBe("info");
    expect(normalizeSeverity("gibberish")).toBe("info");
    expect(normalizeSeverity("")).toBe("info");
    expect(normalizeSeverity(undefined)).toBe("info");
    expect(normalizeSeverity(null)).toBe("info");
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
