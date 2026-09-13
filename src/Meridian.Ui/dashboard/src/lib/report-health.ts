/**
 * Report health, source coverage, and publication gates.
 *
 * A single "status" on a report is too lossy to act on: content can be complete
 * while a reconciliation fails, and a report that is 99% ready on every dimension
 * may still be unpublishable because the 1% is a material break.
 *
 * So health is modelled on two levels:
 *
 *  - **Dimensions** (content, data, reconciliation, commentary, review, approval)
 *    report progress, and are purely informational.
 *  - **Gates** decide publication, and are evaluated independently. The overall
 *    state is the worst gate outcome - never an average of the dimensions - so a
 *    single material failure cannot be diluted by everything else being green.
 *
 * `buildSourceCoverage` supplies the data dimension from report elements, counting
 * every element by its {@link ReportingDataState}.
 */
import {
  normalizeReportingControlState,
  normalizeReportingDataState,
  normalizeReportClass,
  type ReportClass,
  type ReportingControlState,
  type ReportingDataState
} from "@/lib/reporting-lifecycle";
import type { DesignSystemSeverity } from "@/design-system/status";

export interface ReportElementInput {
  elementId: string;
  label: string;
  /** The governed system the value resolved from, e.g. "Accounting Ledger". */
  source: string;
  dataState: string;
  controlState?: string | null;
  /** Critical elements block publication when missing or in exception. */
  isCritical?: boolean | null;
  /** True when the element carries a material open exception. */
  hasMaterialException?: boolean | null;
  sectionId?: string | null;
}

export interface SourceCoverageBreakdownRow {
  source: string;
  elementCount: number;
  currentCount: number;
  sharePercent: number;
}

export interface SourceCoverageModel {
  totalElements: number;
  /** Counts keyed by the controlled data-state vocabulary. */
  stateCounts: Record<ReportingDataState, number>;
  currentCount: number;
  staleCount: number;
  overriddenCount: number;
  missingCount: number;
  exceptionCount: number;
  criticalMissingCount: number;
  /** Exceptions the template flagged material, or attached to a critical element. */
  materialExceptionCount: number;
  /**
   * Share of elements that resolved to a usable value. Stale and overridden
   * elements still carry a value and count as covered; missing ones do not.
   */
  coveragePercent: number;
  bySource: SourceCoverageBreakdownRow[];
  summaryLabel: string;
}

const EMPTY_STATE_COUNTS: Record<ReportingDataState, number> = {
  Confirmed: 0,
  Provisional: 0,
  Estimated: 0,
  Stale: 0,
  Overridden: 0,
  Missing: 0,
  Exception: 0
};

function roundPercent(value: number): number {
  return Math.round(value * 10) / 10;
}

function percentOf(part: number, total: number): number {
  return total <= 0 ? 0 : roundPercent((part / total) * 100);
}

/** Counts report elements by data state and by source, deriving source coverage. */
export function buildSourceCoverage(elements: readonly ReportElementInput[]): SourceCoverageModel {
  const stateCounts = { ...EMPTY_STATE_COUNTS };
  const sourceTotals = new Map<string, { elementCount: number; currentCount: number }>();
  let criticalMissingCount = 0;
  let materialExceptionCount = 0;

  for (const element of elements) {
    const state = normalizeReportingDataState(element.dataState);
    stateCounts[state] += 1;

    if (element.isCritical === true && (state === "Missing" || state === "Exception")) {
      criticalMissingCount += 1;
    }

    // Materiality is explicit: an exception counts as material when the template
    // says so, or when it sits on an element the template marked critical.
    if (state === "Exception" && (element.hasMaterialException === true || element.isCritical === true)) {
      materialExceptionCount += 1;
    }

    const source = element.source?.trim() || "Unattributed";
    const bucket = sourceTotals.get(source) ?? { elementCount: 0, currentCount: 0 };
    bucket.elementCount += 1;
    if (state === "Confirmed") {
      bucket.currentCount += 1;
    }
    sourceTotals.set(source, bucket);
  }

  const totalElements = elements.length;
  const missingCount = stateCounts.Missing;
  const coveragePercent = percentOf(totalElements - missingCount, totalElements);

  const bySource = [...sourceTotals.entries()]
    .map(([source, bucket]) => ({
      source,
      elementCount: bucket.elementCount,
      currentCount: bucket.currentCount,
      sharePercent: percentOf(bucket.elementCount, totalElements)
    }))
    .sort((left, right) => right.elementCount - left.elementCount || left.source.localeCompare(right.source));

  return {
    totalElements,
    stateCounts,
    currentCount: stateCounts.Confirmed,
    staleCount: stateCounts.Stale,
    overriddenCount: stateCounts.Overridden,
    missingCount,
    exceptionCount: stateCounts.Exception,
    criticalMissingCount,
    materialExceptionCount,
    coveragePercent,
    bySource,
    summaryLabel: totalElements === 0
      ? "No report elements"
      : `${stateCounts.Confirmed} of ${totalElements} current · ${coveragePercent}% coverage`
  };
}

