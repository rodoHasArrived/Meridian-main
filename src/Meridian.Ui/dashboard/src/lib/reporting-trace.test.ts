import { describe, expect, it } from "vitest";
import {
  buildCalculationTrace,
  buildDownstreamUsage,
  buildReportingTrace,
  normalizeReportingTraceStage,
  REPORTING_TRACE_STAGES
} from "@/lib/reporting-trace";
import { isPublishedWorkflowState } from "@/lib/reporting-lifecycle";

const FULL_CHAIN = [
  { stage: "Source", label: "Bloomberg BVAL" },
  { stage: "Normalization", label: "Security pricing service" },
  { stage: "Calculation", label: "Position valuation" },
  { stage: "Reconciliation", label: "Portfolio aggregation" },
  { stage: "ReportBlock", label: "Investment value" },
  { stage: "Publication", label: "September Investment Report" }
];

describe("normalizeReportingTraceStage", () => {
  it("accepts the canonical stage names", () => {
    for (const stage of REPORTING_TRACE_STAGES) {
      expect(normalizeReportingTraceStage(stage)).toBe(stage);
    }
  });

  it("accepts common spellings and separators", () => {
    expect(normalizeReportingTraceStage("report block")).toBe("ReportBlock");
    expect(normalizeReportingTraceStage("report_block")).toBe("ReportBlock");
    expect(normalizeReportingTraceStage("normalisation")).toBe("Normalization");
    expect(normalizeReportingTraceStage("recon")).toBe("Reconciliation");
  });

  it("refuses to guess at an unrecognized stage", () => {
    expect(normalizeReportingTraceStage("enrichment")).toBeNull();
    expect(normalizeReportingTraceStage(null)).toBeNull();
  });
});

describe("buildReportingTrace", () => {
  it("orders steps from source to publication", () => {
    const trace = buildReportingTrace({ steps: [...FULL_CHAIN].reverse() });

    expect(trace.steps.map((step) => step.stage)).toEqual([...REPORTING_TRACE_STAGES]);
    expect(trace.isComplete).toBe(true);
    expect(trace.severity).toBe("ready");
  });

  it("keeps input order within a stage", () => {
    const trace = buildReportingTrace({
      steps: [
        { stage: "Source", label: "Bloomberg BVAL" },
        { stage: "Normalization", label: "FX translation" },
        { stage: "Normalization", label: "Security pricing service" },
        { stage: "ReportBlock", label: "Investment value" }
      ]
    });

    expect(trace.steps.filter((step) => step.stage === "Normalization").map((step) => step.label)).toEqual([
      "FX translation",
      "Security pricing service"
    ]);
  });

  it("reports a missing required stage as a gap rather than a shorter trace", () => {
    const trace = buildReportingTrace({
      steps: FULL_CHAIN.filter((step) => step.stage !== "Source")
    });

    expect(trace.gaps.map((gap) => gap.stage)).toEqual(["Source"]);
    expect(trace.gaps[0]?.severity).toBe("action");
    expect(trace.isComplete).toBe(false);
    expect(trace.severity).toBe("action");
    expect(trace.summary).toContain("source missing");
  });

  it("does not treat an absent calculation as a gap for a directly sourced value", () => {
    const trace = buildReportingTrace({
      steps: [
        { stage: "Source", label: "Accounting Ledger" },
        { stage: "ReportBlock", label: "Cash balance" }
      ]
    });

    expect(trace.gaps).toEqual([]);
    expect(trace.isComplete).toBe(true);
  });

  it("reports reconciliation as a gap when the caller requires it", () => {
    const trace = buildReportingTrace({
      steps: [
        { stage: "Source", label: "Accounting Ledger" },
        { stage: "ReportBlock", label: "Cash balance" }
      ],
      requiredStages: ["Reconciliation"]
    });

    expect(trace.gaps.map((gap) => gap.stage)).toEqual(["Reconciliation"]);
    expect(trace.gaps[0]?.severity).toBe("review");
    expect(trace.severity).toBe("review");
  });

  it("retains steps at an unrecognized stage instead of dropping them", () => {
    const trace = buildReportingTrace({
      steps: [...FULL_CHAIN, { stage: "Enrichment", label: "Analyst overlay" }]
    });

    expect(trace.unresolvedSteps.map((step) => step.label)).toEqual(["Analyst overlay"]);
    expect(trace.isComplete).toBe(false);
    expect(trace.summary).toContain("unrecognized stage");
  });

  it("reports an empty trace plainly", () => {
    const trace = buildReportingTrace({ steps: [] });

    expect(trace.summary).toBe("No lineage recorded for this value.");
    expect(trace.gaps.map((gap) => gap.stage)).toEqual(["Source", "ReportBlock"]);
  });
});

