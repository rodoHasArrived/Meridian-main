/**
 * The Reporting Datum: the atomic governed value in a report.
 *
 * `report-health` counts report elements by data state, but it has no model of what
 * an element actually *is*. A number on a published page is not just a value - it is
 * a value that came from a named system, in a known state, as of a governed date,
 * owned by an accountable team. Those four attributes travel with the number all the
 * way to publication, and a reader who cannot see them cannot tell a confirmed
 * ledger figure from a manual override.
 *
 * So a datum carries its provenance (source, state, as-of, owner), its movement
 * against a prior value, and the materiality assessment of that movement. Provenance
 * is quiet by default and surfaced through evidence mode - but it is always present
 * in the model, never reconstructed at render time.
 *
 * This module also builds the **coverage field**: the repeated-mark summary used for
 * completion, source coverage, and review coverage. It is deliberately capped, so a
 * field over a large population stays a readable summary instead of decaying into a
 * decorative dotted texture.
 */
import { formatNumber } from "@/lib/format";
import {
  describeReportBlockState,
  describeReportingDataState,
  normalizeReportBlockState,
  normalizeReportingDataState,
  reportingStateSeverity,
  type ReportBlockState,
  type ReportingDataState
} from "@/lib/reporting-lifecycle";
import {
  assessVariance,
  combineMaterialityAssessments,
  assessSourceFreshness,
  type MaterialityAssessment,
  type MaterialityPolicy
} from "@/lib/reporting-materiality";
import type { DesignSystemSeverity } from "@/design-system/status";

const UNKNOWN_LABEL = "Not set";
const UNAVAILABLE_LABEL = "—";

const AS_OF_FORMATTER = new Intl.DateTimeFormat("en-US", {
  year: "numeric",
  month: "short",
  day: "numeric",
  timeZone: "UTC"
});

export interface ReportingDatumInput {
  datumId: string;
  label: string;
  /** The governed value. `null` means the datum exists but has no value yet. */
  value?: number | null;
  /** Pre-formatted display value; when absent, callers format `value` themselves. */
  formattedValue?: string | null;
  /** Value at the comparison point (prior period, prior version, or review baseline). */
  priorValue?: number | null;
  /** The governed system the value resolved from, e.g. "Accounting Ledger". */
  source?: string | null;
  /** The accountable team, e.g. "Investment Analytics". */
  owner?: string | null;
  /** When the source produced this value. */
  asOfUtc?: string | null;
  dataState?: string | null;
  blockState?: string | null;
  /** Denominator for the portfolio-percentage materiality test. */
  portfolioValue?: number | null;
  /** Performance impact of the movement, in basis points. */
  performanceImpactBasisPoints?: number | null;
  isCritical?: boolean | null;
  blockId?: string | null;
  sectionId?: string | null;
}

/**
 * The four provenance attributes every report block exposes.
 *
 * Absent facts render as "Not set" rather than being omitted or guessed. A block
 * with no recorded owner is a governance gap worth seeing, not a blank to tidy away.
 */
export interface ReportingDatumProvenance {
  source: string;
  sourceKnown: boolean;
  state: string;
  stateKnown: boolean;
  asOf: string;
  asOfKnown: boolean;
  owner: string;
  ownerKnown: boolean;
}

export interface ReportingDatumMovement {
  /** Signed difference from the prior value; `null` when either side is absent. */
  variance: number | null;
  /** Signed fractional change, e.g. 0.0125 for +1.25%; `null` when undefined. */
  variancePercent: number | null;
  hasMoved: boolean;
  /** "$2.411B → $2.414B" style transition, or `null` when there is no comparison. */
  transitionLabel: string | null;
}

export interface ReportingDatum {
  datumId: string;
  label: string;
  value: number | null;
  displayValue: string;
  dataState: ReportingDataState;
  blockState: ReportBlockState;
  severity: DesignSystemSeverity;
  provenance: ReportingDatumProvenance;
  movement: ReportingDatumMovement;
  materiality: MaterialityAssessment;
  isCritical: boolean;
  blockId: string | null;
  sectionId: string | null;
  /** True when provenance is complete enough to defend the number in review. */
  isAttributable: boolean;
  /** Provenance attributes that are missing, in display order. */
  missingProvenance: readonly string[];
}

export interface BuildReportingDatumOptions {
  policy: MaterialityPolicy;
  /** Evaluation instant for the freshness test; defaults to now. */
  evaluatedAtUtc?: string | Date | null;
  /** Formats the value and prior value for the transition label. */
  formatValue?: (value: number) => string;
}

/**
 * Build a datum from a source record.
 *
 * The block state is derived when the caller does not assert one: a datum that moved
 * against its baseline is `Changed`, a missing value is `Missing`, and an overridden
 * or stale data state carries through. Deriving rather than defaulting to `Live`
 * keeps a moved number from presenting as settled.
 */
