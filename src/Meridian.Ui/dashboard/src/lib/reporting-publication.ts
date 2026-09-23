/**
 * Publication records, immutability, and restatement.
 *
 * Publishing a controlled report is not writing a file. It creates a record that
 * fixes what was said, when, by whom, against which data cutoff and which ledger
 * version - and that record never changes again. A correction does not edit v09; it
 * publishes v10 with an explicit relationship to the version it supersedes and a
 * stated reason. Overwriting would destroy the only evidence of what recipients
 * actually received.
 *
 * Two rules carry the model:
 *
 *  - **Published records are terminal.** {@link buildRestatementChain} orders versions
 *    and reports any break in the chain - a missing predecessor, a cycle, two versions
 *    claiming to supersede the same one - rather than rendering a tidy list that hides
 *    a broken lineage.
 *  - **Unknown audience is internal.** An output whose distribution class cannot be
 *    determined is withheld from the external layer. The cost of wrongly withholding
 *    an output is an operator asking why; the cost of wrongly releasing one is a
 *    disclosure that cannot be recalled.
 */
import { formatNumber } from "@/lib/format";
import {
  isPublishedWorkflowState,
  normalizeReportingWorkflowState,
  type ReportingWorkflowState
} from "@/lib/reporting-lifecycle";
import type { DesignSystemSeverity } from "@/design-system/status";

/** Output formats a publication package can carry. */
export const PUBLICATION_OUTPUT_KINDS = [
  "Pdf",
  "Excel",
  "PowerPoint",
  "DataPackage",
  "Evidence",
  "Archive"
] as const;

export type PublicationOutputKind = typeof PUBLICATION_OUTPUT_KINDS[number];

export const PUBLICATION_OUTPUT_LABELS: Record<PublicationOutputKind, string> = {
  Pdf: "PDF",
  Excel: "Excel",
  PowerPoint: "PowerPoint",
  DataPackage: "Data package",
  Evidence: "Evidence package",
  Archive: "Archive"
};

const OUTPUT_LOOKUP: Record<string, PublicationOutputKind> = {
  pdf: "Pdf",
  pdfa: "Pdf",
  excel: "Excel",
  xlsx: "Excel",
  powerpoint: "PowerPoint",
  pptx: "PowerPoint",
  datapackage: "DataPackage",
  data: "DataPackage",
  evidence: "Evidence",
  evidencepackage: "Evidence",
  archive: "Archive"
};

export function normalizePublicationOutputKind(
  value: string | null | undefined
): PublicationOutputKind | null {
  const key = value?.trim().toLowerCase().replace(/[\s_/-]+/g, "") ?? "";
  return key ? OUTPUT_LOOKUP[key] ?? null : null;
}

/** Where an output is permitted to go. */
export type DistributionClass = "Internal" | "External";

export interface PublicationOutputInput {
  kind: string;
  /** "Internal" or "External". Anything else, or absent, is treated as internal. */
  distributionClass?: string | null;
  /** True when the template permits this output for this report. */
  isPermitted?: boolean | null;
  label?: string | null;
}

export interface PublicationOutput {
  kind: PublicationOutputKind | null;
  /** The label to render; falls back to the raw input for an unrecognized kind. */
  label: string;
  distributionClass: DistributionClass;
  /** True when the caller explicitly stated the distribution class. */
  isDistributionClassKnown: boolean;
  isPermitted: boolean;
}

/**
 * Outputs whose content is inherently internal.
 *
 * An evidence package carries reviewer comments, accepted exceptions and manual
 * overrides. It is control material, not a deliverable, and it never crosses the
 * boundary regardless of what a caller declares.
 */
const ALWAYS_INTERNAL: readonly PublicationOutputKind[] = ["Evidence", "Archive"];

export interface PublicationRecordInput {
  /** Version label as published, e.g. "09". */
  version: string;
  reportName?: string | null;
  periodLabel?: string | null;
  publishedAtUtc?: string | null;
  approvedBy?: string | null;
  /** Data cutoff the publication was struck at. */
  dataCutoffUtc?: string | null;
  /** Ledger version the publication is bound to, e.g. "CLOSE-2026-09-v4". */
  ledgerVersion?: string | null;
  outputs?: readonly PublicationOutputInput[];
  /** Version this one supersedes, for a restatement. */
  supersedesVersion?: string | null;
  /** Why this version supersedes the previous one. */
  restatementReason?: string | null;
  /** Sections the restatement changed. */
  affectedSections?: readonly string[] | null;
  workflowState?: string | null;
}

export interface PublicationRecord {
  version: string;
  reportName: string | null;
  periodLabel: string | null;
  publishedAtUtc: string | null;
  approvedBy: string | null;
  dataCutoffUtc: string | null;
  ledgerVersion: string | null;
  outputs: readonly PublicationOutput[];
  supersedesVersion: string | null;
  restatementReason: string | null;
  affectedSections: readonly string[];
  workflowState: ReportingWorkflowState;
  isRestatement: boolean;
  /** True when the record has everything needed to defend the publication. */
  isComplete: boolean;
  /** Fields a complete publication record requires but this one lacks. */
  missingFields: readonly string[];
}

