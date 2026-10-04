/**
 * Lineage for a governed reported number.
 *
 * A published figure should be defensible in both directions. Upstream: which system
 * produced it, what normalized it, what calculation combined it, what reconciled it,
 * which block carried it, which publication released it. Downstream: which other
 * reports depend on it, so changing a source is a decision made with its consequences
 * visible rather than discovered afterwards.
 *
 * Two properties matter more than completeness here:
 *
 *  - **Gaps are stated, not skipped.** A trace with no reconciliation stage is not a
 *    five-stage trace; it is a six-stage trace with a hole in it. Rendering only the
 *    stages that exist would make an unreconciled number look as traceable as a
 *    reconciled one.
 *  - **Calculations must tie.** A component breakdown that does not sum to the total
 *    it claims to explain is reported as a residual, never silently balanced. A trace
 *    that quietly absorbs the difference is worse than no trace.
 */
import { formatCurrency, formatNumber } from "@/lib/format";
import type { DesignSystemSeverity } from "@/design-system/status";

/** The canonical lineage stages, in upstream-to-downstream order. */
export const REPORTING_TRACE_STAGES = [
  "Source",
  "Normalization",
  "Calculation",
  "Reconciliation",
  "ReportBlock",
  "Publication"
] as const;

export type ReportingTraceStage = typeof REPORTING_TRACE_STAGES[number];

export const REPORTING_TRACE_STAGE_LABELS: Record<ReportingTraceStage, string> = {
  Source: "Source",
  Normalization: "Normalization",
  Calculation: "Calculation",
  Reconciliation: "Reconciliation",
  ReportBlock: "Report block",
  Publication: "Publication"
};

const STAGE_ORDINALS: Record<ReportingTraceStage, number> = REPORTING_TRACE_STAGES.reduce(
  (accumulator, stage, index) => {
    accumulator[stage] = index;
    return accumulator;
  },
  {} as Record<ReportingTraceStage, number>
);

const STAGE_LOOKUP: Record<string, ReportingTraceStage> = REPORTING_TRACE_STAGES.reduce(
  (accumulator, stage) => {
    accumulator[stage.toLowerCase()] = stage;
    return accumulator;
  },
  {
    reportblock: "ReportBlock",
    block: "ReportBlock",
    report: "ReportBlock",
    normalisation: "Normalization",
    normalize: "Normalization",
    calc: "Calculation",
    recon: "Reconciliation",
    reconcile: "Reconciliation",
    publish: "Publication",
    published: "Publication"
  } as Record<string, ReportingTraceStage>
);

/**
 * Resolve a stage name.
 *
 * Unrecognized stages resolve to `null` rather than to a guess: placing a step at the
 * wrong point in a lineage misrepresents what produced the number.
 */
export function normalizeReportingTraceStage(value: string | null | undefined): ReportingTraceStage | null {
  const key = value?.trim().toLowerCase().replace(/[\s_-]+/g, "") ?? "";
  return key ? STAGE_LOOKUP[key] ?? null : null;
}

export interface ReportingTraceStepInput {
  stage: string;
  /** What acted at this stage, e.g. "Bloomberg BVAL", "Position valuation". */
  label: string;
  /** The owning system, when distinct from the label. */
  system?: string | null;
  asOfUtc?: string | null;
  detail?: string | null;
  /** Identifier the workspace can navigate to for this step. */
  href?: string | null;
}

export interface ReportingTraceStep {
  stage: ReportingTraceStage;
  stageLabel: string;
  ordinal: number;
  label: string;
  system: string | null;
  asOfUtc: string | null;
  detail: string | null;
  href: string | null;
}

export interface ReportingTraceGap {
  stage: ReportingTraceStage;
  stageLabel: string;
  ordinal: number;
  /** Why the gap matters, for the evidence panel. */
  reason: string;
  severity: DesignSystemSeverity;
}

