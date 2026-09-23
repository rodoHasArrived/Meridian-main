/**
 * Explicit materiality policy for governed reporting.
 *
 * Without a policy, "material" is an upstream boolean somebody set by hand, and a
 * difference is either flagged or invisible. A policy makes the judgement explicit
 * and reproducible: a $847 difference is within tolerance, a $284,711 difference is
 * a material exception, and both statements can be traced to a stated threshold.
 *
 * The central safety property is that **an unassessable difference is not within
 * tolerance**. If no threshold governs a dimension, or the inputs needed to apply it
 * are missing, the outcome is `NotAssessed` - which carries a `review` severity and
 * never reads as cleared. Silence has to look different from a pass, because the two
 * mean opposite things to a preparer deciding whether to publish.
 *
 * Thresholds are compared with `>` rather than `>=`: a difference exactly equal to
 * the stated tolerance is within it. A threshold of $100,000 admits $100,000.
 */
import { formatCurrency, formatNumber } from "@/lib/format";
import { normalizeReportClass, type ReportClass } from "@/lib/reporting-lifecycle";
import type { DesignSystemSeverity } from "@/design-system/status";

/** The dimensions a materiality policy can govern. */
export const MATERIALITY_DIMENSIONS = [
  "absoluteVariance",
  "portfolioPercent",
  "performanceImpact",
  "missingPositions",
  "sourceFreshness"
] as const;

export type MaterialityDimension = typeof MATERIALITY_DIMENSIONS[number];

/** The dimensions {@link assessVariance} can evaluate from a single difference. */
const VARIANCE_DIMENSIONS: readonly MaterialityDimension[] = [
  "absoluteVariance",
  "portfolioPercent",
  "performanceImpact"
];

export const MATERIALITY_DIMENSION_LABELS: Record<MaterialityDimension, string> = {
  absoluteVariance: "Absolute variance",
  portfolioPercent: "Portfolio percentage",
  performanceImpact: "Performance impact",
  missingPositions: "Missing positions",
  sourceFreshness: "Source freshness"
};

/**
 * A report- or template-level materiality policy.
 *
 * Every threshold is independently optional. A `null` threshold means the dimension
 * is ungoverned, which yields `NotAssessed` rather than an implicit pass.
 */
export interface MaterialityPolicy {
  /** Currency amount, e.g. 100_000 for "$100,000". */
  absoluteVariance: number | null;
  /** Percent of portfolio value, e.g. 0.05 for "0.05%". */
  portfolioPercent: number | null;
  /** Performance impact in basis points, e.g. 1 for "1 bp". */
  performanceImpactBasisPoints: number | null;
  /** Share of positions permitted to be missing, e.g. 0.1 for "0.10%". */
  missingPositionsPercent: number | null;
  /** Age a source may reach before it is stale, e.g. 24 for "24 hr". */
  sourceFreshnessHours: number | null;
}

export type MaterialityOutcome = "WithinTolerance" | "MaterialException" | "NotAssessed";

const OUTCOME_SEVERITY: Record<MaterialityOutcome, DesignSystemSeverity> = {
  WithinTolerance: "ready",
  MaterialException: "action",
  // Not a pass. An unassessed dimension needs a preparer to look at it.
  NotAssessed: "review"
};

const OUTCOME_LABEL: Record<MaterialityOutcome, string> = {
  WithinTolerance: "Within tolerance",
  MaterialException: "Material exception",
  NotAssessed: "Not assessed"
};

export interface MaterialityBreach {
  dimension: MaterialityDimension;
  dimensionLabel: string;
  /** The observed magnitude, in the dimension's own unit. */
  observed: number;
  threshold: number;
  observedLabel: string;
  thresholdLabel: string;
}

export interface MaterialityAssessment {
  outcome: MaterialityOutcome;
  severity: DesignSystemSeverity;
  /** "Within tolerance" / "Material exception" / "Not assessed". */
  label: string;
  /** One sentence naming the governing threshold, suitable for a tooltip or row. */
  reason: string;
  /** Every threshold the observation exceeded, in policy order. */
  breaches: readonly MaterialityBreach[];
  /** Dimensions that could not be evaluated, in policy order. */
  notAssessed: readonly MaterialityDimension[];
}

