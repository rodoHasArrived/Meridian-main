/**
 * Controlled vocabularies for the reporting workstation.
 *
 * Reporting collapses four genuinely different axes onto the word "status", which
 * is the single largest source of semantic confusion in governed reporting:
 *
 *  - **Workflow state** - where the report instance sits in Plan -> Prepare ->
 *    Review -> Approve -> Publish -> Preserve.
 *  - **Data state** - whether the numbers behind an element are trustworthy.
 *  - **Control state** - whether a test/reconciliation over those numbers passed.
 *  - **Freeze state** - whether upstream change is still admitted into the report.
 *
 * Keeping them separate means "Approved" (workflow) can coexist with "Provisional"
 * (data) and "Within tolerance" (control) without any of the three being lossy.
 * Each vocabulary normalizes onto the five canonical operator severities in
 * `@/design-system/status` so the existing chips render them without new tokens.
 *
 * This module is pure and free of React so it can be unit-tested directly and
 * reused by both the browser workstation and the shared read-model surface.
 */
import {
  normalizeDesignSystemSeverity,
  type DesignSystemSeverity
} from "@/design-system/status";

/** Canonical ordering of the report workflow lifecycle. */
export const REPORTING_WORKFLOW_STATES = [
  "NotStarted",
  "Preparing",
  "Blocked",
  "ReadyForReview",
  "InReview",
  "ChangesRequested",
  "ReadyForApproval",
  "Approved",
  "Publishing",
  "Published",
  "Superseded",
  "Restated"
] as const;
export type ReportingWorkflowState = typeof REPORTING_WORKFLOW_STATES[number];

/** Trustworthiness of the data behind a report element. */
export const REPORTING_DATA_STATES = [
  "Confirmed",
  "Provisional",
  "Estimated",
  "Stale",
  "Overridden",
  "Missing",
  "Exception"
] as const;
export type ReportingDataState = typeof REPORTING_DATA_STATES[number];

/** Outcome of a control/reconciliation test applied to a report element. */
export const REPORTING_CONTROL_STATES = [
  "Passed",
  "WithinTolerance",
  "Failed",
  "NotTested",
  "Waived"
] as const;
export type ReportingControlState = typeof REPORTING_CONTROL_STATES[number];

/**
 * Whether upstream change is still admitted into the report.
 *
 * `Open` keeps linked data live, `SoftFrozen` detects change without applying it,
 * `HardFrozen` binds the report to an immutable governed dataset version, and
 * `Published` means an immutable publication record exists.
 */
export const REPORTING_FREEZE_STATES = ["Open", "SoftFrozen", "HardFrozen", "Published"] as const;
export type ReportingFreezeState = typeof REPORTING_FREEZE_STATES[number];

/**
 * Data-state of an individual report block. This is deliberately narrower than
 * {@link ReportingDataState}: a block is a rendered artifact, so it additionally
 * distinguishes whether it tracks its source (`Live`) or was pinned (`Snapshot`).
 */
export const REPORT_BLOCK_STATES = [
  "Live",
  "Snapshot",
  "Stale",
  "Changed",
  "Overridden",
  "Blocked",
  "Missing"
] as const;
export type ReportBlockState = typeof REPORT_BLOCK_STATES[number];

/**
 * Report classes drive template rules, review requirements and publication gates.
 * A regulatory schedule and an ad-hoc analytical note should not be governed alike.
 */
export const REPORT_CLASSES = [
  "Accounting",
  "Analytical",
  "Portfolio",
  "Management",
  "Regulatory"
] as const;
export type ReportClass = typeof REPORT_CLASSES[number];

export interface ReportingVocabularyDescriptor<TState extends string> {
  state: TState;
  label: string;
  severity: DesignSystemSeverity;
  description: string;
}

const WORKFLOW_DESCRIPTORS: Record<ReportingWorkflowState, ReportingVocabularyDescriptor<ReportingWorkflowState>> = {
  NotStarted: { state: "NotStarted", label: "Not started", severity: "info", description: "No preparation work has begun for this period." },
  Preparing: { state: "Preparing", label: "Preparing", severity: "review", description: "Content and data are being assembled by the preparer." },
  Blocked: { state: "Blocked", label: "Blocked", severity: "blocked", description: "Preparation cannot continue until an upstream blocker clears." },
  ReadyForReview: { state: "ReadyForReview", label: "Ready for review", severity: "review", description: "Preparation is complete and the report is queued for review." },
  InReview: { state: "InReview", label: "In review", severity: "review", description: "A reviewer is working through data, commentary and evidence." },
  ChangesRequested: { state: "ChangesRequested", label: "Changes requested", severity: "action", description: "Review returned the report to the preparer with required changes." },
  ReadyForApproval: { state: "ReadyForApproval", label: "Ready for approval", severity: "review", description: "Review is complete and the report awaits an approver." },
  Approved: { state: "Approved", label: "Approved", severity: "ready", description: "Approved for publication; no further content changes are expected." },
  Publishing: { state: "Publishing", label: "Publishing", severity: "review", description: "Publication artifacts are being produced and distributed." },
  Published: { state: "Published", label: "Published", severity: "ready", description: "An immutable publication record exists for this period." },
  Superseded: { state: "Superseded", label: "Superseded", severity: "info", description: "A later version replaced this publication." },
  Restated: { state: "Restated", label: "Restated", severity: "action", description: "Published figures were restated through a controlled process." }
};