export interface ReportingTraceModel {
  steps: readonly ReportingTraceStep[];
  /** Governed stages with no recorded step, in lineage order. */
  gaps: readonly ReportingTraceGap[];
  /** Steps whose stage could not be resolved; retained rather than dropped. */
  unresolvedSteps: readonly ReportingTraceStepInput[];
  isComplete: boolean;
  severity: DesignSystemSeverity;
  summary: string;
}

/**
 * Stages whose absence is a control gap rather than a structural fact.
 *
 * Not every number is calculated - a directly sourced figure legitimately has no
 * calculation stage - so only the stages that must exist for a number to be
 * defensible are reported as gaps.
 */
const REQUIRED_STAGES: readonly ReportingTraceStage[] = ["Source", "ReportBlock"];

const GAP_REASONS: Record<ReportingTraceStage, string> = {
  Source: "No originating system recorded; the value cannot be attributed.",
  Normalization: "No normalization step recorded.",
  Calculation: "No calculation step recorded.",
  Reconciliation: "Not reconciled against an independent source.",
  ReportBlock: "Not attached to a report block.",
  Publication: "Not yet published."
};

export interface BuildReportingTraceInput {
  steps: readonly ReportingTraceStepInput[];
  /** Stages to report as gaps beyond the structurally required ones. */
  requiredStages?: readonly string[];
}

/**
 * Build the ordered lineage, reporting every governed stage that has no step.
 *
 * Steps are sorted by stage, and multiple steps in one stage keep their input order -
 * a value can pass through two normalizations, and collapsing them would hide one.
 */
export function buildReportingTrace(input: BuildReportingTraceInput): ReportingTraceModel {
  const steps: ReportingTraceStep[] = [];
  const unresolvedSteps: ReportingTraceStepInput[] = [];

  for (const step of input.steps) {
    const stage = normalizeReportingTraceStage(step.stage);
    if (stage === null) {
      unresolvedSteps.push(step);
      continue;
    }

    steps.push({
      stage,
      stageLabel: REPORTING_TRACE_STAGE_LABELS[stage],
      ordinal: STAGE_ORDINALS[stage],
      label: step.label,
      system: nonEmpty(step.system),
      asOfUtc: nonEmpty(step.asOfUtc),
      detail: nonEmpty(step.detail),
      href: nonEmpty(step.href)
    });
  }

  // Stable sort by stage keeps intra-stage input order intact.
  const ordered = steps
    .map((step, index) => ({ step, index }))
    .sort((left, right) => left.step.ordinal - right.step.ordinal || left.index - right.index)
    .map((entry) => entry.step);

  const present = new Set(ordered.map((step) => step.stage));
  const extraRequired = (input.requiredStages ?? [])
    .map(normalizeReportingTraceStage)
    .filter((stage): stage is ReportingTraceStage => stage !== null);
  const required = new Set<ReportingTraceStage>([...REQUIRED_STAGES, ...extraRequired]);

  const gaps: ReportingTraceGap[] = REPORTING_TRACE_STAGES.filter(
    (stage) => required.has(stage) && !present.has(stage)
  ).map((stage) => ({
    stage,
    stageLabel: REPORTING_TRACE_STAGE_LABELS[stage],
    ordinal: STAGE_ORDINALS[stage],
    reason: GAP_REASONS[stage],
    severity: REQUIRED_STAGES.includes(stage) ? ("action" as const) : ("review" as const)
  }));

  const severity: DesignSystemSeverity = gaps.some((gap) => gap.severity === "action")
    ? "action"
    : gaps.length > 0 || unresolvedSteps.length > 0
      ? "review"
      : "ready";

  return {
    steps: ordered,
    gaps,
    unresolvedSteps,
    isComplete: gaps.length === 0 && unresolvedSteps.length === 0,
    severity,
    summary: summarizeTrace(ordered, gaps, unresolvedSteps)
  };
}

