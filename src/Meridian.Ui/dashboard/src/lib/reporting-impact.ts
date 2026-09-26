/**
 * Downstream impact of a change, shown before the change is committed.
 *
 * Two shapes of question, one model:
 *
 *  - *Replacement*: a manual spreadsheet source is being swapped for an official
 *    ledger feed. What does that touch - how many blocks, how many reports, how many
 *    published-period comparisons, how many open reviews - and how many values are
 *    expected to move?
 *  - *Event*: a security is downgraded. What does the change propagate through, and
 *    where does it land in the reporting estate?
 *
 * The controlling rule is that **undeterminable impact counts as affected**. A block
 * whose dependency cannot be evaluated is reported as impacted and flagged as
 * unresolved, never quietly dropped. A preview that understates its own blind spots
 * is worse than no preview, because it is acted on with confidence.
 */
import { formatNumber, pluralizeCount } from "@/lib/format";
import { isPublishedWorkflowState, normalizeReportingWorkflowState } from "@/lib/reporting-lifecycle";
import type { DesignSystemSeverity } from "@/design-system/status";

export type ImpactCertainty = "Certain" | "Expected" | "Unresolved";

const CERTAINTY_RANK: Record<ImpactCertainty, number> = {
  Certain: 2,
  Expected: 1,
  Unresolved: 0
};

export interface AffectedBlockInput {
  blockId: string;
  blockLabel: string;
  reportId: string;
  reportName: string;
  /** Workflow state of the owning report. */
  reportWorkflowState?: string | null;
  /** True when this block has an open review at the time of the change. */
  hasOpenReview?: boolean | null;
  /**
   * Whether the block's value is expected to move.
   *
   * `null` or `undefined` means the dependency could not be evaluated - which is
   * treated as impacted-and-unresolved, not as unaffected.
   */
  valueChanges?: boolean | null;
  /** True when the block participates in a published-period comparison. */
  isPeriodComparison?: boolean | null;
}

export interface AffectedBlock {
  blockId: string;
  blockLabel: string;
  reportId: string;
  reportName: string;
  certainty: ImpactCertainty;
  isPublishedReport: boolean;
  hasOpenReview: boolean;
  isPeriodComparison: boolean;
}

export interface ReportingImpactCounts {
  blocks: number;
  reports: number;
  publishedReports: number;
  periodComparisons: number;
  openReviews: number;
  /** Blocks whose value is expected to move. */
  expectedValueChanges: number;
  /** Blocks whose dependency could not be evaluated. */
  unresolved: number;
}

export interface ReportingImpactModel {
  blocks: readonly AffectedBlock[];
  counts: ReportingImpactCounts;
  severity: DesignSystemSeverity;
  /** Ordered lines for the impact preview, e.g. "14 report blocks". */
  lines: readonly string[];
  summary: string;
  /**
   * True when the change reaches a published report, making it a restatement
   * decision rather than an edit.
   */
  requiresRestatementAssessment: boolean;
  /** True when at least one dependency could not be evaluated. */
  hasUnresolvedImpact: boolean;
}

export interface SourceReplacementInput {
  currentSource: string;
  proposedSource: string;
  affectedBlocks: readonly AffectedBlockInput[];
}

export interface SourceReplacementModel extends ReportingImpactModel {
  currentSource: string;
  proposedSource: string;
  /** False when the replacement is a no-op, e.g. the same source restated. */
  isChange: boolean;
}

/**
 * Preview the effect of replacing one source with another.
 *
 * A replacement that names the same source on both sides is reported as no change
 * rather than as an impact-free change: the first is a mistake worth surfacing, the
 * second would read as a green light.
 */
export function buildSourceReplacementImpact(input: SourceReplacementInput): SourceReplacementModel {
  const impact = buildReportingImpact(input.affectedBlocks);
  const currentSource = input.currentSource.trim();
  const proposedSource = input.proposedSource.trim();
  const isChange = currentSource.toLowerCase() !== proposedSource.toLowerCase();

  return {
    ...impact,
    currentSource: currentSource || "Not set",
    proposedSource: proposedSource || "Not set",
    isChange,
    summary: isChange
      ? impact.summary
      : "Proposed source matches the current source; nothing would change."
  };
}