const DATA_DESCRIPTORS: Record<ReportingDataState, ReportingVocabularyDescriptor<ReportingDataState>> = {
  Confirmed: { state: "Confirmed", label: "Confirmed", severity: "ready", description: "Sourced from a governed, current, confirmed value." },
  Provisional: { state: "Provisional", label: "Provisional", severity: "review", description: "Sourced but not yet final; expected to change before cutoff." },
  Estimated: { state: "Estimated", label: "Estimated", severity: "action", description: "Modelled or accrued rather than observed." },
  Stale: { state: "Stale", label: "Stale", severity: "action", description: "The source has not refreshed within its expected window." },
  Overridden: { state: "Overridden", label: "Overridden", severity: "action", description: "An approved manual value replaced the sourced value." },
  Missing: { state: "Missing", label: "Missing", severity: "blocked", description: "No value is available from any permitted source." },
  Exception: { state: "Exception", label: "Exception", severity: "blocked", description: "An open exception is attached to this value." }
};

const CONTROL_DESCRIPTORS: Record<ReportingControlState, ReportingVocabularyDescriptor<ReportingControlState>> = {
  Passed: { state: "Passed", label: "Passed", severity: "ready", description: "The control test passed cleanly." },
  WithinTolerance: { state: "WithinTolerance", label: "Within tolerance", severity: "ready", description: "A difference exists but sits inside the agreed tolerance." },
  Failed: { state: "Failed", label: "Failed", severity: "blocked", description: "The control test failed and blocks publication." },
  NotTested: { state: "NotTested", label: "Not tested", severity: "info", description: "The control has not been run for this period." },
  Waived: { state: "Waived", label: "Waived", severity: "action", description: "The control was waived under a recorded approval." }
};

const FREEZE_DESCRIPTORS: Record<ReportingFreezeState, ReportingVocabularyDescriptor<ReportingFreezeState>> = {
  Open: { state: "Open", label: "Open", severity: "info", description: "Linked data continues to update in place." },
  SoftFrozen: { state: "SoftFrozen", label: "Soft frozen", severity: "review", description: "Upstream changes are detected but not applied automatically." },
  HardFrozen: { state: "HardFrozen", label: "Hard frozen", severity: "ready", description: "Bound to an immutable governed dataset version." },
  Published: { state: "Published", label: "Published", severity: "ready", description: "An immutable publication record exists." }
};

const BLOCK_DESCRIPTORS: Record<ReportBlockState, ReportingVocabularyDescriptor<ReportBlockState>> = {
  Live: { state: "Live", label: "Live", severity: "ready", description: "Tracks its governed source and is current." },
  Snapshot: { state: "Snapshot", label: "Snapshot", severity: "info", description: "Pinned to a captured value rather than tracking its source." },
  Stale: { state: "Stale", label: "Stale", severity: "action", description: "The underlying source refreshed after this block was rendered." },
  Changed: { state: "Changed", label: "Changed", severity: "review", description: "The source changed after the last review of this block." },
  Overridden: { state: "Overridden", label: "Overridden", severity: "action", description: "An approved manual value replaced the sourced value." },
  Blocked: { state: "Blocked", label: "Blocked", severity: "blocked", description: "An open blocker prevents this block from resolving." },
  Missing: { state: "Missing", label: "Missing", severity: "blocked", description: "No value resolved for this block." }
};

