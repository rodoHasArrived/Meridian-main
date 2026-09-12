/**
 * The Reporting Period as a first-class object.
 *
 * A global "as of" date picker is not enough for controlled reporting. A period
 * end, a valuation date, an accounting close, a reporting cutoff and a publication
 * deadline are five different dates that move independently, and a report published
 * for June must stay reproducible from June's governed state rather than from
 * whatever the sources say today.
 *
 * This module models that period - its milestones, the governed snapshots bound to
 * it, and its relationship to the accounting close - and derives the operator-facing
 * status. It complements `@/lib/reporting-periods`, which owns pure calendar math;
 * everything here is about period *governance* rather than date arithmetic.
 */
import {
  compareIsoDate,
  formatReportingPeriodLabel,
  isIsoDate,
  todayIsoDate
} from "@/lib/reporting-periods";
import type { DesignSystemSeverity } from "@/design-system/status";

/** Lifecycle of a reporting period, independent of any single report inside it. */
export const REPORTING_PERIOD_STATUSES = [
  "Scheduled",
  "InProduction",
  "CutoffPassed",
  "Published",
  "Reopened",
  "Archived"
] as const;
export type ReportingPeriodStatus = typeof REPORTING_PERIOD_STATUSES[number];

/** The governed milestones that order work inside a reporting period. */
export const REPORTING_PERIOD_MILESTONES = [
  "periodEnd",
  "valuationDate",
  "accountingClose",
  "reportingCutoff",
  "publicationDue"
] as const;
export type ReportingPeriodMilestoneKey = typeof REPORTING_PERIOD_MILESTONES[number];

const MILESTONE_LABELS: Record<ReportingPeriodMilestoneKey, string> = {
  periodEnd: "Period end",
  valuationDate: "Valuation date",
  accountingClose: "Accounting close",
  reportingCutoff: "Reporting cutoff",
  publicationDue: "Publication"
};

/**
 * The governed snapshots a period binds, so that reproducing a published report
 * resolves the same inputs it was originally produced from.
 */
export interface ReportingPeriodSnapshots {
  portfolioSnapshotId?: string | null;
  benchmarkSnapshotId?: string | null;
  fxSnapshotId?: string | null;
  pricingCutoffUtc?: string | null;
  ledgerVersion?: string | null;
  approvedAdjustmentCount?: number | null;
}

/** State of the accounting close that the reporting period depends on. */
export interface AccountingCloseState {
  periodLabel: string;
  /** `Open`, `Closing`, `Closed`, or `Reopened`. */
  status: string;
  closedAtUtc?: string | null;
  ledgerVersion?: string | null;
  /** Set when a controlled reopen posted activity after the close. */
  reopenedAtUtc?: string | null;
}

export interface ReportingPeriodInput {
  periodId: string;
  label?: string | null;
  periodEnd: string;
  valuationDate?: string | null;
  accountingClose?: string | null;
  reportingCutoff?: string | null;
  publicationDue?: string | null;
  status?: string | null;
  snapshots?: ReportingPeriodSnapshots | null;
  close?: AccountingCloseState | null;
  /** ISO timestamp at which reports in this period were frozen, if any. */
  frozenAtUtc?: string | null;
}

export interface ReportingPeriodMilestone {
  key: ReportingPeriodMilestoneKey;
  label: string;
  date: string | null;
  dateLabel: string;
  /** True once the milestone date is on or before the evaluation date. */
  isReached: boolean;
  /** True when the milestone is in the future. */
  isUpcoming: boolean;
  /** Days until (positive) or since (negative) the milestone. */
  daysRemaining: number | null;
}

export interface ReportingPeriodModel {
  periodId: string;
  label: string;
  status: ReportingPeriodStatus;
  statusLabel: string;
  statusSeverity: DesignSystemSeverity;
  milestones: ReportingPeriodMilestone[];
  nextMilestone: ReportingPeriodMilestone | null;
  /** Milestones whose date has passed while the period is still in production. */
  overdueMilestones: ReportingPeriodMilestone[];
  snapshots: ReportingPeriodSnapshots;
  /** True when every governed snapshot needed for reproduction is bound. */
  isReproducible: boolean;
  missingSnapshotLabels: string[];
  close: AccountingCloseState | null;
  closeStatusLabel: string;
  /**
   * True when the accounting period changed after reports in this period were
   * frozen, which invalidates the frozen basis and must be surfaced to preparers.
   */
  requiresRefreezeReview: boolean;
  refreezeReason: string | null;
  summaryLabel: string;
}