export function buildReportingDatum(
  input: ReportingDatumInput,
  options: BuildReportingDatumOptions
): ReportingDatum {
  const dataState = normalizeReportingDataState(input.dataState);
  const value = isFiniteNumber(input.value) ? input.value : null;
  const priorValue = isFiniteNumber(input.priorValue) ? input.priorValue : null;
  const formatValue = options.formatValue ?? defaultFormatValue;

  const movement = buildMovement(value, priorValue, formatValue);
  const provenance = buildProvenance(input, dataState);
  const blockState = resolveBlockState(input.blockState, dataState, movement.hasMoved, value);

  const materiality = combineMaterialityAssessments([
    assessVariance(
      {
        variance: movement.variance,
        portfolioValue: input.portfolioValue,
        performanceImpactBasisPoints: input.performanceImpactBasisPoints
      },
      options.policy
    ),
    assessSourceFreshness(
      { asOfUtc: input.asOfUtc, evaluatedAtUtc: options.evaluatedAtUtc },
      options.policy
    )
  ]);

  const missingProvenance = collectMissingProvenance(provenance);

  return {
    datumId: input.datumId,
    label: input.label,
    value,
    displayValue: resolveDisplayValue(input, value, formatValue),
    dataState,
    blockState,
    severity: worstSeverity(reportingStateSeverity(dataState), reportingStateSeverity(blockState)),
    provenance,
    movement,
    materiality,
    isCritical: input.isCritical === true,
    blockId: nonEmpty(input.blockId),
    sectionId: nonEmpty(input.sectionId),
    isAttributable: missingProvenance.length === 0,
    missingProvenance
  };
}

function buildMovement(
  value: number | null,
  priorValue: number | null,
  formatValue: (value: number) => string
): ReportingDatumMovement {
  if (value === null || priorValue === null) {
    return { variance: null, variancePercent: null, hasMoved: false, transitionLabel: null };
  }

  const variance = value - priorValue;
  const hasMoved = variance !== 0;
  // A move away from zero has no defined percentage; reporting Infinity as a
  // percentage change would be worse than reporting that there isn't one.
  const variancePercent = priorValue === 0 ? null : variance / Math.abs(priorValue);

  return {
    variance,
    variancePercent,
    hasMoved,
    transitionLabel: hasMoved ? `${formatValue(priorValue)} → ${formatValue(value)}` : null
  };
}

function buildProvenance(input: ReportingDatumInput, dataState: ReportingDataState): ReportingDatumProvenance {
  const source = nonEmpty(input.source);
  const owner = nonEmpty(input.owner);
  const asOf = formatAsOf(input.asOfUtc);
  // A data state is only "known" when the caller actually supplied one; the
  // normalizer always yields a member of the vocabulary, so it cannot be the test.
  const stateKnown = nonEmpty(input.dataState) !== null;

  return {
    source: source ?? UNKNOWN_LABEL,
    sourceKnown: source !== null,
    state: describeReportingDataState(dataState).label,
    stateKnown,
    asOf: asOf ?? UNKNOWN_LABEL,
    asOfKnown: asOf !== null,
    owner: owner ?? UNKNOWN_LABEL,
    ownerKnown: owner !== null
  };
}

function collectMissingProvenance(provenance: ReportingDatumProvenance): readonly string[] {
  const missing: string[] = [];
  if (!provenance.sourceKnown) {
    missing.push("Source");
  }
  if (!provenance.stateKnown) {
    missing.push("State");
  }
  if (!provenance.asOfKnown) {
    missing.push("As of");
  }
  if (!provenance.ownerKnown) {
    missing.push("Owner");
  }
  return missing;
}

function resolveBlockState(
  declared: string | null | undefined,
  dataState: ReportingDataState,
  hasMoved: boolean,
  value: number | null
): ReportBlockState {
  if (nonEmpty(declared) !== null) {
    return normalizeReportBlockState(declared);
  }

  if (dataState === "Missing" || value === null) {
    return "Missing";
  }
  if (dataState === "Overridden") {
    return "Overridden";
  }
  if (dataState === "Stale") {
    return "Stale";
  }
  if (dataState === "Exception") {
    return "Blocked";
  }
  return hasMoved ? "Changed" : "Live";
}

function resolveDisplayValue(
  input: ReportingDatumInput,
  value: number | null,
  formatValue: (value: number) => string
): string {
  const provided = nonEmpty(input.formattedValue);
  if (provided !== null) {
    return provided;
  }
  return value === null ? UNAVAILABLE_LABEL : formatValue(value);
}

/** Describe a datum's block state for a provenance strip or evidence panel. */
export function describeDatumBlockState(datum: ReportingDatum): { label: string; severity: DesignSystemSeverity } {
  const descriptor = describeReportBlockState(datum.blockState);
  return { label: descriptor.label, severity: descriptor.severity };
}