const CLASS_DESCRIPTORS: Record<ReportClass, { reportClass: ReportClass; label: string; description: string; requiresReconciliation: boolean; requiresApproval: boolean; structureLocked: boolean }> = {
  Accounting: { reportClass: "Accounting", label: "Accounting", description: "Tables, reconciliation and controls; ties to the ledger.", requiresReconciliation: true, requiresApproval: true, structureLocked: false },
  Analytical: { reportClass: "Analytical", label: "Analytical", description: "Charts, commentary and scenarios over governed analytics.", requiresReconciliation: false, requiresApproval: false, structureLocked: false },
  Portfolio: { reportClass: "Portfolio", label: "Portfolio", description: "Exposure, attribution and comparison against benchmarks.", requiresReconciliation: false, requiresApproval: true, structureLocked: false },
  Management: { reportClass: "Management", label: "Management", description: "Narrative synthesis and decision support for committees.", requiresReconciliation: false, requiresApproval: true, structureLocked: false },
  Regulatory: { reportClass: "Regulatory", label: "Regulatory", description: "Locked structure with strict evidence retention.", requiresReconciliation: true, requiresApproval: true, structureLocked: true }
};

export type ReportClassDescriptor = typeof CLASS_DESCRIPTORS[ReportClass];

function normalizeKey(value: string | null | undefined): string {
  return String(value ?? "").toLowerCase().replace(/[^a-z0-9]/g, "");
}

function buildLookup<TState extends string>(states: readonly TState[], aliases: Record<string, TState>): Record<string, TState> {
  const lookup: Record<string, TState> = { ...aliases };
  for (const state of states) {
    lookup[normalizeKey(state)] = state;
  }
  return lookup;
}

const WORKFLOW_LOOKUP = buildLookup(REPORTING_WORKFLOW_STATES, {
  draft: "Preparing",
  drafted: "Preparing",
  inprogress: "Preparing",
  queued: "Preparing",
  running: "Publishing",
  generating: "Publishing",
  validated: "ReadyForReview",
  submitted: "InReview",
  awaitingreview: "ReadyForReview",
  pendingreview: "ReadyForReview",
  awaitingapproval: "ReadyForApproval",
  pendingapproval: "ReadyForApproval",
  rejected: "ChangesRequested",
  changesrequired: "ChangesRequested",
  released: "Published",
  distributed: "Published",
  succeeded: "Published",
  complete: "Published",
  completed: "Published",
  failed: "Blocked",
  cancelled: "Blocked",
  canceled: "Blocked",
  restatement: "Restated",
  replaced: "Superseded"
});

const DATA_LOOKUP = buildLookup(REPORTING_DATA_STATES, {
  current: "Confirmed",
  final: "Confirmed",
  verified: "Confirmed",
  unconfirmed: "Provisional",
  preliminary: "Provisional",
  accrued: "Estimated",
  modelled: "Estimated",
  modeled: "Estimated",
  expired: "Stale",
  manual: "Overridden",
  absent: "Missing",
  unavailable: "Missing",
  breach: "Exception",
  break: "Exception"
});

const CONTROL_LOOKUP = buildLookup(REPORTING_CONTROL_STATES, {
  pass: "Passed",
  ok: "Passed",
  intolerance: "WithinTolerance",
  tolerated: "WithinTolerance",
  fail: "Failed",
  breached: "Failed",
  notrun: "NotTested",
  untested: "NotTested",
  skipped: "NotTested",
  waiver: "Waived",
  accepted: "Waived"
});

const FREEZE_LOOKUP = buildLookup(REPORTING_FREEZE_STATES, {
  unlocked: "Open",
  live: "Open",
  soft: "SoftFrozen",
  softlock: "SoftFrozen",
  hard: "HardFrozen",
  hardlock: "HardFrozen",
  locked: "HardFrozen",
  frozen: "HardFrozen",
  released: "Published"
});

const BLOCK_LOOKUP = buildLookup(REPORT_BLOCK_STATES, {
  current: "Live",
  tracking: "Live",
  pinned: "Snapshot",
  captured: "Snapshot",
  expired: "Stale",
  modified: "Changed",
  manual: "Overridden",
  failed: "Blocked",
  absent: "Missing",
  unavailable: "Missing"
});

const CLASS_LOOKUP = buildLookup(REPORT_CLASSES, {
  ledger: "Accounting",
  close: "Accounting",
  financial: "Accounting",
  analysis: "Analytical",
  research: "Analytical",
  performance: "Portfolio",
  holdings: "Portfolio",
  attribution: "Portfolio",
  exposure: "Portfolio",
  committee: "Management",
  board: "Management",
  executive: "Management",
  statutory: "Regulatory",
  compliance: "Regulatory",
  controlled: "Regulatory"
});

/**
 * Normalizes a server- or template-provided workflow string onto the controlled
 * vocabulary. Unrecognized values resolve to `Preparing` rather than a terminal
 * state, so an unknown string never reads as "safe to publish".
 */
export function normalizeReportingWorkflowState(value: string | null | undefined): ReportingWorkflowState {
  return WORKFLOW_LOOKUP[normalizeKey(value)] ?? "Preparing";
}