/**
 * Default policies by report class.
 *
 * Regulatory and accounting output is governed tightly; analytical output is
 * deliberately looser because it is not a publication of record. Management
 * reporting sits between the two. These are starting points a template overrides,
 * not assertions about any particular filing's requirements.
 */
export const CLASS_MATERIALITY_POLICIES: Record<ReportClass, MaterialityPolicy> = {
  Regulatory: {
    absoluteVariance: 10_000,
    portfolioPercent: 0.01,
    performanceImpactBasisPoints: 1,
    missingPositionsPercent: 0,
    sourceFreshnessHours: 24
  },
  Accounting: {
    absoluteVariance: 100_000,
    portfolioPercent: 0.05,
    performanceImpactBasisPoints: 1,
    missingPositionsPercent: 0.1,
    sourceFreshnessHours: 24
  },
  Portfolio: {
    absoluteVariance: 250_000,
    portfolioPercent: 0.1,
    performanceImpactBasisPoints: 5,
    missingPositionsPercent: 0.5,
    sourceFreshnessHours: 48
  },
  Management: {
    absoluteVariance: 500_000,
    portfolioPercent: 0.25,
    performanceImpactBasisPoints: 10,
    missingPositionsPercent: 1,
    sourceFreshnessHours: 72
  },
  Analytical: {
    absoluteVariance: 1_000_000,
    portfolioPercent: 0.5,
    performanceImpactBasisPoints: 25,
    missingPositionsPercent: 2,
    sourceFreshnessHours: 168
  }
};

/** The policy governing a report class, defaulting conservatively for unknown classes. */
export function defaultMaterialityPolicyForClass(value: string | null | undefined): MaterialityPolicy {
  return CLASS_MATERIALITY_POLICIES[normalizeReportClass(value)];
}

/**
 * Overlay template overrides on a base policy.
 *
 * An override of `null` deliberately ungoverns a dimension; omitting the key leaves
 * the base threshold in place. The two are different intents, so `undefined` and
 * `null` are not conflated.
 */
export function resolveMaterialityPolicy(
  base: MaterialityPolicy,
  overrides?: Partial<MaterialityPolicy> | null
): MaterialityPolicy {
  if (!overrides) {
    return base;
  }

  return {
    absoluteVariance:
      "absoluteVariance" in overrides ? overrides.absoluteVariance ?? null : base.absoluteVariance,
    portfolioPercent:
      "portfolioPercent" in overrides ? overrides.portfolioPercent ?? null : base.portfolioPercent,
    performanceImpactBasisPoints:
      "performanceImpactBasisPoints" in overrides
        ? overrides.performanceImpactBasisPoints ?? null
        : base.performanceImpactBasisPoints,
    missingPositionsPercent:
      "missingPositionsPercent" in overrides
        ? overrides.missingPositionsPercent ?? null
        : base.missingPositionsPercent,
    sourceFreshnessHours:
      "sourceFreshnessHours" in overrides ? overrides.sourceFreshnessHours ?? null : base.sourceFreshnessHours
  };
}

function isUsableNumber(value: number | null | undefined): value is number {
  return typeof value === "number" && Number.isFinite(value);
}

function isUsableThreshold(value: number | null | undefined): value is number {
  return isUsableNumber(value) && value >= 0;
}

function formatBasisPoints(value: number): string {
  return `${formatNumber(value, { maximumFractionDigits: 2 })} bp`;
}

function formatPercentPoints(value: number): string {
  return `${formatNumber(value, { maximumFractionDigits: 4 })}%`;
}

function formatHours(value: number): string {
  return `${formatNumber(value, { maximumFractionDigits: 1 })} hr`;
}

export interface VarianceAssessmentInput {
  /** Difference between the compared values; sign is ignored. */
  variance: number | null | undefined;
  /** Denominator for the portfolio-percentage test, in the same unit as `variance`. */
  portfolioValue?: number | null;
  /** Performance impact of the variance, in basis points; sign is ignored. */
  performanceImpactBasisPoints?: number | null;
}

/**
 * Assess a variance against every dimension of a policy that can be applied.
 *
 * Any single breach makes the assessment a material exception - dimensions are not
 * averaged or voted on, because they measure genuinely different consequences. A
 * variance that is trivial in dollars can still move performance past its threshold.
 */