export const REPORT_HEALTH_DIMENSIONS = [
  "content",
  "data",
  "reconciliation",
  "commentary",
  "review",
  "approval"
] as const;
export type ReportHealthDimensionKey = typeof REPORT_HEALTH_DIMENSIONS[number];

const DIMENSION_LABELS: Record<ReportHealthDimensionKey, string> = {
  content: "Content",
  data: "Data",
  reconciliation: "Reconciliation",
  commentary: "Commentary",
  review: "Review",
  approval: "Approval"
};

export interface ReportHealthDimension {
  key: ReportHealthDimensionKey;
  label: string;
  /** Null when the dimension does not yet apply, rendered as an em dash. */
  percent: number | null;
  valueLabel: string;
  severity: DesignSystemSeverity;
  detail: string;
}

/** Thresholds a report must clear before it can be published. */
export interface ReportPublicationGatePolicy {
  minimumDataCoveragePercent: number;
  maximumCriticalMissing: number;
  requireMaterialReconciliation: boolean;
  maximumOpenCriticalExceptions: number;
  requireAllRequiredSections: boolean;
  requireReviewComplete: boolean;
  requireApproval: boolean;
}

/**
 * Default gate policy by report class. A statutory schedule and an ad-hoc
 * analytical note should not be held to the same bar.
 */
const CLASS_GATE_POLICIES: Record<ReportClass, ReportPublicationGatePolicy> = {
  Accounting: {
    minimumDataCoveragePercent: 98,
    maximumCriticalMissing: 0,
    requireMaterialReconciliation: true,
    maximumOpenCriticalExceptions: 0,
    requireAllRequiredSections: true,
    requireReviewComplete: true,
    requireApproval: true
  },
  Regulatory: {
    minimumDataCoveragePercent: 100,
    maximumCriticalMissing: 0,
    requireMaterialReconciliation: true,
    maximumOpenCriticalExceptions: 0,
    requireAllRequiredSections: true,
    requireReviewComplete: true,
    requireApproval: true
  },
  Portfolio: {
    minimumDataCoveragePercent: 95,
    maximumCriticalMissing: 0,
    requireMaterialReconciliation: false,
    maximumOpenCriticalExceptions: 0,
    requireAllRequiredSections: true,
    requireReviewComplete: true,
    requireApproval: true
  },
  Management: {
    minimumDataCoveragePercent: 95,
    maximumCriticalMissing: 0,
    requireMaterialReconciliation: false,
    maximumOpenCriticalExceptions: 1,
    requireAllRequiredSections: true,
    requireReviewComplete: true,
    requireApproval: true
  },
  Analytical: {
    minimumDataCoveragePercent: 90,
    maximumCriticalMissing: 0,
    requireMaterialReconciliation: false,
    maximumOpenCriticalExceptions: 2,
    requireAllRequiredSections: false,
    requireReviewComplete: false,
    requireApproval: false
  }
};

/** Returns the default publication gate policy for a report class or family. */
export function defaultGatePolicyForClass(value: string | null | undefined): ReportPublicationGatePolicy {
  return { ...CLASS_GATE_POLICIES[normalizeReportClass(value)] };
}

export type ReportGateStatus = "Passed" | "Failed" | "Pending" | "NotApplicable";

export interface ReportPublicationGate {
  key: string;
  label: string;
  requirementLabel: string;
  actualLabel: string;
  status: ReportGateStatus;
  severity: DesignSystemSeverity;
  /** Whether failing this gate blocks publication. */
  blocking: boolean;
  reason: string | null;
}

export interface ReportHealthInput {
  reportClass?: string | null;
  coverage: SourceCoverageModel;
  requiredSectionCount: number;
  completeSectionCount: number;
  requiredCommentaryCount: number;
  completeCommentaryCount: number;
  reviewedElementCount: number;
  /** Control results across the report's reconciliations. */
  controlStates?: readonly string[];
  /** Reconciliations flagged material by the template. */
  materialControlStates?: readonly string[];
  openCriticalExceptionCount?: number;
  isReviewComplete?: boolean;
  isApproved?: boolean;
  policy?: Partial<ReportPublicationGatePolicy> | null;
}

