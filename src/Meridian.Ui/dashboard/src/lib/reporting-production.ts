/**
 * The reporting production pipeline and its control surface.
 *
 * Reporting is not a gallery of report cards. The first question an operator asks
 * on opening it is "what needs attention for this reporting period?", so the home
 * surface is a production register - one dominant plane listing every report in the
 * period with its owner, workflow state and due date - rather than a set of tiles.
 *
 * The workspace is organised as a pipeline (Plan -> Prepare -> Review -> Approve ->
 * Publish -> Preserve) surfaced as seven lanes, each answering a different job:
 * Library finds what can be produced, Production monitors what is being prepared,
 * Builder constructs, Review checks, Published preserves canonical output,
 * Schedules holds recurring obligations, and Templates governs definitions.
 *
 * Inputs are structural subsets of the workstation read models so this module stays
 * pure and directly unit-testable, matching `@/lib/reporting-hub`.
 */
import {
  describeReportingWorkflowState,
  normalizeReportClass,
  normalizeReportingWorkflowState,
  reportingWorkflowOrdinal,
  type ReportClass,
  type ReportingWorkflowState
} from "@/lib/reporting-lifecycle";
import { formatReportingPeriodLabel, hasRetainedReportingAsOfDateValue } from "@/lib/reporting-periods";
import { workstationRouteWithQuery, type WorkstationRouteKey } from "@/lib/workspace";
import type { DesignSystemSeverity } from "@/design-system/status";

export const REPORTING_LANES = [
  "Library",
  "Production",
  "Builder",
  "Review",
  "Published",
  "Schedules",
  "Templates"
] as const;
export type ReportingLaneKey = typeof REPORTING_LANES[number];

/** The production stage each lane serves, spanning Plan through Preserve. */
export const REPORTING_PIPELINE_STAGES = ["Plan", "Prepare", "Review", "Approve", "Publish", "Preserve"] as const;
export type ReportingPipelineStage = typeof REPORTING_PIPELINE_STAGES[number];

interface LaneDefinition {
  key: ReportingLaneKey;
  label: string;
  purpose: string;
  stage: ReportingPipelineStage;
  routeKey: WorkstationRouteKey;
}

const LANE_DEFINITIONS: readonly LaneDefinition[] = [
  { key: "Library", label: "Library", purpose: "Find everything Meridian can produce.", stage: "Plan", routeKey: "reportingLibrary" },
  { key: "Production", label: "Production", purpose: "Monitor reports being prepared this period.", stage: "Prepare", routeKey: "reportingRunStatus" },
  { key: "Builder", label: "Builder", purpose: "Construct or modify a report.", stage: "Prepare", routeKey: "reportingReportBuilder" },
  { key: "Review", label: "Review", purpose: "Check data, commentary and changes before publication.", stage: "Review", routeKey: "reportingPreviewValidation" },
  { key: "Published", label: "Published", purpose: "Canonical immutable output history.", stage: "Preserve", routeKey: "reportingReportPacks" },
  { key: "Schedules", label: "Schedules", purpose: "Recurring reporting obligations.", stage: "Plan", routeKey: "reportingScheduled" },
  { key: "Templates", label: "Templates", purpose: "Governed report and module definitions.", stage: "Approve", routeKey: "reportingGovernance" }
];

export interface ReportingLane {
  key: ReportingLaneKey;
  /** Two-digit ordinal, so the lane list reads as a numbered operating index. */
  ordinalLabel: string;
  label: string;
  purpose: string;
  stage: ReportingPipelineStage;
  href: string;
  /** Null when no meaningful count exists for the lane. */
  count: number | null;
  countLabel: string;
}

