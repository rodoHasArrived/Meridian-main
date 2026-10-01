/**
 * "Changed since review" and the soft-freeze admission workflow.
 *
 * A reviewer signs off a report at a point in time. Sources keep moving: a price
 * updates, a late journal posts, an attribution result shifts. Asking the reviewer
 * to reread the whole report is the wrong answer - Meridian should be able to say
 * precisely which upstream changes landed after the review, which report elements
 * they moved, and what the values were before and after.
 *
 * The freeze state decides what happens next:
 *
 *  - `Open` applies upstream change in place; the model exists to disclose it.
 *  - `SoftFrozen` detects change but holds it, and the preparer decides whether to
 *    incorporate it or remain frozen.
 *  - `HardFrozen` / `Published` are bound to an immutable basis; change is recorded
 *    against the report but cannot be admitted without an explicit unfreeze.
 *
 * Values arrive pre-formatted so this module stays free of currency and unit
 * knowledge, which belongs at the presentation edge.
 */
import {
  admitsUpstreamChange,
  normalizeReportingFreezeState,
  type ReportBlockState,
  type ReportingFreezeState
} from "@/lib/reporting-lifecycle";
import type { DesignSystemSeverity } from "@/design-system/status";

export interface UpstreamChangeInput {
  changeId: string;
  /** Operator-facing change category, e.g. "Pricing update", "Late journal". */
  kind: string;
  description?: string | null;
  occurredAtUtc: string;
  affectedElementIds: readonly string[];
  /** Signed value impact where one is quantifiable; null when there is none. */
  valueImpact?: number | null;
  valueImpactLabel?: string | null;
  source?: string | null;
}

export interface ReportElementValueInput {
  elementId: string;
  label: string;
  /** Value as it stood when the report was last reviewed. */
  reviewedValueLabel: string;
  /** Value as it stands now. */
  currentValueLabel: string;
  sectionId?: string | null;
}

export interface ChangedUpstreamChange {
  changeId: string;
  kind: string;
  description: string;
  occurredAtUtc: string;
  source: string | null;
  affectedElementIds: string[];
  valueImpact: number | null;
  /** "No value impact" when the change moved no reported figure. */
  valueImpactLabel: string;
  hasValueImpact: boolean;
}

export interface ChangedReportElement {
  elementId: string;
  label: string;
  reviewedValueLabel: string;
  currentValueLabel: string;
  /** Block state implied by the change and the freeze state. */
  blockState: ReportBlockState;
  changeCount: number;
  changeKinds: string[];
  transitionLabel: string;
}

export type ChangeAdmissionAction = "ReviewChanges" | "ApplyChanges" | "RemainFrozen" | "Unfreeze" | "None";

export interface ChangeAdmissionOption {
  action: ChangeAdmissionAction;
  label: string;
  description: string;
  isPrimary: boolean;
}

export interface ChangeSinceReviewInput {
  /** Null when the report has never been reviewed. */
  reviewedAtUtc?: string | null;
  freezeState?: string | null;
  frozenAtUtc?: string | null;
  changes: readonly UpstreamChangeInput[];
  elements: readonly ReportElementValueInput[];
}

export interface ChangeSinceReviewModel {
  reviewedAtUtc: string | null;
  hasBeenReviewed: boolean;
  freezeState: ReportingFreezeState;
  frozenAtUtc: string | null;
  /** Upstream changes that landed strictly after the review timestamp. */
  changes: ChangedUpstreamChange[];
  changeCount: number;
  affectedElements: ChangedReportElement[];
  affectedElementCount: number;
  hasChanges: boolean;
  /** True when the freeze state lets change flow into the report automatically. */
  isAutoApplied: boolean;
  /** True when a preparer must choose whether to incorporate held changes. */
  requiresOperatorDecision: boolean;
  admissionLabel: string;
  severity: DesignSystemSeverity;
  headline: string;
  options: ChangeAdmissionOption[];
}

function parseTimestamp(value: string | null | undefined): number | null {
  if (!value) {
    return null;
  }
  const parsed = Date.parse(value);
  return Number.isFinite(parsed) ? parsed : null;
}

function pluralize(count: number, singular: string, plural = `${singular}s`): string {
  return `${count} ${count === 1 ? singular : plural}`;
}

function describeValueImpact(change: UpstreamChangeInput): { label: string; hasImpact: boolean } {
  const explicit = change.valueImpactLabel?.trim();
  if (explicit) {
    return { label: explicit, hasImpact: true };
  }
  if (typeof change.valueImpact === "number" && Number.isFinite(change.valueImpact) && change.valueImpact !== 0) {
    const sign = change.valueImpact > 0 ? "+" : "−";
    return { label: `${sign}${Math.abs(change.valueImpact).toLocaleString("en-US")}`, hasImpact: true };
  }
  return { label: "No value impact", hasImpact: false };
}

const ADMISSION_LABELS: Record<ReportingFreezeState, string> = {
  Open: "Applied automatically",
  SoftFrozen: "Held for review",
  HardFrozen: "Not admitted",
  Published: "Not admitted"
};

