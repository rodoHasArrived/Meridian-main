import { describe, expect, it } from "vitest";
import { capitalCallIsOverdue } from "@/screens/private-capital-presenters";
import { capitalCall } from "@/screens/private-capital.test-fixtures";

describe("Server-observed overdue capital receivables", () => {
  it.each<[string, string, string, string, boolean]>([
    ["posted outstanding after due date", "2026-11-21T00:00:00Z", "250000.01", "150000.01", true],
    ["due-date boundary", "2026-11-20T23:59:59Z", "250000.01", "150000.01", false],
    ["not yet due", "2026-11-19T00:00:00Z", "250000.01", "150000.01", false],
    ["unposted call", "2026-11-21T00:00:00Z", "0.00", "250000.01", false],
    ["fully funded", "2026-11-21T00:00:00Z", "250000.01", "0.00", false],
    ["invalid server observation", "unavailable", "250000.01", "150000.01", false],
    ["invalid calendar observation", "2026-02-30T00:00:00Z", "250000.01", "150000.01", false],
    ["server observation without timezone", "2026-11-21T00:00:00", "250000.01", "150000.01", false],
    ["server observation converted to UTC", "2026-11-21T00:30:00+02:00", "250000.01", "150000.01", false]
  ])("handles %s", (_scenario, generatedAtUtc, issuedAmount, outstandingAmount, expected) => {
    const call = { ...capitalCall, lines: [{ ...capitalCall.lines[0], issuedAmount, outstandingAmount }] };
    expect(capitalCallIsOverdue(call, generatedAtUtc)).toBe(expected);
  });
  it("withholds overdue status for an invalid due date", () => {
    expect(capitalCallIsOverdue({ ...capitalCall, dueDate: "2026-02-30" }, "2026-11-21T00:00:00Z")).toBe(false);
  });
});
