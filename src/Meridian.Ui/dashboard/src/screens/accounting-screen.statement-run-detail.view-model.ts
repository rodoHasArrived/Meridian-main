/**
 * Presentation logic for the statement run detail tabs.
 *
 * The Validation and Breaks & Cases tabs on the accounting reconciliation
 * surface were built as headers with no body: the tab strip carried counts
 * lifted from the run summary while the panels rendered only their own
 * description. The per-run routes supply what the panels were describing, so
 * this module turns those payloads into rows.
 *
 * Both contracts cross the wire with ordinal enums (the workstation endpoint
 * group registers no `JsonStringEnumConverter`), so severities and break types
 * are resolved through the transcribed maps and an ordinal outside them is
 * reported as unrecognized rather than guessed.
 */

import {
  BLOCKING_STATEMENT_VALIDATION_SEVERITIES,
  STATEMENT_BREAK_TYPE_LABELS,
  STATEMENT_RUN_STATUS_LABELS,
  STATEMENT_VALIDATION_SEVERITY_LABELS,
  type StatementRunBreak,
  type StatementRunReconcileAcknowledgement,
  type StatementRunValidation,
  type StatementValidationIssue
} from "@/types/statement-run-detail.types";

export type StatementRunDetailTone = "default" | "success" | "warning" | "danger";

export interface StatementRunValidationRowViewModel {
  issueId: string;
  severityLabel: string;
  severityTone: StatementRunDetailTone;
  code: string;
  message: string;
  sourceLabel: string;
  rawValue: string | null;
  recommendedAction: string | null;
  evidenceLink: string | null;
  ariaLabel: string;
}

export interface StatementRunValidationViewModel {
  loaded: boolean;
  rows: StatementRunValidationRowViewModel[];
  blocked: boolean;
  /** Server-owned verdict, restated for the operator. */
  blockedNotice: string | null;
  countLabel: string;
  emptyState: string;
}

export interface StatementRunBreakRowViewModel {
  breakId: string;
  typeLabel: string;
  category: string;
  sourceReference: string;
  deltaLabel: string;
  toleranceLabel: string;
  toleranceTone: StatementRunDetailTone;
  toleranceNote: string;
  status: string;
  createdAtUtc: string;
  ariaLabel: string;
}

export interface StatementRunBreaksViewModel {
  loaded: boolean;
  rows: StatementRunBreakRowViewModel[];
  countLabel: string;
  breachedCount: number;
  emptyState: string;
}

export interface StatementRunReconcileActionViewModel {
  enabled: boolean;
  disabledReason: string | null;
  label: string;
  ariaLabel: string;
  /** Only set once a reconcile round-trip has completed in this session. */
  lastOutcome: string | null;
}

const UNRECOGNIZED_PREFIX = "Unrecognized";

export function buildStatementRunValidationViewModel(
  validation: StatementRunValidation | null
): StatementRunValidationViewModel {
  if (!validation) {
    return {
      loaded: false,
      rows: [],
      blocked: false,
      blockedNotice: null,
      countLabel: "—",
      emptyState: "Validation has not loaded for this run."
    };
  }

  const rows = validation.issues.map(buildStatementRunValidationRow);
  return {
    loaded: true,
    rows,
    blocked: validation.isBlocked,
    blockedNotice: validation.isBlocked
      ? "Reconciliation is blocked by validation. The run cannot progress until these issues are cleared at the source."
      : null,
    countLabel: String(rows.length),
    emptyState: "This run reported no validation issues."
  };
}

export function buildStatementRunValidationRow(
  issue: StatementValidationIssue,
  index = 0
): StatementRunValidationRowViewModel {
  const severityLabel = resolveOrdinalLabel(issue.severity, STATEMENT_VALIDATION_SEVERITY_LABELS, "Severity");
  const code = issue.code?.trim() || "Uncoded";
  const message = issue.message?.trim() || "No message supplied.";
  const issueId = issue.issueId?.trim() || `${code}-${index}`;

  return {
    issueId,
    severityLabel,
    severityTone: validationSeverityTone(issue.severity),
    code,
    message,
    sourceLabel: buildValidationSourceLabel(issue),
    rawValue: issue.rawValue?.trim() || null,
    recommendedAction: issue.recommendedAction?.trim() || null,
    evidenceLink: issue.evidenceLink?.trim() || null,
    ariaLabel: `${severityLabel} validation issue ${code}. ${message}`
  };
}

export function buildStatementRunBreaksViewModel(
  breaks: StatementRunBreak[] | null
): StatementRunBreaksViewModel {
  if (!breaks) {
    return {
      loaded: false,
      rows: [],
      countLabel: "—",
      breachedCount: 0,
      emptyState: "Breaks have not loaded for this run."
    };
  }

  const rows = breaks.map(buildStatementRunBreakRow);
  const breachedCount = rows.filter((row) => row.toleranceTone === "danger").length;
  return {
    loaded: true,
    rows,
    countLabel: breachedCount > 0 ? `${rows.length} (${breachedCount} over tolerance)` : String(rows.length),
    breachedCount,
    emptyState: "This run produced no breaks."
  };
}

