// @vitest-environment node
import { describe, expect, it } from "vitest";
import { compareByUrgency, describeDue, sortByUrgency } from "./due-urgency";

// Frozen clock: Tuesday 17 November 2026, 22:30 UTC.
const NOW = Date.parse("2026-11-17T22:30:00Z");

describe("describeDue", () => {
  it("reads today, overdue and soon in calendar days", () => {
    expect(describeDue("2026-11-17T23:59:00Z", NOW)).toMatchObject({ state: "today", label: "Due today", days: 0 });
    expect(describeDue("2026-11-17T01:00:00Z", NOW)).toMatchObject({ state: "today", label: "Due today" });
    expect(describeDue("2026-11-14T09:00:00Z", NOW)).toMatchObject({ state: "overdue", label: "Overdue 3 days", days: -3 });
    expect(describeDue("2026-11-18T00:30:00Z", NOW)).toMatchObject({ state: "soon", label: "Due tomorrow", days: 1 });
    expect(describeDue("2026-11-20", NOW)).toMatchObject({ state: "soon", label: "Due in 3 days" });
  });

  it("is overdue from the first minute of the next calendar day", () => {
    const justAfterMidnight = Date.parse("2026-11-18T00:01:00Z");
    expect(describeDue("2026-11-17T23:00:00Z", justAfterMidnight)).toMatchObject({ state: "overdue", label: "Overdue 1 day" });
  });

  it("reads a date-only value as that calendar day", () => {
    expect(describeDue("2026-11-17", NOW).state).toBe("today");
    expect(describeDue("2026-11-16", NOW)).toMatchObject({ state: "overdue", label: "Overdue 1 day" });
  });

  it("shows a date beyond a week, with the year only when it differs", () => {
    expect(describeDue("2026-12-14", NOW).label).toBe("Due Dec 14");
    expect(describeDue("2027-01-14", NOW).label).toBe("Due Jan 14, 2027");
  });

  it("never marks finished work overdue and ignores missing or invalid dates", () => {
    expect(describeDue("2026-11-01", NOW, { done: true })).toMatchObject({ state: "later", label: "Due Nov 1" });
    expect(describeDue(null, NOW).state).toBe("none");
    expect(describeDue("not a date", NOW).state).toBe("none");
  });
});

describe("sortByUrgency", () => {
  it("puts the most overdue first, then today, soonest, and no due date last", () => {
    const rows = [
      { id: "later", due: "2026-12-30" }, { id: "none", due: null }, { id: "today", due: "2026-11-17" },
      { id: "late-1", due: "2026-11-16" }, { id: "late-5", due: "2026-11-12" }, { id: "soon", due: "2026-11-19" }
    ];
    expect(sortByUrgency(rows, (row) => describeDue(row.due, NOW)).map((row) => row.id))
      .toEqual(["late-5", "late-1", "today", "soon", "later", "none"]);
  });

  it("keeps the original order for equal urgency", () => {
    expect(compareByUrgency(describeDue(null, NOW), describeDue(undefined, NOW))).toBe(0);
    const rows = [{ id: "a", due: null }, { id: "b", due: null }];
    expect(sortByUrgency(rows, (row) => describeDue(row.due, NOW)).map((row) => row.id)).toEqual(["a", "b"]);
  });
});
