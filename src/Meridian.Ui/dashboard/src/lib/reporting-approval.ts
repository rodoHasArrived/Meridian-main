/**
 * Approval and attestation for governed reports.
 *
 * The property that makes approval mean anything is that it binds to a **specific
 * immutable version of a specific set of data**. An approval that survives the data
 * moving underneath it is not a control, it is a rubber stamp: the approver signed
 * off on numbers that no longer exist.
 *
 * So an approval carries the version and the data fingerprint it was given against,
 * and {@link evaluateApproval} compares those with the report's current state. When
 * either has moved, the approval does not stay `Approved` and does not silently
 * vanish - it becomes `ChangesRequireReview`, which says both that somebody approved
 * something and that what they approved is no longer what is on the page.
 *
 * Attestation follows the same principle. Every requirement must be explicitly
 * affirmed; a requirement that is unconfirmed, or whose result is unknown, blocks
 * attestation rather than being assumed satisfied.
 */
import {
  normalizeReportClass,
  normalizeReportingWorkflowState,
  type ReportClass,
  type ReportingWorkflowState
} from "@/lib/reporting-lifecycle";
import type { DesignSystemSeverity } from "@/design-system/status";

/** Lifecycle transitions an approval record can describe. */
export const APPROVAL_TRANSITIONS = ["Prepare", "Review", "Approve", "Publish"] as const;
export type ApprovalTransition = typeof APPROVAL_TRANSITIONS[number];

export const APPROVAL_TRANSITION_LABELS: Record<ApprovalTransition, string> = {
  Prepare: "Prepare",
  Review: "Review",
  Approve: "Approve",
  Publish: "Publish"
};

/** Progress marker for one transition in the Prepare → Review → Approve → Publish strip. */
export type ApprovalTransitionMark = "complete" | "current" | "pending";

export interface ApprovalRecordInput {
  transition: string;
  /** Who acted. Absent means the record cannot be attributed. */
  person?: string | null;
  timestampUtc?: string | null;
  /** The report version this action was taken against. */
  version?: string | null;
  /** Data state of the report at the time of the action, e.g. "Confirmed". */
  dataState?: string | null;
  /**
   * Digest of the data the action was taken against. This - not `dataState` - is
   * what currency is judged on: a report can stay "Confirmed" while every number
   * underneath it moves.
   */
  dataFingerprint?: string | null;
  comments?: string | null;
  /** Exceptions disclosed at the time of the action. */
  exceptionCount?: number | null;
  /** Controls the actor overrode to proceed. */
  overrideCount?: number | null;
}

export interface ApprovalRecord {
  transition: ApprovalTransition;
  transitionLabel: string;
  person: string;
  isAttributed: boolean;
  timestampUtc: string | null;
  version: string | null;
  dataState: string | null;
  dataFingerprint: string | null;
  comments: string | null;
  exceptionCount: number;
  overrideCount: number;
}

export type ApprovalStatus =
  /** No approval has been recorded. */
  | "NotApproved"
  /** Approved, and the approved version and data are still what is on the page. */
  | "Approved"
  /** Approved, but the version or the data has moved since. */
  | "ChangesRequireReview"
  /** An approval exists but cannot be tied to a version, so currency is unknowable. */
  | "Indeterminate";

const STATUS_SEVERITY: Record<ApprovalStatus, DesignSystemSeverity> = {
  NotApproved: "review",
  Approved: "ready",
  ChangesRequireReview: "action",
  // Not a pass. An approval that cannot be tied to anything proves nothing.
  Indeterminate: "action"
};

const STATUS_LABEL: Record<ApprovalStatus, string> = {
  NotApproved: "Not approved",
  Approved: "Approved",
  ChangesRequireReview: "Changes require review",
  Indeterminate: "Approval cannot be verified"
};

export interface ApprovalEvaluationInput {
  records: readonly ApprovalRecordInput[];
  /** The report's current version. */
  currentVersion?: string | null;
  /**
   * A stable digest of the data behind the report - ledger version, snapshot ids, or
   * any value the caller recomputes when the underlying data changes.
   */
  currentDataFingerprint?: string | null;
  /** The report's current workflow state, used for the transition strip. */
  workflowState?: string | null;
}

