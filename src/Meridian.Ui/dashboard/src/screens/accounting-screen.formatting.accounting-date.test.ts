import { describe, expect, it } from "vitest";
import { formatAccountingDate } from "@/screens/accounting-screen.formatting";

describe("formatAccountingDate", () => {
  it("prints date-only and UTC-midnight accounting dates as dates, without an invented time", () => {
    expect(formatAccountingDate("2026-06-30")).toBe("Jun 30, 2026");
    expect(formatAccountingDate("2026-06-30T00:00:00Z")).toBe("Jun 30, 2026");
    expect(formatAccountingDate("2026-06-30T00:00:00.000Z")).toBe("Jun 30, 2026");
    expect(formatAccountingDate("2026-06-30T00:00:00+00:00")).toBe("Jun 30, 2026");
  });

  it("keeps a real time as a timestamp and says when nothing is recorded", () => {
    expect(formatAccountingDate("2026-06-30T17:00:00Z")).toMatch(/17:00/);
    expect(formatAccountingDate(null)).toBe("Not recorded");
  });
});