const STATUS_LABELS: Record<ReportingPeriodStatus, string> = {
  Scheduled: "Scheduled",
  InProduction: "In production",
  CutoffPassed: "Cutoff passed",
  Published: "Published",
  Reopened: "Reopened",
  Archived: "Archived"
};

const STATUS_SEVERITIES: Record<ReportingPeriodStatus, DesignSystemSeverity> = {
  Scheduled: "info",
  InProduction: "review",
  CutoffPassed: "action",
  Published: "ready",
  Reopened: "blocked",
  Archived: "info"
};

const MS_PER_DAY = 86_400_000;

function normalizeKey(value: string | null | undefined): string {
  return String(value ?? "").toLowerCase().replace(/[^a-z0-9]/g, "");
}

function normalizeIsoDate(value: string | null | undefined): string | null {
  const trimmed = value?.trim() ?? "";
  return trimmed.length > 0 && isIsoDate(trimmed) ? trimmed : null;
}

function isoDateToUtcMs(value: string): number {
  return Date.parse(`${value}T00:00:00Z`);
}

function daysBetween(from: string, to: string): number {
  return Math.round((isoDateToUtcMs(to) - isoDateToUtcMs(from)) / MS_PER_DAY);
}

function formatMilestoneDate(value: string | null): string {
  return value ? formatReportingPeriodLabel(value) : "Not set";
}

function buildMilestone(
  key: ReportingPeriodMilestoneKey,
  date: string | null,
  evaluationDate: string
): ReportingPeriodMilestone {
  const isReached = date !== null && compareIsoDate(date, evaluationDate) <= 0;
  return {
    key,
    label: MILESTONE_LABELS[key],
    date,
    dateLabel: formatMilestoneDate(date),
    isReached,
    isUpcoming: date !== null && !isReached,
    daysRemaining: date === null ? null : daysBetween(evaluationDate, date)
  };
}

function resolveDeclaredStatus(value: string | null | undefined): ReportingPeriodStatus | null {
  const key = normalizeKey(value);
  if (!key) {
    return null;
  }
  for (const status of REPORTING_PERIOD_STATUSES) {
    if (normalizeKey(status) === key) {
      return status;
    }
  }
  const aliases: Record<string, ReportingPeriodStatus> = {
    open: "InProduction",
    inprogress: "InProduction",
    preparing: "InProduction",
    producing: "InProduction",
    closed: "Published",
    released: "Published",
    complete: "Published",
    locked: "Published",
    reopen: "Reopened",
    future: "Scheduled",
    pending: "Scheduled"
  };
  return aliases[key] ?? null;
}

function isCloseReopened(close: AccountingCloseState | null): boolean {
  if (!close) {
    return false;
  }
  return normalizeKey(close.status) === "reopened" || Boolean(close.reopenedAtUtc);
}

/**
 * Derives the period status. A declared status wins when the source system supplies
 * one, except that a reopened accounting close always escalates: a period cannot
 * read as `Published` while its ledger basis is being revised.
 */
function deriveStatus(
  input: ReportingPeriodInput,
  milestones: Record<ReportingPeriodMilestoneKey, ReportingPeriodMilestone>,
  close: AccountingCloseState | null
): ReportingPeriodStatus {
  if (isCloseReopened(close)) {
    return "Reopened";
  }

  const declared = resolveDeclaredStatus(input.status);
  if (declared) {
    return declared;
  }

  if (milestones.publicationDue.isReached) {
    return "Published";
  }
  if (milestones.reportingCutoff.isReached) {
    return "CutoffPassed";
  }
  if (milestones.periodEnd.isReached) {
    return "InProduction";
  }
  return "Scheduled";
}

const SNAPSHOT_REQUIREMENTS: ReadonlyArray<{ key: keyof ReportingPeriodSnapshots; label: string }> = [
  { key: "portfolioSnapshotId", label: "Portfolio snapshot" },
  { key: "benchmarkSnapshotId", label: "Benchmark snapshot" },
  { key: "fxSnapshotId", label: "FX snapshot" },
  { key: "pricingCutoffUtc", label: "Pricing cutoff" },
  { key: "ledgerVersion", label: "Ledger version" }
];

function resolveMissingSnapshots(snapshots: ReportingPeriodSnapshots): string[] {
  return SNAPSHOT_REQUIREMENTS.filter(({ key }) => {
    const value = snapshots[key];
    return typeof value !== "string" || value.trim().length === 0;
  }).map(({ label }) => label);
}