function buildOptions(freezeState: ReportingFreezeState, hasChanges: boolean): ChangeAdmissionOption[] {
  if (!hasChanges) {
    return [];
  }

  switch (freezeState) {
    case "Open":
      return [
        { action: "ReviewChanges", label: "Review changes", description: "Walk the changed elements before re-review.", isPrimary: true }
      ];
    case "SoftFrozen":
      return [
        { action: "ReviewChanges", label: "Review changes", description: "Inspect each held change and its value impact.", isPrimary: true },
        { action: "ApplyChanges", label: "Apply changes", description: "Incorporate the held changes and re-run affected elements.", isPrimary: false },
        { action: "RemainFrozen", label: "Remain frozen", description: "Keep the frozen basis and disclose the changes instead.", isPrimary: false }
      ];
    case "HardFrozen":
      return [
        { action: "ReviewChanges", label: "Review changes", description: "Inspect changes recorded against the frozen basis.", isPrimary: true },
        { action: "Unfreeze", label: "Unfreeze report", description: "Release the immutable basis to admit these changes.", isPrimary: false }
      ];
    case "Published":
      return [
        { action: "ReviewChanges", label: "Review changes", description: "Assess whether these changes warrant a restatement.", isPrimary: true }
      ];
    default:
      return [];
  }
}

/**
 * Builds the change-since-review model.
 *
 * When the report has never been reviewed there is no baseline to diff against, so
 * no changes are reported rather than reporting every change as new.
 */
export function buildChangeSinceReview(input: ChangeSinceReviewInput): ChangeSinceReviewModel {
  const freezeState = normalizeReportingFreezeState(input.freezeState);
  const reviewedAtUtc = input.reviewedAtUtc?.trim() || null;
  const reviewedAt = parseTimestamp(reviewedAtUtc);
  const hasBeenReviewed = reviewedAt !== null;

  const relevant = hasBeenReviewed
    ? input.changes.filter((change) => {
        const occurredAt = parseTimestamp(change.occurredAtUtc);
        return occurredAt !== null && occurredAt > reviewedAt;
      })
    : [];

  const changes: ChangedUpstreamChange[] = relevant
    .map((change) => {
      const impact = describeValueImpact(change);
      return {
        changeId: change.changeId,
        kind: change.kind,
        description: change.description?.trim() || change.kind,
        occurredAtUtc: change.occurredAtUtc,
        source: change.source?.trim() || null,
        affectedElementIds: [...change.affectedElementIds],
        valueImpact: typeof change.valueImpact === "number" && Number.isFinite(change.valueImpact)
          ? change.valueImpact
          : null,
        valueImpactLabel: impact.label,
        hasValueImpact: impact.hasImpact
      };
    })
    .sort((left, right) => (parseTimestamp(right.occurredAtUtc) ?? 0) - (parseTimestamp(left.occurredAtUtc) ?? 0));

  const changesByElement = new Map<string, ChangedUpstreamChange[]>();
  for (const change of changes) {
    for (const elementId of change.affectedElementIds) {
      const bucket = changesByElement.get(elementId) ?? [];
      bucket.push(change);
      changesByElement.set(elementId, bucket);
    }
  }

  // A held change leaves the rendered element on its frozen value, so the block is
  // Stale rather than Changed: what the reader sees no longer matches the source.
  const changedBlockState: ReportBlockState = admitsUpstreamChange(freezeState) ? "Changed" : "Stale";

  const affectedElements: ChangedReportElement[] = input.elements
    .filter((element) => changesByElement.has(element.elementId))
    .map((element) => {
      const elementChanges = changesByElement.get(element.elementId) ?? [];
      const changeKinds = [...new Set(elementChanges.map((change) => change.kind))];
      return {
        elementId: element.elementId,
        label: element.label,
        reviewedValueLabel: element.reviewedValueLabel,
        currentValueLabel: element.currentValueLabel,
        blockState: changedBlockState,
        changeCount: elementChanges.length,
        changeKinds,
        transitionLabel: `${element.reviewedValueLabel} → ${element.currentValueLabel}`
      };
    });

  const hasChanges = changes.length > 0;
  const isAutoApplied = admitsUpstreamChange(freezeState);
  const requiresOperatorDecision = hasChanges && freezeState === "SoftFrozen";

  const headline = !hasBeenReviewed
    ? "Not yet reviewed"
    : hasChanges
      ? `${pluralize(changes.length, "underlying change")} affecting ${pluralize(affectedElements.length, "report element")}`
      : "No changes since review";

  const severity: DesignSystemSeverity = !hasChanges
    ? "ready"
    : requiresOperatorDecision
      ? "action"
      : "review";

  return {
    reviewedAtUtc,
    hasBeenReviewed,
    freezeState,
    frozenAtUtc: input.frozenAtUtc?.trim() || null,
    changes,
    changeCount: changes.length,
    affectedElements,
    affectedElementCount: affectedElements.length,
    hasChanges,
    isAutoApplied,
    requiresOperatorDecision,
    admissionLabel: ADMISSION_LABELS[freezeState],
    severity,
    headline,
    options: buildOptions(freezeState, hasChanges)
  };
}