export function buildStatementRunBreakRow(item: StatementRunBreak): StatementRunBreakRowViewModel {
  const typeLabel = resolveOrdinalLabel(item.breakType, STATEMENT_BREAK_TYPE_LABELS, "Break type");
  const deltaLabel = formatSignedAmount(item.delta);
  const toleranceLabel = formatAmount(item.tolerance);

  return {
    breakId: item.breakId,
    typeLabel,
    category: item.category?.trim() || "Uncategorized",
    sourceReference: item.sourceReference?.trim() || "No source reference",
    deltaLabel,
    toleranceLabel,
    toleranceTone: item.toleranceBreached ? "danger" : "default",
    toleranceNote: item.toleranceBreached
      ? `Delta ${deltaLabel} exceeds the ${toleranceLabel} tolerance band.`
      : `Delta ${deltaLabel} is within the ${toleranceLabel} tolerance band.`,
    status: item.status?.trim() || "Unknown",
    createdAtUtc: item.createdAtUtc,
    ariaLabel: `${typeLabel} break ${item.breakId}, delta ${deltaLabel} against ${toleranceLabel} tolerance, status ${item.status?.trim() || "Unknown"}.`
  };
}

/**
 * `forbidden` is only set once the server has actually answered 403. The
 * browser holds no copy of the caller's permission set, so the action starts
 * enabled and the server stays the authority on who may re-run matching.
 */
export function buildStatementRunReconcileAction(input: {
  runId: string | null;
  forbidden: boolean;
  inFlight: boolean;
  blockedByValidation: boolean;
  lastAcknowledgement: StatementRunReconcileAcknowledgement | null;
}): StatementRunReconcileActionViewModel {
  const label = input.inFlight ? "Re-running match…" : "Re-run matching";
  const disabledReason = resolveReconcileDisabledReason(input);

  return {
    enabled: disabledReason === null,
    disabledReason,
    label,
    ariaLabel: input.runId
      ? `Re-run reconciliation matching for statement run ${input.runId}.`
      : "Re-run reconciliation matching. No statement run is selected.",
    lastOutcome: describeReconcileAcknowledgement(input.lastAcknowledgement)
  };
}

function resolveReconcileDisabledReason(input: {
  runId: string | null;
  forbidden: boolean;
  inFlight: boolean;
  blockedByValidation: boolean;
}): string | null {
  if (!input.runId) {
    return "Select a statement run before re-running matching.";
  }

  if (input.forbidden) {
    return "This account does not hold reconciliation mutation permission; the server declined the last attempt.";
  }

  if (input.inFlight) {
    return "A reconciliation pass is already running for this statement run.";
  }

  if (input.blockedByValidation) {
    return "Validation is blocking this run. Clear the blocking issues before re-running matching.";
  }

  return null;
}

function describeReconcileAcknowledgement(
  acknowledgement: StatementRunReconcileAcknowledgement | null
): string | null {
  if (!acknowledgement) {
    return null;
  }

  const statusLabel = resolveOrdinalLabel(acknowledgement.status, STATEMENT_RUN_STATUS_LABELS, "Run status");
  return acknowledgement.completedAtUtc
    ? `Matching returned ${statusLabel}, completed ${acknowledgement.completedAtUtc}.`
    : `Matching returned ${statusLabel}.`;
}

function buildValidationSourceLabel(issue: StatementValidationIssue): string {
  const column = issue.sourceColumn?.trim();
  const row = issue.sourceRowNumber;
  if (typeof row === "number" && column) {
    return `Row ${row}, ${column}`;
  }

  if (typeof row === "number") {
    return `Row ${row}`;
  }

  return column || "Source not identified";
}

function validationSeverityTone(severity: number | null | undefined): StatementRunDetailTone {
  if (typeof severity !== "number") {
    return "default";
  }

  if (BLOCKING_STATEMENT_VALIDATION_SEVERITIES.includes(severity)) {
    return "danger";
  }

  return severity === 1 ? "warning" : "default";
}

/**
 * Resolves an ordinal against a transcribed enum map. An ordinal the map does
 * not cover is surfaced with its number so a contract that gained a member is
 * visible as a gap rather than silently collapsing into a neighbouring label.
 */
function resolveOrdinalLabel(
  ordinal: number | null | undefined,
  labels: Readonly<Record<number, string>>,
  subject: string
): string {
  if (typeof ordinal !== "number") {
    return `${subject} not reported`;
  }

  return labels[ordinal] ?? `${UNRECOGNIZED_PREFIX} ${subject.toLowerCase()} ${ordinal}`;
}

function formatAmount(value: number): string {
  return value.toLocaleString("en-US", { minimumFractionDigits: 2, maximumFractionDigits: 2 });
}

function formatSignedAmount(value: number): string {
  const formatted = formatAmount(Math.abs(value));
  if (value > 0) {
    return `+${formatted}`;
  }

  return value < 0 ? `-${formatted}` : formatted;
}