describe("buildCalculationTrace", () => {
  const COMPONENTS = [
    { label: "Interest income", value: 16_482_190 },
    { label: "Dividend income", value: 2_431_204 },
    { label: "Accretion", value: 412_118 },
    { label: "Amortization", value: -633_721 },
    { label: "Expenses", value: -320_349 }
  ];

  it("ties components to the stated total", () => {
    const trace = buildCalculationTrace({ components: COMPONENTS, statedTotal: 18_371_442 });

    expect(trace.computedTotal).toBe(18_371_442);
    expect(trace.residual).toBe(0);
    expect(trace.tiesOut).toBe(true);
    expect(trace.severity).toBe("ready");
    expect(trace.displayTotal).toBe("$18,371,442");
  });

  it("reports a residual rather than balancing a breakdown that does not tie", () => {
    const trace = buildCalculationTrace({ components: COMPONENTS, statedTotal: 18_500_000 });

    expect(trace.tiesOut).toBe(false);
    expect(trace.residual).toBe(128_558);
    expect(trace.severity).toBe("action");
    expect(trace.summary).toContain("do not tie");
    expect(trace.summary).toContain("$128,558");
    // The stated total is still what the report says; the trace does not quietly
    // substitute its own sum.
    expect(trace.statedTotal).toBe(18_500_000);
    expect(trace.displayTotal).toBe("$18,500,000");
  });

  it("tolerates floating-point noise within half a cent", () => {
    const trace = buildCalculationTrace({
      components: [
        { label: "A", value: 0.1 },
        { label: "B", value: 0.2 }
      ],
      statedTotal: 0.3
    });

    expect(trace.tiesOut).toBe(true);
  });

  it("marks negative components for parenthesized display", () => {
    const trace = buildCalculationTrace({ components: COMPONENTS, statedTotal: 18_371_442 });

    expect(trace.components.filter((component) => component.isNegative).map((component) => component.label)).toEqual([
      "Amortization",
      "Expenses"
    ]);
  });

  it("excludes a valueless component instead of treating it as zero", () => {
    const trace = buildCalculationTrace({
      components: [
        { label: "Interest income", value: 100 },
        { label: "Dividend income", value: null }
      ]
    });

    expect(trace.components).toHaveLength(1);
    expect(trace.excludedLabels).toEqual(["Dividend income"]);
    // No stated total, but a component is missing, so the breakdown is not clean.
    expect(trace.tiesOut).toBe(false);
    expect(trace.severity).toBe("action");
  });

  it("reports the computed sum when no total is stated", () => {
    const trace = buildCalculationTrace({ components: COMPONENTS });

    expect(trace.statedTotal).toBeNull();
    expect(trace.residual).toBeNull();
    expect(trace.tiesOut).toBe(true);
    expect(trace.displayTotal).toBe("$18,371,442");
  });
});

describe("buildDownstreamUsage", () => {
  const USAGE = [
    { reportId: "mgmt", reportName: "Management Dashboard", workflowState: "Preparing" },
    { reportId: "sept", reportName: "September Investment Report", workflowState: "Published" },
    { reportId: "alm", reportName: "ALM Package", workflowState: "InReview" }
  ];

  it("counts and labels the consumers", () => {
    const usage = buildDownstreamUsage(USAGE, isPublishedWorkflowState);

    expect(usage.reportCount).toBe(3);
    expect(usage.label).toBe("Used in 3 reports");
  });

  it("sorts published consumers first so a restatement question surfaces", () => {
    const usage = buildDownstreamUsage(USAGE, isPublishedWorkflowState);

    expect(usage.entries[0]?.reportName).toBe("September Investment Report");
    expect(usage.publishedCount).toBe(1);
    expect(usage.severity).toBe("review");
  });

  it("sums block counts, defaulting an unstated count to one", () => {
    const usage = buildDownstreamUsage(
      [
        { reportId: "a", reportName: "A", blockCount: 4 },
        { reportId: "b", reportName: "B" }
      ],
      isPublishedWorkflowState
    );

    expect(usage.blockCount).toBe(5);
  });

  it("reports an unused value plainly", () => {
    const usage = buildDownstreamUsage([], isPublishedWorkflowState);

    expect(usage.label).toBe("Not used in any report");
    expect(usage.severity).toBe("info");
  });

  it("singularizes a lone consumer", () => {
    const usage = buildDownstreamUsage(
      [{ reportId: "a", reportName: "A", workflowState: "Preparing" }],
      isPublishedWorkflowState
    );

    expect(usage.label).toBe("Used in 1 report");
  });
});