export interface ApprovalModel {
  status: ApprovalStatus;
  severity: DesignSystemSeverity;
  label: string;
  /** One sentence naming what moved, for the approval strip. */
  reason: string;
  /** Every recorded transition, oldest first. */
  records: readonly ApprovalRecord[];
  /** The most recent Approve record, when one exists. */
  approval: ApprovalRecord | null;
  /** Marks for the Prepare → Review → Approve → Publish strip. */
  marks: Record<ApprovalTransition, ApprovalTransitionMark>;
  /** True when the approved version differs from the current version. */
  versionMoved: boolean;
  /** True when the data fingerprint differs from the approved one. */
  dataMoved: boolean;
}

/**
 * Evaluate whether an approval still covers what the report currently says.
 *
 * `currentDataFingerprint` is compared against the fingerprint recorded on the
 * approval. When the caller supplies a current fingerprint but the approval carries
 * none, currency is unknowable, so the result is `Indeterminate` rather than
 * `Approved` - an approval nobody can tie to a state of the data is not evidence
 * that the data was reviewed.
 */
export function evaluateApproval(input: ApprovalEvaluationInput): ApprovalModel {
  const records = input.records.map(normalizeRecord).sort(byTimestampThenLifecycle);
  const approvals = records.filter((record) => record.transition === "Approve");
  const approval = approvals.length > 0 ? (approvals[approvals.length - 1] as ApprovalRecord) : null;

  const currentVersion = nonEmpty(input.currentVersion);
  const currentFingerprint = nonEmpty(input.currentDataFingerprint);

  let status: ApprovalStatus;
  let versionMoved = false;
  let dataMoved = false;

  if (approval === null) {
    status = "NotApproved";
  } else if (currentVersion !== null && approval.version === null) {
    // Somebody approved, but not a version anyone can name.
    status = "Indeterminate";
  } else if (currentFingerprint !== null && approval.dataFingerprint === null) {
    status = "Indeterminate";
  } else {
    versionMoved = currentVersion !== null && approval.version !== null && approval.version !== currentVersion;
    dataMoved =
      currentFingerprint !== null &&
      approval.dataFingerprint !== null &&
      approval.dataFingerprint !== currentFingerprint;
    status = versionMoved || dataMoved ? "ChangesRequireReview" : "Approved";
  }

  return {
    status,
    severity: STATUS_SEVERITY[status],
    label: STATUS_LABEL[status],
    reason: describeApproval(status, approval, versionMoved, dataMoved),
    records,
    approval,
    marks: buildMarks(records, normalizeReportingWorkflowState(input.workflowState)),
    versionMoved,
    dataMoved
  };
}

function describeApproval(
  status: ApprovalStatus,
  approval: ApprovalRecord | null,
  versionMoved: boolean,
  dataMoved: boolean
): string {
  if (status === "NotApproved") {
    return "No approval has been recorded for this report.";
  }

  if (status === "Indeterminate") {
    return "An approval is recorded but is not tied to a version or data state, so it cannot be confirmed to cover the current report.";
  }

  if (status === "ChangesRequireReview") {
    const moved = [versionMoved ? "the version" : null, dataMoved ? "the underlying data" : null]
      .filter((part): part is string => part !== null)
      .join(" and ");
    const who = approval?.isAttributed === true ? ` by ${approval.person}` : "";
    return `Previously approved${who}; ${moved} has changed since.`;
  }

  const who = approval?.isAttributed === true ? ` by ${approval.person}` : "";
  return `Approved${who} against the current version and data.`;
}

function buildMarks(
  records: readonly ApprovalRecord[],
  workflowState: ReportingWorkflowState
): Record<ApprovalTransition, ApprovalTransitionMark> {
  const recorded = new Set(records.map((record) => record.transition));
  const currentTransition = TRANSITION_FOR_WORKFLOW[workflowState];

  const marks = {} as Record<ApprovalTransition, ApprovalTransitionMark>;
  for (const transition of APPROVAL_TRANSITIONS) {
    marks[transition] = recorded.has(transition)
      ? "complete"
      : transition === currentTransition
        ? "current"
        : "pending";
  }
  return marks;
}

/** Where each workflow state sits on the approval strip. */
const TRANSITION_FOR_WORKFLOW: Record<ReportingWorkflowState, ApprovalTransition | null> = {
  NotStarted: "Prepare",
  Preparing: "Prepare",
  Blocked: "Prepare",
  ReadyForReview: "Review",
  InReview: "Review",
  ChangesRequested: "Review",
  ReadyForApproval: "Approve",
  Approved: "Publish",
  Publishing: "Publish",
  Published: null,
  Superseded: null,
  Restated: null,
  Archived: null
};