/** Unrecognized data states resolve to `Provisional`, never `Confirmed`. */
export function normalizeReportingDataState(value: string | null | undefined): ReportingDataState {
  return DATA_LOOKUP[normalizeKey(value)] ?? "Provisional";
}

/** Unrecognized control states resolve to `NotTested`, never `Passed`. */
export function normalizeReportingControlState(value: string | null | undefined): ReportingControlState {
  return CONTROL_LOOKUP[normalizeKey(value)] ?? "NotTested";
}

/** Unrecognized freeze states resolve to `Open`, the least restrictive claim. */
export function normalizeReportingFreezeState(value: string | null | undefined): ReportingFreezeState {
  return FREEZE_LOOKUP[normalizeKey(value)] ?? "Open";
}

/** Unrecognized block states resolve to `Stale`, never `Live`. */
export function normalizeReportBlockState(value: string | null | undefined): ReportBlockState {
  return BLOCK_LOOKUP[normalizeKey(value)] ?? "Stale";
}

/** Unrecognized classes resolve to `Analytical`, the least-governed class. */
export function normalizeReportClass(value: string | null | undefined): ReportClass {
  return CLASS_LOOKUP[normalizeKey(value)] ?? "Analytical";
}

export function describeReportingWorkflowState(value: string | null | undefined): ReportingVocabularyDescriptor<ReportingWorkflowState> {
  return WORKFLOW_DESCRIPTORS[normalizeReportingWorkflowState(value)];
}

export function describeReportingDataState(value: string | null | undefined): ReportingVocabularyDescriptor<ReportingDataState> {
  return DATA_DESCRIPTORS[normalizeReportingDataState(value)];
}

export function describeReportingControlState(value: string | null | undefined): ReportingVocabularyDescriptor<ReportingControlState> {
  return CONTROL_DESCRIPTORS[normalizeReportingControlState(value)];
}

export function describeReportingFreezeState(value: string | null | undefined): ReportingVocabularyDescriptor<ReportingFreezeState> {
  return FREEZE_DESCRIPTORS[normalizeReportingFreezeState(value)];
}

export function describeReportBlockState(value: string | null | undefined): ReportingVocabularyDescriptor<ReportBlockState> {
  return BLOCK_DESCRIPTORS[normalizeReportBlockState(value)];
}

export function describeReportClass(value: string | null | undefined): ReportClassDescriptor {
  return CLASS_DESCRIPTORS[normalizeReportClass(value)];
}

/**
 * Severity for any reporting state across the four vocabularies. Falls back to the
 * shared design-system normalizer so unrecognized strings still render a chip.
 */
export function reportingStateSeverity(value: string | null | undefined): DesignSystemSeverity {
  const key = normalizeKey(value);
  if (key in WORKFLOW_LOOKUP) {
    return WORKFLOW_DESCRIPTORS[WORKFLOW_LOOKUP[key]].severity;
  }
  if (key in DATA_LOOKUP) {
    return DATA_DESCRIPTORS[DATA_LOOKUP[key]].severity;
  }
  if (key in CONTROL_LOOKUP) {
    return CONTROL_DESCRIPTORS[CONTROL_LOOKUP[key]].severity;
  }
  if (key in FREEZE_LOOKUP) {
    return FREEZE_DESCRIPTORS[FREEZE_LOOKUP[key]].severity;
  }
  return normalizeDesignSystemSeverity(value);
}

/** Position of a workflow state in the lifecycle, for ordering production registers. */
export function reportingWorkflowOrdinal(value: string | null | undefined): number {
  return REPORTING_WORKFLOW_STATES.indexOf(normalizeReportingWorkflowState(value));
}

/** Whether the workflow state represents an immutable publication outcome. */
export function isPublishedWorkflowState(value: string | null | undefined): boolean {
  const state = normalizeReportingWorkflowState(value);
  return state === "Published" || state === "Superseded" || state === "Restated";
}

/** Whether the workflow state still expects preparer action before review. */
export function requiresPreparerAction(value: string | null | undefined): boolean {
  const state = normalizeReportingWorkflowState(value);
  return state === "NotStarted" || state === "Preparing" || state === "Blocked" || state === "ChangesRequested";
}

/**
 * Whether a freeze state admits upstream change without an explicit operator decision.
 * Only `Open` reports apply upstream change silently; every other state routes change
 * through the soft-freeze review workflow or rejects it outright.
 */
export function admitsUpstreamChange(value: string | null | undefined): boolean {
  return normalizeReportingFreezeState(value) === "Open";
}
