import { describe, expect, it } from "vitest";
import {
  buildReportHealth,
  buildSourceCoverage,
  defaultGatePolicyForClass,
  type ReportElementInput,
  type ReportHealthInput
} from "@/lib/report-health";

function element(overrides: Partial<ReportElementInput> = {}): ReportElementInput {
  return {
    elementId: "el",
    label: "Element",
    source: "Accounting Ledger",
    dataState: "Confirmed",
    ...overrides
  };
}

function elements(spec: ReadonlyArray<[count: number, overrides: Partial<ReportElementInput>]>): ReportElementInput[] {
  const rows: ReportElementInput[] = [];
  for (const [count, overrides] of spec) {
    for (let index = 0; index < count; index += 1) {
      rows.push(element({ elementId: `${overrides.dataState ?? "el"}-${index}`, ...overrides }));
    }
  }
  return rows;
}

describe("source coverage", () => {
  it("counts elements by data state and derives coverage", () => {
    const coverage = buildSourceCoverage(elements([
      [45, { dataState: "Confirmed" }],
      [1, { dataState: "Stale" }],
      [1, { dataState: "Overridden" }],
      [1, { dataState: "Missing" }]
    ]));

    expect(coverage.totalElements).toBe(48);
    expect(coverage.currentCount).toBe(45);
    expect(coverage.staleCount).toBe(1);
    expect(coverage.overriddenCount).toBe(1);
    expect(coverage.missingCount).toBe(1);
    // Stale and overridden elements still carry a value; only missing ones do not.
    expect(coverage.coveragePercent).toBe(97.9);
  });

  it("breaks coverage down by source, largest first", () => {
    const coverage = buildSourceCoverage([
      element({ source: "Bloomberg" }),
      element({ source: "Bloomberg" }),
      element({ source: "Bloomberg", dataState: "Stale" }),
      element({ source: "Accounting Ledger" }),
      element({ source: "Security Master" })
    ]);

    expect(coverage.bySource).toEqual([
      { source: "Bloomberg", elementCount: 3, currentCount: 2, sharePercent: 60 },
      { source: "Accounting Ledger", elementCount: 1, currentCount: 1, sharePercent: 20 },
      { source: "Security Master", elementCount: 1, currentCount: 1, sharePercent: 20 }
    ]);
  });

  it("attributes unsourced elements rather than dropping them", () => {
    const coverage = buildSourceCoverage([element({ source: "   " })]);
    expect(coverage.bySource[0].source).toBe("Unattributed");
  });

  it("counts critical elements that resolved to nothing", () => {
    const coverage = buildSourceCoverage([
      element({ dataState: "Missing", isCritical: true }),
      element({ dataState: "Exception", isCritical: true }),
      element({ dataState: "Missing", isCritical: false })
    ]);

    expect(coverage.criticalMissingCount).toBe(2);
  });

  it("treats an exception as material when flagged or attached to a critical element", () => {
    const coverage = buildSourceCoverage([
      element({ dataState: "Exception", hasMaterialException: true }),
      element({ dataState: "Exception", isCritical: true }),
      element({ dataState: "Exception" })
    ]);

    expect(coverage.exceptionCount).toBe(3);
    expect(coverage.materialExceptionCount).toBe(2);
  });

  it("handles an empty report without dividing by zero", () => {
    const coverage = buildSourceCoverage([]);
    expect(coverage.coveragePercent).toBe(0);
    expect(coverage.summaryLabel).toBe("No report elements");
  });
});

function health(overrides: Partial<ReportHealthInput> = {}) {
  return buildReportHealth({
    reportClass: "Accounting",
    coverage: buildSourceCoverage(elements([[100, { dataState: "Confirmed" }]])),
    requiredSectionCount: 7,
    completeSectionCount: 7,
    requiredCommentaryCount: 4,
    completeCommentaryCount: 4,
    reviewedElementCount: 100,
    controlStates: ["Passed", "Passed", "WithinTolerance"],
    materialControlStates: ["Passed"],
    isReviewComplete: true,
    isApproved: true,
    ...overrides
  });
}

describe("report health dimensions", () => {
  it("reports each dimension independently", () => {
    const model = health();
    expect(model.dimensions.map((entry) => [entry.key, entry.valueLabel])).toEqual([
      ["content", "100%"],
      ["data", "100%"],
      ["reconciliation", "100%"],
      ["commentary", "100%"],
      ["review", "100%"],
      ["approval", "100%"]
    ]);
  });

  it("renders an em dash rather than zero for dimensions that do not yet apply", () => {
    const model = health({ isApproved: false, requiredCommentaryCount: 0, completeCommentaryCount: 0 });
    const byKey = Object.fromEntries(model.dimensions.map((entry) => [entry.key, entry]));

    expect(byKey.approval.percent).toBeNull();
    expect(byKey.approval.valueLabel).toBe("—");
    expect(byKey.commentary.valueLabel).toBe("—");
    expect(byKey.commentary.detail).toBe("No commentary required");
  });
});

