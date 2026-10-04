import { describe, expect, it } from "vitest";
import {
  buildCoverageField,
  buildReportingDatum,
  COVERAGE_FIELD_MAX_MARKS,
  describeDatumBlockState,
  type BuildReportingDatumOptions,
  type ReportingDatumInput
} from "@/lib/reporting-datum";
import { defaultMaterialityPolicyForClass } from "@/lib/reporting-materiality";

const POLICY = defaultMaterialityPolicyForClass("Accounting");

function options(overrides: Partial<BuildReportingDatumOptions> = {}): BuildReportingDatumOptions {
  return { policy: POLICY, evaluatedAtUtc: "2026-09-15T12:00:00Z", ...overrides };
}

function datum(overrides: Partial<ReportingDatumInput> = {}): ReportingDatumInput {
  return {
    datumId: "investment-value",
    label: "Investment value",
    value: 2_414_000_000,
    source: "Accounting Ledger",
    owner: "Fund Accounting",
    asOfUtc: "2026-09-15T06:00:00Z",
    dataState: "Confirmed",
    ...overrides
  };
}

describe("buildReportingDatum provenance", () => {
  it("carries the four provenance attributes", () => {
    const built = buildReportingDatum(datum(), options());

    expect(built.provenance.source).toBe("Accounting Ledger");
    expect(built.provenance.state).toBe("Confirmed");
    expect(built.provenance.asOf).toBe("Sep 15, 2026");
    expect(built.provenance.owner).toBe("Fund Accounting");
    expect(built.isAttributable).toBe(true);
    expect(built.missingProvenance).toEqual([]);
  });

  it("reports absent provenance rather than omitting or guessing it", () => {
    const built = buildReportingDatum(
      datum({ source: null, owner: "   ", asOfUtc: null, dataState: null }),
      options()
    );

    expect(built.provenance.source).toBe("Not set");
    expect(built.provenance.owner).toBe("Not set");
    expect(built.provenance.asOf).toBe("Not set");
    expect(built.isAttributable).toBe(false);
    expect(built.missingProvenance).toEqual(["Source", "State", "As of", "Owner"]);
  });

  it("does not report a defaulted data state as a known one", () => {
    const built = buildReportingDatum(datum({ dataState: "not-a-state" }), options());

    // The normalizer still yields a vocabulary member, but the caller did supply a
    // value, so the attribute counts as asserted.
    expect(built.dataState).toBe("Provisional");
    expect(built.provenance.stateKnown).toBe(true);
  });

  it("keeps an unparseable as-of token verbatim instead of calling it unknown", () => {
    const built = buildReportingDatum(datum({ asOfUtc: "2026-P03" }), options());

    expect(built.provenance.asOf).toBe("2026-P03");
    expect(built.provenance.asOfKnown).toBe(true);
  });
});

describe("buildReportingDatum movement", () => {
  it("builds a transition label for a moved value", () => {
    const built = buildReportingDatum(
      datum({ value: 2_414_000_000, priorValue: 2_411_000_000 }),
      options({ formatValue: (value) => `$${(value / 1_000_000_000).toFixed(3)}B` })
    );

    expect(built.movement.hasMoved).toBe(true);
    expect(built.movement.variance).toBe(3_000_000);
    expect(built.movement.transitionLabel).toBe("$2.411B → $2.414B");
  });

  it("reports no movement when there is no baseline to compare against", () => {
    const built = buildReportingDatum(datum({ priorValue: null }), options());

    expect(built.movement.hasMoved).toBe(false);
    expect(built.movement.variance).toBeNull();
    expect(built.movement.transitionLabel).toBeNull();
  });

  it("declines to report a percentage move away from zero", () => {
    const built = buildReportingDatum(datum({ value: 500, priorValue: 0 }), options());

    expect(built.movement.variance).toBe(500);
    expect(built.movement.variancePercent).toBeNull();
  });

  it("assesses the movement for materiality", () => {
    const immaterial = buildReportingDatum(datum({ value: 1_000_847, priorValue: 1_000_000 }), options());
    const material = buildReportingDatum(datum({ value: 1_284_711, priorValue: 1_000_000 }), options());

    expect(immaterial.materiality.outcome).toBe("WithinTolerance");
    expect(material.materiality.outcome).toBe("MaterialException");
  });

  it("carries a stale source into the materiality assessment", () => {
    const built = buildReportingDatum(
      datum({ asOfUtc: "2026-09-10T06:00:00Z", value: 10, priorValue: 10 }),
      options()
    );

    expect(built.materiality.outcome).toBe("MaterialException");
    expect(built.materiality.breaches.map((breach) => breach.dimension)).toContain("sourceFreshness");
  });
});