/**
 * Build one immutable publication record.
 *
 * A record declaring a superseded version without a reason is still a restatement -
 * the relationship is the fact, the reason is the documentation - but the missing
 * reason is reported, because a restatement nobody explained is an audit finding.
 */
export function buildPublicationRecord(input: PublicationRecordInput): PublicationRecord {
  const outputs = (input.outputs ?? []).map(normalizeOutput);
  const supersedesVersion = nonEmpty(input.supersedesVersion);
  const restatementReason = nonEmpty(input.restatementReason);
  const affectedSections = (input.affectedSections ?? [])
    .map((section) => nonEmpty(section))
    .filter((section): section is string => section !== null);

  const publishedAtUtc = nonEmpty(input.publishedAtUtc);
  const approvedBy = nonEmpty(input.approvedBy);
  const dataCutoffUtc = nonEmpty(input.dataCutoffUtc);
  const ledgerVersion = nonEmpty(input.ledgerVersion);

  const missingFields: string[] = [];
  if (publishedAtUtc === null) {
    missingFields.push("Published");
  }
  if (approvedBy === null) {
    missingFields.push("Approved by");
  }
  if (dataCutoffUtc === null) {
    missingFields.push("Data cutoff");
  }
  if (ledgerVersion === null) {
    missingFields.push("Ledger version");
  }
  if (supersedesVersion !== null && restatementReason === null) {
    missingFields.push("Restatement reason");
  }

  return {
    version: input.version.trim(),
    reportName: nonEmpty(input.reportName),
    periodLabel: nonEmpty(input.periodLabel),
    publishedAtUtc,
    approvedBy,
    dataCutoffUtc,
    ledgerVersion,
    outputs,
    supersedesVersion,
    restatementReason,
    affectedSections,
    workflowState: normalizeReportingWorkflowState(input.workflowState),
    isRestatement: supersedesVersion !== null,
    isComplete: missingFields.length === 0,
    missingFields
  };
}

function normalizeOutput(input: PublicationOutputInput): PublicationOutput {
  const kind = normalizePublicationOutputKind(input.kind);
  const declared = nonEmpty(input.distributionClass)?.toLowerCase();
  const isDistributionClassKnown = declared === "internal" || declared === "external";

  // Unknown audience is internal, and evidence-bearing outputs are internal whatever
  // the caller says. Withholding is recoverable; releasing is not.
  const distributionClass: DistributionClass =
    kind !== null && ALWAYS_INTERNAL.includes(kind)
      ? "Internal"
      : declared === "external"
        ? "External"
        : "Internal";

  return {
    kind,
    label: nonEmpty(input.label) ?? (kind !== null ? PUBLICATION_OUTPUT_LABELS[kind] : input.kind.trim()),
    distributionClass,
    isDistributionClassKnown,
    isPermitted: input.isPermitted !== false
  };
}

/** The outputs that may leave Meridian, i.e. the external publication layer. */
export function externalOutputs(record: PublicationRecord): readonly PublicationOutput[] {
  return record.outputs.filter(
    (output) => output.distributionClass === "External" && output.isPermitted
  );
}

/** The outputs that stay inside Meridian, i.e. the internal control layer. */
export function internalOutputs(record: PublicationRecord): readonly PublicationOutput[] {
  return record.outputs.filter(
    (output) => output.distributionClass === "Internal" || !output.isPermitted
  );
}

export type RestatementChainIssueKind =
  | "MissingPredecessor"
  | "DuplicateSupersede"
  | "Cycle"
  | "MultipleRoots";

export interface RestatementChainIssue {
  kind: RestatementChainIssueKind;
  version: string;
  detail: string;
}

export interface RestatementChainModel {
  /** Records ordered oldest to newest along the supersede chain. */
  ordered: readonly PublicationRecord[];
  /** The version currently in force, i.e. the one nothing supersedes. */
  current: PublicationRecord | null;
  /** Versions that have been superseded, newest first. */
  superseded: readonly PublicationRecord[];
  issues: readonly RestatementChainIssue[];
  isIntact: boolean;
  severity: DesignSystemSeverity;
  summary: string;
}

/**
 * Order publication versions along their supersede relationships.
 *
 * Breaks in the chain are reported rather than smoothed over. A version naming a
 * predecessor that is not present means the record set is incomplete; two versions
 * superseding the same one means the lineage forked and only one of them can be in
 * force. Either way a reader needs to know the history they are looking at is not
 * the whole history.
 */