export function assessVariance(input: VarianceAssessmentInput, policy: MaterialityPolicy): MaterialityAssessment {
  const breaches: MaterialityBreach[] = [];
  const notAssessed: MaterialityDimension[] = [];
  const hasVariance = isUsableNumber(input.variance);
  const magnitude = hasVariance ? Math.abs(input.variance as number) : 0;

  if (!hasVariance || !isUsableThreshold(policy.absoluteVariance)) {
    notAssessed.push("absoluteVariance");
  } else if (magnitude > policy.absoluteVariance) {
    breaches.push({
      dimension: "absoluteVariance",
      dimensionLabel: MATERIALITY_DIMENSION_LABELS.absoluteVariance,
      observed: magnitude,
      threshold: policy.absoluteVariance,
      observedLabel: formatCurrency(magnitude, { maximumFractionDigits: 0 }),
      thresholdLabel: formatCurrency(policy.absoluteVariance, { maximumFractionDigits: 0 })
    });
  }

  const portfolioValue = input.portfolioValue;
  const canTestPortfolio =
    hasVariance &&
    isUsableThreshold(policy.portfolioPercent) &&
    isUsableNumber(portfolioValue) &&
    Math.abs(portfolioValue) > 0;

  if (!canTestPortfolio) {
    notAssessed.push("portfolioPercent");
  } else {
    const observedPercent = (magnitude / Math.abs(portfolioValue as number)) * 100;
    if (observedPercent > (policy.portfolioPercent as number)) {
      breaches.push({
        dimension: "portfolioPercent",
        dimensionLabel: MATERIALITY_DIMENSION_LABELS.portfolioPercent,
        observed: observedPercent,
        threshold: policy.portfolioPercent as number,
        observedLabel: formatPercentPoints(observedPercent),
        thresholdLabel: formatPercentPoints(policy.portfolioPercent as number)
      });
    }
  }

  const impact = input.performanceImpactBasisPoints;
  if (!isUsableNumber(impact) || !isUsableThreshold(policy.performanceImpactBasisPoints)) {
    notAssessed.push("performanceImpact");
  } else {
    const observedImpact = Math.abs(impact);
    if (observedImpact > policy.performanceImpactBasisPoints) {
      breaches.push({
        dimension: "performanceImpact",
        dimensionLabel: MATERIALITY_DIMENSION_LABELS.performanceImpact,
        observed: observedImpact,
        threshold: policy.performanceImpactBasisPoints,
        observedLabel: formatBasisPoints(observedImpact),
        thresholdLabel: formatBasisPoints(policy.performanceImpactBasisPoints)
      });
    }
  }

  // Only the three variance dimensions are in play here, so "something was assessed"
  // is measured against those - not against the full policy. Counting all five would
  // let a wholly unassessable variance report as within tolerance.
  return finalize(breaches, notAssessed, notAssessed.length < VARIANCE_DIMENSIONS.length);
}

export interface MissingPositionsInput {
  missingCount: number | null | undefined;
  totalCount: number | null | undefined;
}

/** Assess missing-position coverage against the policy's tolerance. */
export function assessMissingPositions(
  input: MissingPositionsInput,
  policy: MaterialityPolicy
): MaterialityAssessment {
  const { missingCount, totalCount } = input;
  const canTest =
    isUsableNumber(missingCount) &&
    isUsableNumber(totalCount) &&
    totalCount > 0 &&
    missingCount >= 0 &&
    isUsableThreshold(policy.missingPositionsPercent);

  if (!canTest) {
    return finalize([], ["missingPositions"], false);
  }

  const observedPercent = ((missingCount as number) / (totalCount as number)) * 100;
  const threshold = policy.missingPositionsPercent as number;
  if (observedPercent <= threshold) {
    return finalize([], [], true);
  }

  return finalize(
    [
      {
        dimension: "missingPositions",
        dimensionLabel: MATERIALITY_DIMENSION_LABELS.missingPositions,
        observed: observedPercent,
        threshold,
        observedLabel: formatPercentPoints(observedPercent),
        thresholdLabel: formatPercentPoints(threshold)
      }
    ],
    [],
    true
  );
}

export interface SourceFreshnessInput {
  /** When the source last produced the value. */
  asOfUtc: string | null | undefined;
  /** Evaluation instant; defaults to now. */
  evaluatedAtUtc?: string | Date | null;
}

/**
 * Assess how stale a source is against the policy's freshness tolerance.
 *
 * A source timestamp in the future is not treated as maximally fresh - it means the
 * clocks disagree, so the age cannot be trusted and the dimension reports
 * `NotAssessed` rather than a pass.
 */