describe("publication gates", () => {
  it("passes every blocking gate when the report is complete", () => {
    const model = health();
    expect(model.canPublish).toBe(true);
    expect(model.overallLabel).toBe("Ready to publish");
    expect(model.overallSeverity).toBe("ready");
    expect(model.blockingReasons).toEqual([]);
  });

  it("blocks on a material reconciliation failure even when everything else is complete", () => {
    const model = health({ materialControlStates: ["Failed"] });

    expect(model.canPublish).toBe(false);
    expect(model.overallLabel).toBe("Blocked");
    expect(model.overallSeverity).toBe("blocked");
    expect(model.blockingReasons).toEqual(["1 material reconciliation(s) failed."]);
    // The other dimensions stay at 100% - the gate is what blocks, not an average.
    expect(model.dimensions.find((entry) => entry.key === "content")?.percent).toBe(100);
  });

  it("blocks when a single critical element is missing despite 99% coverage", () => {
    const model = health({
      coverage: buildSourceCoverage(elements([
        [99, { dataState: "Confirmed" }],
        [1, { dataState: "Missing", isCritical: true }]
      ]))
    });

    expect(model.canPublish).toBe(false);
    expect(model.blockingReasons).toContain("1 critical element(s) have no usable value.");
  });

  it("fails the coverage gate below the class threshold", () => {
    const model = health({
      coverage: buildSourceCoverage(elements([
        [90, { dataState: "Confirmed" }],
        [10, { dataState: "Missing" }]
      ]))
    });

    const coverageGate = model.gates.find((entry) => entry.key === "dataCoverage");
    expect(coverageGate).toMatchObject({ status: "Failed", requirementLabel: "≥ 98%", actualLabel: "90%" });
    expect(model.canPublish).toBe(false);
  });

  it("treats an unrun material reconciliation as pending, not passed", () => {
    const model = health({ materialControlStates: [] });
    const gate = model.gates.find((entry) => entry.key === "materialReconciliation");

    expect(gate?.status).toBe("Pending");
    expect(gate?.actualLabel).toBe("Not run");
    expect(model.canPublish).toBe(false);
    expect(model.overallLabel).toBe("Material reconciliation required");
  });

  it("names the outstanding approval as the next required step", () => {
    const model = health({ isApproved: false });
    expect(model.overallLabel).toBe("Approval required");
    expect(model.overallSeverity).toBe("review");
    expect(model.pendingReasons).toEqual(["Approval is outstanding."]);
  });

  it("prefers a hard failure over a pending step in the overall state", () => {
    const model = health({ isApproved: false, materialControlStates: ["Failed"] });
    expect(model.overallLabel).toBe("Blocked");
  });
});

describe("gate policy by report class", () => {
  it("holds regulatory reports to full coverage", () => {
    expect(defaultGatePolicyForClass("Regulatory")).toMatchObject({
      minimumDataCoveragePercent: 100,
      requireMaterialReconciliation: true,
      requireApproval: true
    });
  });

  it("does not gate analytical reports on review or approval", () => {
    const model = buildReportHealth({
      reportClass: "Analytical",
      coverage: buildSourceCoverage(elements([[95, { dataState: "Confirmed" }], [5, { dataState: "Stale" }]])),
      requiredSectionCount: 3,
      completeSectionCount: 2,
      requiredCommentaryCount: 0,
      completeCommentaryCount: 0,
      reviewedElementCount: 0,
      isReviewComplete: false,
      isApproved: false
    });

    expect(model.canPublish).toBe(true);
    expect(model.gates.find((entry) => entry.key === "review")?.status).toBe("NotApplicable");
    expect(model.gates.find((entry) => entry.key === "approval")?.status).toBe("NotApplicable");
    expect(model.gates.find((entry) => entry.key === "requiredSections")?.status).toBe("NotApplicable");
  });

  it("lets a template override a single threshold without discarding the rest", () => {
    const model = health({
      coverage: buildSourceCoverage(elements([[95, { dataState: "Confirmed" }], [5, { dataState: "Missing" }]])),
      policy: { minimumDataCoveragePercent: 95 }
    });

    expect(model.gates.find((entry) => entry.key === "dataCoverage")?.status).toBe("Passed");
    expect(model.gates.find((entry) => entry.key === "approval")?.status).toBe("Passed");
    expect(model.canPublish).toBe(true);
  });
});