export interface ReportingProductionRunInput {
  runId: string;
  templateId: string;
  family: string;
  status: string;
  /** Display name; falls back to the template name, then the family. */
  reportName?: string | null;
  asOfDate?: string | null;
  owner?: string | null;
  dueAtUtc?: string | null;
  reportClass?: string | null;
  blockingReasons?: readonly string[] | null;
  /** Marks the current attempt for a report; earlier attempts are collapsed away. */
  isLatestGenerated?: boolean | null;
}

export interface ReportingProductionTemplateInput {
  templateId: string;
  name: string;
  family: string;
  lifecycleStatus?: string | null;
}

/** Signals the register cannot derive from runs alone. */
export interface ReportingAttentionSignals {
  staleSourceCount?: number | null;
  openCommentCount?: number | null;
  scheduleCount?: number | null;
}

export interface ReportingProductionRow {
  runId: string;
  templateId: string;
  reportName: string;
  family: string;
  reportClass: ReportClass;
  owner: string;
  workflowState: ReportingWorkflowState;
  stateLabel: string;
  severity: DesignSystemSeverity;
  asOfLabel: string;
  dueAtUtc: string | null;
  dueLabel: string;
  isOverdue: boolean;
  blockingReasons: string[];
  href: string;
  ariaLabel: string;
}

export interface ReportingAttentionItem {
  key: string;
  label: string;
  count: number;
  severity: DesignSystemSeverity;
  href: string | null;
}

export interface ReportingProductionInput {
  runs: readonly ReportingProductionRunInput[];
  templates?: readonly ReportingProductionTemplateInput[];
  signals?: ReportingAttentionSignals | null;
  periodLabel?: string | null;
  asOfDate?: string | null;
  /** ISO timestamp used to decide what is overdue; defaults to now. */
  evaluationAtUtc?: string | null;
}

export interface ReportingProductionModel {
  periodLabel: string;
  asOfLabel: string;
  totalCount: number;
  readyCount: number;
  reviewCount: number;
  blockedCount: number;
  preparingCount: number;
  publishedCount: number;
  headlineLabel: string;
  lanes: ReportingLane[];
  register: ReportingProductionRow[];
  attention: ReportingAttentionItem[];
  recentlyPublished: ReportingProductionRow[];
  isEmpty: boolean;
}

const READY_STATES: ReadonlySet<ReportingWorkflowState> = new Set([
  "ReadyForReview",
  "ReadyForApproval",
  "Approved",
  "Publishing",
  "Published"
]);

const REVIEW_STATES: ReadonlySet<ReportingWorkflowState> = new Set(["InReview"]);
const BLOCKED_STATES: ReadonlySet<ReportingWorkflowState> = new Set(["Blocked"]);
const PREPARING_STATES: ReadonlySet<ReportingWorkflowState> = new Set([
  "NotStarted",
  "Preparing",
  "ChangesRequested"
]);

const DUE_FORMATTER = new Intl.DateTimeFormat("en-US", {
  month: "short",
  day: "numeric",
  timeZone: "UTC"
});

function parseTimestamp(value: string | null | undefined): number | null {
  if (!value) {
    return null;
  }
  const parsed = Date.parse(value);
  return Number.isFinite(parsed) ? parsed : null;
}

function formatDue(value: string | null): string {
  const parsed = parseTimestamp(value);
  return parsed === null ? "—" : DUE_FORMATTER.format(new Date(parsed));
}

function resolveAsOfLabel(value: string | null | undefined): string {
  const trimmed = value?.trim() ?? "";
  if (!hasRetainedReportingAsOfDateValue(trimmed)) {
    return "As-of unavailable";
  }
  return formatReportingPeriodLabel(trimmed);
}

function pluralize(count: number, singular: string, plural = `${singular}s`): string {
  return `${count} ${count === 1 ? singular : plural}`;
}