export interface ReportHealthModel {
  reportClass: ReportClass;
  dimensions: ReportHealthDimension[];
  gates: ReportPublicationGate[];
  /** True only when every blocking gate has passed. */
  canPublish: boolean;
  overallLabel: string;
  overallSeverity: DesignSystemSeverity;
  blockingReasons: string[];
  pendingReasons: string[];
  coverage: SourceCoverageModel;
}

const GATE_SEVERITY: Record<ReportGateStatus, DesignSystemSeverity> = {
  Passed: "ready",
  Failed: "blocked",
  Pending: "review",
  NotApplicable: "info"
};

function dimensionSeverity(percent: number | null, warnBelow: number): DesignSystemSeverity {
  if (percent === null) {
    return "info";
  }
  if (percent >= 100) {
    return "ready";
  }
  if (percent >= warnBelow) {
    return "action";
  }
  return "blocked";
}

function dimension(
  key: ReportHealthDimensionKey,
  percent: number | null,
  detail: string,
  warnBelow = 90
): ReportHealthDimension {
  return {
    key,
    label: DIMENSION_LABELS[key],
    percent,
    valueLabel: percent === null ? "—" : `${percent}%`,
    severity: dimensionSeverity(percent, warnBelow),
    detail
  };
}

function countPassing(states: readonly ReportingControlState[]): number {
  return states.filter((state) => state === "Passed" || state === "WithinTolerance").length;
}

function gate(
  key: string,
  label: string,
  requirementLabel: string,
  actualLabel: string,
  status: ReportGateStatus,
  blocking: boolean,
  reason: string | null = null
): ReportPublicationGate {
  return { key, label, requirementLabel, actualLabel, status, severity: GATE_SEVERITY[status], blocking, reason };
}

/**
 * Builds the multi-dimensional health model and evaluates publication gates.
 *
 * The overall state is derived from gates only: any failed blocking gate makes the
 * report unpublishable regardless of how complete the dimensions look.
 */