function summarizeTrace(
  steps: readonly ReportingTraceStep[],
  gaps: readonly ReportingTraceGap[],
  unresolved: readonly ReportingTraceStepInput[]
): string {
  if (steps.length === 0) {
    return "No lineage recorded for this value.";
  }

  const stageCount = new Set(steps.map((step) => step.stage)).size;
  const base = `${formatNumber(steps.length, { maximumFractionDigits: 0 })} step${steps.length === 1 ? "" : "s"} across ${formatNumber(stageCount, { maximumFractionDigits: 0 })} stage${stageCount === 1 ? "" : "s"}.`;

  const parts = [base];
  if (gaps.length > 0) {
    parts.push(`${gaps.map((gap) => gap.stageLabel.toLowerCase()).join(", ")} missing.`);
  }
  if (unresolved.length > 0) {
    parts.push(`${formatNumber(unresolved.length, { maximumFractionDigits: 0 })} step(s) at an unrecognized stage.`);
  }
  return parts.join(" ");
}

export interface CalculationComponentInput {
  label: string;
  value: number | null | undefined;
  /** Identifier the workspace can navigate to for this component. */
  href?: string | null;
  source?: string | null;
}

export interface CalculationComponent {
  label: string;
  value: number;
  displayValue: string;
  /** True when the component reduces the total, for right-aligned parenthesized display. */
  isNegative: boolean;
  href: string | null;
  source: string | null;
}

export interface CalculationTraceModel {
  components: readonly CalculationComponent[];
  /** Sum of the components. */
  computedTotal: number;
  /** The total the report asserts, when one was supplied. */
  statedTotal: number | null;
  displayTotal: string;
  /** `statedTotal - computedTotal`; `null` when no total was stated. */
  residual: number | null;
  /** True when the components reconcile to the stated total within tolerance. */
  tiesOut: boolean;
  severity: DesignSystemSeverity;
  summary: string;
  /** Components that had no usable value and were excluded from the sum. */
  excludedLabels: readonly string[];
}

export interface BuildCalculationTraceInput {
  components: readonly CalculationComponentInput[];
  /** The total as the report states it. Omit to report the computed sum only. */
  statedTotal?: number | null;
  /** Absolute tolerance for the tie-out; defaults to half a cent. */
  tolerance?: number;
  formatValue?: (value: number) => string;
}

/**
 * Build a component breakdown and check that it explains the stated total.
 *
 * The tie-out is the point of this model. A breakdown that does not sum to the number
 * printed above it is a defect in the report, and it is reported as a residual with an
 * `action` severity rather than being reconciled away by adjusting a component or by
 * displaying the computed sum in place of the stated one.
 *
 * Components with no usable value are excluded from the sum and named, because
 * treating a missing component as zero would manufacture a tie-out that isn't real.
 */
export function buildCalculationTrace(input: BuildCalculationTraceInput): CalculationTraceModel {
  const formatValue = input.formatValue ?? defaultCurrency;
  const tolerance = Math.abs(input.tolerance ?? 0.005);

  const components: CalculationComponent[] = [];
  const excludedLabels: string[] = [];

  for (const component of input.components) {
    if (!isFiniteNumber(component.value)) {
      excludedLabels.push(component.label);
      continue;
    }

    components.push({
      label: component.label,
      value: component.value,
      displayValue: formatValue(component.value),
      isNegative: component.value < 0,
      href: nonEmpty(component.href),
      source: nonEmpty(component.source)
    });
  }

  const computedTotal = components.reduce((sum, component) => sum + component.value, 0);
  const statedTotal = isFiniteNumber(input.statedTotal) ? input.statedTotal : null;
  const residual = statedTotal === null ? null : statedTotal - computedTotal;
  const tiesOut = residual === null ? excludedLabels.length === 0 : Math.abs(residual) <= tolerance;

  const severity: DesignSystemSeverity = !tiesOut
    ? "action"
    : excludedLabels.length > 0
      ? "review"
      : "ready";

  return {
    components,
    computedTotal,
    statedTotal,
    displayTotal: formatValue(statedTotal ?? computedTotal),
    residual,
    tiesOut,
    severity,
    summary: summarizeCalculation(components.length, residual, tiesOut, excludedLabels, formatValue),
    excludedLabels
  };
}