const SEVERITY_RANK: Record<DesignSystemSeverity, number> = {
  ready: 0,
  info: 1,
  review: 2,
  action: 3,
  blocked: 4
};

function worstSeverity(left: DesignSystemSeverity, right: DesignSystemSeverity): DesignSystemSeverity {
  return SEVERITY_RANK[left] >= SEVERITY_RANK[right] ? left : right;
}

/**
 * The maximum marks a coverage field renders.
 *
 * Beyond this the field stops being countable at a glance, so marks become
 * proportional and the label carries the real figures. The discussion this model
 * comes from is explicit that fields must not become decorative texture.
 */
export const COVERAGE_FIELD_MAX_MARKS = 20;

export type CoverageMark = "filled" | "empty";

export interface CoverageFieldModel {
  filled: number;
  total: number;
  /** Fractional completion in [0, 1]; `null` when the population is empty. */
  ratio: number | null;
  marks: readonly CoverageMark[];
  /** True when each mark stands for more than one member of the population. */
  isProportional: boolean;
  /** "8 of 10 complete" / "90% current". */
  label: string;
  /** Screen-reader sentence; the marks themselves are decorative once this exists. */
  accessibleLabel: string;
}

export interface CoverageFieldOptions {
  /** Noun for the population, e.g. "sections", "sources", "elements". */
  noun?: string;
  /** Trailing adjective, e.g. "complete", "current", "reviewed". */
  qualifier?: string;
  /** Render "90% current" instead of "9 of 10 current". */
  style?: "count" | "percent";
  maxMarks?: number;
}

/**
 * Build the repeated-mark coverage summary.
 *
 * Marks round *down* when the field is proportional: a field that shows every mark
 * filled must mean the population is genuinely complete. Rounding up would let 99%
 * render as finished, which is precisely the reading this model exists to prevent.
 */
export function buildCoverageField(
  filled: number | null | undefined,
  total: number | null | undefined,
  options: CoverageFieldOptions = {}
): CoverageFieldModel {
  const maxMarks = Math.max(1, Math.trunc(options.maxMarks ?? COVERAGE_FIELD_MAX_MARKS));
  const safeTotal = isFiniteNumber(total) && total > 0 ? Math.trunc(total) : 0;
  const safeFilled = isFiniteNumber(filled) ? clamp(Math.trunc(filled), 0, safeTotal) : 0;

  if (safeTotal === 0) {
    return {
      filled: 0,
      total: 0,
      ratio: null,
      marks: [],
      isProportional: false,
      label: "No population",
      accessibleLabel: `No ${options.noun ?? "items"} to report.`
    };
  }

  const ratio = safeFilled / safeTotal;
  const isProportional = safeTotal > maxMarks;
  const markCount = isProportional ? maxMarks : safeTotal;
  const filledMarks = isProportional
    ? clamp(Math.floor(ratio * markCount), 0, markCount)
    : safeFilled;

  const marks: CoverageMark[] = Array.from({ length: markCount }, (_, index) =>
    index < filledMarks ? "filled" : "empty"
  );

  const qualifier = options.qualifier ? ` ${options.qualifier}` : "";
  const label = options.style === "percent"
    ? `${formatNumber(ratio * 100, { maximumFractionDigits: 0 })}%${qualifier}`
    : `${formatNumber(safeFilled, { maximumFractionDigits: 0 })} of ${formatNumber(safeTotal, { maximumFractionDigits: 0 })}${qualifier}`;

  const noun = options.noun ?? "items";
  const accessibleLabel = `${formatNumber(safeFilled, { maximumFractionDigits: 0 })} of ${formatNumber(safeTotal, { maximumFractionDigits: 0 })} ${noun}${qualifier}.`;

  return { filled: safeFilled, total: safeTotal, ratio, marks, isProportional, label, accessibleLabel };
}

function clamp(value: number, min: number, max: number): number {
  return Math.min(Math.max(value, min), max);
}

function isFiniteNumber(value: number | null | undefined): value is number {
  return typeof value === "number" && Number.isFinite(value);
}

function nonEmpty(value: string | null | undefined): string | null {
  const trimmed = value?.trim() ?? "";
  return trimmed.length > 0 ? trimmed : null;
}

function defaultFormatValue(value: number): string {
  return formatNumber(value, { maximumFractionDigits: 2 });
}

function formatAsOf(value: string | null | undefined): string | null {
  const trimmed = nonEmpty(value);
  if (trimmed === null) {
    return null;
  }

  const parsed = Date.parse(trimmed);
  if (Number.isNaN(parsed)) {
    // A token the workspace uses but cannot parse (a period label, say) is still a
    // governed as-of assertion; keep it verbatim rather than reporting it unknown.
    return trimmed;
  }

  return AS_OF_FORMATTER.format(new Date(parsed));
}
