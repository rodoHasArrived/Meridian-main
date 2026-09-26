import { describe, expect, it } from "vitest";
import {
  buildEventImpact,
  buildReportingImpact,
  buildSourceReplacementImpact,
  type AffectedBlockInput
} from "@/lib/reporting-impact";

function block(overrides: Partial<AffectedBlockInput> = {}): AffectedBlockInput {
  return {
    blockId: "block",
    blockLabel: "Investment value",
    reportId: "sept",
    reportName: "September Investment Report",
    reportWorkflowState: "Preparing",
    valueChanges: true,
    ...overrides
  };
}

describe("buildReportingImpact", () => {
  it("counts blocks and distinct reports", () => {
    const impact = buildReportingImpact([
      block({ blockId: "a", reportId: "r1" }),
      block({ blockId: "b", reportId: "r1" }),
      block({ blockId: "c", reportId: "r2" })
    ]);

    expect(impact.counts.blocks).toBe(3);
    expect(impact.counts.reports).toBe(2);
  });

  it("counts an unevaluable dependency as affected, not as safe", () => {
    const impact = buildReportingImpact([
      block({ blockId: "a", valueChanges: true }),
      block({ blockId: "b", valueChanges: null }),
      block({ blockId: "c", valueChanges: undefined })
    ]);

    expect(impact.counts.blocks).toBe(3);
    expect(impact.counts.unresolved).toBe(2);
    expect(impact.hasUnresolvedImpact).toBe(true);
    expect(impact.severity).toBe("review");
    expect(impact.summary).toContain("could not be evaluated and are counted as affected");
  });

  it("counts only certain changes as expected value changes", () => {
    const impact = buildReportingImpact([
      block({ blockId: "a", valueChanges: true }),
      block({ blockId: "b", valueChanges: false }),
      block({ blockId: "c", valueChanges: null })
    ]);

    expect(impact.counts.expectedValueChanges).toBe(1);
  });

  it("raises a restatement assessment when a published report is touched", () => {
    const impact = buildReportingImpact([
      block({ blockId: "a", reportId: "draft", reportWorkflowState: "Preparing" }),
      block({ blockId: "b", reportId: "done", reportWorkflowState: "Published" })
    ]);

    expect(impact.requiresRestatementAssessment).toBe(true);
    expect(impact.counts.publishedReports).toBe(1);
    expect(impact.severity).toBe("action");
    expect(impact.summary).toContain("restatement assessment required");
  });

  it("sorts published reports first, then unresolved, then expected", () => {
    const impact = buildReportingImpact([
      block({ blockId: "expected", valueChanges: false }),
      block({ blockId: "unresolved", valueChanges: null }),
      block({ blockId: "published", reportId: "done", reportWorkflowState: "Published" })
    ]);

    expect(impact.blocks.map((entry) => entry.blockId)).toEqual(["published", "unresolved", "expected"]);
  });

  it("builds the preview lines in the order the impact panel reads them", () => {
    const impact = buildReportingImpact([
      block({ blockId: "a", isPeriodComparison: true }),
      block({ blockId: "b", hasOpenReview: true, reportId: "r2" })
    ]);

    expect(impact.lines).toEqual([
      "2 report blocks",
      "2 reports",
      "1 published-period comparison",
      "1 open review"
    ]);
  });

  it("reports no dependants plainly", () => {
    const impact = buildReportingImpact([]);

    expect(impact.summary).toBe("No report blocks depend on this.");
    expect(impact.severity).toBe("ready");
  });
});

describe("buildSourceReplacementImpact", () => {
  it("previews the estate a replacement touches", () => {
    const model = buildSourceReplacementImpact({
      currentSource: "Manual Adjustment File",
      proposedSource: "Investment Ledger",
      affectedBlocks: [block({ blockId: "a" }), block({ blockId: "b", reportId: "r2" })]
    });

    expect(model.isChange).toBe(true);
    expect(model.currentSource).toBe("Manual Adjustment File");
    expect(model.proposedSource).toBe("Investment Ledger");
    expect(model.counts.blocks).toBe(2);
  });

  it("reports a same-source replacement as no change rather than a clean one", () => {
    const model = buildSourceReplacementImpact({
      currentSource: "Investment Ledger",
      proposedSource: "  investment ledger  ",
      affectedBlocks: [block()]
    });

    expect(model.isChange).toBe(false);
    expect(model.summary).toBe("Proposed source matches the current source; nothing would change.");
  });

  it("labels an unnamed source instead of rendering it blank", () => {
    const model = buildSourceReplacementImpact({
      currentSource: "   ",
      proposedSource: "Investment Ledger",
      affectedBlocks: []
    });

    expect(model.currentSource).toBe("Not set");
  });
});

describe("buildEventImpact", () => {
  const CHAIN = [
    { label: "Portfolio classification" },
    { label: "High-yield exposure" },
    { label: "Risk limits" }
  ];

  it("summarizes the propagation path through to the reports", () => {
    const model = buildEventImpact({
      event: "Security rating change",
      transition: "BBB → BB",
      chain: CHAIN,
      affectedBlocks: [
        block({ blockId: "a", reportWorkflowState: "InReview" }),
        block({ blockId: "b", reportWorkflowState: "InReview" }),
        block({ blockId: "c", reportId: "r2", reportWorkflowState: "Approved" })
      ]
    });

    expect(model.transition).toBe("BBB → BB");
    expect(model.chainSummary).toBe(
      "Security rating change → Portfolio classification → High-yield exposure → Risk limits → 3 report blocks changed, 2 reports require review."
    );
  });

  it("counts distinct reports needing review, not blocks", () => {
    const model = buildEventImpact({
      event: "Security rating change",
      chain: CHAIN,
      affectedBlocks: [
        block({ blockId: "a", reportId: "r1", reportWorkflowState: "InReview" }),
        block({ blockId: "b", reportId: "r1", reportWorkflowState: "InReview" })
      ]
    });

    expect(model.counts.blocks).toBe(2);
    expect(model.reportsRequiringReview).toBe(1);
  });

  it("does not send a report still being prepared back for review", () => {
    const model = buildEventImpact({
      event: "Security rating change",
      chain: CHAIN,
      affectedBlocks: [block({ reportWorkflowState: "Preparing" })]
    });

    expect(model.reportsRequiringReview).toBe(0);
    expect(model.chainSummary).not.toContain("require review");
  });

  it("escalates to action when the event reaches a published report", () => {
    const model = buildEventImpact({
      event: "Security rating change",
      chain: CHAIN,
      affectedBlocks: [block({ reportWorkflowState: "Published" })]
    });

    expect(model.severity).toBe("action");
    expect(model.requiresRestatementAssessment).toBe(true);
  });

  it("keeps the chain links in order with their transitions", () => {
    const model = buildEventImpact({
      event: "Security rating change",
      chain: [{ label: "Portfolio classification", transition: "IG → HY" }, { label: "Risk limits" }],
      affectedBlocks: []
    });

    expect(model.chain.map((link) => link.ordinal)).toEqual([0, 1]);
    expect(model.chain[0]?.transition).toBe("IG → HY");
    expect(model.chain[1]?.transition).toBeNull();
  });

  it("honours a caller-supplied set of reviewed states", () => {
    const model = buildEventImpact({
      event: "Security rating change",
      chain: CHAIN,
      affectedBlocks: [block({ reportWorkflowState: "ReadyForReview" })],
      reviewedStates: ["ReadyForReview"]
    });

    expect(model.reportsRequiringReview).toBe(1);
  });
});