describe("buildReportingDatum block state", () => {
  it("derives Changed for a moved value rather than defaulting to Live", () => {
    const built = buildReportingDatum(datum({ value: 101, priorValue: 100 }), options());

    expect(built.blockState).toBe("Changed");
  });

  it("derives Live for a settled value", () => {
    expect(buildReportingDatum(datum({ value: 100, priorValue: 100 }), options()).blockState).toBe("Live");
  });

  it("derives Missing when there is no value", () => {
    const built = buildReportingDatum(datum({ value: null }), options());

    expect(built.blockState).toBe("Missing");
    expect(built.displayValue).toBe("—");
  });

  it.each([
    ["Overridden", "Overridden"],
    ["Stale", "Stale"],
    ["Exception", "Blocked"]
  ])("maps the %s data state to the %s block state", (dataState, expected) => {
    expect(buildReportingDatum(datum({ dataState }), options()).blockState).toBe(expected);
  });

  it("honours an explicitly declared block state over the derived one", () => {
    const built = buildReportingDatum(
      datum({ value: 101, priorValue: 100, blockState: "Snapshot" }),
      options()
    );

    expect(built.blockState).toBe("Snapshot");
  });

  it("takes the worse of the data-state and block-state severities", () => {
    const built = buildReportingDatum(datum({ dataState: "Exception" }), options());

    expect(built.severity).toBe("blocked");
    expect(describeDatumBlockState(built).label).toBe("Blocked");
  });
});

describe("buildCoverageField", () => {
  it("renders one mark per member for a small population", () => {
    const field = buildCoverageField(8, 10, { noun: "sections", qualifier: "complete" });

    expect(field.marks).toHaveLength(10);
    expect(field.marks.filter((mark) => mark === "filled")).toHaveLength(8);
    expect(field.isProportional).toBe(false);
    expect(field.label).toBe("8 of 10 complete");
    expect(field.accessibleLabel).toBe("8 of 10 sections complete.");
  });

  it("renders a percentage label when asked", () => {
    const field = buildCoverageField(9, 10, { style: "percent", qualifier: "current" });

    expect(field.label).toBe("90% current");
  });

  it("caps the marks for a large population and says so", () => {
    const field = buildCoverageField(450, 500, { noun: "elements" });

    expect(field.marks).toHaveLength(COVERAGE_FIELD_MAX_MARKS);
    expect(field.isProportional).toBe(true);
    expect(field.label).toBe("450 of 500");
    expect(field.filled).toBe(450);
  });

  it("never shows a full field for an incomplete population", () => {
    // 499/500 rounds to 19.96 marks; rounding up would render as finished.
    const field = buildCoverageField(499, 500);

    expect(field.marks.filter((mark) => mark === "empty")).toHaveLength(1);
  });

  it("shows a full field only when the population is genuinely complete", () => {
    const field = buildCoverageField(500, 500);

    expect(field.marks.every((mark) => mark === "filled")).toBe(true);
  });

  it("clamps a filled count that exceeds the population", () => {
    const field = buildCoverageField(12, 10);

    expect(field.filled).toBe(10);
    expect(field.ratio).toBe(1);
  });

  it("reports an empty population rather than dividing by zero", () => {
    const field = buildCoverageField(0, 0, { noun: "sources" });

    expect(field.ratio).toBeNull();
    expect(field.marks).toEqual([]);
    expect(field.label).toBe("No population");
    expect(field.accessibleLabel).toBe("No sources to report.");
  });

  it("treats missing counts as an empty population", () => {
    expect(buildCoverageField(null, null).ratio).toBeNull();
    expect(buildCoverageField(5, undefined).total).toBe(0);
  });
});