export function buildReportHealth(input: ReportHealthInput): ReportHealthModel {
  const reportClass = normalizeReportClass(input.reportClass);
  const policy: ReportPublicationGatePolicy = {
    ...CLASS_GATE_POLICIES[reportClass],
    ...(input.policy ?? {})
  };

  const coverage = input.coverage;
  const controlStates = (input.controlStates ?? []).map(normalizeReportingControlState);
  const materialControlStates = (input.materialControlStates ?? []).map(normalizeReportingControlState);
  const openCriticalExceptionCount = input.openCriticalExceptionCount ?? coverage.materialExceptionCount;

  const contentPercent = percentOf(input.completeSectionCount, input.requiredSectionCount);
  const commentaryPercent = input.requiredCommentaryCount === 0
    ? null
    : percentOf(input.completeCommentaryCount, input.requiredCommentaryCount);
  const reconciliationPercent = controlStates.length === 0
    ? null
    : percentOf(countPassing(controlStates), controlStates.length);
  const reviewPercent = coverage.totalElements === 0
    ? null
    : percentOf(input.reviewedElementCount, coverage.totalElements);
  const approvalPercent = input.isApproved === true ? 100 : null;

  const dimensions: ReportHealthDimension[] = [
    dimension("content", contentPercent, `${input.completeSectionCount} of ${input.requiredSectionCount} required sections complete`),
    dimension("data", coverage.coveragePercent, coverage.summaryLabel, policy.minimumDataCoveragePercent),
    dimension("reconciliation", reconciliationPercent, controlStates.length === 0
      ? "No controls defined"
      : `${countPassing(controlStates)} of ${controlStates.length} controls passing`),
    dimension("commentary", commentaryPercent, input.requiredCommentaryCount === 0
      ? "No commentary required"
      : `${input.completeCommentaryCount} of ${input.requiredCommentaryCount} commentary blocks complete`),
    dimension("review", reviewPercent, coverage.totalElements === 0
      ? "No elements to review"
      : `${input.reviewedElementCount} of ${coverage.totalElements} elements reviewed`),
    dimension("approval", approvalPercent, input.isApproved === true ? "Approved" : "Not yet approved")
  ];

  const gates: ReportPublicationGate[] = [];

  gates.push(gate(
    "dataCoverage",
    "Data coverage",
    `≥ ${policy.minimumDataCoveragePercent}%`,
    `${coverage.coveragePercent}%`,
    coverage.coveragePercent >= policy.minimumDataCoveragePercent ? "Passed" : "Failed",
    true,
    coverage.coveragePercent >= policy.minimumDataCoveragePercent
      ? null
      : `Data coverage ${coverage.coveragePercent}% is below the ${policy.minimumDataCoveragePercent}% threshold.`
  ));

  gates.push(gate(
    "criticalMissing",
    "Critical missing data",
    `≤ ${policy.maximumCriticalMissing}`,
    String(coverage.criticalMissingCount),
    coverage.criticalMissingCount <= policy.maximumCriticalMissing ? "Passed" : "Failed",
    true,
    coverage.criticalMissingCount <= policy.maximumCriticalMissing
      ? null
      : `${coverage.criticalMissingCount} critical element(s) have no usable value.`
  ));

  if (policy.requireMaterialReconciliation) {
    const hasMaterialControls = materialControlStates.length > 0;
    const failedMaterial = materialControlStates.filter((state) => state === "Failed").length;
    const untestedMaterial = materialControlStates.filter((state) => state === "NotTested").length;
    const status: ReportGateStatus = !hasMaterialControls
      ? "Pending"
      : failedMaterial > 0
        ? "Failed"
        : untestedMaterial > 0
          ? "Pending"
          : "Passed";
    gates.push(gate(
      "materialReconciliation",
      "Material reconciliation",
      "Passed",
      !hasMaterialControls ? "Not run" : failedMaterial > 0 ? "Failed" : untestedMaterial > 0 ? "Not tested" : "Passed",
      status,
      true,
      status === "Failed"
        ? `${failedMaterial} material reconciliation(s) failed.`
        : status === "Pending"
          ? "Material reconciliation has not been run for this period."
          : null
    ));
  } else {
    gates.push(gate("materialReconciliation", "Material reconciliation", "Not required", "—", "NotApplicable", false));
  }

  gates.push(gate(
    "criticalExceptions",
    "Open critical exceptions",
    `≤ ${policy.maximumOpenCriticalExceptions}`,
    String(openCriticalExceptionCount),
    openCriticalExceptionCount <= policy.maximumOpenCriticalExceptions ? "Passed" : "Failed",
    true,
    openCriticalExceptionCount <= policy.maximumOpenCriticalExceptions
      ? null
      : `${openCriticalExceptionCount} open critical exception(s) exceed the limit of ${policy.maximumOpenCriticalExceptions}.`
  ));

  if (policy.requireAllRequiredSections) {
    gates.push(gate(
      "requiredSections",
      "Required sections",
      "100%",
      `${contentPercent}%`,
      contentPercent >= 100 ? "Passed" : "Pending",
      true,
      contentPercent >= 100
        ? null
        : `${input.requiredSectionCount - input.completeSectionCount} required section(s) are incomplete.`
    ));
  } else {
    gates.push(gate("requiredSections", "Required sections", "Not required", `${contentPercent}%`, "NotApplicable", false));
  }

  if (policy.requireReviewComplete) {
    const complete = input.isReviewComplete === true;
    gates.push(gate(
      "review",
      "Required review",
      "Complete",
      complete ? "Complete" : "Incomplete",
      complete ? "Passed" : "Pending",
      true,
      complete ? null : "Review has not been completed."
    ));
  } else {
    gates.push(gate("review", "Required review", "Not required", "—", "NotApplicable", false));
  }

  if (policy.requireApproval) {
    const approved = input.isApproved === true;
    gates.push(gate(
      "approval",
      "Approval",
      "Complete",
      approved ? "Complete" : "Outstanding",
      approved ? "Passed" : "Pending",
      true,
      approved ? null : "Approval is outstanding."
    ));
  } else {
    gates.push(gate("approval", "Approval", "Not required", "—", "NotApplicable", false));
  }

  const blockingGates = gates.filter((entry) => entry.blocking);
  const failed = blockingGates.filter((entry) => entry.status === "Failed");
  const pending = blockingGates.filter((entry) => entry.status === "Pending");

  const canPublish = failed.length === 0 && pending.length === 0;
  const overallLabel = failed.length > 0
    ? "Blocked"
    : pending.length > 0
      ? `${pending[0].label} required`
      : "Ready to publish";

  return {
    reportClass,
    dimensions,
    gates,
    canPublish,
    overallLabel,
    overallSeverity: failed.length > 0 ? "blocked" : pending.length > 0 ? "review" : "ready",
    blockingReasons: failed.map((entry) => entry.reason ?? entry.label),
    pendingReasons: pending.map((entry) => entry.reason ?? entry.label),
    coverage
  };
}
