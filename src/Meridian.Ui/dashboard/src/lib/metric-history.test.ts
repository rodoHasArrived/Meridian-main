// @vitest-environment node
import { describe, expect, it } from "vitest";
import { describeMetricHistory, trailingKnownRun } from "@/lib/metric-history";

const point = (date: string, value: number | null) => ({ date, value });

describe("metric history", () => {
  it("keeps only the latest run of established days and never bridges a gap", () => {
    const history = [point("2026-11-13", 3), point("2026-11-14", null), point("2026-11-15", 1), point("2026-11-16", 2), point("2026-11-17", 2)];
    expect(trailingKnownRun(history)).toEqual([point("2026-11-15", 1), point("2026-11-16", 2), point("2026-11-17", 2)]);
  });

  it("draws nothing with fewer than two established trailing days", () => {
    expect(trailingKnownRun([point("2026-11-16", 1), point("2026-11-17", null)])).toBeNull();
    expect(trailingKnownRun([point("2026-11-17", 4)])).toBeNull();
    expect(trailingKnownRun(null)).toBeNull();
  });

  it("describes the span with formatted dates and says when earlier days are missing", () => {
    const run = [point("2026-11-15", 1), point("2026-11-17", 2)] as { date: string; value: number }[];
    expect(describeMetricHistory("Open breaks", run, 2)).toBe("Open breaks over 2 days: 1 on Nov 15, 2026, 2 on Nov 17, 2026.");
    expect(describeMetricHistory("Open breaks", run, 14)).toContain("Earlier days could not be established");
  });
});