export function assessSourceFreshness(
  input: SourceFreshnessInput,
  policy: MaterialityPolicy
): MaterialityAssessment {
  const asOf = parseInstant(input.asOfUtc);
  const evaluatedAt = parseInstant(input.evaluatedAtUtc) ?? Date.now();

  if (asOf === null || !isUsableThreshold(policy.sourceFreshnessHours)) {
    return finalize([], ["sourceFreshness"], false);
  }

  const ageHours = (evaluatedAt - asOf) / 3_600_000;
  if (ageHours < 0) {
    return finalize([], ["sourceFreshness"], false);
  }

  const threshold = policy.sourceFreshnessHours;
  if (ageHours <= threshold) {
    return finalize([], [], true);
  }

  return finalize(
    [
      {
        dimension: "sourceFreshness",
        dimensionLabel: MATERIALITY_DIMENSION_LABELS.sourceFreshness,
        observed: ageHours,
        threshold,
        observedLabel: formatHours(ageHours),
        thresholdLabel: formatHours(threshold)
      }
    ],
    [],
    true
  );
}

/**
 * Combine assessments, keeping the worst outcome.
 *
 * `MaterialException` dominates `NotAssessed`, which dominates `WithinTolerance`. An
 * empty list is `NotAssessed`: nothing was measured, so nothing was cleared.
 */
export function combineMaterialityAssessments(
  assessments: readonly MaterialityAssessment[]
): MaterialityAssessment {
  const breaches = assessments.flatMap((assessment) => assessment.breaches);
  const notAssessed = assessments.flatMap((assessment) => assessment.notAssessed);
  const anyAssessed = assessments.some((assessment) => assessment.outcome !== "NotAssessed");
  return finalize(breaches, dedupeDimensions(notAssessed), anyAssessed);
}

function dedupeDimensions(values: readonly MaterialityDimension[]): MaterialityDimension[] {
  return MATERIALITY_DIMENSIONS.filter((dimension) => values.includes(dimension));
}

function finalize(
  breaches: readonly MaterialityBreach[],
  notAssessed: readonly MaterialityDimension[],
  anyAssessed: boolean
): MaterialityAssessment {
  const ordered = MATERIALITY_DIMENSIONS.flatMap((dimension) =>
    breaches.filter((breach) => breach.dimension === dimension)
  );

  const outcome: MaterialityOutcome = ordered.length > 0
    ? "MaterialException"
    : anyAssessed
      ? "WithinTolerance"
      : "NotAssessed";

  return {
    outcome,
    severity: OUTCOME_SEVERITY[outcome],
    label: OUTCOME_LABEL[outcome],
    reason: describe(outcome, ordered, notAssessed),
    breaches: ordered,
    notAssessed: dedupeDimensions(notAssessed)
  };
}

function describe(
  outcome: MaterialityOutcome,
  breaches: readonly MaterialityBreach[],
  notAssessed: readonly MaterialityDimension[]
): string {
  if (outcome === "MaterialException") {
    const first = breaches[0] as MaterialityBreach;
    const rest = breaches.length - 1;
    const primary = `${first.dimensionLabel} ${first.observedLabel} exceeds ${first.thresholdLabel}.`;
    return rest > 0 ? `${primary} ${rest} further threshold${rest === 1 ? "" : "s"} breached.` : primary;
  }

  if (outcome === "NotAssessed") {
    const names = notAssessed.map((dimension) => MATERIALITY_DIMENSION_LABELS[dimension].toLowerCase());
    return names.length > 0
      ? `No materiality threshold could be applied (${names.join(", ")}).`
      : "No materiality threshold could be applied.";
  }

  if (notAssessed.length > 0) {
    const names = notAssessed.map((dimension) => MATERIALITY_DIMENSION_LABELS[dimension].toLowerCase());
    return `Within every applied threshold; ${names.join(", ")} not assessed.`;
  }

  return "Within every applied threshold.";
}

function parseInstant(value: string | Date | null | undefined): number | null {
  if (value instanceof Date) {
    const time = value.getTime();
    return Number.isNaN(time) ? null : time;
  }

  const trimmed = value?.trim() ?? "";
  if (!trimmed) {
    return null;
  }

  const parsed = Date.parse(trimmed);
  return Number.isNaN(parsed) ? null : parsed;
}