export function buildRestatementChain(
  records: readonly PublicationRecord[]
): RestatementChainModel {
  const issues: RestatementChainIssue[] = [];
  const byVersion = new Map<string, PublicationRecord>();
  for (const record of records) {
    byVersion.set(record.version, record);
  }

  const supersededBy = new Map<string, string>();
  for (const record of records) {
    const target = record.supersedesVersion;
    if (target === null) {
      continue;
    }

    if (!byVersion.has(target)) {
      issues.push({
        kind: "MissingPredecessor",
        version: record.version,
        detail: `v${record.version} supersedes v${target}, which is not in this history.`
      });
      continue;
    }

    const existing = supersededBy.get(target);
    if (existing !== undefined) {
      issues.push({
        kind: "DuplicateSupersede",
        version: record.version,
        detail: `v${record.version} and v${existing} both supersede v${target}.`
      });
      continue;
    }
    supersededBy.set(target, record.version);
  }

  const roots = records.filter((record) => {
    const target = record.supersedesVersion;
    return target === null || !byVersion.has(target);
  });

  if (roots.length > 1) {
    issues.push({
      kind: "MultipleRoots",
      version: roots.map((record) => record.version).join(", "),
      detail: `${roots.length} versions have no predecessor in this history.`
    });
  } else if (roots.length === 0 && records.length > 0) {
    // Every record names a predecessor that is present, so the relationships close
    // on themselves. The walk below starts from a root and would never run, leaving
    // the cycle unreported as a set of merely "unreachable" records.
    issues.push({
      kind: "Cycle",
      version: records.map((record) => record.version).join(", "),
      detail: "Every version supersedes another in this history, so the chain has no origin."
    });
  }

  const ordered: PublicationRecord[] = [];
  const visited = new Set<string>();
  let cursor: PublicationRecord | null = roots[0] ?? null;

  while (cursor !== null) {
    if (visited.has(cursor.version)) {
      issues.push({
        kind: "Cycle",
        version: cursor.version,
        detail: `The supersede chain returns to v${cursor.version}.`
      });
      break;
    }
    visited.add(cursor.version);
    ordered.push(cursor);

    const nextVersion = supersededBy.get(cursor.version);
    cursor = nextVersion === undefined ? null : byVersion.get(nextVersion) ?? null;
  }

  // Records the walk never reached, because the chain broke before them.
  const unreached = records.filter((record) => !visited.has(record.version));
  const current = ordered.length > 0 ? (ordered[ordered.length - 1] as PublicationRecord) : null;
  const superseded = ordered.slice(0, -1).reverse();
  const isIntact = issues.length === 0 && unreached.length === 0;

  return {
    ordered,
    current,
    superseded,
    issues,
    isIntact,
    severity: isIntact ? "ready" : "action",
    summary: describeChain(ordered.length, current, issues, unreached.length)
  };
}

function describeChain(
  orderedCount: number,
  current: PublicationRecord | null,
  issues: readonly RestatementChainIssue[],
  unreachedCount: number
): string {
  if (orderedCount === 0) {
    return "No publication has been recorded.";
  }

  const head = current === null
    ? ""
    : `v${current.version} is in force${current.isRestatement ? " as a restatement" : ""}.`;
  const count = `${formatNumber(orderedCount, { maximumFractionDigits: 0 })} version${orderedCount === 1 ? "" : "s"} in the chain.`;

  const parts = [head, count].filter((part) => part.length > 0);
  if (issues.length > 0) {
    parts.push(`${issues.length} lineage issue(s): ${issues[0]?.detail ?? ""}`);
  }
  if (unreachedCount > 0) {
    parts.push(`${unreachedCount} record(s) not reachable from the chain.`);
  }
  return parts.join(" ");
}

export interface DraftStateMarking {
  /** True when the report may be distributed. */
  isApprovedForDistribution: boolean;
  /** "DRAFT" / "PUBLISHED" / "SUPERSEDED", for the header/footer treatment. */
  stateLabel: string;
  /** "Not approved for distribution", or the publication line. */
  distributionLine: string;
  severity: DesignSystemSeverity;
}

export interface DraftStateInput {
  workflowState?: string | null;
  version?: string | null;
  periodLabel?: string | null;
}

/**
 * The header/footer state treatment for a rendered report.
 *
 * A report is approved for distribution only when its workflow state says it was
 * published. Everything else - including `Approved`, which means signed off but not
 * yet released - is marked draft, because an approved-but-unpublished document
 * leaving the building is exactly the accident this marking prevents.
 */
export function buildDraftStateMarking(input: DraftStateInput): DraftStateMarking {
  const state = normalizeReportingWorkflowState(input.workflowState);
  const version = nonEmpty(input.version);
  const period = nonEmpty(input.periodLabel);

  const isPublished = isPublishedWorkflowState(state);
  const isSuperseded = state === "Superseded";
  const isApprovedForDistribution = isPublished && !isSuperseded;

  const stateLabel = isSuperseded ? "SUPERSEDED" : isPublished ? "PUBLISHED" : "DRAFT";
  const context = [period, version === null ? null : `Version ${version}`]
    .filter((part): part is string => part !== null)
    .join(" · ");

  const distributionLine = isSuperseded
    ? "Superseded by a later version; not for distribution"
    : isPublished
      ? context || "Approved for distribution"
      : "Not approved for distribution";

  return {
    isApprovedForDistribution,
    stateLabel,
    distributionLine,
    severity: isApprovedForDistribution ? "ready" : isSuperseded ? "action" : "review"
  };
}

function nonEmpty(value: string | null | undefined): string | null {
  const trimmed = value?.trim() ?? "";
  return trimmed.length > 0 ? trimmed : null;
}