/**
 * Aggregate the reporting estate a change touches.
 *
 * Blocks are ranked by how firmly they are affected and by whether their report is
 * published, so the entries a reviewer must look at first are first.
 */
export function buildReportingImpact(inputs: readonly AffectedBlockInput[]): ReportingImpactModel {
  const blocks: AffectedBlock[] = inputs.map((input) => {
    const workflowState = normalizeReportingWorkflowState(input.reportWorkflowState);
    return {
      blockId: input.blockId,
      blockLabel: input.blockLabel,
      reportId: input.reportId,
      reportName: input.reportName,
      certainty: resolveCertainty(input.valueChanges),
      isPublishedReport: isPublishedWorkflowState(workflowState),
      hasOpenReview: input.hasOpenReview === true,
      isPeriodComparison: input.isPeriodComparison === true
    };
  });

  const ordered = blocks
    .map((block, index) => ({ block, index }))
    .sort((left, right) => {
      if (left.block.isPublishedReport !== right.block.isPublishedReport) {
        return left.block.isPublishedReport ? -1 : 1;
      }
      const rank = CERTAINTY_RANK[right.block.certainty] - CERTAINTY_RANK[left.block.certainty];
      // Unresolved outranks Expected despite its lower rank: a blind spot needs a
      // decision before a merely probable move does.
      if (left.block.certainty === "Unresolved" && right.block.certainty === "Expected") {
        return -1;
      }
      if (right.block.certainty === "Unresolved" && left.block.certainty === "Expected") {
        return 1;
      }
      return rank !== 0 ? rank : left.index - right.index;
    })
    .map((wrapped) => wrapped.block);

  const counts = countImpact(ordered);
  const requiresRestatementAssessment = counts.publishedReports > 0;
  const hasUnresolvedImpact = counts.unresolved > 0;

  const severity: DesignSystemSeverity = requiresRestatementAssessment
    ? "action"
    : hasUnresolvedImpact || counts.openReviews > 0
      ? "review"
      : counts.blocks > 0
        ? "info"
        : "ready";

  return {
    blocks: ordered,
    counts,
    severity,
    lines: buildLines(counts),
    summary: summarize(counts, requiresRestatementAssessment, hasUnresolvedImpact),
    requiresRestatementAssessment,
    hasUnresolvedImpact
  };
}

function resolveCertainty(valueChanges: boolean | null | undefined): ImpactCertainty {
  if (valueChanges === true) {
    return "Certain";
  }
  if (valueChanges === false) {
    return "Expected";
  }
  // Unknown dependency. The block is still in scope; what is unknown is whether its
  // value moves - so it counts as affected and is named as unresolved.
  return "Unresolved";
}

function countImpact(blocks: readonly AffectedBlock[]): ReportingImpactCounts {
  const reportIds = new Set<string>();
  const publishedReportIds = new Set<string>();
  let periodComparisons = 0;
  let openReviews = 0;
  let expectedValueChanges = 0;
  let unresolved = 0;

  for (const block of blocks) {
    reportIds.add(block.reportId);
    if (block.isPublishedReport) {
      publishedReportIds.add(block.reportId);
    }
    if (block.isPeriodComparison) {
      periodComparisons += 1;
    }
    if (block.hasOpenReview) {
      openReviews += 1;
    }
    if (block.certainty === "Certain") {
      expectedValueChanges += 1;
    }
    if (block.certainty === "Unresolved") {
      unresolved += 1;
    }
  }

  return {
    blocks: blocks.length,
    reports: reportIds.size,
    publishedReports: publishedReportIds.size,
    periodComparisons,
    openReviews,
    expectedValueChanges,
    unresolved
  };
}

function buildLines(counts: ReportingImpactCounts): readonly string[] {
  const lines: string[] = [];
  if (counts.blocks > 0) {
    lines.push(pluralizeCount(counts.blocks, "report block"));
  }
  if (counts.reports > 0) {
    lines.push(pluralizeCount(counts.reports, "report"));
  }
  if (counts.periodComparisons > 0) {
    lines.push(pluralizeCount(counts.periodComparisons, "published-period comparison"));
  }
  if (counts.openReviews > 0) {
    lines.push(pluralizeCount(counts.openReviews, "open review"));
  }
  return lines;
}