/**
 * Detects the cross-workspace condition from the accounting lane: a controlled
 * reopen, or a ledger version that moved after reports were frozen, leaves frozen
 * reports bound to a basis that no longer exists.
 */
function resolveRefreezeReason(
  close: AccountingCloseState | null,
  snapshots: ReportingPeriodSnapshots,
  frozenAtUtc: string | null
): string | null {
  if (!close) {
    return null;
  }

  if (isCloseReopened(close)) {
    return "Accounting period was reopened after reports were frozen.";
  }

  const boundLedger = snapshots.ledgerVersion?.trim() ?? "";
  const closeLedger = close.ledgerVersion?.trim() ?? "";
  if (boundLedger.length > 0 && closeLedger.length > 0 && boundLedger !== closeLedger) {
    return `Ledger moved from ${boundLedger} to ${closeLedger} after report freeze.`;
  }

  if (frozenAtUtc && close.closedAtUtc) {
    const frozenAt = Date.parse(frozenAtUtc);
    const closedAt = Date.parse(close.closedAtUtc);
    if (Number.isFinite(frozenAt) && Number.isFinite(closedAt) && closedAt > frozenAt) {
      return "Accounting period closed after reports were frozen.";
    }
  }

  return null;
}

function describeCloseStatus(close: AccountingCloseState | null): string {
  if (!close) {
    return "Not linked";
  }
  const status = close.status?.trim();
  if (!status) {
    return "Unknown";
  }
  return status.charAt(0).toUpperCase() + status.slice(1);
}

/**
 * Builds the operator-facing model for a reporting period.
 *
 * `evaluationDate` defaults to today so the model is deterministic in tests.
 */
export function buildReportingPeriodModel(
  input: ReportingPeriodInput,
  evaluationDate: string = todayIsoDate()
): ReportingPeriodModel {
  const asOf = normalizeIsoDate(evaluationDate) ?? todayIsoDate();
  const periodEnd = normalizeIsoDate(input.periodEnd);

  const milestoneMap = {
    periodEnd: buildMilestone("periodEnd", periodEnd, asOf),
    valuationDate: buildMilestone("valuationDate", normalizeIsoDate(input.valuationDate) ?? periodEnd, asOf),
    accountingClose: buildMilestone("accountingClose", normalizeIsoDate(input.accountingClose), asOf),
    reportingCutoff: buildMilestone("reportingCutoff", normalizeIsoDate(input.reportingCutoff), asOf),
    publicationDue: buildMilestone("publicationDue", normalizeIsoDate(input.publicationDue), asOf)
  } satisfies Record<ReportingPeriodMilestoneKey, ReportingPeriodMilestone>;

  const milestones = REPORTING_PERIOD_MILESTONES.map((key) => milestoneMap[key]);
  const close = input.close ?? null;
  const status = deriveStatus(input, milestoneMap, close);
  const snapshots = input.snapshots ?? {};
  const missingSnapshotLabels = resolveMissingSnapshots(snapshots);
  const refreezeReason = resolveRefreezeReason(close, snapshots, input.frozenAtUtc ?? null);

  const nextMilestone = milestones.find((milestone) => milestone.isUpcoming) ?? null;
  const isTerminal = status === "Published" || status === "Archived";
  const overdueMilestones = isTerminal
    ? []
    : milestones.filter((milestone) => milestone.isReached && milestone.key !== "periodEnd" && milestone.key !== "valuationDate");

  const label = input.label?.trim()
    || (periodEnd ? formatReportingPeriodLabel(periodEnd) : input.periodId);

  const summaryParts = [STATUS_LABELS[status]];
  if (nextMilestone?.date) {
    summaryParts.push(`${nextMilestone.label} ${nextMilestone.dateLabel}`);
  }
  if (missingSnapshotLabels.length > 0) {
    summaryParts.push(`${missingSnapshotLabels.length} snapshot${missingSnapshotLabels.length === 1 ? "" : "s"} unbound`);
  }

  return {
    periodId: input.periodId,
    label,
    status,
    statusLabel: STATUS_LABELS[status],
    statusSeverity: STATUS_SEVERITIES[status],
    milestones,
    nextMilestone,
    overdueMilestones,
    snapshots,
    isReproducible: missingSnapshotLabels.length === 0,
    missingSnapshotLabels,
    close,
    closeStatusLabel: describeCloseStatus(close),
    requiresRefreezeReview: refreezeReason !== null,
    refreezeReason,
    summaryLabel: summaryParts.join(" · ")
  };
}