function normalizeRecord(input: ApprovalRecordInput): ApprovalRecord {
  const transition = normalizeTransition(input.transition);
  const person = nonEmpty(input.person);
  return {
    transition,
    transitionLabel: APPROVAL_TRANSITION_LABELS[transition],
    person: person ?? "Unattributed",
    isAttributed: person !== null,
    timestampUtc: nonEmpty(input.timestampUtc),
    version: nonEmpty(input.version),
    dataState: nonEmpty(input.dataState),
    dataFingerprint: nonEmpty(input.dataFingerprint),
    comments: nonEmpty(input.comments),
    exceptionCount: countOf(input.exceptionCount),
    overrideCount: countOf(input.overrideCount)
  };
}

const TRANSITION_LOOKUP: Record<string, ApprovalTransition> = {
  prepare: "Prepare",
  prepared: "Prepare",
  preparing: "Prepare",
  review: "Review",
  reviewed: "Review",
  approve: "Approve",
  approved: "Approve",
  approval: "Approve",
  publish: "Publish",
  published: "Publish",
  publication: "Publish"
};

/**
 * Resolve a transition name.
 *
 * An unrecognized transition resolves to `Prepare` - the earliest, least privileged
 * point on the strip. Guessing a later stage would let an unknown record present as
 * an approval or a publication.
 */
export function normalizeApprovalTransition(value: string | null | undefined): ApprovalTransition {
  return normalizeTransition(value ?? "");
}

function normalizeTransition(value: string): ApprovalTransition {
  const key = value.trim().toLowerCase().replace(/[\s_-]+/g, "");
  return TRANSITION_LOOKUP[key] ?? "Prepare";
}

const LIFECYCLE_ORDER: Record<ApprovalTransition, number> = {
  Prepare: 0,
  Review: 1,
  Approve: 2,
  Publish: 3
};

function byTimestampThenLifecycle(left: ApprovalRecord, right: ApprovalRecord): number {
  const leftTime = parseInstant(left.timestampUtc);
  const rightTime = parseInstant(right.timestampUtc);

  if (leftTime !== null && rightTime !== null && leftTime !== rightTime) {
    return leftTime - rightTime;
  }
  // Records with no usable timestamp fall back to lifecycle order rather than to
  // input order, so a strip built from an unordered payload still reads correctly.
  return LIFECYCLE_ORDER[left.transition] - LIFECYCLE_ORDER[right.transition];
}

/** A single statement the approver must affirm. */
export const ATTESTATION_REQUIREMENTS = [
  "sectionsComplete",
  "exceptionsDisclosed",
  "sourceCoverageMeetsPolicy",
  "reconciliationPassed",
  "commentaryReviewed"
] as const;

export type AttestationRequirement = typeof ATTESTATION_REQUIREMENTS[number];

export const ATTESTATION_REQUIREMENT_LABELS: Record<AttestationRequirement, string> = {
  sectionsComplete: "Required sections are complete",
  exceptionsDisclosed: "Material exceptions are disclosed",
  sourceCoverageMeetsPolicy: "Source coverage meets policy",
  reconciliationPassed: "Reconciliation controls passed",
  commentaryReviewed: "Commentary has been reviewed"
};

/**
 * Requirements by report class.
 *
 * Regulatory and accounting output attests to everything; analytical output, which is
 * not a publication of record, attests to less. A template can require more than its
 * class default but the model does not let it require less - {@link resolveAttestationRequirements}
 * unions rather than replaces, so a class's floor cannot be configured away.
 */
export const CLASS_ATTESTATION_REQUIREMENTS: Record<ReportClass, readonly AttestationRequirement[]> = {
  Regulatory: [...ATTESTATION_REQUIREMENTS],
  Accounting: [...ATTESTATION_REQUIREMENTS],
  Portfolio: ["sectionsComplete", "exceptionsDisclosed", "sourceCoverageMeetsPolicy", "commentaryReviewed"],
  Management: ["sectionsComplete", "exceptionsDisclosed", "commentaryReviewed"],
  Analytical: ["sectionsComplete", "exceptionsDisclosed"]
};

/** The requirements a report must attest to, as its class floor plus any additions. */
export function resolveAttestationRequirements(
  reportClass: string | null | undefined,
  additional?: readonly string[] | null
): readonly AttestationRequirement[] {
  const base = new Set(CLASS_ATTESTATION_REQUIREMENTS[normalizeReportClass(reportClass)]);
  for (const value of additional ?? []) {
    const requirement = normalizeAttestationRequirement(value);
    if (requirement !== null) {
      base.add(requirement);
    }
  }
  return ATTESTATION_REQUIREMENTS.filter((requirement) => base.has(requirement));
}

