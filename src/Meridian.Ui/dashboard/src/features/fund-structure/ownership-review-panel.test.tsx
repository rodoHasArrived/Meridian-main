import { describe, expect, it } from "vitest";
import { projectOwnershipReview, type OwnershipReviewRow } from "@/features/fund-structure/ownership-review-panel";
import type { FundStructureGraph } from "@/lib/api";

describe("projectOwnershipReview", () => {
  it("keeps projection ordering and rollup summary stable", () => {
    const review = projectOwnershipReview(createGraph(), new Date("2026-05-29T00:00:00.000Z"));

    expect(review.summary).toMatchObject({
      totalLinkCount: 2,
      activeLinkCount: 2,
      invalidLinkCount: 0,
      blockingLinkCount: 0,
      primaryLinkCount: 1,
      primaryOwnershipPercentTotal: 80,
      statusLabel: "Ownership review ready"
    });
    expect(review.rows.map((row) => row.nodeDisplayLabel)).toEqual([
      "Fund FUND — Flagship Fund allocates to InvestmentPortfolio PORT — Core Portfolio",
      "Organization ORG — Organization owns Fund FUND — Flagship Fund"
    ]);
  });

  it("returns an explicit empty state summary", () => {
    const review = projectOwnershipReview({ nodes: [], ownershipLinks: [] }, new Date("2026-05-29T00:00:00.000Z"));

    expect(review.rows).toEqual([]);
    expect(review.summary.statusLabel).toBe("No ownership links to review");
  });

  it("surfaces invalid ownership warnings and remediation", () => {
    const graph = createGraph();
    graph.ownershipLinks[0] = { ...graph.ownershipLinks[0], ownershipPercent: null };

    const row = projectOwnershipReview(graph, new Date("2026-05-29T00:00:00.000Z")).rows[0] as OwnershipReviewRow;

    expect(row.validationState).toBe("Warning");
    expect(row.blockingMessages).toContain("Economic ownership or allocation links should carry an explicit percent before setup completion.");
    expect(row.suggestedRemediationActions).toContain("Edit the link and add the ownership percent, or replace it with a non-economic relationship type.");
  });

  it("filters expired links when active-only is selected", () => {
    const graph = createGraph();
    graph.ownershipLinks.push({
      ownershipLinkId: "expired",
      parentNodeId: "org",
      childNodeId: "fund",
      relationshipType: "Advises",
      ownershipPercent: null,
      isPrimary: false,
      effectiveFrom: "2026-01-01T00:00:00.000Z",
      effectiveTo: "2026-02-01T00:00:00.000Z",
      notes: null
    });

    const review = projectOwnershipReview(graph, new Date("2026-05-29T00:00:00.000Z"), true);

    expect(review.rows.map((row) => row.ownershipLinkId)).toEqual(["allocates", "owns"]);
  });
});

function createGraph(): FundStructureGraph {
  return {
    nodes: [
      { nodeId: "portfolio", kind: "InvestmentPortfolio", code: "PORT", name: "Core Portfolio", isActive: true, effectiveFrom: "2026-01-01T00:00:00.000Z" },
      { nodeId: "fund", kind: "Fund", code: "FUND", name: "Flagship Fund", isActive: true, effectiveFrom: "2026-01-01T00:00:00.000Z" },
      { nodeId: "org", kind: "Organization", code: "ORG", name: "Organization", isActive: true, effectiveFrom: "2026-01-01T00:00:00.000Z" }
    ],
    ownershipLinks: [
      { ownershipLinkId: "allocates", parentNodeId: "fund", childNodeId: "portfolio", relationshipType: "AllocatesTo", ownershipPercent: 100, isPrimary: false, effectiveFrom: "2026-01-01T00:00:00.000Z", effectiveTo: null, notes: null },
      { ownershipLinkId: "owns", parentNodeId: "org", childNodeId: "fund", relationshipType: "Owns", ownershipPercent: 80, isPrimary: true, effectiveFrom: "2026-01-01T00:00:00.000Z", effectiveTo: null, notes: null }
    ],
    assignments: []
  };
}
