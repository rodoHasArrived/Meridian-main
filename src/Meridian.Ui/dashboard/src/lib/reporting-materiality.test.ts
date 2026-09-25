import { describe, expect, it } from "vitest";
import {
  assessMissingPositions,
  assessSourceFreshness,
  assessVariance,
  combineMaterialityAssessments,
  defaultMaterialityPolicyForClass,
  resolveMaterialityPolicy,
  type MaterialityPolicy
} from "@/lib/reporting-materiality";

const POLICY: MaterialityPolicy = {
  absoluteVariance: 100_000,
  portfolioPercent: 0.05,
  performanceImpactBasisPoints: 1,
  missingPositionsPercent: 0.1,
  sourceFreshnessHours: 24
};

const UNGOVERNED: MaterialityPolicy = {
  absoluteVariance: null,
  portfolioPercent: null,
  performanceImpactBasisPoints: null,
  missingPositionsPercent: null,
  sourceFreshnessHours: null
};

describe("assessVariance", () => {
  it("separates a tolerable difference from a material one", () => {
    const small = assessVariance({ variance: 847 }, POLICY);
    const large = assessVariance({ variance: 284_711 }, POLICY);

    expect(small.outcome).toBe("WithinTolerance");
    expect(small.label).toBe("Within tolerance");
    expect(large.outcome).toBe("MaterialException");
    expect(large.reason).toContain("$284,711");
    expect(large.reason).toContain("$100,000");
  });

  it("admits a difference exactly equal to the threshold", () => {
    expect(assessVariance({ variance: 100_000 }, POLICY).outcome).toBe("WithinTolerance");
    expect(assessVariance({ variance: 100_000.01 }, POLICY).outcome).toBe("MaterialException");
  });

  it("ignores the sign of the difference", () => {
    expect(assessVariance({ variance: -284_711 }, POLICY).outcome).toBe("MaterialException");
  });

  it("reports NotAssessed - never a pass - when no threshold can be applied", () => {
    const assessment = assessVariance({ variance: 5_000_000 }, UNGOVERNED);

    expect(assessment.outcome).toBe("NotAssessed");
    expect(assessment.severity).toBe("review");
    expect(assessment.notAssessed).toEqual([
      "absoluteVariance",
      "portfolioPercent",
      "performanceImpact"
    ]);
  });

  it("does not treat a missing variance as within tolerance", () => {
    expect(assessVariance({ variance: null }, POLICY).outcome).toBe("NotAssessed");
    expect(assessVariance({ variance: Number.NaN }, POLICY).outcome).toBe("NotAssessed");
  });

  it("breaches on portfolio percentage even when the absolute amount is tolerable", () => {
    // $50,000 is under the $100,000 absolute threshold but is 0.5% of a $10M book.
    const assessment = assessVariance({ variance: 50_000, portfolioValue: 10_000_000 }, POLICY);

    expect(assessment.outcome).toBe("MaterialException");
    expect(assessment.breaches.map((breach) => breach.dimension)).toEqual(["portfolioPercent"]);
    expect(assessment.breaches[0]?.observedLabel).toBe("0.5%");
  });

  it("breaches on performance impact independently of the amount", () => {
    const assessment = assessVariance({ variance: 500, performanceImpactBasisPoints: 4 }, POLICY);

    expect(assessment.outcome).toBe("MaterialException");
    expect(assessment.breaches.map((breach) => breach.dimension)).toEqual(["performanceImpact"]);
  });

  it("skips the portfolio test when the denominator is zero rather than dividing", () => {
    const assessment = assessVariance({ variance: 500, portfolioValue: 0 }, POLICY);

    expect(assessment.outcome).toBe("WithinTolerance");
    expect(assessment.notAssessed).toContain("portfolioPercent");
    expect(assessment.reason).toContain("not assessed");
  });

  it("orders breaches by policy dimension and counts the remainder", () => {
    const assessment = assessVariance(
      { variance: 900_000, portfolioValue: 10_000_000, performanceImpactBasisPoints: 40 },
      POLICY
    );

    expect(assessment.breaches.map((breach) => breach.dimension)).toEqual([
      "absoluteVariance",
      "portfolioPercent",
      "performanceImpact"
    ]);
    expect(assessment.reason).toContain("2 further thresholds breached");
  });
});

describe("assessMissingPositions", () => {
  it("passes within the tolerated share", () => {
    expect(assessMissingPositions({ missingCount: 1, totalCount: 2_000 }, POLICY).outcome).toBe(
      "WithinTolerance"
    );
  });

  it("breaches above the tolerated share", () => {
    const assessment = assessMissingPositions({ missingCount: 5, totalCount: 1_000 }, POLICY);

    expect(assessment.outcome).toBe("MaterialException");
    expect(assessment.breaches[0]?.observedLabel).toBe("0.5%");
  });

  it("treats a zero-tolerance policy as admitting nothing missing", () => {
    const regulatory = defaultMaterialityPolicyForClass("Regulatory");

    expect(regulatory.missingPositionsPercent).toBe(0);
    expect(assessMissingPositions({ missingCount: 0, totalCount: 500 }, regulatory).outcome).toBe(
      "WithinTolerance"
    );
    expect(assessMissingPositions({ missingCount: 1, totalCount: 500 }, regulatory).outcome).toBe(
      "MaterialException"
    );
  });

  it("does not report a pass for an empty population", () => {
    expect(assessMissingPositions({ missingCount: 0, totalCount: 0 }, POLICY).outcome).toBe("NotAssessed");
  });
});

