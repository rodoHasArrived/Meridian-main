import { describe, expect, it } from "vitest";
import {
  operatingScopePeriodMode,
  operatingScopePeriodPresets,
  windowForPeriodPreset
} from "@/lib/operating-scope/period";

describe("operatingScopePeriodPresets", () => {
  it("computes Today, MTD, QTD, YTD and Last month on a fixed clock", () => {
    const presets = operatingScopePeriodPresets(new Date(2026, 10, 14, 15, 45));
    expect(presets.map((preset) => [preset.id, preset.label, preset.from, preset.to])).toEqual([
      ["today", "Today", "2026-11-14", "2026-11-14"],
      ["mtd", "MTD", "2026-11-01", "2026-11-14"],
      ["qtd", "QTD", "2026-10-01", "2026-11-14"],
      ["ytd", "YTD", "2026-01-01", "2026-11-14"],
      ["last-month", "Last month", "2026-10-01", "2026-10-31"]
    ]);
  });

  it("uses the operator's calendar day late in the evening, not the UTC day", () => {
    // 23:30 local on 31 March: whatever the zone, the local day is still 31 March.
    const [today] = operatingScopePeriodPresets(new Date(2026, 2, 31, 23, 30));
    expect(today).toMatchObject({ from: "2026-03-31", to: "2026-03-31" });
  });

  it("crosses the year for Last month in January and handles leap February", () => {
    const january = operatingScopePeriodPresets(new Date(2026, 0, 5));
    expect(january.find((preset) => preset.id === "last-month")).toMatchObject({ from: "2025-12-01", to: "2025-12-31" });
    const march = operatingScopePeriodPresets(new Date(2028, 2, 10));
    expect(march.find((preset) => preset.id === "last-month")).toMatchObject({ from: "2028-02-01", to: "2028-02-29" });
    expect(march.find((preset) => preset.id === "qtd")).toMatchObject({ from: "2028-01-01" });
  });
});

describe("windowForPeriodPreset and operatingScopePeriodMode", () => {
  const now = new Date(2026, 10, 14, 9);

  it("writes the preset range and clears date and as-of", () => {
    const mtd = operatingScopePeriodPresets(now).find((preset) => preset.id === "mtd")!;
    expect(windowForPeriodPreset(mtd)).toEqual({ from: "2026-11-01", to: "2026-11-14", date: null, asOf: null });
  });

  it("recognises presets, custom ranges, as-of, workflow dates and the empty window", () => {
    expect(operatingScopePeriodMode({ from: "2026-11-01", to: "2026-11-14" }, now)).toBe("mtd");
    expect(operatingScopePeriodMode({ from: "2026-11-14", to: "2026-11-14" }, now)).toBe("today");
    // An MTD applied yesterday is no longer MTD today.
    expect(operatingScopePeriodMode({ from: "2026-11-01", to: "2026-11-13" }, now)).toBe("custom");
    expect(operatingScopePeriodMode({ from: "2026-11-01" }, now)).toBe("custom");
    expect(operatingScopePeriodMode({ from: "2026-11-01", to: "2026-11-14", asOf: "2026-11-14" }, now)).toBe("custom");
    expect(operatingScopePeriodMode({ asOf: "2026-11-10" }, now)).toBe("as-of");
    expect(operatingScopePeriodMode({ date: "2026-11-10" }, now)).toBe("workflow");
    expect(operatingScopePeriodMode({}, now)).toBe("all");
  });
});