function buildRow(
  run: ReportingProductionRunInput,
  templatesById: Map<string, ReportingProductionTemplateInput>,
  evaluationAt: number
): ReportingProductionRow {
  const template = templatesById.get(run.templateId);
  const reportName = run.reportName?.trim() || template?.name?.trim() || run.family?.trim() || run.templateId;
  const workflowState = normalizeReportingWorkflowState(run.status);
  const descriptor = describeReportingWorkflowState(run.status);
  const dueAtUtc = run.dueAtUtc?.trim() || null;
  const dueAt = parseTimestamp(dueAtUtc);
  const isTerminal = workflowState === "Published" || workflowState === "Superseded";
  const asOfLabel = resolveAsOfLabel(run.asOfDate);

  return {
    runId: run.runId,
    templateId: run.templateId,
    reportName,
    family: run.family,
    reportClass: normalizeReportClass(run.reportClass ?? run.family),
    owner: run.owner?.trim() || "Unassigned",
    workflowState,
    stateLabel: descriptor.label,
    severity: descriptor.severity,
    asOfLabel,
    dueAtUtc,
    dueLabel: formatDue(dueAtUtc),
    isOverdue: dueAt !== null && !isTerminal && dueAt < evaluationAt,
    blockingReasons: [...(run.blockingReasons ?? [])],
    href: workstationRouteWithQuery("reportingRunDetail", { runId: run.runId }),
    ariaLabel: `${reportName}, ${descriptor.label}, owner ${run.owner?.trim() || "Unassigned"}, as of ${asOfLabel}`
  };
}

function buildLanes(
  register: readonly ReportingProductionRow[],
  templates: readonly ReportingProductionTemplateInput[],
  signals: ReportingAttentionSignals
): ReportingLane[] {
  const counts: Record<ReportingLaneKey, number | null> = {
    Library: templates.length,
    Production: register.filter((row) => row.workflowState !== "Published" && row.workflowState !== "Superseded").length,
    Builder: register.filter((row) => PREPARING_STATES.has(row.workflowState)).length,
    Review: register.filter((row) => REVIEW_STATES.has(row.workflowState) || row.workflowState === "ReadyForReview").length,
    Published: register.filter((row) => row.workflowState === "Published").length,
    Schedules: signals.scheduleCount ?? null,
    Templates: templates.length
  };

  return LANE_DEFINITIONS.map((definition, index) => {
    const count = counts[definition.key];
    return {
      key: definition.key,
      ordinalLabel: String(index + 1).padStart(2, "0"),
      label: definition.label,
      purpose: definition.purpose,
      stage: definition.stage,
      href: workstationRouteWithQuery(definition.routeKey, {}),
      count,
      countLabel: count === null ? "—" : String(count)
    };
  });
}

function buildAttention(
  register: readonly ReportingProductionRow[],
  signals: ReportingAttentionSignals
): ReportingAttentionItem[] {
  const items: ReportingAttentionItem[] = [];

  const blocked = register.filter((row) => BLOCKED_STATES.has(row.workflowState)).length;
  if (blocked > 0) {
    items.push({ key: "blocked", label: pluralize(blocked, "blocked"), count: blocked, severity: "blocked", href: workstationRouteWithQuery("reportingRunStatus", {}) });
  }

  const overdue = register.filter((row) => row.isOverdue).length;
  if (overdue > 0) {
    items.push({ key: "overdue", label: pluralize(overdue, "past due"), count: overdue, severity: "blocked", href: workstationRouteWithQuery("reportingRunStatus", {}) });
  }

  const staleSources = signals.staleSourceCount ?? 0;
  if (staleSources > 0) {
    items.push({ key: "staleSources", label: pluralize(staleSources, "stale source"), count: staleSources, severity: "action", href: workstationRouteWithQuery("reportingEvidence", {}) });
  }

  const changesRequested = register.filter((row) => row.workflowState === "ChangesRequested").length;
  if (changesRequested > 0) {
    items.push({ key: "changesRequested", label: pluralize(changesRequested, "changes requested"), count: changesRequested, severity: "action", href: workstationRouteWithQuery("reportingPreviewValidation", {}) });
  }

  const comments = signals.openCommentCount ?? 0;
  if (comments > 0) {
    items.push({ key: "comments", label: pluralize(comments, "comment"), count: comments, severity: "review", href: workstationRouteWithQuery("reportingPreviewValidation", {}) });
  }

  const approvals = register.filter((row) => row.workflowState === "ReadyForApproval").length;
  if (approvals > 0) {
    items.push({ key: "approvals", label: pluralize(approvals, "approval due"), count: approvals, severity: "review", href: workstationRouteWithQuery("reportingGovernance", {}) });
  }

  return items;
}