describe("assessSourceFreshness", () => {
  const evaluatedAtUtc = "2026-09-15T12:00:00Z";

  it("passes a source inside the freshness window", () => {
    const assessment = assessSourceFreshness(
      { asOfUtc: "2026-09-14T18:00:00Z", evaluatedAtUtc },
      POLICY
    );

    expect(assessment.outcome).toBe("WithinTolerance");
  });

  it("breaches a source beyond the freshness window", () => {
    const assessment = assessSourceFreshness(
      { asOfUtc: "2026-09-13T06:00:00Z", evaluatedAtUtc },
      POLICY
    );

    expect(assessment.outcome).toBe("MaterialException");
    expect(assessment.breaches[0]?.observedLabel).toBe("54 hr");
    expect(assessment.breaches[0]?.thresholdLabel).toBe("24 hr");
  });

  it("refuses to call a future timestamp fresh", () => {
    const assessment = assessSourceFreshness(
      { asOfUtc: "2026-09-16T12:00:00Z", evaluatedAtUtc },
      POLICY
    );

    expect(assessment.outcome).toBe("NotAssessed");
  });

  it("reports NotAssessed for an unparseable or absent timestamp", () => {
    expect(assessSourceFreshness({ asOfUtc: null, evaluatedAtUtc }, POLICY).outcome).toBe("NotAssessed");
    expect(assessSourceFreshness({ asOfUtc: "CurrentMonth", evaluatedAtUtc }, POLICY).outcome).toBe(
      "NotAssessed"
    );
  });
});

describe("resolveMaterialityPolicy", () => {
  it("distinguishes an omitted override from an explicit ungoverning", () => {
    const kept = resolveMaterialityPolicy(POLICY, { portfolioPercent: 0.2 });
    const ungoverned = resolveMaterialityPolicy(POLICY, { absoluteVariance: null });

    expect(kept.absoluteVariance).toBe(100_000);
    expect(kept.portfolioPercent).toBe(0.2);
    expect(ungoverned.absoluteVariance).toBeNull();
    expect(ungoverned.portfolioPercent).toBe(0.05);
  });

  it("returns the base policy untouched when there are no overrides", () => {
    expect(resolveMaterialityPolicy(POLICY, null)).toBe(POLICY);
  });
});

describe("defaultMaterialityPolicyForClass", () => {
  it("governs regulatory output more tightly than analytical output", () => {
    const regulatory = defaultMaterialityPolicyForClass("Regulatory");
    const analytical = defaultMaterialityPolicyForClass("Analytical");

    expect(regulatory.absoluteVariance as number).toBeLessThan(analytical.absoluteVariance as number);
    expect(regulatory.sourceFreshnessHours as number).toBeLessThan(
      analytical.sourceFreshnessHours as number
    );
  });

  it("falls back to the conservative accounting policy for an unknown class", () => {
    expect(defaultMaterialityPolicyForClass("something-new")).toEqual(
      defaultMaterialityPolicyForClass("Accounting")
    );
  });
});

describe("combineMaterialityAssessments", () => {
  it("keeps the worst outcome", () => {
    const combined = combineMaterialityAssessments([
      assessVariance({ variance: 10 }, POLICY),
      assessMissingPositions({ missingCount: 5, totalCount: 1_000 }, POLICY)
    ]);

    expect(combined.outcome).toBe("MaterialException");
  });

  it("does not let one applied threshold clear an unassessed one", () => {
    const combined = combineMaterialityAssessments([
      assessVariance({ variance: 10 }, POLICY),
      assessSourceFreshness({ asOfUtc: null }, POLICY)
    ]);

    expect(combined.outcome).toBe("WithinTolerance");
    expect(combined.notAssessed).toContain("sourceFreshness");
    expect(combined.reason).toContain("source freshness not assessed");
  });

  it("reports NotAssessed when nothing at all was measured", () => {
    const combined = combineMaterialityAssessments([
      assessVariance({ variance: null }, UNGOVERNED),
      assessSourceFreshness({ asOfUtc: null }, UNGOVERNED)
    ]);

    expect(combined.outcome).toBe("NotAssessed");
    expect(combined.severity).toBe("review");
  });

  it("reports NotAssessed for an empty list rather than a pass", () => {
    expect(combineMaterialityAssessments([]).outcome).toBe("NotAssessed");
  });
});