export function normalizeAttestationRequirement(
  value: string | null | undefined
): AttestationRequirement | null {
  const key = value?.trim().toLowerCase().replace(/[\s_-]+/g, "") ?? "";
  return ATTESTATION_REQUIREMENTS.find((requirement) => requirement.toLowerCase() === key) ?? null;
}

export interface AttestationLine {
  requirement: AttestationRequirement;
  label: string;
  /** `true` affirmed, `false` denied, `null` not answered. */
  confirmed: boolean | null;
  severity: DesignSystemSeverity;
}

export interface AttestationModel {
  lines: readonly AttestationLine[];
  /** True only when every required line is explicitly affirmed. */
  isAttested: boolean;
  /** Requirements that were denied. */
  deniedRequirements: readonly AttestationRequirement[];
  /** Requirements with no answer either way. */
  unansweredRequirements: readonly AttestationRequirement[];
  attestedBy: string | null;
  attestedAtUtc: string | null;
  severity: DesignSystemSeverity;
  summary: string;
}

export interface BuildAttestationInput {
  reportClass?: string | null;
  additionalRequirements?: readonly string[] | null;
  /** Answers keyed by requirement; a missing key is an unanswered requirement. */
  confirmations?: Partial<Record<AttestationRequirement, boolean | null>> | null;
  attestedBy?: string | null;
  attestedAtUtc?: string | null;
}

/**
 * Build the attestation block.
 *
 * A requirement with no answer is not satisfied. Treating an absent confirmation as
 * affirmed would let an approver attest to statements they never saw, which is the
 * one thing an attestation exists to prevent. An attestation with no named approver
 * is likewise not attested: an unsigned statement is not a signature.
 */
export function buildAttestation(input: BuildAttestationInput): AttestationModel {
  const required = resolveAttestationRequirements(input.reportClass, input.additionalRequirements);
  const confirmations = input.confirmations ?? {};

  const lines: AttestationLine[] = required.map((requirement) => {
    const raw = confirmations[requirement];
    const confirmed = raw === true ? true : raw === false ? false : null;
    return {
      requirement,
      label: ATTESTATION_REQUIREMENT_LABELS[requirement],
      confirmed,
      severity: confirmed === true ? "ready" : confirmed === false ? "action" : "review"
    };
  });

  const deniedRequirements = lines
    .filter((line) => line.confirmed === false)
    .map((line) => line.requirement);
  const unansweredRequirements = lines
    .filter((line) => line.confirmed === null)
    .map((line) => line.requirement);

  const attestedBy = nonEmpty(input.attestedBy);
  const isAttested =
    lines.length > 0 &&
    deniedRequirements.length === 0 &&
    unansweredRequirements.length === 0 &&
    attestedBy !== null;

  const severity: DesignSystemSeverity = isAttested
    ? "ready"
    : deniedRequirements.length > 0
      ? "action"
      : "review";

  return {
    lines,
    isAttested,
    deniedRequirements,
    unansweredRequirements,
    attestedBy,
    attestedAtUtc: nonEmpty(input.attestedAtUtc),
    severity,
    summary: describeAttestation(lines.length, deniedRequirements, unansweredRequirements, attestedBy)
  };
}

function describeAttestation(
  total: number,
  denied: readonly AttestationRequirement[],
  unanswered: readonly AttestationRequirement[],
  attestedBy: string | null
): string {
  if (total === 0) {
    return "No attestation is required for this report class.";
  }

  if (denied.length > 0) {
    return `${denied.length} of ${total} statement(s) could not be affirmed.`;
  }

  if (unanswered.length > 0) {
    return `${unanswered.length} of ${total} statement(s) unanswered; attestation is incomplete.`;
  }

  if (attestedBy === null) {
    return "Every statement is affirmed but no approver is recorded, so the attestation is unsigned.";
  }

  return `All ${total} statements affirmed by ${attestedBy}.`;
}

function countOf(value: number | null | undefined): number {
  return typeof value === "number" && Number.isFinite(value) && value > 0 ? Math.trunc(value) : 0;
}

function nonEmpty(value: string | null | undefined): string | null {
  const trimmed = value?.trim() ?? "";
  return trimmed.length > 0 ? trimmed : null;
}

function parseInstant(value: string | null): number | null {
  if (value === null) {
    return null;
  }
  const parsed = Date.parse(value);
  return Number.isNaN(parsed) ? null : parsed;
}
