import { describe, expect, it } from "vitest";
import { buildPortfolioScreenViewModel } from "./portfolio-screen.view-model";
import { presentMarkFreshness } from "@/lib/mark-freshness";
import type { MarkFreshnessAssessmentDto, TradingWorkspaceResponse } from "@/types";

const current: MarkFreshnessAssessmentDto = {
  symbol: "AAPL", securityId: "apple-security", financialAccountId: "account-1",
  observedOn: "2026-09-04", valuationDate: "2026-09-04", ageDays: 0,
  policyVersion: "marks-v2", status: "Current", blockReason: null
};

function portfolio(markFreshness: MarkFreshnessAssessmentDto | null) {
  const trading = {
    positions: [{ symbol: "AAPL", side: "Long", quantity: "10", averagePrice: "$90",
      markPrice: "$100", exposure: "$1,000", dayPnl: "$0", unrealizedPnl: "$100", markFreshness }],
    metrics: [], openOrders: [], fills: [], risk: null, brokerage: null
  } as unknown as TradingWorkspaceResponse;
  return buildPortfolioScreenViewModel({ trading, strategy: null, accounting: null });
}

describe("Portfolio shared mark readiness", () => {
  it.each([
    { observedOn: "2026-08-01", ageDays: 34, blockReason: "AAPL observation is stale for marks-v2." },
    { observedOn: null, ageDays: null, blockReason: "AAPL mark observation is missing." },
    { observedOn: "2026-09-05", ageDays: -1, blockReason: "AAPL observation follows the valuation date." }
  ])("names a blocked position and restores readiness after evidence refresh: $blockReason", (issue) => {
    const blocked = portfolio({ ...current, ...issue, status: "ReviewRequired" });
    expect(blocked.positionRows[0].markFreshness).toMatchObject({ reviewRequired: true, label: "Review required", reason: issue.blockReason });
    expect(blocked.selectedPosition?.statusTitle).toBe("AAPL · Review required");
    expect(blocked.selectedPosition?.statusDetail).toContain(issue.blockReason);
    expect(blocked.selectedPosition?.fields).toContainEqual({ label: "Mark observed on", value: issue.observedOn ?? "Unknown", tone: "muted" });

    const recovered = portfolio(current);
    expect(recovered.positionRows[0].markFreshness).toMatchObject({ reviewRequired: false, label: "Current", age: "0 day(s)" });
    expect(recovered.selectedPosition?.statusTitle).toBe("AAPL selected");
    expect(recovered.selectedPosition?.statusDetail).not.toContain(issue.blockReason);
  });

  it("does not infer readiness from a number when the assessment is absent", () => {
    const vm = portfolio(null);
    expect(vm.positionRows[0].markPrice).toBe("$100");
    expect(vm.positionRows[0].markFreshness).toMatchObject({ label: "Review required", observedOn: "Unknown", age: "Unknown" });
    expect(vm.selectedPosition?.statusDetail).toContain("Shared mark assessment unavailable");
  });

  it("projects the shared decision without applying an independent browser age threshold", () => {
    expect(presentMarkFreshness({ ...current, ageDays: 40 })).toMatchObject({ label: "Current", age: "40 day(s)" });
    expect(presentMarkFreshness({ ...current, status: "ReviewRequired", blockReason: "Position scope requires review." })).toMatchObject({ reviewRequired: true, reason: "Position scope requires review." });
  });
});