function summarize(
  counts: ReportingImpactCounts,
  requiresRestatementAssessment: boolean,
  hasUnresolvedImpact: boolean
): string {
  if (counts.blocks === 0) {
    return "No report blocks depend on this.";
  }

  const parts = [
    `Affects ${pluralizeCount(counts.blocks, "report block")} across ${pluralizeCount(counts.reports, "report")}.`
  ];

  if (counts.expectedValueChanges > 0) {
    parts.push(`${formatNumber(counts.expectedValueChanges, { maximumFractionDigits: 0 })} value(s) expected to change.`);
  }

  if (requiresRestatementAssessment) {
    parts.push(
      `${pluralizeCount(counts.publishedReports, "published report")} affected; restatement assessment required.`
    );
  }

  if (hasUnresolvedImpact) {
    parts.push(
      `${pluralizeCount(counts.unresolved, "block")} could not be evaluated and are counted as affected.`
    );
  }

  return parts.join(" ");
}

export interface ImpactChainLinkInput {
  label: string;
  /** Movement at this link, e.g. "BBB → BB". */
  transition?: string | null;
  detail?: string | null;
}

export interface ImpactChainLink {
  ordinal: number;
  label: string;
  transition: string | null;
  detail: string | null;
}

export interface EventImpactModel extends ReportingImpactModel {
  /** The originating event, e.g. "Security rating change". */
  event: string;
  transition: string | null;
  /** The propagation path from the event to the reporting estate. */
  chain: readonly ImpactChainLink[];
  /** Reports whose review must reopen because a reviewed value moved. */
  reportsRequiringReview: number;
  chainSummary: string;
}

export interface BuildEventImpactInput {
  event: string;
  transition?: string | null;
  chain: readonly ImpactChainLinkInput[];
  affectedBlocks: readonly AffectedBlockInput[];
  /**
   * Workflow states that mean a report has already been reviewed. A value moving
   * underneath one of these sends the report back for review.
   */
  reviewedStates?: readonly string[];
}

const DEFAULT_REVIEWED_STATES: readonly string[] = [
  "InReview",
  "ReadyForApproval",
  "Approved",
  "Published",
  "Restated",
  "Superseded"
];

/**
 * Trace an upstream event through to the reports it lands on.
 *
 * `reportsRequiringReview` counts distinct reports that had already reached a
 * reviewed state, since those are the ones where a moved value invalidates work
 * somebody already signed off. A block in a report still being prepared is affected
 * but costs nobody a second review.
 */
export function buildEventImpact(input: BuildEventImpactInput): EventImpactModel {
  const impact = buildReportingImpact(input.affectedBlocks);
  const reviewed = new Set(
    (input.reviewedStates ?? DEFAULT_REVIEWED_STATES).map((state) => normalizeReportingWorkflowState(state))
  );

  const reportsRequiringReview = new Set(
    input.affectedBlocks
      .filter((block) => reviewed.has(normalizeReportingWorkflowState(block.reportWorkflowState)))
      .map((block) => block.reportId)
  ).size;

  const chain: ImpactChainLink[] = input.chain.map((link, index) => ({
    ordinal: index,
    label: link.label,
    transition: nonEmpty(link.transition),
    detail: nonEmpty(link.detail)
  }));

  const severity: DesignSystemSeverity = impact.requiresRestatementAssessment
    ? "action"
    : reportsRequiringReview > 0
      ? "review"
      : impact.severity;

  return {
    ...impact,
    severity,
    event: input.event,
    transition: nonEmpty(input.transition),
    chain,
    reportsRequiringReview,
    chainSummary: summarizeChain(input.event, chain, impact.counts.blocks, reportsRequiringReview)
  };
}

function summarizeChain(
  event: string,
  chain: readonly ImpactChainLink[],
  blockCount: number,
  reportsRequiringReview: number
): string {
  const path = [event, ...chain.map((link) => link.label)].join(" → ");
  const tail = blockCount === 0
    ? "no report blocks affected"
    : `${pluralizeCount(blockCount, "report block")} changed`;
  const review = reportsRequiringReview > 0
    ? `, ${pluralizeCount(reportsRequiringReview, "report")} require review`
    : "";
  return `${path} → ${tail}${review}.`;
}

function nonEmpty(value: string | null | undefined): string | null {
  const trimmed = value?.trim() ?? "";
  return trimmed.length > 0 ? trimmed : null;
}
