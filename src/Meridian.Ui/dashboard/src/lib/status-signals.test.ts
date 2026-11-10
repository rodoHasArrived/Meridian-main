import { describe, expect, it } from "vitest";
import {
  isStatusIssue,
  statusToneLabel,
  statusToneRank,
  summarizeStatusSignals,
  worstStatusTone,
  type StatusSignalTone
} from "@/lib/status-signals";

const signal = (id: string, tone: StatusSignalTone) => ({ id, tone });

describe("status signal rule", () => {
  it("ranks blocked above review above unconfirmed above ready", () => {
    expect(statusToneRank("blocked")).toBeGreaterThan(statusToneRank("review"));
    expect(statusToneRank("review")).toBeGreaterThan(statusToneRank("unknown"));
    expect(statusToneRank("unknown")).toBe(statusToneRank("pending"));
    expect(statusToneRank("pending")).toBeGreaterThan(statusToneRank("ready"));
  });

  it("treats everything short of ready as an issue", () => {
    expect(isStatusIssue("ready")).toBe(false);
    for (const tone of ["pending", "unknown", "review", "blocked"] as const) {
      expect(isStatusIssue(tone)).toBe(true);
    }
  });

  it("gives every tone a text label so colour is never the only signal", () => {
    expect((["ready", "pending", "unknown", "review", "blocked"] as const).map(statusToneLabel))
      .toEqual(["Ready", "Pending", "Unknown", "Needs review", "Blocked"]);
  });

  it("picks the worst tone, and ready when there is nothing to rank", () => {
    expect(worstStatusTone([])).toBe("ready");
    expect(worstStatusTone(["ready", "review", "unknown"])).toBe("review");
    expect(worstStatusTone(["review", "blocked", "review"])).toBe("blocked");
  });

  it("is clear only when every signal is ready", () => {
    expect(summarizeStatusSignals([])).toMatchObject({ clear: true, tone: "ready", worst: null, issues: [] });
    expect(summarizeStatusSignals([signal("a", "ready"), signal("b", "ready")]))
      .toMatchObject({ clear: true, tone: "ready", worst: null });
  });

  it("names only the issues, worst first, keeping the caller's order between equal tones", () => {
    const summary = summarizeStatusSignals([
      signal("scope", "unknown"),
      signal("source", "ready"),
      signal("freshness", "review"),
      signal("blocker", "blocked"),
      signal("completeness", "review")
    ]);

    expect(summary.clear).toBe(false);
    expect(summary.tone).toBe("blocked");
    expect(summary.worst?.id).toBe("blocker");
    expect(summary.issues.map((issue) => issue.id)).toEqual(["blocker", "freshness", "completeness", "scope"]);
    expect(summary.counts).toEqual({ blocked: 1, review: 2, unconfirmed: 1 });
  });

  it("gives the same answer for the same signals in any order", () => {
    const signals = [signal("a", "review"), signal("b", "blocked"), signal("c", "ready"), signal("d", "pending")];
    const forward = summarizeStatusSignals(signals);
    const reversed = summarizeStatusSignals([...signals].reverse());

    expect(reversed.tone).toBe(forward.tone);
    expect(reversed.clear).toBe(forward.clear);
    expect(reversed.counts).toEqual(forward.counts);
    expect(reversed.issues.map((issue) => issue.id).sort()).toEqual(forward.issues.map((issue) => issue.id).sort());
  });
});