/**
 * Builds the reporting production control surface.
 *
 * The register is ordered by urgency - blocked and overdue first, then by lifecycle
 * position - so the rows needing attention sit at the top without the operator
 * sorting anything.
 */
export function buildReportingProductionModel(input: ReportingProductionInput): ReportingProductionModel {
  const evaluationAt = parseTimestamp(input.evaluationAtUtc) ?? Date.now();
  const templates = input.templates ?? [];
  const signals = input.signals ?? {};
  const templatesById = new Map(templates.map((template) => [template.templateId, template]));

  // `runs` carries every attempt. The production register is a register of reports,
  // not of attempts, so collapse to the current attempt per report before ranking.
  const latestRunPerTemplate = new Map<string, ReportingProductionRunInput>();
  for (const run of input.runs) {
    const existing = latestRunPerTemplate.get(run.templateId);
    if (!existing || (run.isLatestGenerated === true && existing.isLatestGenerated !== true)) {
      latestRunPerTemplate.set(run.templateId, run);
    }
  }

  const rows = [...latestRunPerTemplate.values()].map((run) => buildRow(run, templatesById, evaluationAt));

  const register = [...rows].sort((left, right) => {
    const leftBlocked = BLOCKED_STATES.has(left.workflowState) ? 0 : 1;
    const rightBlocked = BLOCKED_STATES.has(right.workflowState) ? 0 : 1;
    if (leftBlocked !== rightBlocked) {
      return leftBlocked - rightBlocked;
    }

    if (left.isOverdue !== right.isOverdue) {
      return left.isOverdue ? -1 : 1;
    }

    const leftOrdinal = reportingWorkflowOrdinal(left.workflowState);
    const rightOrdinal = reportingWorkflowOrdinal(right.workflowState);
    if (leftOrdinal !== rightOrdinal) {
      return leftOrdinal - rightOrdinal;
    }

    return left.reportName.localeCompare(right.reportName);
  });

  const readyCount = register.filter((row) => READY_STATES.has(row.workflowState)).length;
  const reviewCount = register.filter((row) => REVIEW_STATES.has(row.workflowState)).length;
  const blockedCount = register.filter((row) => BLOCKED_STATES.has(row.workflowState)).length;
  const preparingCount = register.filter((row) => PREPARING_STATES.has(row.workflowState)).length;
  const publishedCount = register.filter((row) => row.workflowState === "Published").length;

  const recentlyPublished = register
    .filter((row) => row.workflowState === "Published")
    .slice(0, 5);

  const headlineParts = [`${readyCount} / ${register.length} ready`];
  if (reviewCount > 0) {
    headlineParts.push(`${reviewCount} review`);
  }
  if (blockedCount > 0) {
    headlineParts.push(`${blockedCount} blocked`);
  }

  return {
    periodLabel: input.periodLabel?.trim() || "Current period",
    asOfLabel: resolveAsOfLabel(input.asOfDate),
    totalCount: register.length,
    readyCount,
    reviewCount,
    blockedCount,
    preparingCount,
    publishedCount,
    headlineLabel: register.length === 0 ? "No reports in production" : headlineParts.join("  ·  "),
    lanes: buildLanes(register, templates, signals),
    register,
    attention: buildAttention(register, signals),
    recentlyPublished,
    isEmpty: register.length === 0
  };
}
