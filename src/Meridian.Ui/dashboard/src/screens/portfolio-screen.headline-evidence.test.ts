import { describe, expect, it } from "vitest";
import { withPortfolioMarkEvidence } from "@/screens/portfolio-screen.headline-evidence";
import { buildPortfolioFallbackMetrics } from "@/screens/portfolio-screen.view-model";

const metrics = buildPortfolioFallbackMetrics({ openPositionCount: 3, totalExposure: 18_900, totalUnrealizedPnl: 90 });
const current = { markFreshness: { reviewRequired: false } };
const needsReview = { markFreshness: { reviewRequired: true } };
const byId = (result: typeof metrics) => Object.fromEntries(result.map((metric) => [metric.id, metric]));

describe("withPortfolioMarkEvidence", () => {
  it("leaves the totals alone when every position's mark is current", () => {
    expect(withPortfolioMarkEvidence(metrics, [current, current, current], "fresh")).toEqual(metrics);
  });

  it("marks the valuation totals unverified and says how many marks need review", () => {
    const result = byId(withPortfolioMarkEvidence(metrics, [current, needsReview, needsReview], "fresh"));

    expect(result["portfolio-total-exposure"]).toMatchObject({ freshness: "unverified", delta: "2 of 3 marks need review" });
    expect(result["portfolio-unrealized-pnl"]).toMatchObject({ freshness: "unverified", delta: "2 of 3 marks need review" });
    // A count of positions does not depend on any mark.
    expect(result["portfolio-open-positions"]!.freshness).toBeUndefined();
  });

  it("words a single mark correctly", () => {
    const result = byId(withPortfolioMarkEvidence(metrics, [needsReview], "fresh"));
    expect(result["portfolio-total-exposure"]!.delta).toBe("1 of 1 mark needs review");
  });

  it("never raises a stale payload's verdict", () => {
    const result = byId(withPortfolioMarkEvidence(metrics, [needsReview, current], "stale"));
    expect(result["portfolio-total-exposure"]!.freshness).toBe("stale");
  });
});