function summarizeCalculation(
  componentCount: number,
  residual: number | null,
  tiesOut: boolean,
  excludedLabels: readonly string[],
  formatValue: (value: number) => string
): string {
  if (!tiesOut && residual !== null) {
    return `Components do not tie to the stated total; ${formatValue(residual)} unexplained.`;
  }

  if (excludedLabels.length > 0) {
    const names = excludedLabels.join(", ");
    return `${formatNumber(componentCount, { maximumFractionDigits: 0 })} component(s) summed; ${names} excluded for want of a value.`;
  }

  return `${formatNumber(componentCount, { maximumFractionDigits: 0 })} component(s) tie to the stated total.`;
}

export interface DownstreamUsageInput {
  reportId: string;
  reportName: string;
  /** Workflow state of the consuming report. */
  workflowState?: string | null;
  /** How many blocks in that report consume the value. */
  blockCount?: number | null;
  href?: string | null;
}

export interface DownstreamUsageEntry {
  reportId: string;
  reportName: string;
  blockCount: number;
  isPublished: boolean;
  href: string | null;
}

export interface DownstreamUsageModel {
  entries: readonly DownstreamUsageEntry[];
  reportCount: number;
  blockCount: number;
  /** Consumers already published; changing the value raises a restatement question. */
  publishedCount: number;
  label: string;
  severity: DesignSystemSeverity;
}

/**
 * Build the "Used in N reports" view for a source or calculated value.
 *
 * Published consumers are counted separately and sorted first: a change that only
 * touches drafts is an edit, while one that touches a published report is a
 * restatement decision, and the difference should not need to be inferred from a list.
 */
export function buildDownstreamUsage(
  inputs: readonly DownstreamUsageInput[],
  isPublishedState: (value: string | null | undefined) => boolean
): DownstreamUsageModel {
  const entries: DownstreamUsageEntry[] = inputs.map((usage) => ({
    reportId: usage.reportId,
    reportName: usage.reportName,
    blockCount: isFiniteNumber(usage.blockCount) && usage.blockCount > 0 ? Math.trunc(usage.blockCount) : 1,
    isPublished: isPublishedState(usage.workflowState),
    href: nonEmpty(usage.href)
  }));

  const ordered = entries
    .map((entry, index) => ({ entry, index }))
    .sort((left, right) => {
      if (left.entry.isPublished !== right.entry.isPublished) {
        return left.entry.isPublished ? -1 : 1;
      }
      return left.index - right.index;
    })
    .map((wrapped) => wrapped.entry);

  const blockCount = ordered.reduce((sum, entry) => sum + entry.blockCount, 0);
  const publishedCount = ordered.filter((entry) => entry.isPublished).length;

  return {
    entries: ordered,
    reportCount: ordered.length,
    blockCount,
    publishedCount,
    label: ordered.length === 0
      ? "Not used in any report"
      : `Used in ${formatNumber(ordered.length, { maximumFractionDigits: 0 })} report${ordered.length === 1 ? "" : "s"}`,
    severity: publishedCount > 0 ? "review" : "info"
  };
}

function isFiniteNumber(value: number | null | undefined): value is number {
  return typeof value === "number" && Number.isFinite(value);
}

function nonEmpty(value: string | null | undefined): string | null {
  const trimmed = value?.trim() ?? "";
  return trimmed.length > 0 ? trimmed : null;
}

function defaultCurrency(value: number): string {
  return formatCurrency(value, { maximumFractionDigits: 0 });
}
